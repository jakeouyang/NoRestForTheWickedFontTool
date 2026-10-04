using System.Text.Json;

namespace WickedFontTool;

public static class Legacy
{
    public static void Import(string root, Action<string> report)
    {
        string state = Path.Combine(root, "font_mod_state.json");
        if (!File.Exists(state)) return;
        string directory = Path.Combine(root, ".WickedFontTool", "runs");
        Directory.CreateDirectory(directory);
        var known = Directory.EnumerateFiles(directory, "transaction.json", SearchOption.AllDirectories)
            .Select(Store.Load<Transaction>).Where(x => x.State == "complete" && x.Action == "install")
            .SelectMany(x => x.Changes).ToList();
        using var document = JsonDocument.Parse(File.ReadAllText(state));
        if (!document.RootElement.TryGetProperty("files", out var records)) return;
        string scratch = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-legacy-" + Guid.NewGuid().ToString("N"));
        var tx = new Transaction { State = "complete" };
        foreach (var record in records.EnumerateArray())
        {
            string oldPath = record.GetProperty("path").GetString() ?? "";
            int start = oldPath.IndexOf("NoRestForTheWicked_Data", StringComparison.OrdinalIgnoreCase);
            if (start < 0) throw new InvalidDataException("旧脚本资源路径无效。");
            string relative = oldPath[start..];
            string path = Store.Within(root, relative);
            if (!File.Exists(path)) continue;
            string currentHash = Store.HashFile(path);
            if (known.Any(c => c.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase) && (c.After == currentHash || c.Original == currentHash))) continue;
            string fontHash = record.GetProperty("patched_font_hash").GetString() ?? "";
            Directory.CreateDirectory(scratch);
            using var current = new UnityFonts(path, scratch);
            var selected = current.Inspect().Where(f => f.Target).ToList();
            if (selected.Count == 0 || !selected.All(f => f.Hash.Equals(fontHash, StringComparison.OrdinalIgnoreCase)))
            { report("旧 Python 记录不匹配当前资源，按当前游戏版本处理：" + relative); continue; }
            string backupName = Path.GetFileName(record.GetProperty("backup").GetString() ?? "");
            string backup = Store.Within(root, Path.Combine("FontModBackups", backupName));
            if (!File.Exists(backup)) throw new IOException("检测到旧字体补丁，但原版备份丢失：" + backup);
            report("验证旧 Python 备份的全部非字体内容：" + relative);
            using var original = new UnityFonts(backup, scratch);
            if (current.ContentIdentity() != original.ContentIdentity())
                throw new IOException("旧备份与当前游戏的非字体内容不一致，拒绝导入：" + relative);
            string originalHash = Store.HashFile(backup);
            string destination = Store.Within(root, ".WickedFontTool/originals/" + originalHash + ".bin");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                File.Copy(backup, destination + ".new", true);
                if (Store.HashFile(destination + ".new") != originalHash) throw new IOException("导入备份校验失败。");
                File.Move(destination + ".new", destination);
            }
            if (Store.HashFile(destination) != originalHash) throw new IOException("已保存的原版备份损坏。");
            tx.Changes.Add(new Change { Relative = relative, Before = originalHash, After = currentHash, Original = originalHash });
            Store.Save(Path.Combine(scratch, "transaction.json"), tx);
            report("旧备份验证并接管成功：" + relative);
        }
    }
}
