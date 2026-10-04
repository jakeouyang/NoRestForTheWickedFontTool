using System.Diagnostics;
using System.Text;

namespace WickedFontTool;

public static class FontService
{
    const string Data = "NoRestForTheWicked_Data";
    const string Work = ".WickedFontTool";
    public static string? ValidateGameDir(string root)
    {
        if (!File.Exists(Path.Combine(root, "NoRestForTheWicked.exe"))) return L10n.S("请选择包含 NoRestForTheWicked.exe 的游戏根目录。", "請選擇包含 NoRestForTheWicked.exe 的遊戲根目錄。");
        if (!File.Exists(Path.Combine(root, "UnityPlayer.dll")) || !Directory.Exists(Path.Combine(root, Data)))
            return L10n.S("游戏目录不完整：缺少 UnityPlayer.dll 或 NoRestForTheWicked_Data。", "遊戲目錄不完整：缺少 UnityPlayer.dll 或 NoRestForTheWicked_Data。");
        return null;
    }
    static void Validate(string root)
    {
        if (ValidateGameDir(root) is { } error) throw new InvalidDataException(error);
        if (Process.GetProcessesByName("NoRestForTheWicked").Length > 0)
            throw new IOException(L10n.S("请先退出恶意不息，再替换或还原字体。", "請先退出惡意不息，再替換或還原字體。"));
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

    public static void Install(string root, string fontPath, IReadOnlySet<string>? languages, Action<string> report)
    {
        root = Path.GetFullPath(root); Validate(root);
        var selected = languages ?? L10n.User;
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
        report(L10n.S($"检查 {candidates.Count} 个资源容器，字体位置不依赖 bundle 文件名。首次扫描可能需要几分钟。",
            $"檢查 {candidates.Count} 個資源容器，字體位置不依賴 bundle 檔案名稱。首次掃描可能需要幾分鐘。"));
        foreach (string path in candidates)
        {
            report(L10n.S("扫描：", "掃描：") + Relative(root, path));
            try
            {
                using var reader = new UnityFonts(path, scratch, selected);
                var fonts = reader.Inspect();
                inventory.Add(new { File = Relative(root, path), Fonts = fonts });
                if (fonts.Any(f => f.Target)) targets.Add((path, fonts, reader.ExpandedSize));
            }
            catch (Exception ex) { errors.Add(Relative(root, path) + ": " + ex.Message); }
        }
        Store.Save(Path.Combine(scratch, "scan.json"), new { Inventory = inventory, Errors = errors });
        if (errors.Count > 0) throw new InvalidDataException(L10n.S("有资源无法解析，未修改游戏。诊断：", "有資源無法解析，未修改遊戲。診斷：") + Path.Combine(scratch, "scan.json") + "\n" + string.Join("\n", errors.Take(4)));
        if (targets.Count == 0) throw new InvalidDataException(L10n.S("未找到所选语言的字体。游戏可能调整了字体名称或渲染方式。诊断：", "未找到所選語言的字體。遊戲可能調整了字體名稱或渲染方式。診斷：") + Path.Combine(scratch, "scan.json"));
        string fontHash = Store.Hash(font);
        long required = targets.Sum(x => new FileInfo(x.Path).Length * 2 + x.Expanded * 2) + 512L * 1024 * 1024;
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < required)
            throw new IOException(L10n.S($"备份和临时打包预计需 {required / 1073741824.0:F1} GB 可用空间。", $"備份和暫存預計需要 {required / 1073741824.0:F1} GB 可用空間。"));
        report(L10n.S($"找到 {targets.Count} 个容器、{targets.Sum(x => x.Fonts.Count(f => f.Target))} 个目标字体（{string.Join(" / ", selected.Select(L10n.Name))}）。",
            $"找到 {targets.Count} 個容器、{targets.Sum(x => x.Fonts.Count(f => f.Target))} 個目標字體（{string.Join(" / ", selected.Select(L10n.Name))}）。"));
        try
        {
            foreach (var target in targets)
            {
                string relative = Relative(root, target.Path);
                List<FontObject> source;
                string identity;
                string before;
                string original;
                string backup;
                string stage;
                using (var reader = new UnityFonts(target.Path, scratch, selected))
                {
                    if (target.Fonts.Where(f => f.Target).All(f => f.Hash == fontHash))
                    {
                        report(L10n.S("字体已一致，跳过：", "字體已一致，跳過：") + relative);
                        continue;
                    }
                    before = Store.HashFile(target.Path);
                    var previous = history.SelectMany(x => x.Changes).FirstOrDefault(c => c.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase) && c.After == before);
                    original = previous?.Original ?? before;
                    backup = Backup(root, original);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    if (!File.Exists(backup))
                    {
                        if (previous != null) throw new IOException(L10n.S("当前补丁对应的原始备份已丢失：", "當前補丁對應的原始備份已丟失：") + backup);
                        report(L10n.S("备份当前版本：", "備份目前版本：") + relative);
                        File.Copy(target.Path, backup + ".new", true);
                        if (Store.HashFile(backup + ".new") != before) throw new IOException(L10n.S("备份校验失败。", "備份校驗失敗。"));
                        File.Move(backup + ".new", backup);
                    }
                    if (Store.HashFile(backup) != original) throw new IOException(L10n.S("原始备份校验失败：", "原始備份校驗失敗：") + backup);
                    stage = Path.Combine(scratch, tx.Changes.Count + ".stage");
                    report(L10n.S("生成：", "生成：") + relative);
                    source = reader.Inspect(); identity = reader.ContentIdentity(); reader.Write(stage, font, report);
                }
                using (var verify = new UnityFonts(stage, scratch, selected))
                {
                    var actual = verify.Inspect();
                    if (actual.Count != source.Count || source.Any(s => !actual.Any(a => a.Entry == s.Entry && a.Id == s.Id && a.Name == s.Name && a.Hash == (s.Target ? fontHash : s.Hash))))
                        throw new IOException(L10n.S("重新读取校验失败，未替换游戏文件：", "重新讀取校驗失敗，未替換遊戲檔案：") + relative);
                    report(L10n.S("核对全部非字体资源：", "核對全部非字體資源：") + relative);
                    if (verify.ContentIdentity() != identity) throw new IOException(L10n.S("非字体资源一致性校验失败：", "非字體資源一致性校驗失敗：") + relative);
                }
                tx.Changes.Add(new Change { Relative = relative, Before = before, After = Store.HashFile(stage), Original = original,
                    Stage = Relative(root, stage), Rollback = Relative(root, Path.Combine(scratch, tx.Changes.Count + ".rollback")) });
                Store.Save(run, tx);
            }
            Validate(root);
            Store.Commit(root, run, tx, report);
            report(L10n.S("完成。请在游戏内检查此前混合字形的文本；备份位于 ", "完成。請在遊戲內檢查此前混合字形的文本；備份位於 ") + Path.Combine(root, Work, "originals"));
        }
        catch
        {
            if (tx.State == "preparing") { tx.State = "aborted"; Store.Save(run, tx); }
            throw;
        }
    }

    static string Backup(string root, string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException(L10n.S("备份摘要无效。", "備份摘要無效。"));
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
            if (!File.Exists(path)) { report(L10n.S("更新后文件已移除，跳过：", "更新後檔案已移除，跳過：") + group.Key); continue; }
            string current = Store.HashFile(path);
            var entry = group.FirstOrDefault(c => c.After == current);
            if (entry == null)
            {
                report(group.Any(c => c.Original == current) ? L10n.S("已是原版：", "已是原版：") + group.Key
                    : L10n.S("文件已更新或由其他工具修改，保留当前文件：", "檔案已更新或由其他工具修改，保留目前檔案：") + group.Key);
                continue;
            }
            string backup = Backup(root, entry.Original);
            if (!File.Exists(backup) || Store.HashFile(backup) != entry.Original) throw new IOException(L10n.S("原始备份丢失或损坏：", "原始備份丟失或損壞：") + backup);
            string stage = Path.Combine(dir, tx.Changes.Count + ".stage"); File.Copy(backup, stage);
            tx.Changes.Add(new Change { Relative = group.Key, Before = current, After = entry.Original, Original = entry.Original,
                Stage = Relative(root, stage), Rollback = Relative(root, Path.Combine(dir, tx.Changes.Count + ".rollback")) });
            Store.Save(journal, tx);
        }
        if (tx.Changes.Count == 0)
        {
            tx.State = "complete"; Store.Save(journal, tx);
            report(L10n.S("没有与当前文件匹配的可还原补丁，未修改游戏文件。", "沒有與目前檔案匹配的可還原補丁，未修改遊戲檔案。"));
            return;
        }
        Validate(root); Store.Commit(root, journal, tx, report);
        report(L10n.S("还原完成，备份保留。", "還原完成，備份保留。"));
    }
}

