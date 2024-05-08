using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace K4ryuuCS2GOTVDiscord
{
	public sealed class PluginConfig : BasePluginConfig
	{
		[JsonPropertyName("minimum-demo-duration")]
		public float MinimumDemoDuration { get; set; } = 5.0f;

		[JsonPropertyName("discord-webhook-url")]
		public string DiscordWebhookURL { get; set; } = "";

		[JsonPropertyName("discord-webhook-avatar")]
		public string DiscordWebhookAvatar { get; set; } = "";

		[JsonPropertyName("discord-webhook-name")]
		public string DiscordWebhookName { get; set; } = "CSGO Demo Bot";

		[JsonPropertyName("discord-embed-title")]
		public string DiscordEmbedTitle { get; set; } = "New CSGO Demo Available";

		[JsonPropertyName("discord-embed-description")]
		public string DiscordEmbedDescription { get; set; } = "Match demo details:\nMap: {map}\nRecording Date: {date}\nRecording Time: {time}\nRecording Timedate: {timedate}\nDemo Length: {length}\nRound: {round}";

		[JsonPropertyName("discord-embed-color")]
		public int DiscordEmbedColor { get; set; } = 3447003;

		[JsonPropertyName("discord-message-text")]
		public string DiscordMessageText { get; set; } = "@everyone New CSGO Demo Available!";

		[JsonPropertyName("delete-demo-after-upload")]
		public bool DeleteDemoAfterUpload { get; set; } = true;

		[JsonPropertyName("delete-zipped-demo-after-upload")]
		public bool DeleteZippedDemoAfterUpload { get; set; } = true;

		[JsonPropertyName("log-uploads")]
		public bool LogUploads { get; set; } = true;

		[JsonPropertyName("ConfigVersion")]
		public override int Version { get; set; } = 1;
	}

	[MinimumApiVersion(227)]
	public class CS2GOTVDiscordPlugin : BasePlugin, IPluginConfig<PluginConfig>
	{
		private double DemoStartTime;
		private bool IsPluginExecution = false;

		public override string ModuleName => "CS2 GOTV Discord";
		public override string ModuleVersion => "1.0.0";
		public override string ModuleAuthor => "K4ryuu";

		public required PluginConfig Config { get; set; } = new PluginConfig();

		public string? fileName = null;

		public override void Load(bool hotReload)
		{
			AddCommandListener("tv_record", CommandListener_Record);
			AddCommandListener("tv_stoprecord", CommandListener_StopRecord);

			RegisterEventHandler<EventCsWinPanelMatch>((@event, info) =>
			{
				Server.ExecuteCommand("tv_stoprecord");
				return HookResult.Continue;
			});

			Directory.CreateDirectory(Path.Combine(Server.GameDirectory, "csgo", "discord_demos"));
		}

		private HookResult CommandListener_Record(CCSPlayerController? player, CommandInfo info)
		{
			if (!IsPluginExecution)
			{
				IsPluginExecution = true;

				DemoStartTime = Server.EngineTime;
				fileName = $"demo-{DemoStartTime}";
				Server.ExecuteCommand($"tv_record \"discord_demos/{fileName}.dem\"");
			}
			else
				IsPluginExecution = false;

			return HookResult.Continue;
		}

		private HookResult CommandListener_StopRecord(CCSPlayerController? player, CommandInfo info)
		{
			if ((Server.EngineTime - DemoStartTime) > Config.MinimumDemoDuration)
			{
				string demoPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.dem");
				string zipPath = Path.Combine(Server.GameDirectory, "csgo", "discord_demos", $"{fileName}.zip");

				var demoLength = TimeSpan.FromSeconds(Server.EngineTime - DemoStartTime);
				var placeholderValues = new Dictionary<string, string>
				{
					{ "map", Server.MapName },
					{ "date", DateTime.Now.ToString("yyyy-MM-dd") },
					{ "time", DateTime.Now.ToString("HH:mm:ss") },
					{ "timedate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
					{ "length", $"{demoLength.Minutes:00}:{demoLength.Seconds:00}" },
					{ "round", (Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules?.TotalRoundsPlayed + 1)?.ToString() ?? "Unknown" }
				};

				var description = Config.DiscordEmbedDescription;
				foreach (var placeholder in placeholderValues)
				{
					description = description.Replace($"{{{placeholder.Key}}}", placeholder.Value);
				}

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

						var webhookData = new
						{
							username = Config.DiscordWebhookName,
							avatar_url = Config.DiscordWebhookAvatar,
							content = Config.DiscordMessageText,
							embeds = new[]
							{
								new
								{
									title = Config.DiscordEmbedTitle,
									description,
									color = Config.DiscordEmbedColor
								}
							}
						};

						using (var httpClient = new HttpClient())
						using (var content = new MultipartFormDataContent())
						{
							content.Add(new StringContent(JsonSerializer.Serialize(webhookData), Encoding.UTF8, "application/json"), "payload_json");
							content.Add(new ByteArrayContent(File.ReadAllBytes(zipPath)), "file", $"{fileName}.zip");

							var response = await httpClient.PostAsync(Config.DiscordWebhookURL, content);
							response.EnsureSuccessStatusCode();

							Server.NextWorldUpdate(() =>
							{
								if (Config.LogUploads)
									base.Logger.LogInformation($"Demo uploaded successfully: {fileName}");

								if (Config.DeleteDemoAfterUpload)
									File.Delete(demoPath);

								if (Config.DeleteZippedDemoAfterUpload)
									File.Delete(zipPath);
							});
						}
					}
					catch (Exception ex)
					{
						base.Logger.LogError($"Error processing demo: {ex.Message}");
					}
				});
			}

			return HookResult.Continue;
		}

		public void OnConfigParsed(PluginConfig config)
		{
			if (config.Version < Config.Version)
				base.Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", this.Config.Version, config.Version);

			this.Config = config;
		}

		private bool IsFileLocked(string filePath)
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
	}
}
