using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using FluentFTP;
using System.Text;
using System.IO.Compression;
using CG.Web.MegaApiClient;
using FluentFTP.Exceptions;
using CounterStrikeSharp.API.Modules.Cvars;

namespace K4ryuuCS2GOTVDiscord
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("general")]
		public GeneralSettings General { get; set; } = new GeneralSettings();

		[JsonPropertyName("discord")]
		public DiscordSettings Discord { get; set; } = new DiscordSettings();

		[JsonPropertyName("auto-record")]
		public AutoRecordSettings AutoRecord { get; set; } = new AutoRecordSettings();

		[JsonPropertyName("mega")]
		public MegaSettings Mega { get; set; } = new MegaSettings();

		[JsonPropertyName("demo-request")]
		public DemoRequestSettings DemoRequest { get; set; } = new DemoRequestSettings();

		[JsonPropertyName("ftp")]
		public FtpSettings Ftp { get; set; } = new FtpSettings();

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 9;

		public class GeneralSettings
		{
			[JsonPropertyName("minimum-demo-duration")]
			public float MinimumDemoDuration { get; set; } = 5.0f;

			[JsonPropertyName("delete-demo-after-upload")]
			public bool DeleteDemoAfterUpload { get; set; } = true;

			[JsonPropertyName("delete-zipped-demo-after-upload")]
			public bool DeleteZippedDemoAfterUpload { get; set; } = true;

			[JsonPropertyName("log-uploads")]
			public bool LogUploads { get; set; } = true;

			[JsonPropertyName("log-deletions")]
			public bool LogDeletions { get; set; } = true;

			[JsonPropertyName("default-file-name")]
			public string DefaultFileName { get; set; } = "demo";

			[JsonPropertyName("regular-file-naming-pattern")]
			public string RegularFileNamingPattern { get; set; } = "{fileName}_{map}_{date}_{time}";

			[JsonPropertyName("crop-rounds-file-naming-pattern")]
			public string CropRoundsFileNamingPattern { get; set; } = "{fileName}_{map}_round{round}_{date}_{time}";

		}

		public class DiscordSettings
		{
			[JsonPropertyName("webhook-url")]
			public string WebhookURL { get; set; } = "";

			[JsonPropertyName("webhook-avatar")]
			public string WebhookAvatar { get; set; } = "";

			[JsonPropertyName("webhook-upload-file")]
			public bool WebhookUploadFile { get; set; } = true;

			[JsonPropertyName("webhook-name")]
			public string WebhookName { get; set; } = "CSGO Demo Bot";

			[JsonPropertyName("embed-title")]
			public string EmbedTitle { get; set; } = "New CSGO Demo Available";

			[JsonPropertyName("message-text")]
			public string MessageText { get; set; } = "@everyone New CSGO Demo Available!";
		}

		public class AutoRecordSettings
		{
			[JsonPropertyName("enabled")]
			public bool Enabled { get; set; } = false;

			[JsonPropertyName("crop-rounds")]
			public bool CropRounds { get; set; } = false;

			[JsonPropertyName("stop-on-idle")]
			public bool StopOnIdle { get; set; } = false;

			[JsonPropertyName("idle-player-count-threshold")]
			public int IdlePlayerCountThreshold { get; set; } = 0;

			[JsonPropertyName("idle-time-seconds")]
			public int IdleTimeSeconds { get; set; } = 300;
		}

		public class MegaSettings
		{
			[JsonPropertyName("enabled")]
			public bool Enabled { get; set; } = false;

			[JsonPropertyName("email")]
			public string Email { get; set; } = "";

			[JsonPropertyName("password")]
			public string Password { get; set; } = "";
		}

		public class DemoRequestSettings
		{
			[JsonPropertyName("enabled")]
			public bool Enabled { get; set; } = false;

			[JsonPropertyName("print-all")]
			public bool PrintAll { get; set; } = true;

			[JsonPropertyName("delete-unused")]
			public bool DeleteUnused { get; set; } = true;
		}

		public class FtpSettings
		{
			[JsonPropertyName("enabled")]
			public bool Enabled { get; set; } = false;

			[JsonPropertyName("host")]
			public string Host { get; set; } = "";

			[JsonPropertyName("port")]
			public int Port { get; set; } = 21;

			[JsonPropertyName("username")]
			public string Username { get; set; } = "";

			[JsonPropertyName("password")]
			public string Password { get; set; } = "";

			[JsonPropertyName("remote-directory")]
			public string RemoteDirectory { get; set; } = "/";

			[JsonPropertyName("use-sftp")]
			public bool UseSftp { get; set; } = false;
		}
	}

	[MinimumApiVersion(250)]
	public class CS2GOTVDiscordPlugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "CS2 GOTV Discord";
		public override string ModuleVersion => "1.3.1";
		public override string ModuleAuthor => "K4ryuu @ KitsuneLab";

		public required PluginConfig Config { get; set; } = new PluginConfig();
		public string? fileName = null;
		public double LastPlayerCheckTime;
		public bool DemoRequestedThisRound = false;
		public List<(string name, ulong steamid)> Requesters = [];
		public CounterStrikeSharp.API.Modules.Timers.Timer? reservedTimer = null;
		public double DemoStartTime = 0.0;
		public bool IsPluginExecution = false;
		public bool PluginRecording = false;

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
				if (!string.IsNullOrEmpty(fileName) && DemoStartTime != 0.0)
				{
					Server.ExecuteCommand("tv_stoprecord");
				}
			});

			RegisterEventHandler((EventRoundStart @event, GameEventInfo info) =>
			{
				if (Config.AutoRecord.Enabled && Config.AutoRecord.CropRounds)
				{
					if (DemoStartTime != 0.0)
						Server.ExecuteCommand("tv_stoprecord");

					Requesters.Clear();
					Server.NextWorldUpdate(() => Server.ExecuteCommand("tv_record \"autodemo\""));
				}

				return HookResult.Continue;
			});

			RegisterEventHandler((EventPlayerActivate @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player?.IsValid == true && !player.IsBot && !player.IsHLTV)
					LastPlayerCheckTime = Server.EngineTime;

				if (!PluginRecording && Config.AutoRecord.Enabled)
					Server.ExecuteCommand("tv_record");
				return HookResult.Continue;
			});

			Directory.CreateDirectory(Path.Combine(Server.GameDirectory, "csgo", "discord_demos"));

			if (Config.DemoRequest.Enabled)
				AddCommand($"css_demo", "Request a demo upload at the end of the round", Command_DemoRequest);

			if (Config.AutoRecord.StopOnIdle)
			{
				reservedTimer = AddTimer(1.0f, () =>
				{
					if (DemoStartTime == 0.0)
						return;

					if (GetPlayerCount() < Config.AutoRecord.IdlePlayerCountThreshold)
					{
						double idleTime = Server.EngineTime - LastPlayerCheckTime;
						if (idleTime > Config.AutoRecord.IdleTimeSeconds)
						{
							Server.ExecuteCommand("tv_stoprecord");
							base.Logger.LogInformation($"Recording stopped due to idle time exceeding {Config.AutoRecord.IdleTimeSeconds} seconds with player count < {Config.AutoRecord.IdlePlayerCountThreshold}.");
						}
					}
					else
					{
						LastPlayerCheckTime = Server.EngineTime;
					}
				});
			}

			if (Config.AutoRecord.Enabled && hotReload)
				Server.ExecuteCommand("tv_record \"autodemo\"");
		}

		public override void Unload(bool hotReload)
		{
			Server.ExecuteCommand("tv_stoprecord");
		}

		private HookResult CommandListener_Changelevel(CCSPlayerController? player, CommandInfo info)
		{
			if (!string.IsNullOrEmpty(fileName) && DemoStartTime != 0.0)
			{
				Server.ExecuteCommand("tv_stoprecord");
			}
			return HookResult.Continue;
		}

		private HookResult CommandListener_Record(CCSPlayerController? player, CommandInfo info)
		{
			if (!Config.AutoRecord.Enabled)
				return HookResult.Continue;

			if (PluginRecording)
				return HookResult.Continue;

			if (!IsPluginExecution)
			{
				IsPluginExecution = true;

				DemoStartTime = Server.EngineTime;

				string fileNameArgument = info.ArgString.Trim().Replace("\"", "");
				string baseFileName = string.IsNullOrEmpty(fileNameArgument) ? Config.General.DefaultFileName : fileNameArgument;

				string pattern = Config.AutoRecord.Enabled && Config.AutoRecord.CropRounds
					? Config.General.CropRoundsFileNamingPattern
					: Config.General.RegularFileNamingPattern;

				fileName = ReplacePlaceholdersForFileName(pattern, baseFileName);

				// Ensure unique filename
				string fullPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.dem");
				int counter = 1;
				while (File.Exists(fullPath))
				{
					fileName = $"{fileName}_{counter}";
					fullPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.dem");
					counter++;
				}

				Server.ExecuteCommand($"tv_record \"discord_demos/{fileName}.dem\"");
				return HookResult.Stop;
			}
			else
			{
				PluginRecording = true;

				IsPluginExecution = false;
				return HookResult.Continue;
			}
		}

		private HookResult CommandListener_StopRecord(CCSPlayerController? player, CommandInfo info)
		{
			if (!PluginRecording)
				return HookResult.Continue;

			PluginRecording = false;

			Logger.LogInformation("Recording stopped. Filename: {0}", fileName);

			if (string.IsNullOrEmpty(fileName))
			{
				ResetVariables();
				return HookResult.Continue;
			}

			string demoPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.dem");

			if (!File.Exists(demoPath))
			{
				Logger.LogError($"Demo file not found: {demoPath} - Recording stopped without processing.");
				return HookResult.Continue;
			}

			if (Config.DemoRequest.Enabled && !DemoRequestedThisRound)
			{
				if (Config.DemoRequest.DeleteUnused)
					Task.Run(() => DeleteFileAsync(demoPath));

				ResetVariables();
				return HookResult.Continue;
			}

			if (DemoStartTime != 0.0 && (Server.EngineTime - DemoStartTime) > Config.General.MinimumDemoDuration && !string.IsNullOrEmpty(fileName))
			{
				ProcessUpload(fileName, demoPath);
			}

			ResetVariables();
			return HookResult.Continue;
		}

		public void ProcessUpload(string fileName, string demoPath)
		{
			string zipPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.zip");

			var demoLength = TimeSpan.FromSeconds(Server.EngineTime - DemoStartTime);
			var placeholderValues = new Dictionary<string, string>
			{
				{ "webhook_name", Config.Discord.WebhookName },
				{ "webhook_avatar", Config.Discord.WebhookAvatar },
				{ "message_text", Config.Discord.MessageText },
				{ "embed_title", Config.Discord.EmbedTitle },
				{ "map", Server.MapName },
				{ "date", DateTime.Now.ToString("yyyy-MM-dd") },
				{ "time", DateTime.Now.ToString("HH:mm:ss") },
				{ "timedate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
				{ "length", $"{demoLength.Minutes:00}:{demoLength.Seconds:00}" },
				{ "round", (GameRules()?.GameRules?.TotalRoundsPlayed + 1)?.ToString() ?? "Unknown" },
				{ "mega_link", "Not uploaded to Mega." },
				{ "ftp_link", "Not uploaded to FTP." },
				{ "requester_name", string.Join(", ", Requesters.Select(x => x.name)) },
				{ "requester_steamid", string.Join(", ", Requesters.Select(x => x.steamid)) },
				{ "requester_both", string.Join("\n", Requesters.Select(x => $"{x.name} ({x.steamid})")) },
				{ "requester_count", Requesters.Count.ToString() },
				{ "player_count", GetPlayerCount().ToString() },
				{ "server_name", ConVar.Find("hostname")?.StringValue ?? "Unknown Server" },
				{ "fileName", Path.GetFileNameWithoutExtension(fileName) },
				{ "iso_timestamp", DateTime.UtcNow.ToString("o") },
				{ "file_size_warning", "" }
			};

			try
			{
				string remoteFilePath = ReplacePlaceholdersForFileName(Path.Combine(Config.Ftp.RemoteDirectory, Path.GetFileName(zipPath)).Replace("\\", "/"), Path.GetFileName(zipPath));

				Task.Run(async () =>
				{
					int retryCount = 5; // Maximum number of retries
					int delayMilliseconds = 2000; // Wait 2 seconds between retries

					bool isFileReady = false;
					while (retryCount > 0 && !isFileReady)
					{
						try
						{
							// Check if the file can be accessed (open and immediately close it)
							using FileStream fs = new FileStream(demoPath, FileMode.Open, FileAccess.Read, FileShare.None);
							isFileReady = true;
						}
						catch (IOException)
						{
							retryCount--;

							// Wait before retrying
							await Task.Delay(delayMilliseconds);
						}
					}

					if (isFileReady)
					{
						try
						{
							// Now that the file is ready, proceed with zipping it
							using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
							archive.CreateEntryFromFile(demoPath, Path.GetFileName(demoPath), CompressionLevel.Fastest);
						}
						catch (Exception ex)
						{
							Logger.LogError($"An error occurred while zipping the file: {ex.Message}");
						}
					}
					else
					{
						Logger.LogError($"Failed to access the file '{demoPath}' after multiple attempts. File is still in use.");
					}

					// Upload to FTP if enabled
					if (Config.Ftp.Enabled && !string.IsNullOrEmpty(Config.Ftp.Host) && !string.IsNullOrEmpty(Config.Ftp.Username) && !string.IsNullOrEmpty(Config.Ftp.Password))
					{
						string ftpLink = await UploadToFtp(zipPath, remoteFilePath);
						placeholderValues["ftp_link"] = ftpLink;
					}

					// Upload to Mega if enabled
					if (Config.Mega.Enabled && !string.IsNullOrEmpty(Config.Mega.Email) && !string.IsNullOrEmpty(Config.Mega.Password))
					{
						var client = new MegaApiClient();
						await client.LoginAsync(Config.Mega.Email, Config.Mega.Password);

						var rootNode = (await client.GetNodesAsync()).Single(x => x.Type == NodeType.Root);
						var uploadedNode = await client.UploadFileAsync(zipPath, rootNode);
						var downloadLink = await client.GetDownloadLinkAsync(uploadedNode);

						placeholderValues["mega_link"] = downloadLink.ToString();
					}

					// Load and process the payload template
					string payloadTemplatePath = Path.Combine(ModuleDirectory, "payload.json");
					if (!File.Exists(payloadTemplatePath))
					{
						Logger.LogError($"Payload template not found at: {payloadTemplatePath}");
						return;
					}

					string payloadTemplate = await File.ReadAllTextAsync(payloadTemplatePath);
					string payloadJson = ReplacePlaceholders(payloadTemplate, placeholderValues);

					using var httpClient = new HttpClient();
					MultipartFormDataContent content = new MultipartFormDataContent();

					// Add the JSON payload
					content.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");

					// Check file size and handle upload
					if (File.Exists(zipPath))
					{
						long fileSizeInBytes = new FileInfo(zipPath).Length;
						long fileSizeInMB = fileSizeInBytes / (1024 * 1024);

						if (fileSizeInMB > 25)
						{
							Logger.LogWarning($"Zip file size ({fileSizeInMB}MB) exceeds Discord's 25MB limit. File will not be uploaded to Discord.");
							placeholderValues["file_size_warning"] = $"⚠️ File size ({fileSizeInMB}MB) exceeds Discord's limit. Use Mega or FTP links to download.";

							// Suggest enabling Mega or FTP if not already enabled
							if (!Config.Mega.Enabled && !Config.Ftp.Enabled)
							{
								Logger.LogWarning("Consider enabling Mega or FTP upload for large files in the configuration.");
							}

							// Regenerate payload JSON with updated placeholders
							payloadJson = ReplacePlaceholders(payloadTemplate, placeholderValues);
							content = new MultipartFormDataContent
							{
								{ new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json" }
							};
						}
						else if (Config.Discord.WebhookUploadFile)
						{
							content.Add(new ByteArrayContent(await File.ReadAllBytesAsync(zipPath)), "file", $"{fileName}.zip");
						}
					}
					else
					{
						Logger.LogWarning($"Zip file not found for upload: {zipPath}");
					}

					// Send the webhook
					var response = await httpClient.PostAsync(Config.Discord.WebhookURL, content);
					response.EnsureSuccessStatusCode();

					if (Config.General.LogUploads)
						Logger.LogInformation($"Demo information uploaded successfully: {fileName}");

					// Clean up files if configured
					if (Config.General.DeleteDemoAfterUpload)
						await DeleteFileAsync(demoPath);

					if (Config.General.DeleteZippedDemoAfterUpload)
						await DeleteFileAsync(zipPath);
				});
			}
			catch (HttpRequestException ex)
			{
				Logger.LogError($"Error uploading to Discord: {ex.Message}");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Unexpected error in ProcessUpload: {ex.Message}");
			}
		}

		private async Task<string> UploadToFtp(string filePath, string remoteFilePath)
		{
			using (var client = new AsyncFtpClient(Config.Ftp.Host, Config.Ftp.Username, Config.Ftp.Password, Config.Ftp.Port))
			{
				try
				{
					client.Config.EncryptionMode = Config.Ftp.UseSftp ? FtpEncryptionMode.Implicit : FtpEncryptionMode.None;
					client.Config.ValidateAnyCertificate = true;

					await client.AutoConnect();

					await client.UploadFile(filePath, remoteFilePath);

					// Generate a download link
					string protocol = Config.Ftp.UseSftp ? "sftp" : "ftp";
					string ftpLink = $"{protocol}://{Config.Ftp.Host}/{remoteFilePath}";
					return ftpLink;
				}
				catch (FtpException ex)
				{
					Logger.LogError($"FTP upload error: {ex.Message}");
					throw;
				}
				finally
				{
					await client.Disconnect();
				}
			}
		}

		public static string ReplacePlaceholders(string input, Dictionary<string, string> placeholders)
		{
			foreach (var placeholder in placeholders)
			{
				input = input.Replace($"{{{placeholder.Key}}}", placeholder.Value);
			}

			return input;
		}

		private async Task DeleteFileAsync(string path)
		{
			if (!File.Exists(path))
			{
				Logger.LogWarning($"File not found for deletion: {path}");
				return;
			}

			int retryCount = 0;
			const int maxRetries = 3;
			const int retryDelayMs = 1000;

			while (retryCount < maxRetries)
			{
				try
				{
					using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
					File.Delete(path);

					if (Config.General.LogDeletions)
						Logger.LogInformation($"File deleted successfully: {path}");
					return;
				}
				catch (IOException)
				{
					retryCount++;

					if (retryCount < maxRetries)
					{
						await Task.Delay(retryDelayMs);
					}
					else
					{
						Logger.LogError($"Failed to delete file after {maxRetries} attempts due to file lock: {path}");
					}
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error deleting file {path}: {ex.Message}");
					return;
				}
			}
		}

		public void ResetVariables()
		{
			DemoRequestedThisRound = false;
			DemoStartTime = 0.0;
			fileName = null;
		}

		private string ReplacePlaceholdersForFileName(string pattern, string baseFileName)
		{
			var placeholders = new Dictionary<string, string>
			{
				{ "fileName", baseFileName },
				{ "map", Server.MapName },
				{ "date", DateTime.Now.ToString("yyyy-MM-dd") },
				{ "time", DateTime.Now.ToString("HH-mm-ss") },
				{ "timestamp", DateTime.Now.ToString("yyyyMMdd_HHmmss") },
				{ "round", (GameRules()?.GameRules?.TotalRoundsPlayed + 1)?.ToString() ?? "Unknown" },
				{ "playerCount", GetPlayerCount().ToString() },
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
				info.ReplyToCommand($" {Localizer["k4.general.prefix"]} {Localizer["k4.chat.demo.request.self"]}");

			if (player?.IsValid == true && !Requesters.Contains((player.PlayerName, player.SteamID)))
				Requesters.Add((player.PlayerName, player.SteamID));

			DemoRequestedThisRound = true;
		}

		public void OnConfigParsed(PluginConfig config)
		{
			if (config.Version < Config.Version)
				base.Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", this.Config.Version, config.Version);

			if (config.DemoRequest.Enabled)
			{
				config.AutoRecord.Enabled = true;
				config.AutoRecord.CropRounds = true;
			}

			if (config.AutoRecord.CropRounds && !config.AutoRecord.Enabled)
				base.Logger.LogWarning("AutoRecord.CropRounds is enabled but AutoRecord.Enabled is disabled. AutoRecord.CropRounds will not work without AutoRecord.Enabled enabled.");

			if (string.IsNullOrEmpty(config.Discord.WebhookURL))
				base.Logger.LogWarning("Discord.WebhookURL is not set. Plugin will not function without a valid webhook URL.");

			if (config.AutoRecord.StopOnIdle && config.AutoRecord.IdleTimeSeconds <= 0)
				base.Logger.LogWarning("AutoRecord.IdleTimeSeconds must be greater than 0 when AutoRecord.StopOnIdle is enabled.");

			if (config.Ftp.Enabled)
			{
				if (string.IsNullOrEmpty(config.Ftp.Host))
					base.Logger.LogWarning("FTP.Host is not set. FTP uploads will not function without a valid host.");
				if (string.IsNullOrEmpty(config.Ftp.Username) || string.IsNullOrEmpty(config.Ftp.Password))
					base.Logger.LogWarning("FTP credentials are not set. FTP uploads may fail without valid credentials.");
			}

			this.Config = config;
		}

		public static bool IsFileLocked(string filePath)
		{
			try
			{
				using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
				stream.Close();
			}
			catch (IOException)
			{
				return true;
			}

			return false;
		}

		public static int GetPlayerCount()
		{
			return Utilities.GetPlayers().Count(p => p?.IsValid == true && !p.IsBot && !p.IsHLTV);
		}

		public static CCSGameRulesProxy? GameRules()
		{
			return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
		}
	}
}