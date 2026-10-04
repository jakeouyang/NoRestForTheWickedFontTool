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
            if (start < 0) throw new InvalidDataException(L10n.S("旧脚本资源路径无效。", "舊腳本資源路徑無效。"));
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
            { report(L10n.S("旧 Python 记录不匹配当前资源，按当前游戏版本处理：", "舊 Python 記錄不匹配目前資源，按目前遊戲版本處理：") + relative); continue; }
            string backupName = Path.GetFileName(record.GetProperty("backup").GetString() ?? "");
            string backup = Store.Within(root, Path.Combine("FontModBackups", backupName));
            if (!File.Exists(backup)) throw new IOException(L10n.S("检测到旧字体补丁，但原版备份丢失：", "檢測到舊字體補丁，但原版備份丟失：") + backup);
            report(L10n.S("验证旧 Python 备份的全部非字体内容：", "驗證舊 Python 備份的全部非字體內容：") + relative);
            using var original = new UnityFonts(backup, scratch);
            if (current.ContentIdentity() != original.ContentIdentity())
                throw new IOException(L10n.S("旧备份与当前游戏的非字体内容不一致，拒绝导入：", "舊備份與目前遊戲的非字體內容不一致，拒絕匯入：") + relative);
            string originalHash = Store.HashFile(backup);
            string destination = Store.Within(root, ".WickedFontTool/originals/" + originalHash + ".bin");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination))
            {
                File.Copy(backup, destination + ".new", true);
                if (Store.HashFile(destination + ".new") != originalHash) throw new IOException(L10n.S("导入备份校验失败。", "匯入備份校驗失敗。"));
                File.Move(destination + ".new", destination);
            }
            if (Store.HashFile(destination) != originalHash) throw new IOException(L10n.S("已保存的原版备份损坏。", "已保存的原版備份損壞。"));
            tx.Changes.Add(new Change { Relative = relative, Before = originalHash, After = currentHash, Original = originalHash });
            Store.Save(Path.Combine(scratch, "transaction.json"), tx);
            report(L10n.S("旧备份验证并接管成功：", "舊備份驗證並接管成功：") + relative);
        }
    }
}
