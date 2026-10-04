using System.Text.Json;

namespace WickedFontTool;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public string UiLanguage { get; set; } = "en";
    public string GameDir { get; set; } = "";
    public string FontFile { get; set; } = "";
    public List<string> Languages { get; set; } = ["SC", "TC", "EN"];

    public IReadOnlySet<string> Selected() => new HashSet<string>(
        Languages.Where(x => L10n.User.Contains(x)), StringComparer.OrdinalIgnoreCase);

    public static AppSettings Load(string path)
    {
        // v1 stored a plain [gameDir, fontFile] pair.
        try
        {
            var legacy = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
            if (legacy is { Length: 2 }) return new AppSettings { GameDir = legacy[0], FontFile = legacy[1] };
        }
        catch (JsonException) { }
        var settings = Store.Load<AppSettings>(path);
        if (settings.Languages.Count == 0) settings.Languages = ["SC", "TC", "EN"];
        return settings;
    }
}
