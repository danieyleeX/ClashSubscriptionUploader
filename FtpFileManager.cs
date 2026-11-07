using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenRungClashUploader;

internal sealed record FtpEntry(string Name, bool IsDirectory, long? Size);

internal sealed class FtpFileManager(string host, int port, string user, string password, bool useFtps)
{
    public async Task<IReadOnlyList<FtpEntry>> ListAsync(string directory)
    {
        var request = CreateRequest(directory, WebRequestMethods.Ftp.ListDirectoryDetails);
        using var response = (FtpWebResponse)await request.GetResponseAsync();
        using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
        var entries = new List<FtpEntry>();
        while (await reader.ReadLineAsync() is { } line)
        {
            var entry = ParseListingLine(line);
            if (entry is not null && entry.Name is not ("." or "..")) entries.Add(entry);
        }
        return entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task CreateDirectoryAsync(string path) => await CompleteAsync(CreateRequest(path, WebRequestMethods.Ftp.MakeDirectory));

    public async Task DeleteAsync(string path, bool isDirectory) =>
        await CompleteAsync(CreateRequest(path, isDirectory ? WebRequestMethods.Ftp.RemoveDirectory : WebRequestMethods.Ftp.DeleteFile));

    public async Task RenameAsync(string path, string newPath)
    {
        var request = CreateRequest(path, WebRequestMethods.Ftp.Rename);
        request.RenameTo = "/" + EncodePath(newPath);
        await CompleteAsync(request);
    }

    public async Task DownloadAsync(string remotePath, string localPath)
    {
        var request = CreateRequest(remotePath, WebRequestMethods.Ftp.DownloadFile);
        using var response = (FtpWebResponse)await request.GetResponseAsync();
        await using var input = response.GetResponseStream();
        await using var output = File.Create(localPath);
        await input.CopyToAsync(output);
    }

    public Task UploadAsync(string localPath, string remotePath) =>
        FtpUploader.UploadAsync(host, port, user, password, remotePath, localPath, useFtps);

    private FtpWebRequest CreateRequest(string path, string method)
    {
        var encodedPath = EncodePath(path);
        var uri = new Uri($"ftp://{host}:{port}/{encodedPath}");
        var request = (FtpWebRequest)WebRequest.Create(uri);
        request.Method = method;
        request.Credentials = new NetworkCredential(user, password);
        request.EnableSsl = useFtps;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        return request;
    }

    private static string EncodePath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(p => p is "." or "..")) throw new ArgumentException("FTP 路径不能包含 . 或 .. 目录段。");
        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    private static async Task CompleteAsync(FtpWebRequest request)
    {
        using var response = (FtpWebResponse)await request.GetResponseAsync();
        if (response.StatusCode is not (FtpStatusCode.FileActionOK or FtpStatusCode.PathnameCreated or FtpStatusCode.DirectoryStatus))
            throw new IOException($"FTP 返回：{response.StatusDescription}");
    }

    private static FtpEntry? ParseListingLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return null;

        // Common MLSD format: type=file;size=123; name
        if (trimmed.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
        {
            var separator = trimmed.IndexOf(' ');
            if (separator > 0)
            {
                var facts = trimmed[..separator];
                var name = trimmed[(separator + 1)..];
                var isDirectory = facts.Contains("type=dir", StringComparison.OrdinalIgnoreCase);
                var sizeMatch = Regex.Match(facts, @"(?:^|;)size=(\d+)(?:;|$)", RegexOptions.IgnoreCase);
                return new FtpEntry(name, isDirectory, sizeMatch.Success ? long.Parse(sizeMatch.Groups[1].Value) : null);
            }
        }

        // Unix LIST format.
        var unix = Regex.Match(trimmed, @"^([bcdlps-][rwxStTs-]{9})\s+\d+\s+\S+\s+\S+\s+(\d+)\s+\w{3}\s+\d{1,2}\s+\S+\s+(.+)$");
        if (unix.Success)
        {
            long.TryParse(unix.Groups[2].Value, out var size);
            var name = unix.Groups[3].Value;
            if (unix.Groups[1].Value[0] == 'l' && name.Contains(" -> ", StringComparison.Ordinal))
                name = name[..name.IndexOf(" -> ", StringComparison.Ordinal)];
            return new FtpEntry(name, unix.Groups[1].Value[0] == 'd', size);
        }

        // Windows IIS FTP LIST format.
        var windows = Regex.Match(trimmed, @"^(\d{2}-\d{2}-\d{2,4}\s+\d{1,2}:\d{2}(?:AM|PM))\s+(<DIR>|\d+)\s+(.+)$", RegexOptions.IgnoreCase);
        if (windows.Success)
        {
            var isDirectory = windows.Groups[2].Value.Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
            long? size = long.TryParse(windows.Groups[2].Value, out var value) ? value : null;
            return new FtpEntry(windows.Groups[3].Value, isDirectory, size);
        }

        // Some servers return one bare filename per line.
        return new FtpEntry(trimmed, false, null);
    }
}
