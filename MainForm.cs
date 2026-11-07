using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace OpenRungClashUploader;

public sealed class MainForm : Form
{
    private readonly TextBox _host = new() { Text = "t3.3vftp.vip" };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 3543 };
    private readonly TextBox _user = new() { Text = "au104745" };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly TextBox _remote = new() { Text = "wwwroot/clash.asp" };
    private readonly CheckBox _ftps = new() { Text = "使用显式 FTPS（服务器需支持）", AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button _fetch = new() { Text = "获取OpenRung订阅" };
    private readonly Button _fetchZhuhai = new() { Text = "获取Zhuhai订阅" };
    private readonly Button _upload = new() { Text = "上传当前 YAML" };
    private readonly NumericUpDown _limit = new() { Minimum = 1, Maximum = 50, Value = 10, Width = 70 };
    private readonly TextBox _preview = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private string? _currentYaml;
    private string _clientId = Guid.NewGuid().ToString();
    private readonly string _localFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClashSubscriptionUploader", "clash.yaml");
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRungClashUploader", "settings.json");

    public MainForm()
    {
        Text = "Clash 节点订阅工具";
        MinimumSize = new Size(900, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        LoadSettings();
        _fetch.Click += async (_, _) => await FetchNodesAsync();
        _fetchZhuhai.Click += async (_, _) => await FetchZhuhaiAsync();
        _upload.Click += async (_, _) => await UploadAsync();
        FormClosing += (_, _) => SaveSettings();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        Controls.Add(root);

        var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 0, 0, 8) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddField(settings, "FTP 地址", _host, 0, 0);
        AddField(settings, "端口", _port, 2, 0);
        AddField(settings, "帐号", _user, 0, 1);
        AddField(settings, "FTP 密码（本地加密保存）", _password, 2, 1);
        AddField(settings, "FTP 根目录下的上传路径", _remote, 0, 2);
        settings.Controls.Add(_ftps, 2, 2);
        settings.SetColumnSpan(_ftps, 2);
        root.Controls.Add(settings, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
        buttons.Controls.Add(_fetch);
        buttons.Controls.Add(_fetchZhuhai);
        buttons.Controls.Add(new Label { Text = "候选数量", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
        buttons.Controls.Add(_limit);
        buttons.Controls.Add(_upload);
        root.Controls.Add(buttons, 0, 1);

        var previewGroup = new GroupBox { Text = "YAML 预览", Dock = DockStyle.Fill };
        _preview.Dock = DockStyle.Fill;
        previewGroup.Controls.Add(_preview);
        root.Controls.Add(previewGroup, 0, 2);

        var logGroup = new GroupBox { Text = "状态", Dock = DockStyle.Fill };
        _log.Dock = DockStyle.Fill;
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 3);
    }

    private static void AddField(TableLayoutPanel panel, string label, Control control, int column, int row)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, column, row);
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, column + 1, row);
    }

    private async Task FetchNodesAsync()
    {
        SetFetchButtonsEnabled(false);
        try
        {
            var result = await OpenRungBroker.FetchMihomoYamlAsync(_clientId, (int)_limit.Value);
            _currentYaml = result.Yaml;
            _preview.Text = result.Yaml;
            SaveLocalYaml();
            Log($"获取并验证完成：{result.CompatibleCount}/{result.TotalCount} 个候选可转换；Broker 到期时间 {result.NotAfter:yyyy-MM-dd HH:mm:ss} UTC");
        }
        catch (Exception ex) { Log("获取失败：" + ex.Message); }
        finally { SetFetchButtonsEnabled(true); }
    }

    private async Task FetchZhuhaiAsync()
    {
        SetFetchButtonsEnabled(false);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await http.GetAsync(
                "https://raw.githubusercontent.com/zhuhaiuk/free-nodes/main/clash_config.yaml",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 5_000_000)
                throw new InvalidDataException("Zhuhai 订阅文件超过 5 MB，已停止获取。");

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk);
                if (read == 0) break;
                if (buffer.Length + read > 5_000_000)
                    throw new InvalidDataException("Zhuhai 订阅文件超过 5 MB，已停止获取。");
                buffer.Write(chunk, 0, read);
            }

            var yaml = new UTF8Encoding(false, true).GetString(buffer.ToArray());
            if (!yaml.Contains("proxies:", StringComparison.OrdinalIgnoreCase) ||
                !yaml.Contains("rules:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载内容不像 Clash YAML 订阅（缺少 proxies 或 rules）。");

            _currentYaml = yaml;
            _preview.Text = yaml;
            SaveLocalYaml();
            Log("已获取 Zhuhai 订阅并载入 YAML 预览；现在可点击“上传当前 YAML”。");
        }
        catch (Exception ex) { Log("获取 Zhuhai 订阅失败：" + ex.Message); }
        finally { SetFetchButtonsEnabled(true); }
    }

    private void SetFetchButtonsEnabled(bool enabled) =>
        _fetch.Enabled = _fetchZhuhai.Enabled = _upload.Enabled = enabled;

    private void SaveLocalYaml()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_localFile)!);
        File.WriteAllText(_localFile, _currentYaml!, new UTF8Encoding(false));
        Log($"已保存 YAML：{_localFile}");
    }

    private async Task UploadAsync()
    {
        SetFetchButtonsEnabled(false);
        try
        {
            if (string.IsNullOrEmpty(_currentYaml))
                throw new InvalidOperationException("请先获取一个订阅并生成 YAML 预览，再上传。");
            if (string.IsNullOrWhiteSpace(_host.Text) || string.IsNullOrWhiteSpace(_user.Text) || string.IsNullOrWhiteSpace(_password.Text))
                throw new InvalidOperationException("请填写 FTP 地址、帐号和密码。");
            await File.WriteAllTextAsync(_localFile, _currentYaml!, new UTF8Encoding(false));
            await FtpUploader.UploadAsync(_host.Text.Trim(), (int)_port.Value, _user.Text.Trim(), _password.Text,
                _remote.Text.Trim(), _localFile, _ftps.Checked);
            Log($"上传成功：/{_remote.Text.Trim()}（同名文件将被覆盖）");
        }
        catch (Exception ex) { Log("上传失败：" + ex.Message); }
        finally { SetFetchButtonsEnabled(true); }
    }

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));
            if (s is null) return;
            _host.Text = s.Host;
            _port.Value = Math.Clamp(s.Port, (int)_port.Minimum, (int)_port.Maximum);
            _user.Text = s.User;
            _remote.Text = string.IsNullOrWhiteSpace(s.RemotePath) || s.RemotePath == "wwwroot/clash.yaml"
                ? "wwwroot/clash.asp"
                : s.RemotePath;
            _ftps.Checked = s.UseFtps;
            if (s.Limit > 0)
            {
                // 5 was the previous default; migrate it to the new default of 10.
                var savedLimit = s.Limit == 5 ? 10 : s.Limit;
                _limit.Value = Math.Clamp(savedLimit, (int)_limit.Minimum, (int)_limit.Maximum);
            }
            if (!string.IsNullOrWhiteSpace(s.ClientId)) _clientId = s.ClientId;
            if (!string.IsNullOrEmpty(s.ProtectedPassword))
                _password.Text = Dpapi.Unprotect(Convert.FromBase64String(s.ProtectedPassword));
        }
        catch { Log("读取设置失败，将使用默认值。"); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var encrypted = string.IsNullOrEmpty(_password.Text) ? "" : Convert.ToBase64String(Dpapi.Protect(_password.Text));
            var s = new Settings(_host.Text, (int)_port.Value, _user.Text, _remote.Text, _ftps.Checked, encrypted, (int)_limit.Value, _clientId);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* A settings write failure must not prevent the window from closing. */ }
    }

    private sealed record Settings(string Host, int Port, string User, string RemotePath, bool UseFtps, string ProtectedPassword, int Limit, string ClientId);
}

