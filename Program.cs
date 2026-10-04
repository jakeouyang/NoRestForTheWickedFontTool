using System.Runtime.InteropServices;
namespace WickedFontTool;

internal static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 0) { Application.Run(new MainForm()); return 0; }
        AttachConsole(-1);
        try
        {
            switch (args)
            {
                case ["install", var root, var font]: FontService.Install(root, font, Console.WriteLine); break;
                case ["restore", var root]: FontService.Restore(root, Console.WriteLine); break;
                case ["inspect", var path, var output]:
                    using (var fonts = new UnityFonts(path, Path.GetDirectoryName(Path.GetFullPath(output))!)) Store.Save(output, fonts.Inspect());
                    break;
                case ["identity", var path, var output]:
                    using (var fonts = new UnityFonts(path, Path.GetDirectoryName(Path.GetFullPath(output))!)) File.WriteAllText(output, fonts.ContentIdentity());
                    break;
                case ["patch-copy", var path, var font, var output]:
                    var bytes = File.ReadAllBytes(font); FontCheck.Validate(bytes);
                    using (var fonts = new UnityFonts(path, Path.GetDirectoryName(Path.GetFullPath(output))!)) fonts.Write(output, bytes, Console.WriteLine);
                    break;
                case ["scan", var root, var output]:
                    var scan = new List<object>();
                    foreach (string path in FontService.Containers(root))
                    {
                        Console.WriteLine(path);
                        using var container = new UnityFonts(path, Path.GetDirectoryName(Path.GetFullPath(output))!);
                        scan.Add(new { Path = path, Fonts = container.Inspect() });
                    }
                    Store.Save(output, scan);
                    break;
                case ["--render-ui", var output]:
                    using (var form = new MainForm(loadSavedPaths: false))
                    {
                        form.Show(); Application.DoEvents(); Thread.Sleep(250); form.Refresh(); Application.DoEvents();
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(output);
                    }
                    break;
                case ["extract-icon", var exe, var output]:
                    using (var icon = Icon.ExtractAssociatedIcon(exe) ?? throw new IOException("没有找到游戏图标。"))
                    using (var stream = File.Create(output)) icon.Save(stream);
                    break;
                case ["--self-test", var output]: SelfTest.Run(output); break;
                case ["--integration-test", var root, var font, var output]: IntegrationTest.Run(root, font, output); break;
                default: throw new ArgumentException("install <game> <font> | restore <game> | inspect <asset> <json> | --capture-ui <png> | --self-test <directory>");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
