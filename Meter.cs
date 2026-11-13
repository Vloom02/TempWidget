using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using LibreHardwareMonitor.Hardware;

namespace TempWidget;

/// <summary>One card of the widget: a component with its temperature, load and recent history.</summary>
public sealed class Meter(string key, string label, string name) : INotifyPropertyChanged
{
    public const double SparkW = 150, SparkH = 26;
    const int History = 60;
    // Gauge ring: 62 px circle, 6 px stroke. Dash units are multiples of the stroke width.
    const double RingUnits = Math.PI * (62 - 6) / 6;

    readonly Queue<double> history = new();
    float? temp, load, low, high; // low/high: session range of what the ring shows

    /// <summary>Show temperatures in Fahrenheit. Readings, levels and alerts stay in Celsius.</summary>
    public static bool Fahrenheit { get; set; }
    public static string Deg(float celsius) => Fahrenheit ? $"{celsius * 9 / 5 + 32:0}°F" : $"{celsius:0}°";

    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Name { get; } = name;
    public Alarm Alarm { get; } = new();
    public float? Temp => temp;

    /// <summary>The ring shows the temperature, or the load when the component reports no temperature.</summary>
    double Value => Math.Clamp(temp ?? load ?? 0, 0, 100);
    public string ValueText => temp is { } t ? Deg(t) : load is { } l ? $"{l:0}%" : "—";
    string Shown(float v) => temp is not null ? Deg(v) : $"{v:0}%";
    public string RangeText => low is { } a && high is { } b ? $"{Shown(a)} / {Shown(b)}" : "—";
    /// <summary>Which ring color of the current style to use: Plain (no temperature), Cool, Warm or Hot.</summary>
    public string Level => temp switch { null => "Plain", < 60 => "Cool", < 80 => "Warm", _ => "Hot" };
    public DoubleCollection Dash => [Value / 100 * RingUnits, 100];
    public string LoadText => load is { } l ? $"Carga {l:0}%" : "";
    public Visibility LoadVis => temp is not null && load is not null ? Visibility.Visible : Visibility.Collapsed;
    public GridLength LoadStar => new(Math.Clamp(load ?? 0, 0, 100), GridUnitType.Star);
    public GridLength RestStar => new(100 - Math.Clamp(load ?? 0, 0, 100), GridUnitType.Star);

    /// <summary>History as a closed area (baseline included), drawn on a fixed 0–100 scale.</summary>
    public PointCollection Points
    {
        get
        {
            var pts = new PointCollection { new(0, SparkH) };
            int i = History - history.Count;
            double x = 0;
            foreach (var v in history)
            {
                x = i++ * SparkW / (History - 1);
                if (pts.Count == 1) pts[0] = new(x, SparkH);
                pts.Add(new(x, SparkH - v / 100 * SparkH));
            }
            pts.Add(new(x, SparkH));
            return pts;
        }
    }

    /// <summary>Extra readings from the sensors.</summary>
    public IReadOnlyList<Detail> Details { get; private set; } = [];
    /// <summary>What the bar lists: three extras and the session range, all that fits its height.</summary>
    public IEnumerable<Detail> BarDetails => Details.Take(3).Append(new("Mín / máx", RangeText));
    /// <summary>Hover text: everything known about the component.</summary>
    public string Tip => string.Join("\n", Details.Select(d => $"{d.Label}: {d.Value}")
        .Prepend($"{Label} · {Name}").Append($"Mínimo / máximo desde que se abrió: {RangeText}"));
    public bool Wide { get; set; }
    public Visibility DetailsVis => Wide ? Visibility.Visible : Visibility.Collapsed;
    public void Notify() => PropertyChanged?.Invoke(this, new(null));

    public void Set(float? newTemp, float? newLoad, IReadOnlyList<Detail> details)
    {
        // A component that switches between reporting a temperature and not would mix two scales in one range.
        if ((temp is null) != (newTemp is null)) ResetRange();
        (temp, load, Details) = (newTemp, newLoad, details);
        if ((temp ?? load) is { } v) (low, high) = (Math.Min(low ?? v, v), Math.Max(high ?? v, v));
        history.Enqueue(Value);
        if (history.Count > History) history.Dequeue();
        Notify(); // null property name = every property changed
    }

