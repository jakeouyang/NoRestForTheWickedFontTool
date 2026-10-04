using System.Security.Cryptography;
using System.Text.Json;

namespace WickedFontTool;

public sealed class Change
{
    public string Relative { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string Original { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Rollback { get; set; } = "";
}
public sealed class Transaction
{
    public string State { get; set; } = "preparing";
    public string Action { get; set; } = "install";
    public List<Change> Changes { get; set; } = new();
}

public static class Store
{
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static void Save<T>(string path, T value)
    {
        string temp = path + ".new";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
    public static T Load<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path))
        ?? throw new InvalidDataException(L10n.S($"记录损坏：{path}", $"記錄損壞：{path}"));
    public static string Within(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(L10n.S("记录路径超出游戏目录。", "記錄路徑超出遊戲目錄。"));
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir != null && dir.FullName.Length >= prefix.Length; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(L10n.S("资源目录包含链接，无法安全写入。", "資源目錄包含連結，無法安全寫入。"));
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(L10n.S("资源文件包含链接，无法安全写入。", "資源檔案包含連結，無法安全寫入。"));
        return path;
    }

    public static void Commit(string root, string journal, Transaction tx, Action<string> report)
    {
        foreach (var c in tx.Changes)
            if (HashFile(Within(root, c.Relative)) != c.Before || HashFile(Within(root, c.Stage)) != c.After)
                throw new IOException(L10n.S("资源在操作过程中发生变化，已停止安装。", "資源在操作過程中發生變化，已停止安裝。"));
        tx.State = "committing"; Save(journal, tx);
        try
        {
            foreach (var c in tx.Changes)
            {
                if (HashFile(Within(root, c.Relative)) != c.Before)
                    throw new IOException(L10n.S("提交时发现资源已被外部修改。", "提交時發現資源已被外部修改。"));
                File.Replace(Within(root, c.Stage), Within(root, c.Relative), Within(root, c.Rollback));
                if (HashFile(Within(root, c.Relative)) != c.After) throw new IOException(L10n.S("写入后校验失败。", "寫入後校驗失敗。"));
                report(L10n.S(tx.Action == "restore" ? "已还原：" : "已替换：", tx.Action == "restore" ? "已還原：" : "已替換：") + c.Relative);
            }
            tx.State = "complete"; Save(journal, tx);
        }
        catch { Recover(root, journal, tx, report); throw; }
    }

    public static void Recover(string root, string journal, Transaction tx, Action<string> report)
    {
        var errors = new List<string>();
        foreach (var c in tx.Changes.AsEnumerable().Reverse())
        {
            string path = Within(root, c.Relative), rollback = Within(root, c.Rollback);
            if (!File.Exists(path)) { errors.Add(L10n.S($"待恢复的文件已丢失：{path}", $"待恢復的檔案已丟失：{path}")); continue; }
            string hash = HashFile(path);
            if (hash == c.Before) continue;
            if (hash != c.After || !File.Exists(rollback) || HashFile(rollback) != c.Before)
            { errors.Add(L10n.S($"中断恢复发现外部修改，请保留备份并检查：{path}", $"中斷恢復發現外部修改，請保留備份並檢查：{path}")); continue; }
            File.Replace(rollback, path, null);
        }
        if (errors.Count > 0) throw new IOException(string.Join(Environment.NewLine, errors));
        tx.State = "rolled-back"; Save(journal, tx);
        report(L10n.S("已恢复中断操作之前的资源。", "已恢復中斷操作之前的資源。"));
    }
}
