using System.Text;
using System.Text.Json;
using System.Linq;
using CG.Web.MegaApiClient;
using FluentFTP;
using FluentFTP.Exceptions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;

namespace K4GOTV;

[MinimumApiVersion(300)]
public sealed partial class Plugin : BasePlugin, IPluginConfig<PluginConfig>
{
	public override string ModuleName => "K4-GOTV";
	public override string ModuleDescription => "Advanced GOTV handler with Discord, database, FTP, SFTP and Mega integration";
	public override string ModuleVersion => "2.0.1";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";

	public required PluginConfig Config { get; set; } = new PluginConfig();

	private string? fileName = null;
	private double LastPlayerCheckTime;
	private bool DemoRequestedThisRound = false;
	private readonly List<(string name, ulong steamid)> Requesters = [];
	private double DemoStartTime = 0.0;
	private int maxFileSizeInMB = 25;
	private string DemoDirectory => Path.Combine(Server.GameDirectory, "csgo", Config.General.DemoDirectory);
	private UploadService? uploadService;
	private DatabaseService? databaseService;
	private string RetentionFilePath => Path.Combine(ModuleDirectory, "uploads_retention.json");
	private record UploadRetentionRecord(string Service, string Identifier, DateTime UploadedAt);

	public override void Load(bool hotReload)
	{
		AddCommandListener("tv_record", CommandListener_Record);
		AddCommandListener("tv_stoprecord", CommandListener_StopRecord);
		AddCommandListener("changelevel", CommandListener_Changelevel, HookMode.Pre);
		AddCommandListener("map", CommandListener_Changelevel, HookMode.Pre);
		AddCommandListener("host_workshop_map", CommandListener_Changelevel, HookMode.Pre);
		AddCommandListener("ds_workshop_changelevel", CommandListener_Changelevel, HookMode.Pre);

		RegisterEventHandler((EventCsWinPanelMatch @event, GameEventInfo info) =>
		{
			Server.ExecuteCommand("tv_stoprecord");
			return HookResult.Continue;
		});

		RegisterListener<Listeners.OnMapEnd>(() =>
		{
			if (!string.IsNullOrEmpty(fileName))
				Server.ExecuteCommand("tv_stoprecord");
		});

		RegisterEventHandler((EventRoundStart @event, GameEventInfo info) =>
		{
			if (Config.AutoRecord.Enabled)
			{
				if (Config.AutoRecord.CropRounds && !string.IsNullOrEmpty(fileName))
					Server.ExecuteCommand("tv_stoprecord");

				if (Config.AutoRecord.CropRounds)
					Requesters.Clear();

				if (string.IsNullOrEmpty(fileName) && PlayerCount() > 0)
					Server.NextWorldUpdate(() => Server.ExecuteCommand("tv_record \"autodemo\""));
			}
			return HookResult.Continue;
		});

		RegisterEventHandler((EventPlayerActivate @event, GameEventInfo info) =>
		{
			var player = @event.Userid;
			if (player?.IsValid == true && !player.IsBot && !player.IsHLTV)
				LastPlayerCheckTime = Server.EngineTime;

			if (string.IsNullOrEmpty(fileName) && Config.AutoRecord.Enabled)
			{
				Server.ExecuteCommand("tv_record");
				Logger.LogInformation("Recording started due to player activity detected.");
			}

			return HookResult.Continue;
		});

		Directory.CreateDirectory(Path.Combine(Server.GameDirectory, "csgo", Config.General.DemoDirectory));

		if (Config.DemoRequest.Enabled)
			AddCommand("css_demo", "Request the upload of the current demo", Command_DemoRequest);

		if (Config.AutoRecord.StopOnIdle)
		{
			AddTimer(1.0f, () =>
			{
				if (DemoStartTime == 0.0)
					return;

				int _playerCount = PlayerCount();
				if (_playerCount < Config.AutoRecord.IdlePlayerCountThreshold)
				{
					double idleTime = Server.EngineTime - LastPlayerCheckTime;
					if (idleTime > Config.AutoRecord.IdleTimeSeconds)
					{
						Server.ExecuteCommand("tv_stoprecord");
						Logger.LogInformation($"Recording stopped due to inactivity exceeding {Config.AutoRecord.IdleTimeSeconds} seconds, player count: {_playerCount}.");
					}
				}
				else
					LastPlayerCheckTime = Server.EngineTime;
			}, TimerFlags.REPEAT);
		}

		if (Config.AutoRecord.Enabled && hotReload && PlayerCount() > 0)
			Server.ExecuteCommand("tv_record \"autodemo\"");

		maxFileSizeInMB = (Config.Discord.ServerBoost == 2) ? 50 : (Config.Discord.ServerBoost == 3) ? 100 : 25;
		uploadService = new UploadService(Config, Logger);

		if (Config.General.AutoCleanupEnabled)
		{
			AddTimer(Config.General.AutoCleanupIntervalMinutes * 60f, () =>
			{
				var cutoff = DateTime.Now.AddHours(-Config.General.AutoCleanupFileAgeHours);
				var files = Directory.GetFiles(DemoDirectory, "*.dem").Concat(Directory.GetFiles(DemoDirectory, "*.zip"));
				foreach (var file in files)
				{
					if (File.GetCreationTime(file) < cutoff)
						CSSThread.RunOnMainThread(async () => await FileManager.DeleteFileAsync(file, Logger, Config.General.LogDeletions));
				}
			}, TimerFlags.REPEAT);
		}

		if (Config.Ftp.RetentionEnabled)
		{
			AddTimer(3600f, () => CSSThread.RunOnMainThread(async () => await CleanFtpRetention()), TimerFlags.REPEAT);
		}

		if (Config.Mega.RetentionEnabled)
		{
			AddTimer(3600f, () => CSSThread.RunOnMainThread(async () => await CleanMegaRetention()), TimerFlags.REPEAT);
		}
	}

