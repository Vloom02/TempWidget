using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TempWidget;

/// <summary>The options menu. The widget's right click, the tray icon and the bar's "Opciones" button all open it.</summary>
public partial class MainWindow
{
    static readonly string[] Components = ["CPU", "GPU", "RAM", "Disco", "Placa"];
    readonly List<(MenuItem Item, Func<bool> IsOn)> checks = [];

    static bool On(string key, bool fallback) => Settings.Get(key, fallback ? "1" : "0") == "1";

    static double Number(string key, double fallback, double min, double max) =>
        double.TryParse(Settings.Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max ? v : fallback;

    double Scale => Number("escala", 1, 1, 2);
    double IdleOpacity => Number("reposo", 0.3, 0.1, 1);
    float AlertLimit => (float)Number("umbral", 85, 50, 110);

    void BuildMenu()
    {
        var m = ContextMenu!;
        m.Opened += (_, _) => { foreach (var (item, isOn) in checks) item.IsChecked = isOn(); };

        Add(m, "Modo compacto", () => ApplyMode(false), () => !isBar);
        Add(m, "Barra superior", () => ApplyMode(true), () => isBar);
        var styles = Sub(m, "Estilo");
        foreach (var t in Theme.All) Add(styles, t.Name, () => ApplyTheme(t), () => theme == t);

        var show = Sub(m, "Componentes");
        foreach (var c in Components) Toggle(show, c, "ver_" + c, true, () => _ = Refresh());

        var look = Sub(m, "Apariencia");
        Choice(Sub(look, "Tamaño"), "escala", "1",
            [("Normal", "1"), ("Grande (125 %)", "1.25"), ("Muy grande (150 %)", "1.5")], () => ApplyMode(isBar));
        Choice(Sub(look, "Transparencia sin el cursor"), "reposo", "0.3",
            [("Mucha", "0.3"), ("Poca", "0.65"), ("Ninguna", "1")], () => Fade(IsMouseOver ? 1 : IdleOpacity));
        Choice(Sub(look, "Unidades"), "unidad", "C",
            [("Celsius (°C)", "C"), ("Fahrenheit (°F)", "F")], () => { Meter.Fahrenheit = Settings.Get("unidad", "C") == "F"; _ = Refresh(); });
        Toggle(look, "Aviso de la temperatura de CPU", "aviso_cpu", true, ShowHint);

        var alerts = Sub(m, "Alertas");
        Toggle(alerts, "Avisar si algo se calienta", "alertas", true, () => { });
        Choice(Sub(alerts, "Avisar desde"), "umbral", "85",
            [.. new[] { 75, 80, 85, 90, 95 }.Select(c => ($"{c} °C  ({c * 9 / 5 + 32} °F)", c.ToString(CultureInfo.InvariantCulture)))], () => { });
        Toggle(alerts, "Con sonido", "sonido", true, () => { });

        m.Items.Add(new Separator());
        Add(m, "Siempre visible", () => { Topmost = !Topmost; SaveToggles(this, new()); }, () => Topmost);
        Toggle(m, "Dejar pasar los clics (solo tarjeta; se maneja desde la bandeja)", "atravesar", false, ApplyClickThrough);
        Add(m, "Ocultar (sigue en la bandeja)", () => SetShown(false));
        Add(m, "Iniciar con Windows", () => Startup.Set(!Startup.IsOn()), Startup.IsOn);
        var log = Sub(m, "Registro");
        Toggle(log, "Guardar lecturas cada 5 segundos", "registro", false, () => { });
        Add(log, "Abrir carpeta del registro", ReadingLog.OpenFolder);
        Add(m, "Reiniciar mínimos y máximos", () => { foreach (var x in Meters) x.ResetRange(); });

        m.Items.Add(new Separator());
        Add(m, "Temperatura de la CPU…", ExplainCpuTemperature);
        Add(m, "Salir", Close);
    }

    MenuItem Add(ItemsControl parent, string header, Action click, Func<bool>? isOn = null)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        if (isOn is not null) checks.Add((item, isOn));
        parent.Items.Add(item);
        return item;
    }

    static MenuItem Sub(ItemsControl parent, string header)
    {
        var item = new MenuItem { Header = header };
        parent.Items.Add(item);
        return item;
    }

    void Choice(ItemsControl parent, string key, string fallback, (string Label, string Value)[] options, Action apply)
    {
        foreach (var (label, value) in options)
            Add(parent, label, () => { Settings.Set(key, value); apply(); }, () => Settings.Get(key, fallback) == value);
    }

    void Toggle(ItemsControl parent, string header, string key, bool fallback, Action apply) =>
        Add(parent, header, () => { Settings.Set(key, On(key, fallback) ? "0" : "1"); apply(); }, () => On(key, fallback));

    /// <summary>Opens the menu under a control (the bar's button) or at the cursor (the tray icon).</summary>
    void OpenMenu(UIElement? under)
    {
        var m = ContextMenu!;
        m.PlacementTarget = under ?? this;
        m.Placement = under is null ? PlacementMode.MousePoint : PlacementMode.Bottom;
        Activate(); // without focus the menu would not close when the user clicks elsewhere
        m.IsOpen = true;
    }

    void ExplainCpuTemperature()
    {
        bool driver = PawnIO.Installed();
        string text =
            "La temperatura de la CPU (y su consumo, su frecuencia y los sensores de la placa) necesita dos cosas:\n\n" +
            $"1. El controlador PawnIO instalado.  {(driver ? "✔ Instalado." : "✘ No está instalado.")}\n" +
            $"2. Abrir el widget como administrador.  {(Elevated ? "✔ Ya es administrador." : "✘ No es administrador.")}\n\n" +
            "PawnIO es gratuito y de código abierto; lo usa LibreHardwareMonitor, la librería con que el widget lee los sensores. " +
            "Es un controlador del sistema: hay un reporte aislado, sin confirmar, de que el antitrampas de algún juego en línea lo objeta.";

        if (!driver)
        {
            if (MessageBox.Show(text + "\n\n¿Abrir la página oficial de PawnIO para descargarlo?", "Temperatura de la CPU",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PawnIO.Site) { UseShellExecute = true });
        }
        else if (!Elevated)
        {
            if (MessageBox.Show(text + "\n\n¿Reiniciar el widget como administrador ahora?", "Temperatura de la CPU",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                RunAsAdmin();
        }
        else MessageBox.Show(text + "\n\nLas dos cosas están: si igual no aparece, tu procesador no la informa a esta librería.",
            "Temperatura de la CPU", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
