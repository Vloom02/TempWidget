using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TempWidget;

/// <summary>
/// The notification-area icon. It shows the hottest temperature as a colored number, lists every reading on hover,
/// toggles the widget on a left click and opens the widget's menu on a right click.
/// </summary>
sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon icon = new() { Text = "Temperaturas" };
    readonly Drawing.Icon appIcon;
    IntPtr drawn; // handle of the icon we drew last; Windows does not free it for us
    string shown = "";

    static readonly Dictionary<string, Drawing.Color> Colors = new()
    {
        ["Cool"] = Drawing.Color.FromArgb(0x2E, 0xB8, 0x72),
        ["Warm"] = Drawing.Color.FromArgb(0xE8, 0xA3, 0x17),
        ["Hot"] = Drawing.Color.FromArgb(0xE0, 0x48, 0x3C),
    };

    public Tray(Action leftClick, Action rightClick)
    {
        appIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application;
        icon.Icon = appIcon;
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) leftClick();
            else if (e.Button == Forms.MouseButtons.Right) rightClick();
        };
        icon.Visible = true;
    }

    /// <summary>Updates the number and the hover text; redraws only when the number or its color changed.</summary>
    public void Show(string? number, string level, string tooltip)
    {
        icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip; // Windows' limit for this text
        string key = number is null ? "" : number + level;
        if (key == shown) return;
        shown = key;

        IntPtr old = drawn;
        if (number is null) { icon.Icon = appIcon; drawn = IntPtr.Zero; }
        else
        {
            using var bitmap = Draw(number, Colors.GetValueOrDefault(level, Drawing.Color.FromArgb(0x3D, 0x8B, 0xD4)));
            drawn = bitmap.GetHicon();
            icon.Icon = Drawing.Icon.FromHandle(drawn);
        }
        if (old != IntPtr.Zero) DestroyIcon(old);
    }

    static Drawing.Bitmap Draw(string number, Drawing.Color color)
    {
        int s = Forms.SystemInformation.SmallIconSize.Width;
        var bitmap = new Drawing.Bitmap(s, s);
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var back = new Drawing.SolidBrush(color)) g.FillEllipse(back, 0, 0, s - 1, s - 1);
        // Two digits fill the circle; a third needs a smaller font.
        using var font = new Drawing.Font("Segoe UI", s * (number.Length > 2 ? 0.5f : 0.68f), Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
        g.DrawString(number, font, Drawing.Brushes.White, new Drawing.RectangleF(-1, 0, s + 2, s + 1), format);
        return bitmap;
    }

    public void Notify(string title, string text) => icon.ShowBalloonTip(8000, title, text, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        icon.Visible = false; // otherwise a dead icon stays in the tray until the mouse passes over it
        icon.Dispose();
        if (drawn != IntPtr.Zero) DestroyIcon(drawn);
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);
}
