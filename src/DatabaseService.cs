using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using static K4GOTV.Plugin;

namespace K4GOTV;

public class DatabaseService
{
	private readonly PluginConfig.DatabaseSettings _dbConfig;
	private readonly string _connectionString;
	private readonly ILogger _logger;


	public DatabaseService(PluginConfig.DatabaseSettings dbConfig, ILogger logger)
	{
		_dbConfig = dbConfig;

		var builder = new MySqlConnectionStringBuilder
		{
			Server = _dbConfig.Host,
			Database = _dbConfig.Name,
			UserID = _dbConfig.Username,
			Password = _dbConfig.Password,
			Port = _dbConfig.Port,
			SslMode = Enum.TryParse(_dbConfig.Sslmode, true, out MySqlSslMode sslMode) ? sslMode : MySqlSslMode.Preferred,
			CharacterSet = "utf8mb4"
		};

		_connectionString = builder.ConnectionString;
		_logger = logger;

		CSSThread.RunOnMainThread(async () =>
		{
			await this.CreateTableIfNotExistsAsync();
		});
	}

	public async Task StoreDemoRecordAsync(Dictionary<string, string> placeholders)
	{
		try
		{
			string tableName = $"{_dbConfig.Table_prefix}k4_gotv";

			string insertQuery = $@"
			INSERT INTO `{tableName}` (
				map, date, time, timedate, length, round, mega_link, ftp_link,
				requester_name, requester_steamid, requester_count,
				player_count, server_name, file_name, file_size
			) VALUES (
				@map, @date, @time, @timedate, @length, @round, @mega_link, @ftp_link,
				@requester_name, @requester_steamid, @requester_count,
				@player_count, @server_name, @fileName, @fileSizeInKB
			);";

			await using var connection = new MySqlConnection(_connectionString);
			await connection.OpenAsync();
			await connection.ExecuteAsync(insertQuery, new
			{
				map = placeholders["map"],
				date = placeholders["date"],
				time = placeholders["time"],
				timedate = placeholders["timedate"],
				length = placeholders["length"],
				round = placeholders["round"],
				mega_link = placeholders["mega_link"],
				ftp_link = placeholders["ftp_link"],
				requester_name = placeholders["requester_name"],
				requester_steamid = placeholders["requester_steamid"],
				requester_count = int.Parse(placeholders["requester_count"]),
				player_count = int.Parse(placeholders["player_count"]),
				server_name = placeholders["server_name"],
				fileName = placeholders["fileName"],
				fileSizeInKB = int.Parse(placeholders["fileSizeInKB"])
			});

			_logger.LogInformation("Demo URL successfully recorded in the database.");
		}
		catch (Exception ex)
		{
			_logger.LogError($"Failed to write to database: {ex.Message}");
		}
	}

	public async Task CreateTableIfNotExistsAsync()
	{
		try
		{
			string tableName = $"{_dbConfig.Table_prefix}k4_gotv";
			string sql = $@"
				CREATE TABLE IF NOT EXISTS `{tableName}` (
					id BIGINT AUTO_INCREMENT PRIMARY KEY,
					map VARCHAR(255) NOT NULL,
					date DATE NOT NULL COMMENT 'Date in yyyy-MM-dd format',
					time TIME NOT NULL COMMENT 'Time in HH:mm:ss format',
					timedate DATETIME NOT NULL COMMENT 'Datetime in yyyy-MM-dd HH:mm:ss format',
					length VARCHAR(8) NOT NULL,
					round VARCHAR(50) NOT NULL,
					mega_link TEXT NOT NULL,
					ftp_link TEXT NOT NULL,
					requester_name TEXT NOT NULL,
					requester_steamid TEXT NOT NULL,
					requester_count INT NOT NULL,
					player_count INT NOT NULL,
					server_name VARCHAR(255) NOT NULL,
					file_name VARCHAR(255) NOT NULL,
					file_size BIGINT NOT NULL,
					KEY idx_requester_steamid (requester_steamid(50)),
					KEY idx_length (length),
					KEY idx_date_time (date, time),
					KEY idx_map (map),
					KEY idx_map_date_time (map, date, time),
					KEY idx_server_map_date_time (server_name, map, date, time),
					KEY idx_server_name (server_name),
					KEY idx_server_map (server_name, map)
				) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

			await using var connection = new MySqlConnection(_connectionString);
			await connection.OpenAsync();
			await connection.ExecuteAsync(sql);
		}
		catch (Exception ex)
		{
			_logger.LogError($"Failed to create table: {ex.Message}");
		}
	}
}