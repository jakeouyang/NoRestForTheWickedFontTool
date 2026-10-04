using System.Diagnostics;
using System.Text;

namespace WickedFontTool;

public static class FontService
{
    const string Data = "NoRestForTheWicked_Data";
    const string Work = ".WickedFontTool";
    public static string? ValidateGameDir(string root)
    {
        if (!File.Exists(Path.Combine(root, "NoRestForTheWicked.exe"))) return "请选择包含 NoRestForTheWicked.exe 的游戏根目录。";
        if (!File.Exists(Path.Combine(root, "UnityPlayer.dll")) || !Directory.Exists(Path.Combine(root, Data)))
            return "游戏目录不完整：缺少 UnityPlayer.dll 或 NoRestForTheWicked_Data。";
        return null;
    }
    static void Validate(string root)
    {
        if (ValidateGameDir(root) is { } error) throw new InvalidDataException(error);
        if (Process.GetProcessesByName("NoRestForTheWicked").Length > 0)
            throw new IOException("请先退出恶意不息，再替换或还原字体。");
    }
    static string Relative(string root, string path) => Path.GetRelativePath(root, path);

    static FileStream Lock(string root, Action<string> report)
    {
        string dir = Store.Within(root, Work + "/lock");
        Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
        var handle = new FileStream(dir, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            foreach (string manifest in Journals(root))
            {
                var tx = Store.Load<Transaction>(manifest);
                if (tx.State == "committing") Store.Recover(root, manifest, tx, report);
            }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }
    static IEnumerable<string> Journals(string root) => Directory.Exists(Path.Combine(root, Work, "runs"))
        ? Directory.EnumerateFiles(Path.Combine(root, Work, "runs"), "transaction.json", SearchOption.AllDirectories).OrderByDescending(x => x)
        : Enumerable.Empty<string>();

    public static List<string> Containers(string root)
    {
        var paths = new List<string>();
        string data = Path.Combine(root, Data);
        foreach (string path in Directory.EnumerateFiles(data))
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".assets", StringComparison.OrdinalIgnoreCase) || name == "globalgamemanagers" ||
                (name.StartsWith("level", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[5..], out _)))
                paths.Add(path);
        }
        string streaming = Path.Combine(data, "StreamingAssets");
        if (Directory.Exists(streaming))
        {
            // Detect UnityFS by signature, even when a later build changes the suffix.
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string path in Directory.EnumerateFiles(streaming, "*", options))
            {
                if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) continue;
                using var stream = File.OpenRead(path);
                var magic = new byte[8];
                if (stream.Read(magic) == 8 && Encoding.ASCII.GetString(magic) == "UnityFS\0") paths.Add(path);
            }
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => new FileInfo(x).Length).ToList();
    }

    public static void Install(string root, string fontPath, Action<string> report)
    {
        root = Path.GetFullPath(root); Validate(root);
        byte[] font = File.ReadAllBytes(fontPath);
        FontCheck.Validate(font);
        using var handle = Lock(root, report);
        Legacy.Import(root, report);
        string id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        string run = Store.Within(root, Work + "/runs/" + id + "/transaction.json");
        Directory.CreateDirectory(Path.GetDirectoryName(run)!);
        string scratch = Path.GetDirectoryName(run)!;
        var tx = new Transaction(); Store.Save(run, tx);
        var history = Journals(root).Where(x => x != run).Select(Store.Load<Transaction>)
            .Where(x => x.State == "complete" && x.Action == "install").ToList();
        var candidates = Containers(root);
        var targets = new List<(string Path, List<FontObject> Fonts, long Expanded)>();
        var inventory = new List<object>();
        var errors = new List<string>();
        report($"检查 {candidates.Count} 个资源容器，字体位置不依赖 bundle 文件名。首次扫描可能需要几分钟。");
        foreach (string path in candidates)
        {
            report("扫描：" + Relative(root, path));
            try
            {
                using var reader = new UnityFonts(path, scratch);
                var fonts = reader.Inspect();
                inventory.Add(new { File = Relative(root, path), Fonts = fonts });
                if (fonts.Any(f => f.Target)) targets.Add((path, fonts, reader.ExpandedSize));
            }
            catch (Exception ex) { errors.Add(Relative(root, path) + ": " + ex.Message); }
        }
        Store.Save(Path.Combine(scratch, "scan.json"), new { Inventory = inventory, Errors = errors });
        if (errors.Count > 0) throw new InvalidDataException("有资源无法解析，未修改游戏。诊断：" + Path.Combine(scratch, "scan.json") + "\n" + string.Join("\n", errors.Take(4)));
        if (targets.Count == 0) throw new InvalidDataException("未找到已知 CJK 字体。游戏可能调整了字体名称或渲染方式。诊断：" + Path.Combine(scratch, "scan.json"));
        string fontHash = Store.Hash(font);
        long required = targets.Sum(x => new FileInfo(x.Path).Length * 2 + x.Expanded * 2) + 512L * 1024 * 1024;
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < required)
            throw new IOException($"备份和临时打包预计需 {required / 1073741824.0:F1} GB 可用空间。");
        report($"找到 {targets.Count} 个容器、{targets.Sum(x => x.Fonts.Count(f => f.Target))} 个目标字体。");
        try
        {
            foreach (var target in targets)
            {
                if (target.Fonts.Where(f => f.Target).All(f => f.Hash == fontHash))
                {
                    report("字体已一致，跳过：" + Relative(root, target.Path)); continue;
                }
                string relative = Relative(root, target.Path);
                string before = Store.HashFile(target.Path);
                var previous = history.SelectMany(x => x.Changes).FirstOrDefault(c => c.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase) && c.After == before);
                string original = previous?.Original ?? before;
                string backup = Backup(root, original);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (!File.Exists(backup))
                {
                    if (previous != null) throw new IOException("当前补丁对应的原始备份已丢失：" + backup);
                    report("备份当前版本：" + relative);
                    File.Copy(target.Path, backup + ".new", true);
                    if (Store.HashFile(backup + ".new") != before) throw new IOException("备份校验失败。");
                    File.Move(backup + ".new", backup);
                }
                if (Store.HashFile(backup) != original) throw new IOException("原始备份校验失败：" + backup);
                string stage = Path.Combine(scratch, tx.Changes.Count + ".stage");
                report("生成：" + relative);
                List<FontObject> source;
                string identity;
                using (var reader = new UnityFonts(backup, scratch))
                {
                    source = reader.Inspect(); identity = reader.ContentIdentity(); reader.Write(stage, font, report);
                }
                using (var verify = new UnityFonts(stage, scratch))
                {
                    var actual = verify.Inspect();
                    if (actual.Count != source.Count || source.Any(s => !actual.Any(a => a.Entry == s.Entry && a.Id == s.Id && a.Name == s.Name && a.Hash == (s.Target ? fontHash : s.Hash))))
                        throw new IOException("重新读取校验失败，未替换游戏文件：" + relative);
                    report("核对全部非字体资源：" + relative);
                    if (verify.ContentIdentity() != identity) throw new IOException("非字体资源一致性校验失败：" + relative);
                }
                tx.Changes.Add(new Change { Relative = relative, Before = before, After = Store.HashFile(stage), Original = original,
                    Stage = Relative(root, stage), Rollback = Relative(root, Path.Combine(scratch, tx.Changes.Count + ".rollback")) });
                Store.Save(run, tx);
            }
            Validate(root);
            Store.Commit(root, run, tx, report);
            report("完成。请在游戏内检查此前混合字形的文本；备份位于 " + Path.Combine(root, Work, "originals"));
        }
        catch
        {
            if (tx.State == "preparing") { tx.State = "aborted"; Store.Save(run, tx); }
            throw;
        }
    }

    static string Backup(string root, string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("备份摘要无效。");
        return Store.Within(root, Work + "/originals/" + hash + ".bin");
    }

    public static void Restore(string root, Action<string> report)
    {
        root = Path.GetFullPath(root); Validate(root);
        using var handle = Lock(root, report);
        Legacy.Import(root, report);
        var history = Journals(root).Select(Store.Load<Transaction>).Where(t => t.State == "complete" && t.Action == "install").ToList();
        var groups = history.SelectMany(t => t.Changes).GroupBy(c => c.Relative, StringComparer.OrdinalIgnoreCase);
        string dir = Store.Within(root, Work + "/runs/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string journal = Path.Combine(dir, "transaction.json");
        var tx = new Transaction { Action = "restore" }; Store.Save(journal, tx);
        foreach (var group in groups)
        {
            string path = Store.Within(root, group.Key);
            if (!File.Exists(path)) { report("更新后文件已移除，跳过：" + group.Key); continue; }
            string current = Store.HashFile(path);
            var entry = group.FirstOrDefault(c => c.After == current);
            if (entry == null)
            {
                report(group.Any(c => c.Original == current) ? "已是原版：" + group.Key : "文件已更新或由其他工具修改，保留当前文件：" + group.Key);
                continue;
            }
            string backup = Backup(root, entry.Original);
            if (!File.Exists(backup) || Store.HashFile(backup) != entry.Original) throw new IOException("原始备份丢失或损坏：" + backup);
            string stage = Path.Combine(dir, tx.Changes.Count + ".stage"); File.Copy(backup, stage);
            tx.Changes.Add(new Change { Relative = group.Key, Before = current, After = entry.Original, Original = entry.Original,
                Stage = Relative(root, stage), Rollback = Relative(root, Path.Combine(dir, tx.Changes.Count + ".rollback")) });
            Store.Save(journal, tx);
        }
        if (tx.Changes.Count == 0)
        {
            tx.State = "complete"; Store.Save(journal, tx);
            report("没有与当前文件匹配的可还原补丁，未修改游戏文件。"); return;
        }
        Validate(root); Store.Commit(root, journal, tx, report);
        report("还原完成，备份保留。");
    }
}

public static class FontCheck
{
    // Validate the sfnt directory, not only the extension, before changing game files.
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length < 12 || bytes.Length > 128 * 1024 * 1024) throw new InvalidDataException("字体文件大小无效。");
        uint tag = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (tag != 0x00010000 && tag != 0x4F54544F) throw new InvalidDataException("请选择独立 TTF 或 OTF 字体，暂不支持 TTC 字体集合。");
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        if (count == 0 || 12L + count * 16 > bytes.Length) throw new InvalidDataException("字体表目录无效。");
        var tags = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            int pos = 12 + i * 16;
            uint offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 8));
            uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 12));
            if ((ulong)offset + size > (ulong)bytes.Length) throw new InvalidDataException("字体表越界，文件可能损坏。");
            tags.Add(Encoding.ASCII.GetString(bytes, pos, 4));
        }
        if (!tags.Contains("cmap") || !tags.Contains("head") || !tags.Contains("name")) throw new InvalidDataException("字体缺少必要数据表。");
    }
}
