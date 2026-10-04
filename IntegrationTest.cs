namespace WickedFontTool;

public static class IntegrationTest
{
    public static void Run(string realGame, string font, string output)
    {
        string root = Path.Combine(Path.GetFullPath(output), "integration-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "NoRestForTheWicked_Data"); Directory.CreateDirectory(data);
        File.Copy(Path.Combine(realGame, "NoRestForTheWicked.exe"), Path.Combine(root, "NoRestForTheWicked.exe"));
        File.Copy(Path.Combine(realGame, "UnityPlayer.dll"), Path.Combine(root, "UnityPlayer.dll"));
        string original = Path.Combine(realGame, "FontModBackups", "NoRestForTheWicked_Data__resources.assets.original.bak");
        if (!File.Exists(original))
        {
            var entry = Directory.EnumerateFiles(Path.Combine(realGame, ".WickedFontTool", "runs"), "transaction.json", SearchOption.AllDirectories)
                .Select(Store.Load<Transaction>).Where(t => t.State == "complete" && t.Action == "install")
                .SelectMany(t => t.Changes).First(c => c.Relative == "NoRestForTheWicked_Data\\resources.assets");
            original = Store.Within(realGame, ".WickedFontTool/originals/" + entry.Original + ".bin");
            if (Store.HashFile(original) != entry.Original) throw new IOException("测试夹具备份校验失败。");
        }
        string asset = Path.Combine(data, "resources.assets"); File.Copy(original, asset);
        string originalHash = Store.HashFile(asset);
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var langs = L10n.User;
        FontService.Install(root, font, langs, Console.WriteLine);
        string installedHash = Store.HashFile(asset);
        Check(installedHash != originalHash, "real resources.assets patched");
        using (var reader = new UnityFonts(asset, output, langs))
        {
            var targets = reader.Inspect().Where(x => x.Target).ToList();
            Check(targets.Count > 0 && targets.All(x => x.Hash == Store.HashFile(font)), $"{targets.Count} selected-language font hashes");
        }
        FontService.Install(root, font, langs, Console.WriteLine);
        Check(Store.HashFile(asset) == installedHash, "repeat install idempotent");
        FontService.Restore(root, Console.WriteLine);
        Check(Store.HashFile(asset) == originalHash, "byte-exact restore");
        string renamed = Path.Combine(data, "fonts_after_update.assets"); File.Move(asset, renamed);
        FontService.Install(root, font, langs, Console.WriteLine);
        Check(Store.HashFile(renamed) == installedHash, "rediscovery after resource rename");
        // Simulate a same-name update while a mod is installed.
        File.Copy(original, renamed, true);
        using (var stream = new FileStream(renamed, FileMode.Append)) stream.WriteByte(0);
        string updatedHash = Store.HashFile(renamed);
        FontService.Restore(root, Console.WriteLine);
        Check(Store.HashFile(renamed) == updatedHash, "restore preserves same-name update");
        File.Copy(original, renamed, true);
        FontService.Install(root, font, langs, Console.WriteLine);
        var journal = Directory.GetFiles(Path.Combine(root, ".WickedFontTool", "runs"), "transaction.json", SearchOption.AllDirectories)
            .Select(Store.Load<Transaction>).First(t => t.Changes.Any(c => c.Relative.EndsWith("fonts_after_update.assets") && t.Action == "install"));
        var c = journal.Changes[0];
        string backup = Path.Combine(root, ".WickedFontTool", "originals", c.Original + ".bin");
        File.WriteAllText(backup, "damaged");
        bool refused = false;
        try { FontService.Restore(root, Console.WriteLine); } catch (IOException) { refused = true; }
        Check(refused && Store.HashFile(renamed) == installedHash, "damaged backup rejected");
        Console.WriteLine("INTEGRATION PASSED: " + root);
    }
}
