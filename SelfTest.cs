namespace WickedFontTool;

public static class SelfTest
{
    public static void Run(string output)
    {
        string root = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        void Reject(Action action, string name) { try { action(); } catch { Console.WriteLine("PASS: " + name); return; } throw new Exception("FAIL: " + name); }
        Reject(() => Store.Within(root, "../escape"), "path traversal");
        Reject(() => FontCheck.Validate(new byte[100]), "invalid font");
        Check(UnityFonts.IsTarget("NotoSerifSC-Regular", L10n.User) && UnityFonts.IsTarget("NotoSerifTC-Bold", L10n.User) &&
              UnityFonts.IsTarget("Noto Serif CJK JP Bold", L10n.All) && !UnityFonts.IsTarget("NotoColorEmoji", L10n.All) &&
              !UnityFonts.IsTarget("NotoSerif-Regular", L10n.User) && !UnityFonts.IsTarget("LiberationSans", L10n.User) &&
              !UnityFonts.IsTarget("Arcon-Regular", L10n.User) && !UnityFonts.IsTarget("NotoSerifJP-Regular", L10n.User), "font selection");
        File.WriteAllText(Path.Combine(root, "game"), "original");
        File.WriteAllText(Path.Combine(root, "stage"), "patched");
        var c = new Change { Relative = "game", Stage = "stage", Rollback = "rollback", Before = Store.HashFile(Path.Combine(root, "game")), After = Store.HashFile(Path.Combine(root, "stage")) };
        var tx = new Transaction { Changes = new() { c } };
        string journal = Path.Combine(root, "transaction.json");
        Store.Commit(root, journal, tx, Console.WriteLine);
        Check(File.ReadAllText(Path.Combine(root, "game")) == "patched" && File.ReadAllText(Path.Combine(root, "rollback")) == "original", "atomic commit and backup");
        tx.State = "committing"; Store.Save(journal, tx);
        Store.Recover(root, journal, tx, Console.WriteLine);
        Check(File.ReadAllText(Path.Combine(root, "game")) == "original", "interrupted transaction recovery");
        File.WriteAllText(Path.Combine(root, "game"), "updated game");
        Reject(() => Store.Recover(root, journal, tx, Console.WriteLine), "refuse old backup over updated game");
        Check(File.ReadAllText(Path.Combine(root, "game")) == "updated game", "update preserved");
        Console.WriteLine("TESTS PASSED: " + root);
    }
}