internal static class ClashConverter
{
    internal sealed record Proxy(string Name, string Server, int Port, string Uuid, string Network,
        string Security, string Sni, string Fingerprint, string PublicKey, string ShortId, string Flow);

    public static List<Proxy> Parse(string input)
    {
        var result = new List<Proxy>();
        var lineNo = 0;
        foreach (var raw in input.Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || uri.Scheme != "vless" || uri.Port < 1)
                throw new FormatException($"第 {lineNo} 行不是有效的 VLESS 链接。");
            var uuid = Uri.UnescapeDataString(uri.UserInfo);
            if (!Guid.TryParse(uuid, out _)) throw new FormatException($"第 {lineNo} 行的 UUID 无效。");
            var query = ParseQuery(uri.Query);
            query.TryGetValue("security", out var security);
            query.TryGetValue("type", out var network);
            query.TryGetValue("sni", out var sni);
            query.TryGetValue("fp", out var fp);
            query.TryGetValue("pbk", out var pbk);
            query.TryGetValue("sid", out var sid);
            query.TryGetValue("flow", out var flow);
            var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
            result.Add(new Proxy(string.IsNullOrWhiteSpace(name) ? $"VLESS-{lineNo}" : name, uri.Host, uri.Port,
                uuid, string.IsNullOrWhiteSpace(network) ? "tcp" : network,
                string.IsNullOrWhiteSpace(security) ? "none" : security, sni ?? "", fp ?? "", pbk ?? "", sid ?? "", flow ?? ""));
        }
        return result;
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "", StringComparer.OrdinalIgnoreCase);

    public static string Render(List<Proxy> proxies)
    {
        var sb = new StringBuilder("# Generated locally from user-provided VLESS links\nproxies:\n");
        foreach (var p in proxies)
        {
            sb.Append("  - name: ").Append(Q(p.Name)).Append("\n    type: vless\n    server: ").Append(Q(p.Server))
                .Append("\n    port: ").Append(p.Port).Append("\n    uuid: ").Append(Q(p.Uuid)).Append("\n    network: ").Append(Q(p.Network)).Append("\n    udp: true\n");
            if (p.Flow.Length > 0) sb.Append("    flow: ").Append(Q(p.Flow)).Append('\n');
            if (p.Security.Equals("reality", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("    tls: true\n    reality-opts:\n      enabled: true\n");
                if (p.PublicKey.Length > 0) sb.Append("      public-key: ").Append(Q(p.PublicKey)).Append('\n');
                if (p.ShortId.Length > 0) sb.Append("      short-id: ").Append(Q(p.ShortId)).Append('\n');
            }
            else if (p.Security.Equals("tls", StringComparison.OrdinalIgnoreCase)) sb.Append("    tls: true\n");
            if (p.Sni.Length > 0) sb.Append("    servername: ").Append(Q(p.Sni)).Append('\n');
            if (p.Fingerprint.Length > 0) sb.Append("    client-fingerprint: ").Append(Q(p.Fingerprint)).Append('\n');
        }
        sb.Append("\nproxy-groups:\n  - name: \"My-Nodes\"\n    type: select\n    proxies:\n");
        foreach (var p in proxies) sb.Append("      - ").Append(Q(p.Name)).Append('\n');
        return sb.Append("\nrules:\n  - MATCH,My-Nodes\n").ToString();
    }

    private static string Q(string value) => JsonSerializer.Serialize(value);
}

