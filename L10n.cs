namespace WickedFontTool;

public static class L10n
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SC", "TC", "JP", "KR" };
    public static readonly IReadOnlySet<string> User = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SC", "TC" };
    public static string Lang { get; set; } = "hans";
    public static bool Traditional => Lang.Equals("hant", StringComparison.OrdinalIgnoreCase);
    public static string S(string hans, string hant) => Traditional ? hant : hans;
    public static string OtherName => Traditional ? "简体" : "繁體";
    public static string Name(string code) => code.ToUpperInvariant() switch
    {
        "SC" => S("简体中文", "簡體中文"),
        "TC" => S("繁体中文", "繁體中文"),
        _ => code,
    };
}
