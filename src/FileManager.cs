using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace K4GOTV;

public static class FileManager
{
	public static async Task<bool> ZipDemoAsync(string demoPath, string zipPath, ILogger logger)
	{
		int retryCount = 5;
		int delayMilliseconds = 2000;
		bool isFileReady = false;

		while (retryCount > 0 && !isFileReady)
		{
			try
			{
				using FileStream fs = new FileStream(demoPath, FileMode.Open, FileAccess.Read, FileShare.None);
				isFileReady = true;
			}
			catch (IOException)
			{
				retryCount--;
				await Task.Delay(delayMilliseconds);
			}
		}

		if (!isFileReady)
		{
			logger.LogError($"Failed to access file: {demoPath}");
			return false;
		}

		try
		{
			using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
			archive.CreateEntryFromFile(demoPath, Path.GetFileName(demoPath), CompressionLevel.Fastest);
			return true;
		}
		catch (Exception ex)
		{
			logger.LogError($"Error occurred during compression: {ex.Message}");
			return false;
		}
	}

	public static async Task DeleteFileAsync(string path, ILogger logger, bool logDeletion = true)
	{
		if (!File.Exists(path))
		{
			logger.LogWarning($"File not found for deletion: {path}");
			return;
		}

		int retryCount = 0;
		const int maxRetries = 10;

		while (retryCount < maxRetries)
		{
			try
			{
				// Try to open the file to check if it's locked
				using (FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					// File is open, we can close the stream now
				}

				// Now that we've confirmed the file is accessible and the stream is closed, delete it
				File.Delete(path);

				if (logDeletion)
					logger.LogInformation($"File successfully deleted: {path}");
				return;
			}
			catch (IOException)
			{
				retryCount++;

				if (retryCount < maxRetries)
				{
					// Incremental delay: 1s, 3s, 5s, 7s, 9s, 11s, 13s, 15s, 17s
					int delayMs = 1000 + (retryCount * 2000);
					await Task.Delay(delayMs);
				}
				else
				{
					logger.LogError($"Failed to delete file after {maxRetries} attempts: {path}");
				}
			}
			catch (Exception ex)
			{
				logger.LogError($"Error occurred while deleting file ({path}): {ex.Message}");
				return;
			}
		}
	}
}