internal static class FtpUploader
{
    public static async Task UploadAsync(string host, int port, string user, string password, string remotePath, string localFile, bool ftps)
    {
        var parts = remotePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or ".."))
            throw new ArgumentException("请填写相对于 FTP 根目录的上传路径，例如 wwwroot/clash.yaml。");
        var uriBuilder = new UriBuilder("ftp", host, port)
        {
            Path = string.Join('/', parts.Select(Uri.EscapeDataString))
        };
        var uri = uriBuilder.Uri;
        var request = (FtpWebRequest)WebRequest.Create(uri);
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(user, password);
        request.EnableSsl = ftps;
        request.UseBinary = true;
        request.UsePassive = true;
        request.KeepAlive = false;
        var bytes = await File.ReadAllBytesAsync(localFile);
        request.ContentLength = bytes.Length;
        await using (var stream = await request.GetRequestStreamAsync()) await stream.WriteAsync(bytes);
        using var response = (FtpWebResponse)await request.GetResponseAsync();
        if (response.StatusCode != FtpStatusCode.ClosingData && response.StatusCode != FtpStatusCode.FileActionOK)
            throw new IOException($"FTP 返回：{response.StatusDescription}");
    }
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Protect(string value) => Transform(Encoding.UTF8.GetBytes(value), protect: true);
    public static string Unprotect(byte[] value) => Encoding.UTF8.GetString(Transform(value, protect: false));

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var source = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, source.Data, bytes.Length);
            DataBlob result;
            var ok = protect
                ? CryptProtectData(ref source, "Clash uploader settings", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var output = new byte[result.Size];
                Marshal.Copy(result.Data, output, 0, output.Length);
                return output;
            }
            finally { LocalFree(result.Data); }
        }
        finally { Marshal.FreeHGlobal(source.Data); }
    }
}
