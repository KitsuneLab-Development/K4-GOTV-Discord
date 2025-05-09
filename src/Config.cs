using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace K4GOTV;

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

	public DatabaseSettings Database { get; set; } = new DatabaseSettings();

	[JsonPropertyName("ConfigVersion")]
	public override int Version { get; set; } = 11;

	public class GeneralSettings
	{
		[JsonPropertyName("minimum-demo-duration")]
		public float MinimumDemoDuration { get; set; } = 5.0f;

		[JsonPropertyName("delete-demo-after-upload")]
		public bool DeleteDemoAfterUpload { get; set; } = true;

		[JsonPropertyName("delete-zipped-demo-after-upload")]
		public bool DeleteZippedDemoAfterUpload { get; set; } = true;

		[JsonPropertyName("delete-every-demo-from-server-after-server-start")]
		public bool DeleteEveryDemoFromServerAfterServerStart { get; set; } = false;

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

		[JsonPropertyName("demo-directory")]
		public string DemoDirectory { get; set; } = "discord_demos";

		[JsonPropertyName("auto-cleanup-enabled")]
		public bool AutoCleanupEnabled { get; set; } = false;

		[JsonPropertyName("auto-cleanup-interval-minutes")]
		public int AutoCleanupIntervalMinutes { get; set; } = 60;

		[JsonPropertyName("auto-cleanup-file-age-hours")]
		public int AutoCleanupFileAgeHours { get; set; } = 48;
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

		[JsonPropertyName("server-boost")]
		public int ServerBoost { get; set; } = 0;
	}

	public class AutoRecordSettings
	{
		[JsonPropertyName("enabled")]
		public bool Enabled { get; set; } = false;

		[JsonPropertyName("crop-rounds")]
		public bool CropRounds { get; set; } = false;

		[JsonPropertyName("stop-on-idle")]
		public bool StopOnIdle { get; set; } = false;

		[JsonPropertyName("record-warmup")]
		public bool RecordWarmup { get; set; } = true;

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

	public class DatabaseSettings
	{
		[JsonPropertyName("enabled")]
		public bool Enable { get; set; } = false;

		[JsonPropertyName("host")]
		public string Host { get; set; } = "localhost";

		[JsonPropertyName("port")]
		public uint Port { get; set; } = 3306;

		[JsonPropertyName("name")]
		public string Name { get; set; } = "";

		[JsonPropertyName("username")]
		public string Username { get; set; } = "";

		[JsonPropertyName("password")]
		public string Password { get; set; } = "";

		[JsonPropertyName("ssl-mode")]
		public string Sslmode { get; set; } = "preferred";

		[JsonPropertyName("table_prefix")]
		public string Table_prefix { get; set; } = "";
	}
}