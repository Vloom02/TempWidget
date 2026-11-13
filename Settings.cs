using System.IO;

namespace TempWidget;

/// <summary>The user's choices, kept as key=value lines in %APPDATA%\TempWidget\ajustes.txt.</summary>
static class Settings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempWidget", "ajustes.txt");
    static readonly Dictionary<string, string> values = Load();

    /// <summary>When set, changes stay in memory (used while rendering the style sheets).</summary>
    public static bool ReadOnly { get; set; }

    public static string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);

    public static void Set(string key, string value)
    {
        values[key] = value;
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, values.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // settings are a convenience: never fail over them
    }

    static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadAllLines(FilePath))
                if (line.Split('=', 2) is [var k, var v]) d[k.Trim()] = v.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // first run: no file yet
        return d;
    }
}
