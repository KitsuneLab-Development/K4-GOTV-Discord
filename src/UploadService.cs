using CG.Web.MegaApiClient;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;

namespace K4GOTV;

public class UploadService
{
	private readonly PluginConfig _config;
	private readonly ILogger _logger;

	public UploadService(PluginConfig config, ILogger logger)
	{
		_config = config;
		_logger = logger;
	}

	public async Task<string> UploadToFtpAsync(string filePath, string remoteFilePath)
	{
		using var client = new AsyncFtpClient(_config.Ftp.Host, _config.Ftp.Username, _config.Ftp.Password, _config.Ftp.Port);
		try
		{
			client.Config.EncryptionMode = _config.Ftp.UseSftp ? FtpEncryptionMode.Implicit : FtpEncryptionMode.None;
			client.Config.ValidateAnyCertificate = true;
			await client.AutoConnect();
			await client.UploadFile(filePath, remoteFilePath);
			string protocol = _config.Ftp.UseSftp ? "sftp" : "ftp";
			return $"{protocol}://{_config.Ftp.Host}/{remoteFilePath}";
		}
		catch (FtpException ex)
		{
			_logger.LogError($"FTP upload error: {ex.Message}");
			throw;
		}
		finally
		{
			await client.Disconnect();
		}
	}

	public async Task<(string Link, string NodeId)> UploadToMegaAsync(string filePath)
	{
		try
		{
			var client = new MegaApiClient();
			await client.LoginAsync(_config.Mega.Email, _config.Mega.Password);
			var rootNode = (await client.GetNodesAsync()).Single(x => x.Type == NodeType.Root);
			var uploadedNode = await client.UploadFileAsync(filePath, rootNode);
			var downloadLink = await client.GetDownloadLinkAsync(uploadedNode);
			return (downloadLink.ToString(), uploadedNode.Id.ToString());
		}
		catch (Exception ex)
		{
			_logger.LogError($"Mega upload error: {ex.Message}");
			return ("Not uploaded to Mega.", string.Empty);
		}
	}
}