	public override void Unload(bool hotReload)
	{
		Server.ExecuteCommand("tv_stoprecord");
	}

	public override void OnAllPluginsLoaded(bool isReload)
	{
		CSSThread.RunOnMainThread(async () =>
		{
			if (Config.General.DeleteEveryDemoFromServerAfterServerStart)
			{
				var allFiles = Directory.GetFiles(DemoDirectory, "*.dem").Concat(Directory.GetFiles(DemoDirectory, "*.zip")).ToArray();

				foreach (var file in allFiles)
					await FileManager.DeleteFileAsync(file, Logger, Config.General.LogDeletions);
			}
		});
	}

	private HookResult CommandListener_Changelevel(CCSPlayerController? player, CommandInfo info)
	{
		if (!string.IsNullOrEmpty(fileName))
			Server.ExecuteCommand("tv_stoprecord");

		return HookResult.Continue;
	}

	private HookResult CommandListener_Record(CCSPlayerController? player, CommandInfo info)
	{
		if (!Config.AutoRecord.Enabled || fileName != null)
			return HookResult.Continue;

		if (!Config.AutoRecord.RecordWarmup && GameRules()?.GameRules?.WarmupPeriod == true)
			return HookResult.Continue;

		DemoStartTime = Server.EngineTime;
		string fileNameArg = info.ArgString.Trim().Replace("\"", "");
		string baseName = string.IsNullOrEmpty(fileNameArg) ? Config.General.DefaultFileName : fileNameArg;
		string pattern = (Config.AutoRecord.Enabled && Config.AutoRecord.CropRounds)
			? Config.General.CropRoundsFileNamingPattern
			: Config.General.RegularFileNamingPattern;
		fileName = ReplacePlaceholdersForFileName(pattern, baseName);

		string fullPath = Path.Combine(DemoDirectory, $"{fileName}.dem");
		int counter = 1;
		while (File.Exists(fullPath))
		{
			fileName = $"{fileName}_{counter}";
			fullPath = Path.Combine(DemoDirectory, $"{fileName}.dem");
			counter++;
		}

		string relativePath = Path.Combine(Config.General.DemoDirectory, $"{fileName}.dem");
		Server.ExecuteCommand($"tv_record \"{relativePath}\"");
		return HookResult.Stop;
	}

	private HookResult CommandListener_StopRecord(CCSPlayerController? player, CommandInfo info)
	{
		if (string.IsNullOrEmpty(fileName) || (Server.EngineTime - DemoStartTime) < Config.General.MinimumDemoDuration)
		{
			ResetVariables();
			return HookResult.Continue;
		}

		string demoPath = Path.Combine(DemoDirectory, $"{fileName}.dem");
		if (!File.Exists(demoPath))
		{
			Logger.LogError($"Demo file not found: {demoPath} - Recording stopped without processing.");
			return HookResult.Continue;
		}

		if (Config.DemoRequest.Enabled && !DemoRequestedThisRound)
		{
			if (Config.DemoRequest.DeleteUnused)
				CSSThread.RunOnMainThread(async () => await FileManager.DeleteFileAsync(demoPath, Logger, Config.General.LogDeletions));

			ResetVariables();
			return HookResult.Continue;
		}

		ProcessUpload(fileName, demoPath);
		ResetVariables();

		return HookResult.Continue;
	}

