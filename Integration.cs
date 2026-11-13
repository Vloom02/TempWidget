using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace TempWidget;

/// <summary>Starting with Windows, through the current user's Run key (no administrator rights needed).</summary>
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Value = "TempWidget";

    public static bool IsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Value) is string;
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Value, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(Value, throwOnMissingValue: false);
    }
}

/// <summary>Daily CSV files of the readings, in the user's list separator so a spreadsheet opens them as columns.</summary>
static class ReadingLog
{
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempWidget", "registro");

    public static void Write(IEnumerable<Reading> rows, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        string sep = culture.TextInfo.ListSeparator;
        string file = Path.Combine(Folder, now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
        try
        {
            Directory.CreateDirectory(Folder);
            var lines = rows.Select(r => Line(now, r, sep, culture)).ToList();
            if (!File.Exists(file)) lines.Insert(0, string.Join(sep, "fecha", "componente", "modelo", "temperatura_c", "carga_pct"));
            File.AppendAllLines(file, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // a locked file (open in a spreadsheet) skips one row
    }

    internal static string Line(DateTime time, Reading r, string sep, CultureInfo culture) => string.Join(sep,
        time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), r.Label, r.Name.Replace(sep, " "),
        r.Temp?.ToString("0.0", culture) ?? "", r.Load?.ToString("0.0", culture) ?? "");

    public static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
    }
}

/// <summary>
/// PawnIO is the kernel driver LibreHardwareMonitor needs for CPU temperature, power and clocks and for motherboard
/// sensors. It is installed separately by the user; the widget only checks for it and points to its site.
/// </summary>
static class PawnIO
{
    public const string Site = "https://pawnio.eu";

    public static bool Installed()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }
}