public static class FontCheck
{
    // Validate the sfnt directory, not only the extension, before changing game files.
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length < 12 || bytes.Length > 128 * 1024 * 1024) throw new InvalidDataException(L10n.S("字体文件大小无效。", "字體檔案大小無效。"));
        uint tag = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (tag != 0x00010000 && tag != 0x4F54544F) throw new InvalidDataException(L10n.S("请选择独立 TTF 或 OTF 字体，暂不支持 TTC 字体集合。", "請選擇獨立 TTF 或 OTF 字體，暫不支援 TTC 字體集合。"));
        int count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        if (count == 0 || 12L + count * 16 > bytes.Length) throw new InvalidDataException(L10n.S("字体表目录无效。", "字體表目錄無效。"));
        var tags = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            int pos = 12 + i * 16;
            uint offset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 8));
            uint size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 12));
            if ((ulong)offset + size > (ulong)bytes.Length) throw new InvalidDataException(L10n.S("字体表越界，文件可能损坏。", "字體表越界，檔案可能損壞。"));
            tags.Add(Encoding.ASCII.GetString(bytes, pos, 4));
        }
        if (!tags.Contains("cmap") || !tags.Contains("head") || !tags.Contains("name")) throw new InvalidDataException(L10n.S("字体缺少必要数据表。", "字體缺少必要資料表。"));
    }
}