    public void ResetRange()
    {
        (low, high) = (null, null);
        Notify();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Decides when a temperature deserves a warning: five readings in a row at or over the limit.</summary>
public sealed class Alarm
{
    public const int Readings = 5, Hysteresis = 5; // °C
    int hot;
    bool armed = true;

    /// <summary>True once per episode; it re-arms after the component cools clearly below the limit.</summary>
    public bool Check(float? celsius, float limit)
    {
        if (celsius is not { } t || t < limit)
        {
            hot = 0;
            if (celsius is not { } c || c < limit - Hysteresis) armed = true;
            return false;
        }
        if (++hot < Readings || !armed) return false;
        armed = false;
        return true;
    }
}

public sealed record Detail(string Label, string Value);
public sealed record Reading(string Key, string Label, string Name, float? Temp, float? Load, IReadOnlyList<Detail> Details);

/// <summary>Reads the machine's sensors through LibreHardwareMonitor.</summary>
public sealed class Sensors : IDisposable
{
    readonly Computer pc = new()
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
        IsMotherboardEnabled = true, IsStorageEnabled = true,
    };

    // Extra readings for the wide bar, by the sensor names LibreHardwareMonitor uses. Missing or zero ones are skipped
    // (without administrator rights the CPU reports 0 W and no clock).
    static readonly (SensorType Type, string Sensor, string Text, string Format)[] Extras =
    [
        (SensorType.Load, "CPU Core Max", "Núcleo más cargado", "{0:0}%"),
        (SensorType.Temperature, "Core Max", "Núcleo más caliente", ""), // temperatures go through Meter.Deg
        (SensorType.Power, "CPU Package", "Consumo", "{0:0} W"),
        (SensorType.Clock, "CPU Core #1", "Frecuencia", "{0:0} MHz"),
        (SensorType.Temperature, "GPU Hot Spot", "Punto caliente", ""),
        (SensorType.Fan, "GPU Fan", "Ventilador", "{0:0} RPM"),
        (SensorType.SmallData, "GPU Memory Used", "Memoria de video", "{0:0} MB"),
        (SensorType.Power, "GPU Package", "Consumo", "{0:0} W"),
        (SensorType.Data, "Memory Used", "En uso", "{0:0.0} GB"),
        (SensorType.Data, "Memory Available", "Libre", "{0:0.0} GB"),
    ];

    public Sensors() => pc.Open();
    public void Dispose() => pc.Close();

    public List<Reading> Read()
    {
        var rows = new List<Reading>();
        foreach (var h in pc.Hardware)
        {
            string? label = h.HardwareType switch
            {
                HardwareType.Cpu => "CPU",
                HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => "GPU",
                HardwareType.Memory => "RAM",
                HardwareType.Storage => "Disco",
                HardwareType.Motherboard => "Placa",
                _ => null,
            };
            if (label is null || h.Name == "Virtual Memory") continue;

            var all = All(h).ToList();
            var temps = all.Where(s => s.SensorType == SensorType.Temperature && !s.Name.Contains("Distance"));
            var loads = all.Where(s => s.SensorType == SensorType.Load);
            float? temp = Pick(temps.Select(s => (s.Name, s.Value)), max: true, "CPU Package", "GPU Core");
            float? load = Pick(loads.Select(s => (s.Name, s.Value)), max: false, "CPU Total", "GPU Core", "Memory", "Total Activity");
            if (temp is null && load is null) continue;

            var details = new List<Detail>();
            foreach (var (type, sensor, text, format) in Extras)
                if (details.Count < 4 && all.FirstOrDefault(s => s.SensorType == type && s.Name == sensor)?.Value is > 0 and var v)
                    details.Add(new(text, type == SensorType.Temperature ? Meter.Deg(v) : string.Format(format, v)));
            string name = h.HardwareType == HardwareType.Memory ? "Memoria del sistema" : h.Name; // the library calls it "Total Memory"
            rows.Add(new(h.Identifier.ToString(), label, name, temp, load, details));
        }
        return rows;
    }

    // Motherboard sensors live in sub-hardware (the Super I/O chip), so walk the tree.
    static IEnumerable<ISensor> All(IHardware h)
    {
        h.Update();
        return h.Sensors.Concat(h.SubHardware.SelectMany(All));
    }

    /// <summary>The sensor with a preferred name, else the highest (max) or the first one that has a value.</summary>
    internal static float? Pick(IEnumerable<(string Name, float? Value)> sensors, bool max, params string[] prefer)
    {
        var live = sensors.Where(s => s.Value is not null).ToList();
        foreach (var p in prefer)
            foreach (var s in live)
                if (s.Name == p) return s.Value;
        return live.Count == 0 ? null : max ? live.Max(s => s.Value) : live[0].Value;
    }
}
