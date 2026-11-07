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
    private readonly Button _upload = new() { Text = "上传订阅" };
    private readonly NumericUpDown _limit = new() { Minimum = 1, Maximum = 50, Value = 10, Width = 70 };
    private readonly RichTextBox _preview = new() { ReadOnly = true, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Both, WordWrap = true, Font = new Font("Consolas", 10F) };
    private readonly Button _clearLog = new() { Text = "清空" };
    private readonly Button _ftpConnect = new() { Text = "连接 FTP" };
    private readonly Button _ftpParent = new() { Text = "上级目录" };
    private readonly Button _ftpRefresh = new() { Text = "刷新" };
    private readonly Button _ftpNewFolder = new() { Text = "新建文件夹" };
    private readonly Button _ftpUpload = new() { Text = "上传文件" };
    private readonly Button _ftpDownload = new() { Text = "下载文件" };
    private readonly Button _ftpRename = new() { Text = "重命名" };
    private readonly Button _ftpDelete = new() { Text = "删除" };
    private readonly Label _ftpPathLabel = new() { Text = "FTP 未连接", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ListView _ftpList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, AllowDrop = true };
    private string _ftpCurrentDirectory = "";
    private bool _ftpConnected;
    private string? _currentYaml;
    private string _clientId = Guid.NewGuid().ToString();
    private readonly string _localFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClashSubscriptionUploader", "clash.yaml");
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRungClashUploader", "settings.json");

    public MainForm()
    {
        Text = "Clash 节点订阅工具";
        ClientSize = new Size(1320, 820);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        LoadSettings();
        _fetch.Click += async (_, _) => await FetchNodesAsync();
        _fetchZhuhai.Click += async (_, _) => await FetchZhuhaiAsync();
        _upload.Click += async (_, _) => await UploadAsync();
        _clearLog.Click += (_, _) => _log.Clear();
        _ftpConnect.Click += async (_, _) => await ToggleFtpConnectionAsync();
        _ftpRefresh.Click += async (_, _) => await RefreshFtpListAsync();
        _ftpParent.Click += async (_, _) => await GoToParentDirectoryAsync();
        _ftpNewFolder.Click += async (_, _) => await CreateFtpDirectoryAsync();
        _ftpUpload.Click += async (_, _) => await ChooseAndUploadFilesAsync();
        _ftpDownload.Click += async (_, _) => await DownloadSelectedFileAsync();
        _ftpRename.Click += async (_, _) => await RenameSelectedEntryAsync();
        _ftpDelete.Click += async (_, _) => await DeleteSelectedEntriesAsync();
        _ftpList.DoubleClick += async (_, _) => await ActivateSelectedFtpEntryAsync();
        _ftpList.DragEnter += FtpListDragEnter;
        _ftpList.DragDrop += async (_, e) => await FtpListDragDropAsync(e);
        FormClosing += (_, _) => SaveSettings();
    }

    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            Size = new Size(1290, 780),
            Panel1MinSize = 720,
            Panel2MinSize = 360
        };
        split.SplitterDistance = 865;
        Controls.Add(split);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        split.Panel1.Controls.Add(root);

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
        _fetch.AutoSize = false;
        _fetch.Size = new Size(185, 34);
        _fetchZhuhai.AutoSize = false;
        _fetchZhuhai.Size = new Size(185, 34);
        _upload.AutoSize = false;
        _upload.Size = new Size(120, 34);
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
        var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(5) };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        logToolbar.Controls.Add(_clearLog);
        _log.Dock = DockStyle.Fill;
        logLayout.Controls.Add(logToolbar, 0, 0);
        logLayout.Controls.Add(_log, 0, 1);
        logGroup.Controls.Add(logLayout);
        root.Controls.Add(logGroup, 0, 3);

        BuildFtpBrowser(split.Panel2);
    }

    private void BuildFtpBrowser(Control parent)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var group = new GroupBox { Text = "FTP 文件管理", Dock = DockStyle.Fill };
        parent.Controls.Add(group);
        group.Controls.Add(layout);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        foreach (var button in new[] { _ftpConnect, _ftpParent, _ftpRefresh })
        {
            button.AutoSize = false;
            button.Size = new Size(108, 32);
            toolbar.Controls.Add(button);
        }
        layout.Controls.Add(toolbar, 0, 0);
        _ftpPathLabel.Padding = new Padding(3, 2, 3, 2);
        layout.Controls.Add(_ftpPathLabel, 0, 1);

        _ftpList.Columns.Add("名称", 230);
        _ftpList.Columns.Add("类型", 65);
        _ftpList.Columns.Add("大小", 90);
        _ftpList.AllowDrop = true;
        layout.Controls.Add(_ftpList, 0, 2);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 3, Padding = new Padding(0, 6, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 3; row++) actions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (var button in new[] { _ftpNewFolder, _ftpUpload, _ftpDownload, _ftpRename, _ftpDelete })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(3);
            actions.Controls.Add(button);
        }
        actions.SetColumnSpan(_ftpDelete, 2);
        layout.Controls.Add(actions, 0, 3);
        UpdateFtpControls();
    }

    private void UpdateFtpControls()
    {
        _ftpConnect.Text = _ftpConnected ? "断开 FTP" : "连接 FTP";
        _ftpPathLabel.Text = _ftpConnected ? $"/{_ftpCurrentDirectory}" : "FTP 未连接";
        _ftpParent.Enabled = _ftpConnected && _ftpCurrentDirectory.Length > 0;
        _ftpRefresh.Enabled = _ftpConnected;
        _ftpNewFolder.Enabled = _ftpConnected;
        _ftpUpload.Enabled = _ftpConnected;
        _ftpDownload.Enabled = _ftpConnected;
        _ftpRename.Enabled = _ftpConnected;
        _ftpDelete.Enabled = _ftpConnected;
    }

    private FtpFileManager CreateFtpManager()
    {
        if (string.IsNullOrWhiteSpace(_host.Text) || string.IsNullOrWhiteSpace(_user.Text) || string.IsNullOrWhiteSpace(_password.Text))
            throw new InvalidOperationException("请先填写 FTP 地址、帐号和密码。");
        return new FtpFileManager(_host.Text.Trim(), (int)_port.Value, _user.Text.Trim(), _password.Text, _ftps.Checked);
    }

    private async Task ToggleFtpConnectionAsync()
    {
        if (_ftpConnected)
        {
            _ftpConnected = false;
            _ftpCurrentDirectory = "";
            _ftpList.Items.Clear();
            UpdateFtpControls();
            Log("已断开 FTP 文件浏览。");
            return;
        }

        _ftpConnect.Enabled = false;
        try
        {
            _ftpCurrentDirectory = "";
            var entries = await CreateFtpManager().ListAsync(_ftpCurrentDirectory);
            _ftpConnected = true;
            ShowFtpEntries(entries);
            Log("FTP 已连接，已列出根目录文件。");
        }
        catch (Exception ex) { Log("连接 FTP 失败：" + ex.Message); }
        finally { _ftpConnect.Enabled = true; UpdateFtpControls(); }
    }

    private async Task RefreshFtpListAsync()
    {
        if (!_ftpConnected) return;
        try
        {
            var entries = await CreateFtpManager().ListAsync(_ftpCurrentDirectory);
            ShowFtpEntries(entries);
            UpdateFtpControls();
        }
        catch (Exception ex) { Log("读取 FTP 目录失败：" + ex.Message); }
    }

    private void ShowFtpEntries(IReadOnlyList<FtpEntry> entries)
    {
        _ftpList.BeginUpdate();
        try
        {
            _ftpList.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.Name) { Tag = entry };
                item.SubItems.Add(entry.IsDirectory ? "文件夹" : "文件");
                item.SubItems.Add(entry.IsDirectory || entry.Size is null ? "" : FormatFileSize(entry.Size.Value));
                _ftpList.Items.Add(item);
            }
        }
        finally { _ftpList.EndUpdate(); }
    }

    private async Task GoToParentDirectoryAsync()
    {
        if (!_ftpConnected || _ftpCurrentDirectory.Length == 0) return;
        var slash = _ftpCurrentDirectory.LastIndexOf('/');
        _ftpCurrentDirectory = slash < 0 ? "" : _ftpCurrentDirectory[..slash];
        UpdateFtpControls();
        await RefreshFtpListAsync();
    }

    private async Task ActivateSelectedFtpEntryAsync()
    {
        if (_ftpList.SelectedItems.Count != 1 || _ftpList.SelectedItems[0].Tag is not FtpEntry entry) return;
        if (entry.IsDirectory)
        {
            _ftpCurrentDirectory = CombineFtpPath(_ftpCurrentDirectory, entry.Name);
            UpdateFtpControls();
            await RefreshFtpListAsync();
        }
        else await DownloadSelectedFileAsync();
    }

    private async Task CreateFtpDirectoryAsync()
    {
        var name = PromptForText("新建文件夹", "文件夹名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Contains('/') || name.Contains('\\') || name is "." or "..")
        {
            Log("文件夹名称不能包含路径分隔符或 .、..。");
            return;
        }
        try
        {
            await CreateFtpManager().CreateDirectoryAsync(CombineFtpPath(_ftpCurrentDirectory, name));
            Log($"已创建 FTP 文件夹：{name}");
            await RefreshFtpListAsync();
        }
        catch (Exception ex) { Log("创建 FTP 文件夹失败：" + ex.Message); }
    }

    private async Task ChooseAndUploadFilesAsync()
    {
        using var dialog = new OpenFileDialog { Title = "选择要上传的文件", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await UploadLocalFilesAsync(dialog.FileNames);
    }

    private void FtpListDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async Task FtpListDragDropAsync(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await UploadLocalFilesAsync(files);
    }

    private async Task UploadLocalFilesAsync(IEnumerable<string> files)
    {
        if (!_ftpConnected) return;
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Log($"跳过非文件项目：{file}");
                continue;
            }
            try
            {
                var remote = CombineFtpPath(_ftpCurrentDirectory, Path.GetFileName(file));
                await CreateFtpManager().UploadAsync(file, remote);
                Log($"已上传：{Path.GetFileName(file)}");
            }
            catch (Exception ex) { Log($"上传 {Path.GetFileName(file)} 失败：{ex.Message}"); }
        }
        await RefreshFtpListAsync();
    }

    private async Task DownloadSelectedFileAsync()
    {
        if (_ftpList.SelectedItems.Count != 1 || _ftpList.SelectedItems[0].Tag is not FtpEntry entry) return;
        if (entry.IsDirectory)
        {
            Log("请进入文件夹后选择要下载的文件。");
            return;
        }
        using var dialog = new SaveFileDialog { FileName = entry.Name, Title = "保存 FTP 文件" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await CreateFtpManager().DownloadAsync(CombineFtpPath(_ftpCurrentDirectory, entry.Name), dialog.FileName);
            Log($"已下载：{entry.Name}");
        }
        catch (Exception ex) { Log("下载 FTP 文件失败：" + ex.Message); }
    }

    private async Task RenameSelectedEntryAsync()
    {
        if (_ftpList.SelectedItems.Count != 1 || _ftpList.SelectedItems[0].Tag is not FtpEntry entry) return;
        var newName = PromptForText("重命名 FTP 项目", "新名称：", entry.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == entry.Name) return;
        if (newName.Contains('/') || newName.Contains('\\') || newName is "." or "..")
        {
            Log("名称不能包含路径分隔符或 .、..。");
            return;
        }
        try
        {
            await CreateFtpManager().RenameAsync(
                CombineFtpPath(_ftpCurrentDirectory, entry.Name),
                CombineFtpPath(_ftpCurrentDirectory, newName));
            Log($"已重命名：{entry.Name} → {newName}");
            await RefreshFtpListAsync();
        }
        catch (Exception ex) { Log("重命名 FTP 项目失败：" + ex.Message); }
    }

    private async Task DeleteSelectedEntriesAsync()
    {
        var selected = _ftpList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<FtpEntry>().ToArray();
        if (selected.Length == 0) return;
        var names = string.Join(Environment.NewLine, selected.Take(8).Select(e => e.Name));
        if (selected.Length > 8) names += Environment.NewLine + $"……等 {selected.Length} 项";
        if (MessageBox.Show(this, $"确定删除所选 FTP 项目吗？\n\n{names}\n\n文件夹必须为空才能删除。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        foreach (var entry in selected)
        {
            try
            {
                await CreateFtpManager().DeleteAsync(CombineFtpPath(_ftpCurrentDirectory, entry.Name), entry.IsDirectory);
                Log($"已删除：{entry.Name}");
            }
            catch (Exception ex) { Log($"删除 {entry.Name} 失败：{ex.Message}"); }
        }
        await RefreshFtpListAsync();
    }

    private static string CombineFtpPath(string directory, string name) =>
        string.IsNullOrEmpty(directory) ? name : $"{directory}/{name}";

    private static string FormatFileSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024
        ? $"{bytes / 1024d:0.#} KB" : $"{bytes / (1024d * 1024):0.#} MB";

    private static string? PromptForText(string title, string label, string initialValue = "")
    {
        using var dialog = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(380, 125) };
        var prompt = new Label { Text = label, Left = 12, Top = 14, AutoSize = true };
        var input = new TextBox { Text = initialValue, Left = 12, Top = 38, Width = 356 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 206, Top = 78, Width = 76 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 292, Top = 78, Width = 76 };
        dialog.Controls.AddRange([prompt, input, ok, cancel]);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog() == DialogResult.OK ? input.Text.Trim() : null;
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
            Log("已获取 Zhuhai 订阅并载入 YAML 预览；现在可点击“上传订阅”。");
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
            throw new ArgumentException("请填写相对于 FTP 根目录的上传路径，例如 wwwroot/clash.asp。");
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
