using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CG.Web.MegaApiClient;

namespace K4ryuuCS2GOTVDiscord
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("prefix")]
		public string Prefix { get; set; } = "{silver}[ {lime}K4-Demo {silver}]";

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

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 4;

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

			[JsonPropertyName("use-timestamped-filename")]
			public bool UseTimestampedFilename { get; set; } = true;
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

			[JsonPropertyName("embed-description")]
			public string EmbedDescription { get; set; } = "Match demo details:\nMap: {map}\nRecording Date: {date}\nRecording Time: {time}\nRecording Timedate: {timedate}\nDemo Length: {length}\nRound: {round}\nMega URL: {mega_link}";

			[JsonPropertyName("embed-color")]
			public int EmbedColor { get; set; } = 3447003;

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
	}

	[MinimumApiVersion(227)]
	public class CS2GOTVDiscordPlugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		public override string ModuleName => "CS2 GOTV Discord";
		public override string ModuleVersion => "1.2.0";
		public override string ModuleAuthor => "K4ryuu";

		public required PluginConfig Config { get; set; } = new PluginConfig();
		public string? fileName = null;
		public double LastPlayerCheckTime;
		public bool DemoRequestedThisRound;
		public CounterStrikeSharp.API.Modules.Timers.Timer? reservedTimer = null;
		public double DemoStartTime;
		public bool IsPluginExecution = false;

		public override void Load(bool hotReload)
		{
			AddCommandListener("tv_record", CommandListener_Record);
			AddCommandListener("tv_stoprecord", CommandListener_StopRecord);

			RegisterListener<Listeners.OnMapStart>((mapName) =>
			{
				AddTimer(1.0f, () =>
				{
					if (Config.AutoRecord.Enabled)
						Server.ExecuteCommand("tv_record");
				});
			});

			RegisterListener<Listeners.OnMapEnd>(() =>
			{
				Server.ExecuteCommand("tv_stoprecord");
			});

			RegisterEventHandler((EventRoundStart @event, GameEventInfo info) =>
			{
				if (Config.AutoRecord.Enabled && Config.AutoRecord.CropRounds)
				{
					Server.ExecuteCommand("tv_stoprecord");
					Server.NextWorldUpdate(() => Server.ExecuteCommand("tv_record"));
				}

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
		}

		private HookResult CommandListener_Record(CCSPlayerController? player, CommandInfo info)
		{
			if (!IsPluginExecution)
			{
				IsPluginExecution = true;

				DemoStartTime = Server.EngineTime;
				fileName = string.IsNullOrEmpty(info.ArgString) ? "demo" : info.ArgString;

				if (Config.AutoRecord.Enabled && Config.AutoRecord.CropRounds)
					fileName = $"{fileName}-{Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules?.TotalRoundsPlayed + 1}";

				if (Config.General.UseTimestampedFilename)
					fileName = $"{fileName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

				Server.ExecuteCommand($"tv_record \"discord_demos/{fileName}.dem\"");
			}
			else
				IsPluginExecution = false;

			return HookResult.Continue;
		}

		private HookResult CommandListener_StopRecord(CCSPlayerController? player, CommandInfo info)
		{
			string demoPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.dem");

			if (Config.DemoRequest.Enabled && !DemoRequestedThisRound)
			{
				if (Config.DemoRequest.DeleteUnused)
				{
					Task.Run(() =>
					{
						while (IsFileLocked(demoPath))
							Thread.Sleep(1000);

						File.Delete(demoPath);
					});
				}

				return HookResult.Continue;
			}

			if (DemoStartTime != 0.0 && (Server.EngineTime - DemoStartTime) > Config.General.MinimumDemoDuration)
			{
				string zipPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.zip");

				var demoLength = TimeSpan.FromSeconds(Server.EngineTime - DemoStartTime);
				var placeholderValues = new Dictionary<string, string>
				{
					{ "map", Server.MapName },
					{ "date", DateTime.Now.ToString("yyyy-MM-dd") },
					{ "time", DateTime.Now.ToString("HH:mm:ss") },
					{ "timedate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
					{ "length", $"{demoLength.Minutes:00}:{demoLength.Seconds:00}" },
					{ "round", (Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules?.TotalRoundsPlayed + 1)?.ToString() ?? "Unknown" },
					{ "mega_link", "Not uploaded to mega." },
				};

				Task.Run(async () =>
				{
					try
					{
						while (IsFileLocked(demoPath))
							Thread.Sleep(1000);

						using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
						{
							archive.CreateEntryFromFile(demoPath, Path.GetFileName(demoPath), CompressionLevel.Optimal);
						}

						if (!string.IsNullOrEmpty(Config.Mega.Email) && !string.IsNullOrEmpty(Config.Mega.Password))
						{
							var client = new MegaApiClient();
							client.Login(Config.Mega.Email, Config.Mega.Password);

							var rootNode = client.GetNodes().Single(x => x.Type == NodeType.Root);
							var uploadedNode = await client.UploadFileAsync(zipPath, rootNode);
							var downloadLink = client.GetDownloadLink(uploadedNode).ToString();

							placeholderValues["mega_link"] = downloadLink;
						}

						var description = Config.Discord.EmbedDescription;
						foreach (var placeholder in placeholderValues)
						{
							description = description.Replace($"{{{placeholder.Key}}}", placeholder.Value);
						}

						var webhookData = new
						{
							username = Config.Discord.WebhookName,
							avatar_url = Config.Discord.WebhookAvatar,
							content = Config.Discord.MessageText,
							embeds = new[]
							{
								new
								{
									title = Config.Discord.EmbedTitle,
									description,
									color = Config.Discord.EmbedColor
								}
							}
						};

						using (var httpClient = new HttpClient())
						using (var content = new MultipartFormDataContent())
						{
							content.Add(new StringContent(JsonSerializer.Serialize(webhookData), Encoding.UTF8, "application/json"), "payload_json");
							if (Config.Discord.WebhookUploadFile)
							{
								content.Add(new ByteArrayContent(File.ReadAllBytes(zipPath)), "file", $"{fileName}.zip");
							}

							var response = await httpClient.PostAsync(Config.Discord.WebhookURL, content);
							response.EnsureSuccessStatusCode();

							Server.NextWorldUpdate(() =>
							{
								if (Config.General.LogUploads)
									base.Logger.LogInformation($"Demo uploaded successfully: {fileName}");

								if (Config.General.DeleteDemoAfterUpload)
									File.Delete(demoPath);

								if (Config.General.DeleteZippedDemoAfterUpload)
									File.Delete(zipPath);
							});
						}
					}
					catch (Exception ex)
					{
						Server.NextWorldUpdate(() => base.Logger.LogError($"Error processing demo: {ex.Message}"));
					}
				});
			}

			DemoStartTime = 0.0;
			fileName = null;
			DemoRequestedThisRound = false;
			return HookResult.Continue;
		}

		public void Command_DemoRequest(CCSPlayerController? player, CommandInfo info)
		{
			DemoRequestedThisRound = true;

			if (Config.DemoRequest.PrintAll)
			{
				if (!DemoRequestedThisRound)
					Server.PrintToChatAll($" {Config.Prefix} {Localizer["k4.chat.demo.request.all", player?.PlayerName ?? "Server"]}");
			}
			else
				info.ReplyToCommand($" {Config.Prefix} {Localizer["k4.chat.demo.request.self"]}");
		}

		public void OnConfigParsed(PluginConfig config)
		{
			if (config.Version < Config.Version)
				base.Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", this.Config.Version, config.Version);

			if (config.AutoRecord.CropRounds && !config.AutoRecord.Enabled)
				base.Logger.LogWarning("AutoRecord.CropRounds is enabled but AutoRecord.Enabled is disabled. AutoRecord.CropRounds will not work without AutoRecord.Enabled enabled.");

			if (string.IsNullOrEmpty(config.Discord.WebhookURL))
				base.Logger.LogWarning("Discord.WebhookURL is not set. Plugin will not function without a valid webhook URL.");

			if (config.AutoRecord.StopOnIdle && config.AutoRecord.IdleTimeSeconds <= 0)
				base.Logger.LogWarning("AutoRecord.IdleTimeSeconds must be greater than 0 when AutoRecord.StopOnIdle is enabled.");

			this.Config = config;
		}

		public bool IsFileLocked(string filePath)
		{
			try
			{
				using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					stream.Close();
				}
			}
			catch (IOException)
			{
				return true;
			}

			return false;
		}

		public int GetPlayerCount()
		{
			return Utilities.GetPlayers().Count(p => p?.IsValid == true && !p.IsBot && !p.IsHLTV && p.Connected == PlayerConnectedState.PlayerConnected);
		}
	}
}
