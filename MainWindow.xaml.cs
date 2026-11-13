using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TempWidget;

public partial class MainWindow : Window
{
    const double BarHeight = 108, BarPeek = 4; // DIPs at 100 %; BarPeek is the strip left on screen when hidden
    const double CompactWidth = 276, ShellMargin = 12; // the card, and the room around it for its shadow
    const double DetailsMinCard = 440; // DIPs a bar card needs before its extra readings fit

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer showTimer = new() { Interval = TimeSpan.FromMilliseconds(250) }, hideTimer = new();
    Sensors? sensors; // opened on a worker thread: probing the hardware can take seconds
    Tray? tray;
    Theme theme = Theme.All[0];
    string error = "";
    bool busy, closed, isBar, read, cpuMissing;
    DateTime lastLog;

    public ObservableCollection<Meter> Meters { get; } = [];

    static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public MainWindow()
    {
        SelfCheck();
        InitializeComponent();
        DataContext = this;
        BuildMenu();
        Decos.SizeChanged += (_, _) => ClipDecos();

        Meter.Fahrenheit = Settings.Get("unidad", "C") == "F";
        Topmost = On("arriba", true);
        AutoHide.IsChecked = On("ocultar", true);
        ApplyTheme(Theme.ByName(Settings.Get("estilo", "")));

        Loaded += (_, _) => ApplyMode(Settings.Get("modo", "compact") == "bar");
        SourceInitialized += (_, _) => ApplyClickThrough();
        // Keep the bottom edge fixed when the card grows or shrinks (compact mode only).
        SizeChanged += (_, e) => { if (IsLoaded && !isBar) Top -= e.NewSize.Height - e.PreviousSize.Height; };
        MouseEnter += (_, _) => { Fade(1); hideTimer.Stop(); if (isBar) showTimer.Start(); };
        MouseLeave += (_, _) => { Fade(IdleOpacity); showTimer.Stop(); if (isBar) StartHide(1); };
        ContextMenu!.Closed += (_, _) => { if (isBar && !IsMouseOver) StartHide(1); };
        // The short wait before showing keeps the bar from dropping when the cursor only brushes the top edge.
        showTimer.Tick += (_, _) => { showTimer.Stop(); if (isBar && IsMouseOver) Slide(0); };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            // Only while it stays on top: tucked away under another window, the strip could not be reached to bring it back.
            if (isBar && Topmost && AutoHide.IsChecked == true && !IsMouseOver && ContextMenu is not { IsOpen: true }) Slide(BarPeek - Height);
        };
        timer.Tick += async (_, _) => await Refresh();
    }

    /// <summary>Puts the icon in the tray, begins reading the sensors once a second and shows the widget unless it was left hidden.</summary>
    public void Start()
    {
        tray = new Tray(leftClick: () => SetShown(!IsVisible), rightClick: () => OpenMenu(null));
        timer.Start();
        _ = Refresh();
        if (!On("oculto", false)) Show();
    }

    /// <summary>Brings the widget back (tray click, or the user opening the program a second time).</summary>
    public void SetShown(bool show)
    {
        Settings.Set("oculto", show ? "0" : "1");
        if (!show) { Hide(); return; }
        Show();
        if (IsLoaded) ApplyMode(isBar);
    }

    async Task Refresh()
    {
        if (busy || closed) return; // a slow sensor read must not pile up ticks
        busy = true;
        try
        {
            var all = await Task.Run(() => (sensors ??= new Sensors()).Read());
            if (closed) return;
            var rows = all.Where(r => On("ver_" + r.Label, true)).ToList();
            foreach (var gone in Meters.Where(m => rows.All(r => r.Key != m.Key)).ToList()) Meters.Remove(gone);
            bool alerts = On("alertas", true);
            foreach (var r in rows)
            {
                var m = Meters.FirstOrDefault(x => x.Key == r.Key);
                if (m is null) Meters.Add(m = new(r.Key, r.Label, r.Name));
                m.Set(r.Temp, r.Load, r.Details);
                if (alerts && m.Alarm.Check(r.Temp, AlertLimit)) Warn(m);
            }
            UpdateWide();
            UpdateTray();
            if (On("registro", false) && (DateTime.Now - lastLog).TotalSeconds >= 4.5)
                ReadingLog.Write(all, lastLog = DateTime.Now);
            cpuMissing = !all.Any(r => r.Label == "CPU" && r.Temp is not null);
            (read, error) = (true, "");
        }
        catch (Exception e) // whatever the sensor library throws, keep the widget alive and say so
        {
            error = "No se pudieron leer los sensores: " + e.Message;
        }
        finally { busy = false; }
        ShowHint();
    }

    void Warn(Meter m)
    {
        tray?.Notify("Temperatura alta",
            $"{m.Label} ({m.Name}) está a {Meter.Deg(m.Temp ?? 0)}. Avisás desde {Meter.Deg(AlertLimit)}.");
        if (On("sonido", true)) System.Media.SystemSounds.Exclamation.Play();
    }

    void UpdateTray()
    {
        var hottest = Meters.Where(m => m.Temp is not null).MaxBy(m => m.Temp);
        string? number = hottest?.Temp is { } t ? $"{(Meter.Fahrenheit ? t * 9 / 5 + 32 : t):0}" : null;
        tray?.Show(number, hottest?.Level ?? "", string.Join("\n", Meters.Select(m => $"{m.Label}: {m.ValueText}").Prepend("Temperaturas")));
    }

    void ShowHint()
    {
        string text = error != "" ? error
            : !read ? "Leyendo sensores…"
            : !cpuMissing || !On("aviso_cpu", true) ? ""
            : !PawnIO.Installed() ? "Sin temperatura de CPU: falta el controlador PawnIO. Clic derecho → Temperatura de la CPU."
            : !Elevated ? "Sin temperatura de CPU: hay que abrirlo como administrador. Clic derecho → Temperatura de la CPU."
            : "La CPU no informa su temperatura en este equipo.";
        Hint.Text = text;
        Hint.Visibility = text != "" && !isBar ? Visibility.Visible : Visibility.Collapsed; // the bar has no room for it
    }

    // ---- Styles ----

    void NextStyle(object sender, RoutedEventArgs e) => ApplyTheme(Theme.All[(Array.IndexOf(Theme.All, theme) + 1) % Theme.All.Length]);

    void ApplyTheme(Theme t)
    {
        theme = t;
        t.ApplyTo(Resources);
        string[] decos = t.Deco.Split(',');
        foreach (FrameworkElement layer in Decos.Children)
            layer.Visibility = decos.Contains((string)layer.Tag) ? Visibility.Visible : Visibility.Collapsed;
        Shell.CornerRadius = isBar ? new(0, 0, t.Corner, t.Corner) : new(t.Corner);
        ClipDecos();
        StyleButton.Content = "Estilo: " + t.Name;
        Settings.Set("estilo", t.Name);
    }

    /// <summary>Keeps the decorations inside the window's rounded corners.</summary>
    void ClipDecos()
    {
        double r = Math.Max(0, theme.Corner - Shell.BorderThickness.Left);
        Decos.Clip = new RectangleGeometry(new Rect(Decos.RenderSize), r, r);
    }

    // ---- Modes: compact card or bar across the top ----

    void UseCompact(object sender, RoutedEventArgs e) => ApplyMode(false);

    void ApplyMode(bool bar)
    {
        isBar = bar;
        double scale = Scale;
        Shell.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        TitleText.Visibility = bar ? Visibility.Collapsed : Visibility.Visible;
        List.ItemsPanel = (ItemsPanelTemplate)FindResource(bar ? "BarPanel" : "CompactPanel");
        BarButtons.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
        Body.Margin = bar ? new(10, 0, 10, 6) : new(13, 10, 13, 13);
        ShowHint();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        BeginAnimation(TopProperty, null); // release the slide, or the Top set below would be ignored
        showTimer.Stop();
        hideTimer.Stop();

        if (bar)
        {
            Shell.Margin = new(0);
            Shell.CornerRadius = new(0, 0, theme.Corner, theme.Corner);
            SizeToContent = SizeToContent.Manual;
            // Floats over other windows. It must not reserve screen space: that pushes every app down.
            (Left, Top, Width, Height) = (0, 0, SystemParameters.PrimaryScreenWidth, BarHeight * scale);
            if (!IsMouseOver) StartHide(3); // stay long enough to be seen before tucking away
        }
        else
        {
            Shell.Margin = new(ShellMargin);
            Shell.CornerRadius = new(theme.Corner);
            Width = CompactWidth * scale + 2 * ShellMargin;
            SizeToContent = SizeToContent.Height;
            UpdateLayout();
            PlaceCompact();
            if (!IsMouseOver) Fade(IdleOpacity);
        }
        UpdateWide();
        ApplyClickThrough();
        Settings.Set("modo", bar ? "bar" : "compact");
    }

    /// <summary>Puts the card where the user left it, else in the bottom-right corner, where Windows 7 gadgets sat.</summary>
    void PlaceCompact()
    {
        var area = SystemParameters.WorkArea;
        double x = area.Right - ActualWidth - 16, bottom = area.Bottom - 16;
        if (Saved("x") is { } sx && Saved("abajo") is { } sb) (x, bottom) = (sx, sb);
        // A saved spot can be off-screen after a monitor change.
        double minX = SystemParameters.VirtualScreenLeft, minY = SystemParameters.VirtualScreenTop;
        Left = Math.Clamp(x, minX, Math.Max(minX, minX + SystemParameters.VirtualScreenWidth - ActualWidth));
        Top = Math.Clamp(bottom - ActualHeight, minY, Math.Max(minY, minY + SystemParameters.VirtualScreenHeight - ActualHeight));

        static double? Saved(string key) =>
            double.TryParse(Settings.Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>The bar shows the extra readings only while every card has room for them.</summary>
    void UpdateWide()
    {
        double buttons = BarButtons.ActualWidth > 0 ? BarButtons.ActualWidth : 400;
        bool wide = isBar && Meters.Count > 0 && (Width / Scale - buttons - 30) / Meters.Count >= DetailsMinCard;
        foreach (var m in Meters)
            if (m.Wide != wide) { m.Wide = wide; m.Notify(); }
    }

    /// <summary>The compact card fades when the cursor is away; the bar stays solid.</summary>
    void Fade(double to)
    {
        if (!isBar) BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180)));
    }

    void StartHide(double seconds)
    {
        hideTimer.Stop();
        hideTimer.Interval = TimeSpan.FromSeconds(seconds);
        hideTimer.Start();
    }

    /// <summary>Slides the bar up out of the screen, or back down.</summary>
    void Slide(double top) =>
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase() });

    /// <summary>
    /// Lets clicks fall through the card to whatever is under it, for a game or a video. Only the compact card:
    /// a bar that ignored the cursor could never be brought back. While it is on, the menu is in the tray icon.
    /// </summary>
    void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, On("atravesar", false) && !isBar ? style | WsExTransparent : style & ~WsExTransparent);
    }

    const int GwlExStyle = -20, WsExTransparent = 0x20;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    // ---- User actions ----

    void Drag(object sender, MouseButtonEventArgs e)
    {
        if (isBar) return;
        DragMove(); // returns when the button is released
        // The bottom edge is what stays put when cards come and go, so that is what gets remembered.
        Settings.Set("x", Left.ToString(CultureInfo.InvariantCulture));
        Settings.Set("abajo", (Top + ActualHeight).ToString(CultureInfo.InvariantCulture));
    }

    void SaveToggles(object sender, RoutedEventArgs e)
    {
        Settings.Set("arriba", Topmost ? "1" : "0");
        Settings.Set("ocultar", AutoHide.IsChecked == true ? "1" : "0");
    }

    void OpenOptions(object sender, RoutedEventArgs e) => OpenMenu((UIElement)sender);
    void Exit(object sender, RoutedEventArgs e) => Close();

    void RunAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Close();
        }
        catch (Win32Exception) { } // the user said no to the Windows prompt: keep running as we are
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        timer.Stop();
        tray?.Dispose();
        if (!busy) sensors?.Dispose(); // mid-read the worker still uses it; the process is ending anyway
        base.OnClosed(e);
    }

    // ---- Checks ----

    /// <summary>
    /// Draws every style, in both modes, with made-up readings that exercise the four ring colors, into
    /// compacto.png and barra.png. Nothing is shown on screen and no setting is saved.
    /// </summary>
    public void RenderSheets(string folder)
    {
        Settings.ReadOnly = true;
        Settings.Set("escala", "1"); // the sheets are laid out for 100 %
        read = true;
        (string Label, string Name, float? Temp, float Load, Detail[] Details)[] sample =
        [
            ("CPU", "Intel Core i5-8400", 47, 35, [new("Núcleo más cargado", "52%"), new("Núcleo más caliente", "49°"), new("Consumo", "38 W"), new("Frecuencia", "3800 MHz")]),
            ("GPU", "NVIDIA GeForce GTX 1650", 68, 80, [new("Punto caliente", "79°"), new("Ventilador", "1850 RPM"), new("Memoria de video", "3120 MB"), new("Consumo", "61 W")]),
            ("Disco", "Samsung SSD 860 EVO 500GB", 84, 12, []),
            ("RAM", "Memoria del sistema", null, 71, [new("En uso", "11,4 GB"), new("Libre", "4,6 GB")]),
        ];
        foreach (var s in sample)
        {
            var m = new Meter(s.Label, s.Label, s.Name);
            Meters.Add(m);
            for (int i = 0; i < 60; i++) m.Set(s.Temp + 4 * MathF.Sin(i / 4f), s.Load + 12 * MathF.Sin(i / 6f), s.Details);
        }

        Directory.CreateDirectory(folder);
        var root = (FrameworkElement)Content;
        foreach (bool bar in new[] { false, true })
        {
            if (bar) Meters.RemoveAt(Meters.Count - 1); // three cards, like a PC without elevated sensors, leave room for the extras
            var shots = new List<BitmapSource>();
            foreach (var t in Theme.All)
            {
                ApplyTheme(t);
                ApplyMode(bar);
                var size = new Size(bar ? 1920 : Width, BarHeight);
                for (int pass = 0; pass < 2; pass++) // the second pass runs after templates and bindings have settled
                {
                    root.Measure(bar ? size : new Size(size.Width, double.PositiveInfinity));
                    if (!bar) size.Height = Math.Ceiling(root.DesiredSize.Height);
                    root.Arrange(new Rect(size));
                    root.UpdateLayout();
                    Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }
                var shot = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                shot.Render(root);
                shots.Add(shot);
            }

            // One sheet per mode over a desktop-like backdrop: cards four to a row, bars stacked.
            int columns = bar ? 1 : 4, rows = (shots.Count + columns - 1) / columns;
            double cellW = shots.Max(s => s.Width), cellH = shots.Max(s => s.Height) + 10;
            var sheet = new DrawingVisual();
            using (var dc = sheet.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x4A, 0x5A, 0x6C), Color.FromRgb(0x1C, 0x22, 0x2B), 90), null,
                    new Rect(0, 0, columns * cellW, rows * cellH));
                for (int i = 0; i < shots.Count; i++)
                    dc.DrawImage(shots[i], new Rect(i % columns * cellW, i / columns * cellH, shots[i].Width, shots[i].Height));
            }
            var image = new RenderTargetBitmap((int)(columns * cellW), (int)(rows * cellH), 96, 96, PixelFormats.Pbgra32);
            image.Render(sheet);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(folder, bar ? "barra.png" : "compacto.png"));
            png.Save(file);
        }
    }

    [Conditional("DEBUG")]
    static void SelfCheck()
    {
        (string, float?)[] s = [("Core #1", 50f), ("CPU Package", 47f), ("Dead", null), ("Core #2", 61f)];
        Debug.Assert(Sensors.Pick(s, max: true, "CPU Package") == 47f, "preferred name wins");
        Debug.Assert(Sensors.Pick(s, max: true, "Nope") == 61f, "falls back to the highest");
        Debug.Assert(Sensors.Pick(s, max: false) == 50f, "falls back to the first");
        Debug.Assert(Sensors.Pick([("Dead", null)], max: true) is null, "no live sensor gives null");

        var m = new Meter("k", "CPU", "x");
        for (int i = 0; i < 100; i++) m.Set(150, 20, []);
        Debug.Assert(m.Points.Count == 62 && m.Points.All(p => p.X is >= 0 and <= Meter.SparkW && p.Y is >= 0 and <= Meter.SparkH),
            "history is capped and stays inside the sparkline box");
        Debug.Assert(m.Level == "Hot" && new Meter("k", "RAM", "x").Level == "Plain", "ring level follows the temperature");
        m.Set(40, 20, []);
        Debug.Assert(m.RangeText == "40° / 150°", "range keeps the session's lowest and highest");
        m.Set(null, 20, []);
        Debug.Assert(m.RangeText == "20% / 20%", "losing the temperature restarts the range in load units");

        Meter.Fahrenheit = true;
        Debug.Assert(Meter.Deg(100) == "212°F" && Meter.Deg(0) == "32°F", "Fahrenheit conversion");
        Meter.Fahrenheit = false;
        Debug.Assert(Meter.Deg(49.6f) == "50°", "Celsius rounds to whole degrees");

        var a = new Alarm();
        bool[] fired = [.. Enumerable.Range(0, 7).Select(_ => a.Check(90, 85))];
        Debug.Assert(fired.Count(f => f) == 1 && fired[Alarm.Readings - 1], "warns once, on the fifth hot reading in a row");
        a.Check(83, 85);
        Debug.Assert(!Enumerable.Range(0, 6).Select(_ => a.Check(90, 85)).ToList().Contains(true), "no new warning without cooling down first");
        a.Check(79, 85);
        Debug.Assert(Enumerable.Range(0, 5).Select(_ => a.Check(90, 85)).ToList()[^1], "warns again after cooling below the limit minus 5");

        string line = ReadingLog.Line(new DateTime(2026, 1, 2, 3, 4, 5), new Reading("k", "CPU", "Intel; X", 47.5f, null, []), ";",
            CultureInfo.GetCultureInfo("es-AR"));
        Debug.Assert(line == "2026-01-02 03:04:05;CPU;Intel  X;47,5;", "CSV line: local decimals, separator removed from the name, empty load");

        foreach (var t in Theme.All) t.ApplyTo(new ResourceDictionary()); // throws on a malformed color
        Debug.Assert(Theme.All.Select(t => t.Name).Distinct().Count() == Theme.All.Length, "style names are unique");
        Debug.Assert(Theme.ByName("no existe") == Theme.All[0], "an unknown saved style falls back to the first");
    }
}
