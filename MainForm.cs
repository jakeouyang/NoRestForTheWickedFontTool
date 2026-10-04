using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WickedFontTool;

public sealed class MainForm : Form
{
    static readonly Color Red = Color.FromArgb(235, 32, 39);
    readonly TextBox game = Input(), font = Input();
    readonly RichTextBox log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(18, 18, 18), ForeColor = Color.Silver, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 10), DetectUrls = false };
    readonly Label status = new() { Text = "就绪", AutoSize = true, ForeColor = Color.FromArgb(40, 200, 90), Anchor = AnchorStyles.Left };
    readonly List<Control> inputs = new();
    PictureBox titlePicture = null!;
    static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WickedFontTool", "settings.json");
    bool busy;
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);

    public MainForm(bool loadSavedPaths = true)
    {
        Text = "No Rest for the Wicked Font Tool"; BackColor = Color.Black; ForeColor = Red;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 11);
        ClientSize = new Size(940, 620); MinimumSize = new Size(900, 540); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("WickedFontTool.Resources.game.ico");
        if (iconStream != null) Icon = new Icon(iconStream);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 16, 28, 14), ColumnCount = 1, RowCount = 8 };
        foreach (int h in new[] { 56, 38, 52, 52, 42, 54 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(layout);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
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
        var github = Button("GitHub", () => Process.Start(new ProcessStartInfo("https://github.com/jakeouyang/NoRestForTheWickedFontTool") { UseShellExecute = true }));
        github.Dock = DockStyle.Fill; github.Margin = new Padding(0, 2, 8, 2);
        header.Controls.Add(github, 1, 0);
        header.Controls.Add(Button("—", () => WindowState = FormWindowState.Minimized), 2, 0);
        header.Controls.Add(Button("×", Close), 3, 0); layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(new Label { Text = "简体中文字体 MOD  /  一键生成 · 支持还原", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Silver }, 0, 1);
        layout.Controls.Add(PathRow("游戏目录", game, true), 0, 2);
        layout.Controls.Add(PathRow("字体文件", font, false), 0, 3);
        layout.Controls.Add(new Label { Text = "替换游戏资源并保留版本备份；操作前请退出游戏。", Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9) }, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var replace = Button("生成并安装", () => Run(true)); var restore = Button("还原字体", () => Run(false));
        replace.Width = restore.Width = 150; replace.Height = restore.Height = 40;
        actions.Controls.Add(replace); actions.Controls.Add(restore); inputs.AddRange(new[] { replace, restore }); layout.Controls.Add(actions, 0, 5);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = log.BackColor, Padding = new Padding(12), Margin = new Padding(0, 8, 0, 8) };
        panel.Controls.Add(log); layout.Controls.Add(panel, 0, 6);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        footer.Controls.Add(status); footer.Controls.Add(new Label { Text = "v1.0", Dock = DockStyle.Fill, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 9) }); layout.Controls.Add(footer, 0, 7);
        Append("请选择恶意不息根目录和 TTF / OTF 字体。");
        Append("还原仅处理与本工具补丁记录匹配的资源，备份会保留。");
        string defaultGame = @"C:\Program Files (x86)\Steam\steamapps\common\NoRestForTheWicked";
        if (loadSavedPaths && FontService.ValidateGameDir(defaultGame) == null) game.Text = defaultGame;
        try
        {
            if (loadSavedPaths && File.Exists(SettingsPath))
            {
                var saved = Store.Load<string[]>(SettingsPath);
                if (saved.Length == 2) { game.Text = saved[0]; font.Text = saved[1]; }
            }
        }
        catch { Append("上次路径记录不可用，请重新选择。"); }
        ApplyGameIcon(game.Text, titlePicture);
        font.AllowDrop = true;
        font.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        font.DragDrop += (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) font.Text = paths[0]; };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; status.Text = "正在操作，请等待完成"; } };
    }
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
        catch (Exception ex) { Append("无法读取游戏图标：" + ex.Message); }
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
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 20, 0), TextAlign = ContentAlignment.MiddleLeft }); row.Controls.Add(box);
        var browse = Button("浏览", () =>
        {
            if (directory) { using var dialog = new FolderBrowserDialog { Description = "选择恶意不息游戏根目录", UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) { box.Text = dialog.SelectedPath; ApplyGameIcon(box.Text, titlePicture); var error = FontService.ValidateGameDir(box.Text); Append(error ?? "游戏目录校验通过。"); } }
            else { using var dialog = new OpenFileDialog { Filter = "字体文件 (*.ttf;*.otf)|*.ttf;*.otf", Title = "选择替换字体" }; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; }
        });
        browse.Anchor = AnchorStyles.Left; row.Controls.Add(browse); inputs.Add(box); inputs.Add(browse); return row;
    }
    void Append(string text) { log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}"); log.ScrollToCaret(); }
    async void Run(bool replace)
    {
        if (busy) return;
        string root = game.Text.Trim(), file = font.Text.Trim().Trim('"');
        if (root.Length == 0 || (replace && !File.Exists(file))) { Append("请先选择有效的游戏目录和字体文件。"); return; }
        busy = true; inputs.ForEach(c => c.Enabled = false); status.Text = "处理中…";
        try
        {
            if (FontService.ValidateGameDir(root) is { } error) throw new InvalidDataException(error);
            ApplyGameIcon(root, titlePicture);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            Store.Save(SettingsPath, new[] { root, file });
            var progress = new Progress<string>(Append);
            await Task.Run(() => { Action<string> report = s => ((IProgress<string>)progress).Report(s); if (replace) FontService.Install(root, file, report); else FontService.Restore(root, report); });
            status.Text = "完成";
        }
        catch (UnauthorizedAccessException) { status.Text = "没有写入权限"; Append("无法写入游戏目录。请关闭工具后右键选择“以管理员身份运行”，再重试。"); }
        catch (Exception ex) { status.Text = "操作未完成"; Append(ex.Message); }
        finally { busy = false; inputs.ForEach(c => c.Enabled = true); }
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var p = new Pen(Red); e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1); }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x84 && (int)m.Result == 1) { var p = PointToClient(Cursor.Position); if (p.X >= Width - 12 && p.Y >= Height - 12) m.Result = (IntPtr)17; }
    }
}
