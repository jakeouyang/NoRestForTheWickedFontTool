using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WickedFontTool;

public sealed class MainForm : Form
{
    static readonly Color Red = Color.FromArgb(235, 32, 39);
    readonly TextBox game = Input(), font = Input();
    readonly Dictionary<string, CheckBox> languageBoxes = new();
    readonly RichTextBox log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.Silver, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 10), DetectUrls = false };
    readonly Label status = new() { Text = L10n.S("就绪", "就緒"), AutoSize = true, ForeColor = Color.FromArgb(40, 200, 90), Anchor = AnchorStyles.Left };
    PictureBox titlePicture = null!;
    static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WickedFontTool", "settings.json");
    static readonly string DefaultGame = @"C:\Program Files (x86)\Steam\steamapps\common\NoRestForTheWicked";
    static readonly List<string> History = new();
    const int HistoryLimit = 800;
    bool busy, restarting;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

    public MainForm(bool loadSavedPaths = true)
    {
        LoadSettings(loadSavedPaths);
        status.Text = L10n.S("就绪", "就緒");
        Text = "No Rest for the Wicked Font Tool"; BackColor = Color.Black; ForeColor = Red;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 11);
        ClientSize = new Size(1180, 700); MinimumSize = new Size(1080, 620); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("WickedFontTool.Resources.game.ico");
        if (iconStream != null) Icon = new Icon(iconStream);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 16, 28, 14), ColumnCount = 1, RowCount = 9 };
        foreach (int h in new[] { 56, 38, 52, 52, 40, 34, 54 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(layout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        var titleArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        using var titleIcon = new Icon(Icon ?? SystemIcons.Application, 32, 32);
        var titleImage = new PictureBox { Image = titleIcon.ToBitmap(), Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        titlePicture = titleImage;
        titleImage.Disposed += (_, _) => titleImage.Image?.Dispose();
        var title = new Label { Text = "No Rest for the Wicked Font Tool", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 18), AutoEllipsis = true, Margin = Padding.Empty };
        title.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
        titleArea.Controls.Add(titleImage, 0, 0); titleArea.Controls.Add(title, 1, 0);
        header.Controls.Add(titleArea, 0, 0);
        var language = Button(L10n.OtherName, SwitchLanguage);
        language.Dock = DockStyle.Fill; language.Margin = new Padding(0, 2, 8, 2);
        header.Controls.Add(language, 1, 0);
        var github = Button("GitHub", () => Process.Start(new ProcessStartInfo("https://github.com/jakeouyang/NoRestForTheWickedFontTool") { UseShellExecute = true }));
        github.Dock = DockStyle.Fill; github.Margin = new Padding(0, 2, 8, 2);
        header.Controls.Add(github, 2, 0);
        header.Controls.Add(Button("—", () => WindowState = FormWindowState.Minimized), 3, 0);
        header.Controls.Add(Button("×", Close), 4, 0); layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(new Label { Text = L10n.S("恶意不息字体 MOD  /  生成 · 安装 · 还原", "惡意不息字體 MOD  /  生成 · 安裝 · 還原"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Silver }, 0, 1);
        layout.Controls.Add(PathRow(L10n.S("游戏目录", "遊戲目錄"), game, true), 0, 2);
        layout.Controls.Add(PathRow(L10n.S("字体文件", "字體檔案"), font, false), 0, 3);
        layout.Controls.Add(LanguageRow(), 0, 4);
        layout.Controls.Add(new Label { Text = L10n.S("可多选游戏语言；所选语言的全部字体槽位都会替换为所选字体，操作前请退出游戏。",
                "可多選遊戲語言；所選語言的全部字體槽位都會替換為所選字體，操作前請退出遊戲。"),
            Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9) }, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var replace = Button(L10n.S("生成并安装", "生成並安裝"), () => Run(true)); var restore = Button(L10n.S("还原字体", "還原字體"), () => Run(false));
        replace.Width = restore.Width = 150; replace.Height = restore.Height = 40;
        actions.Controls.Add(replace); actions.Controls.Add(restore); layout.Controls.Add(actions, 0, 6);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = log.BackColor, Padding = new Padding(12), Margin = new Padding(0, 8, 0, 8) };
        panel.Controls.Add(log); layout.Controls.Add(panel, 0, 7);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        footer.Controls.Add(status); footer.Controls.Add(new Label { Text = "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1"), Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9) }); layout.Controls.Add(footer, 0, 8);
        if (loadSavedPaths && History.Count > 0) { log.Text = string.Join(Environment.NewLine, History) + Environment.NewLine; log.ScrollToCaret(); }
        Append(L10n.S("请选择恶意不息根目录和 TTF / OTF 字体。", "請選擇惡意不息根目錄和 TTF / OTF 字體。"));
        Append(L10n.S("还原仅处理与本工具补丁记录匹配的资源，备份会保留。", "還原僅處理與本工具補丁記錄匹配的資源，備份會保留。"));
        if (loadSavedPaths && FontService.ValidateGameDir(DefaultGame) == null && game.Text.Length == 0) game.Text = DefaultGame;
        ApplyGameIcon(game.Text, titlePicture);
        font.AllowDrop = true;
        font.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        font.DragDrop += (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) font.Text = paths[0]; };
        FormClosing += (_, e) => { if (busy && !restarting) { e.Cancel = true; status.Text = L10n.S("正在操作，请等待完成", "正在操作，請等待完成"); } else if (!restarting) SaveSettings(); };
    }
    void LoadSettings(bool loadPaths)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = AppSettings.Load(SettingsPath);
            L10n.Lang = settings.UiLanguage.Equals("hant", StringComparison.OrdinalIgnoreCase) ? "hant" : "hans";
            if (loadPaths) { game.Text = settings.GameDir; font.Text = settings.FontFile; }
            foreach (var code in L10n.User) languageBoxes[code] = new CheckBox { AutoSize = true, Checked = settings.Languages.Contains(code, StringComparer.OrdinalIgnoreCase), Margin = new Padding(0, 8, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
        }
        catch { Append(L10n.S("上次记录不可用，请重新选择。", "上次記錄不可用，請重新選擇。")); }
        foreach (var code in L10n.User)
            if (!languageBoxes.ContainsKey(code)) languageBoxes[code] = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(0, 8, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
    }
    AppSettings CaptureSettings() => new()
    {
        UiLanguage = L10n.Traditional ? "hant" : "hans",
        GameDir = game.Text.Trim(),
        FontFile = font.Text.Trim().Trim('"'),
        Languages = languageBoxes.Where(x => x.Value.Checked).Select(x => x.Key).ToList(),
    };
    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            Store.Save(SettingsPath, CaptureSettings());
        }
        catch { }
    }
    void SwitchLanguage()
    {
        if (busy) return;
        L10n.Lang = L10n.Traditional ? "hans" : "hant";
        SaveSettings();
        restarting = true;
        var next = new MainForm { StartPosition = FormStartPosition.Manual, Location = Location, Size = Size, WindowState = WindowState };
        next.FormClosed += (_, _) => Close();
        next.Show();
        Hide();
    }
    Control LanguageRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (int i in Enumerable.Range(1, 2)) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.Controls.Add(new Label { Text = L10n.S("替换语言", "替換語言"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 20, 0), TextAlign = ContentAlignment.MiddleLeft });
        foreach (var (code, index) in L10n.User.Select((code, index) => (code, index)))
        {
            var box = languageBoxes[code]; box.Text = L10n.Name(code); box.Dock = DockStyle.Fill;
            box.CheckedChanged += (_, _) => SaveSettings();
            row.Controls.Add(box, index + 1, 0);
        }
        return row;
    }
    void SetLock(bool locked) { foreach (var box in languageBoxes.Values) box.AutoCheck = !locked; }
    void ApplyGameIcon(string root, PictureBox picture)
    {
        string exe = Path.Combine(root, "NoRestForTheWicked.exe");
        if (!File.Exists(exe)) return;
        try
        {
            using var extracted = Icon.ExtractAssociatedIcon(exe);
            if (extracted == null) return;
            Icon = (Icon)extracted.Clone();
            var old = picture.Image; picture.Image = extracted.ToBitmap(); old?.Dispose();
        }
        catch (Exception ex) { Append(L10n.S("无法读取游戏图标：", "無法讀取遊戲圖標：") + ex.Message); }
    }
    static TextBox Input() => new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 10, 10, 0) };
    Button Button(string text, Action action)
    {
        var b = new Button { Text = text, Dock = DockStyle.None, ForeColor = Red, BackColor = Color.Black, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Height = 38, Width = 86, Margin = new Padding(0, 2, 12, 2), UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 12, 12); b.Click += (_, _) => action(); return b;
    }
    Control PathRow(string label, TextBox box, bool directory)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 20, 0), TextAlign = ContentAlignment.MiddleLeft }); row.Controls.Add(box);
        var browse = Button(L10n.S("浏览", "瀏覽"), () =>
        {
            if (busy) return;
            if (directory)
            {
                using var dialog = new FolderBrowserDialog { Description = L10n.S("选择恶意不息游戏根目录", "選擇惡意不息遊戲根目錄"), UseDescriptionForTitle = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) { box.Text = dialog.SelectedPath; ApplyGameIcon(box.Text, titlePicture); var error = FontService.ValidateGameDir(box.Text); Append(error ?? L10n.S("游戏目录校验通过。", "遊戲目錄校驗通過。")); SaveSettings(); }
            }
            else
            {
                using var dialog = new OpenFileDialog { Filter = L10n.S("字体文件 (*.ttf;*.otf)|*.ttf;*.otf", "字體檔案 (*.ttf;*.otf)|*.ttf;*.otf"), Title = L10n.S("选择替换字体", "選擇替換字體") };
                if (dialog.ShowDialog(this) == DialogResult.OK) { box.Text = dialog.FileName; SaveSettings(); }
            }
        });
        browse.AutoSize = true; browse.Margin = new Padding(0, 2, 8, 2); browse.Anchor = AnchorStyles.Left; row.Controls.Add(browse); return row;
    }
    void Append(string text)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        History.Add(line); if (History.Count > HistoryLimit) History.RemoveRange(0, History.Count - HistoryLimit);
        log.AppendText(line + Environment.NewLine); log.ScrollToCaret();
    }
    async void Run(bool replace)
    {
        if (busy) return;
        string root = game.Text.Trim(), file = font.Text.Trim().Trim('"');
        if (root.Length == 0 || (replace && !File.Exists(file))) { Append(L10n.S("请先选择有效的游戏目录和字体文件。", "請先選擇有效的遊戲目錄和字體檔案。")); return; }
        if (!languageBoxes.Values.Any(x => x.Checked)) { Append(L10n.S("请至少选择一种要替换的语言。", "請至少選擇一種要替換的語言。")); return; }
        var selected = CaptureSettings().Selected();
        busy = true; SetLock(true); status.Text = L10n.S("处理中…", "處理中…");
        try
        {
            if (FontService.ValidateGameDir(root) is { } error) throw new InvalidDataException(error);
            ApplyGameIcon(root, titlePicture);
            SaveSettings();
            var progress = new Progress<string>(Append);
            await Task.Run(() => { Action<string> report = s => ((IProgress<string>)progress).Report(s); if (replace) FontService.Install(root, file, selected, report); else FontService.Restore(root, report); });
            status.Text = L10n.S("完成", "完成");
        }
        catch (UnauthorizedAccessException) { status.Text = L10n.S("没有写入权限", "沒有寫入權限"); Append(L10n.S("无法写入游戏目录。请关闭工具后右键选择“以管理员身份运行”，再重试。", "無法寫入遊戲目錄。請關閉工具後右鍵選擇「以管理員身份運行」，再重試。")); }
        catch (Exception ex) { status.Text = L10n.S("操作未完成", "操作未完成"); Append(ex.Message); }
        finally { busy = false; SetLock(false); }
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var p = new Pen(Red); e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1); }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x84 && (int)m.Result == 1) { var p = PointToClient(Cursor.Position); if (p.X >= Width - 12 && p.Y >= Height - 12) m.Result = (IntPtr)17; }
    }
}