	public void ProcessUpload(string fileName, string demoPath)
	{
		string zipPath = Path.Combine(DemoDirectory, $"{fileName}.zip");
		var demoLength = TimeSpan.FromSeconds(Server.EngineTime - DemoStartTime);
		var placeholders = new Dictionary<string, string>
		{
			["webhook_name"] = Config.Discord.WebhookName,
			["webhook_avatar"] = Config.Discord.WebhookAvatar,
			["message_text"] = Config.Discord.MessageText,
			["embed_title"] = Config.Discord.EmbedTitle,
			["map"] = Server.MapName,
			["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
			["time"] = DateTime.Now.ToString("HH:mm:ss"),
			["timedate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			["length"] = $"{demoLength.Minutes:00}:{demoLength.Seconds:00}",
			["round"] = (GameRules()?.GameRules?.TotalRoundsPlayed + 1)?.ToString() ?? "Unknown",
			["mega_link"] = "Not uploaded to Mega.",
			["ftp_link"] = "Not uploaded to FTP.",
			["requester_name"] = string.Join(", ", Requesters.Select(x => x.name)),
			["requester_steamid"] = string.Join(", ", Requesters.Select(x => x.steamid)),
			["requester_both"] = string.Join("\n", Requesters.Select(x => $"{x.name} ({x.steamid})")),
			["requester_count"] = Requesters.Count.ToString(),
			["player_count"] = PlayerCount().ToString(),
			["server_name"] = ConVar.Find("hostname")?.StringValue ?? "Unknown Server",
			["fileName"] = Path.GetFileNameWithoutExtension(fileName),
			["iso_timestamp"] = DateTime.UtcNow.ToString("o"),
			["file_size_warning"] = "",
			["fileSizeInKB"] = "0"
		};

		CSSThread.RunOnMainThread(async () =>
		{
			long fileSizeInBytes = 0;
			try
			{
				if (!await FileManager.ZipDemoAsync(demoPath, zipPath, Logger))
					return;

				fileSizeInBytes = new FileInfo(zipPath).Length;
				long fileSizeInKB = fileSizeInBytes / 1024;
				placeholders["fileSizeInKB"] = fileSizeInKB.ToString();

				if (Config.Ftp.Enabled && !string.IsNullOrEmpty(Config.Ftp.Host) && !string.IsNullOrEmpty(Config.Ftp.Username) && !string.IsNullOrEmpty(Config.Ftp.Password) && uploadService != null)
				{
					string remoteFilePath = ReplacePlaceholdersForFileName(Path.Combine(Config.Ftp.RemoteDirectory, Path.GetFileName(zipPath)).Replace("\\", "/"), Path.GetFileName(zipPath));
					string ftpLink = await uploadService.UploadToFtpAsync(zipPath, remoteFilePath);
					placeholders["ftp_link"] = ftpLink;
					if (Config.Ftp.RetentionEnabled)
						AppendRetentionRecord("ftp", remoteFilePath);
				}

				if (Config.Mega.Enabled && !string.IsNullOrEmpty(Config.Mega.Email) && !string.IsNullOrEmpty(Config.Mega.Password) && uploadService != null)
				{
					var (megaLink, megaNodeId) = await uploadService.UploadToMegaAsync(zipPath);
					placeholders["mega_link"] = megaLink;
					if (Config.Mega.RetentionEnabled && !string.IsNullOrEmpty(megaNodeId))
						AppendRetentionRecord("mega", megaNodeId);
				}

				string payloadTemplatePath = Path.Combine(ModuleDirectory, "payload.json");
				if (!File.Exists(payloadTemplatePath))
				{
					Logger.LogError($"Payload template not found: {payloadTemplatePath}");
					return;
				}

				string payloadTemplate = await File.ReadAllTextAsync(payloadTemplatePath);
				if (fileSizeInBytes / (1024 * 1024) > maxFileSizeInMB)
				{
					Logger.LogWarning($"Zip file size ({fileSizeInBytes / (1024 * 1024)}MB) exceeds Discord's {maxFileSizeInMB}MB limit.");
					placeholders["file_size_warning"] = $"⚠️ File size ({fileSizeInBytes / (1024 * 1024)}MB) exceeds Discord limit. Please use Mega or FTP link.";
				}

				string payloadJson = ReplacePlaceholders(payloadTemplate, placeholders);
				if (!string.IsNullOrWhiteSpace(Config.Discord.WebhookURL))
				{
					using var httpClient = new HttpClient();
					var content = new MultipartFormDataContent
					{
						{ new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json" }
					};

					if (File.Exists(zipPath) && (fileSizeInBytes / (1024 * 1024) <= maxFileSizeInMB) && Config.Discord.WebhookUploadFile)
					{
						content.Add(new ByteArrayContent(await File.ReadAllBytesAsync(zipPath)), "file", $"{fileName}.zip");
					}

					var response = await httpClient.PostAsync(Config.Discord.WebhookURL, content);
					response.EnsureSuccessStatusCode();

					if (Config.General.LogUploads)
						Logger.LogInformation($"Demo uploaded via Discord: {fileName}");
				}
				else if (Config.General.LogUploads)
				{
					Logger.LogInformation($"Demo processed (no Discord webhook configured): {fileName}");
				}

				if (Config.General.DeleteDemoAfterUpload)
					await FileManager.DeleteFileAsync(demoPath, Logger, Config.General.LogDeletions);

				if (Config.General.DeleteZippedDemoAfterUpload)
					await FileManager.DeleteFileAsync(zipPath, Logger, Config.General.LogDeletions);

				if (Config.Database.Enable && databaseService != null && (placeholders["mega_link"] != "Not uploaded to Mega." || placeholders["ftp_link"] != "Not uploaded to FTP."))
				{
					await databaseService.StoreDemoRecordAsync(placeholders);
				}
			}
			catch (HttpRequestException ex)
			{
				Logger.LogError($"Error during Discord upload: {ex.Message}");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Unexpected error in ProcessUpload: {ex.Message}");
			}
		});
	}

	private void ResetVariables()
	{
		DemoRequestedThisRound = false;
		DemoStartTime = 0.0;
		fileName = null;
	}

	private static string ReplacePlaceholders(string input, Dictionary<string, string> placeholders)
	{
		foreach (var kv in placeholders)
		{
			// Replace newlines in placeholder values with escaped newlines for Discord JSON
			string value = kv.Value.Replace("\r\n", "\\n").Replace("\n", "\\n");
			input = input.Replace($"{{{kv.Key}}}", value);
		}

		return input;
	}

	private static string ReplacePlaceholdersForFileName(string pattern, string baseFileName)
	{
		var placeholders = new Dictionary<string, string>
		{
			["fileName"] = baseFileName,
			["map"] = Server.MapName,
			["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
			["time"] = DateTime.Now.ToString("HH-mm-ss"),
			["timestamp"] = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
			["round"] = (GameRules()?.GameRules?.TotalRoundsPlayed + 1)?.ToString() ?? "Unknown",
			["playerCount"] = PlayerCount().ToString()
		};
		return ReplacePlaceholders(pattern, placeholders);
	}

	public void Command_DemoRequest(CCSPlayerController? player, CommandInfo info)
	{
		if (Config.DemoRequest.PrintAll)
		{
			if (!DemoRequestedThisRound)
				Server.PrintToChatAll($" {Localizer["k4.general.prefix"]} {Localizer["k4.chat.demo.request.all", player?.PlayerName ?? "Server"]}");
		}
		else
		{
			info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.chat.demo.request.self"]}");
		}

		if (player?.IsValid == true && !Requesters.Contains((player.PlayerName, player.SteamID)))
			Requesters.Add((player.PlayerName, player.SteamID));

		DemoRequestedThisRound = true;
	}

	public void OnConfigParsed(PluginConfig config)
	{
		if (config.Version < Config.Version)
			Logger.LogWarning("Config version mismatch (Expected: {0} | Current: {1})", Config.Version, config.Version);

		if (string.IsNullOrWhiteSpace(config.General.DemoDirectory))
		{
			Logger.LogWarning("DemoDirectory is empty, using default 'discord_demos'");
			config.General.DemoDirectory = "discord_demos";
		}

		string fullDemoPath = Path.Combine(Server.GameDirectory, "csgo", config.General.DemoDirectory);
		try
		{
			Directory.CreateDirectory(fullDemoPath);
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to create demo directory: {ex.Message}");
			config.General.DemoDirectory = "discord_demos";
		}

		if (config.DemoRequest.Enabled)
		{
			config.AutoRecord.Enabled = true;
			config.AutoRecord.CropRounds = true;
		}

		if (config.AutoRecord.CropRounds && !config.AutoRecord.Enabled)
			Logger.LogWarning("AutoRecord.CropRounds enabled but AutoRecord is disabled. CropRounds will not work.");

		if (string.IsNullOrEmpty(config.Discord.WebhookURL))
			Logger.LogWarning("Discord.WebhookURL is not set. Discord upload will be skipped.");

		if (config.AutoRecord.StopOnIdle && config.AutoRecord.IdleTimeSeconds <= 0)
			Logger.LogWarning("AutoRecord.IdleTimeSeconds must be greater than 0 when StopOnIdle is enabled.");

		if (Config.Database.Enable)
			databaseService = new DatabaseService(config.Database, Logger);

		this.Config = config;
	}

	private List<UploadRetentionRecord> LoadRetentionRecords()
	{
		if (!File.Exists(RetentionFilePath))
			return new List<UploadRetentionRecord>();
		var json = File.ReadAllText(RetentionFilePath);
		return JsonSerializer.Deserialize<List<UploadRetentionRecord>>(json) ?? new List<UploadRetentionRecord>();
	}

	private void SaveRetentionRecords(List<UploadRetentionRecord> records)
	{
		var json = JsonSerializer.Serialize(records);
		File.WriteAllText(RetentionFilePath, json);
	}

	private void AppendRetentionRecord(string service, string identifier)
	{
		var records = LoadRetentionRecords();
		records.Add(new UploadRetentionRecord(service, identifier, DateTime.Now));
		SaveRetentionRecords(records);
	}

	private async Task CleanFtpRetention()
	{
		var records = LoadRetentionRecords();
		var currentTime = DateTime.Now;
		var recordsToRemove = new List<UploadRetentionRecord>();

		using var ftpClient = new AsyncFtpClient(Config.Ftp.Host, Config.Ftp.Username, Config.Ftp.Password, Config.Ftp.Port);

		ftpClient.Config.EncryptionMode = Config.Ftp.UseSftp ? FtpEncryptionMode.Implicit : FtpEncryptionMode.None;
		ftpClient.Config.ValidateAnyCertificate = true;

		await ftpClient.AutoConnect();

		var expiredRecords = records.Where(r => r.Service == "ftp" && (currentTime - r.UploadedAt).TotalHours >= Config.Ftp.RetentionHours);

		foreach (var record in expiredRecords)
		{
			try
			{
				await ftpClient.DeleteFile(record.Identifier);
				recordsToRemove.Add(record);

				if (Config.General.LogDeletions)
					Logger.LogInformation($"Deleted FTP file {record.Identifier} due to retention policy.");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error deleting FTP file {record.Identifier}: {ex.Message}");
			}
		}

		await ftpClient.Disconnect();

		if (recordsToRemove.Count != 0)
		{
			SaveRetentionRecords(records.Except(recordsToRemove).ToList());
		}
	}

	private async Task CleanMegaRetention()
	{
		var records = LoadRetentionRecords();
		var now = DateTime.Now;
		var toRemove = new List<UploadRetentionRecord>();
		var client = new MegaApiClient();

		await client.LoginAsync(Config.Mega.Email, Config.Mega.Password);

		var expiredRecords = records.Where(r => r.Service == "mega" && (now - r.UploadedAt).TotalHours >= Config.Mega.RetentionHours);

		foreach (var r in expiredRecords)
		{
			try
			{
				var nodes = await client.GetNodesAsync();
				var node = nodes.SingleOrDefault(n => n.Id.ToString() == r.Identifier);

				if (node != null)
					await client.DeleteAsync(node);

				toRemove.Add(r);

				if (Config.General.LogDeletions)
					Logger.LogInformation($"Deleted Mega node {r.Identifier} due to retention policy.");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error deleting Mega node {r.Identifier}: {ex.Message}");
			}
		}

		if (toRemove.Count != 0)
			SaveRetentionRecords(records.Except(toRemove).ToList());
	}

	public static int PlayerCount()
		=> Utilities.GetPlayers().Count(p => p?.IsValid == true && !p.IsBot && !p.IsHLTV);

	public static CCSGameRulesProxy? GameRules()
		=> Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
}
