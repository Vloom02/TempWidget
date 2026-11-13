using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TempWidget;

/// <summary>
/// A visual style. The window reads everything through DynamicResource, so adding a style is adding one entry to
/// <see cref="All"/>. Colors are #AARRGGBB.
/// </summary>
/// <param name="Back">Window gradient, top to bottom.</param>
/// <param name="Gloss">Glass highlight across the top; alpha 0 makes the whole style flat (no gloss on buttons either).</param>
/// <param name="Shade">The style's dark tone: its alpha is the strength of the shadow behind text, its color tints orb rims and bar grooves.</param>
/// <param name="Orb">Highlight of the gauge orbs and of the bubbles.</param>
/// <param name="Fill">Load bars and history graphs.</param>
/// <param name="Deco">Decoration layers of the window to show, comma separated (their Tag).</param>
public sealed record Theme(
    string Name, string[] Back, string Edge, string Gloss, string Card, string CardEdge,
    string Text, string Shade, string Orb, string Track,
    string Plain, string Cool, string Warm, string Hot,
    string Fill, string Button, string Accent, string Deco, double Corner)
{
    public static readonly Theme[] All =
    [
        // Frutiger Aero: sky, water and grass behind glossy glass.
        new("Aero", ["#E66FD0FF", "#E61C8EE0", "#E62FBF8F"], Edge: "#C0FFFFFF", Gloss: "#A0FFFFFF", Card: "#38FFFFFF", CardEdge: "#80FFFFFF",
            Text: "#FFFFFFFF", Shade: "#8C003A66", Orb: "#90FFFFFF", Track: "#50FFFFFF",
            Plain: "#FFFFFFFF", Cool: "#FF8CFFB5", Warm: "#FFFFD75E", Hot: "#FFFF6B5E",
            Fill: "#FFFFFFFF", Button: "#FFFFFFFF", Accent: "#FF8CFFB5", Deco: "Bubbles", Corner: 18),

        // Dark Aero: the same glass over glossy black, with neon accents.
        new("Dark Aero", ["#F02A3644", "#F00B0F15", "#F0122033"], Edge: "#9066D9FF", Gloss: "#48FFFFFF", Card: "#20FFFFFF", CardEdge: "#5066D9FF",
            Text: "#FFFFFFFF", Shade: "#B0000000", Orb: "#7066D9FF", Track: "#38FFFFFF",
            Plain: "#FF66D9FF", Cool: "#FF5CFFA0", Warm: "#FFFFC940", Hot: "#FFFF5A5A",
            Fill: "#FF66D9FF", Button: "#FF9FE6FF", Accent: "#FF5CFFA0", Deco: "Bubbles", Corner: 18),

        // Frutiger Eco: greens, leaves and sunlight.
        new("Eco", ["#E68FD856", "#E63FAE4A", "#E6128060"], Edge: "#C0FFFFFF", Gloss: "#90FFFFFF", Card: "#40FFFFFF", CardEdge: "#90FFFFFF",
            Text: "#FFFFFFFF", Shade: "#8C0B4A2A", Orb: "#90FFFFFF", Track: "#50FFFFFF",
            Plain: "#FFFFFFFF", Cool: "#FFE8FF6A", Warm: "#FFFFB03A", Hot: "#FFFF5A4A",
            Fill: "#FFFFFFFF", Button: "#FFFFFFFF", Accent: "#FFE8FF6A", Deco: "Leaves", Corner: 18),

        // Aqua (Helvetica Aqua Aero): the ocean, from aquamarine shallows to deep blue.
        new("Aqua", ["#E640E0D0", "#E60E7FC8", "#E6083A8C"], Edge: "#C0E0FFFF", Gloss: "#90FFFFFF", Card: "#30FFFFFF", CardEdge: "#80C8FFF4",
            Text: "#FFFFFFFF", Shade: "#8C00214F", Orb: "#90FFFFFF", Track: "#50FFFFFF",
            Plain: "#FFFFFFFF", Cool: "#FFB6FF5C", Warm: "#FFFFE14D", Hot: "#FFFF7A6B",
            Fill: "#FFC8FFF4", Button: "#FFFFFFFF", Accent: "#FFB6FF5C", Deco: "Bubbles,Waves", Corner: 22),

        // Frutiger Aurora: beams of northern lights over a night sky.
        new("Aurora", ["#F01B1464", "#F00B2545", "#F003181F"], Edge: "#907CFFCB", Gloss: "#30FFFFFF", Card: "#26FFFFFF", CardEdge: "#507CFFCB",
            Text: "#FFFFFFFF", Shade: "#B0000000", Orb: "#607CFFCB", Track: "#40FFFFFF",
            Plain: "#FFC7B8FF", Cool: "#FF7CFFCB", Warm: "#FFFFD166", Hot: "#FFFF6B8A",
            Fill: "#FF7CFFCB", Button: "#FFC7FFE8", Accent: "#FF7CFFCB", Deco: "Aurora", Corner: 18),

        // Frutiger Metro: flat, 2-D and loud, with vector shapes instead of glass.
        new("Metro", ["#F200AEEF", "#F26A2BD9"], Edge: "#00FFFFFF", Gloss: "#00FFFFFF", Card: "#2EFFFFFF", CardEdge: "#00FFFFFF",
            Text: "#FFFFFFFF", Shade: "#00101040", Orb: "#00FFFFFF", Track: "#45FFFFFF",
            Plain: "#FFFFFFFF", Cool: "#FFB6FF00", Warm: "#FFFFD400", Hot: "#FFFF3B6B",
            Fill: "#FFFFFFFF", Button: "#FFFFFFFF", Accent: "#FFB6FF00", Deco: "Metro", Corner: 4),

        // Technozen: calm, pale and minimal, after late-2000s Japanese gadgets. The only style with dark text.
        new("Technozen", ["#F2FBFCFD", "#F2E4EAF0"], Edge: "#FFD0D8E0", Gloss: "#60FFFFFF", Card: "#B0FFFFFF", CardEdge: "#FFD9E0E7",
            Text: "#FF2C3A47", Shade: "#00506070", Orb: "#FFFFFFFF", Track: "#30506070",
            Plain: "#FF6B7C8D", Cool: "#FF2BB3A3", Warm: "#FFE8A020", Hot: "#FFE0483C",
            Fill: "#FF3D8BD4", Button: "#FF7F93A6", Accent: "#FF2BB3A3", Deco: "Zen", Corner: 14),
    ];

    public static Theme ByName(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    /// <summary>Publishes this style's brushes under the keys the window's XAML refers to.</summary>
    public void ApplyTo(ResourceDictionary r)
    {
        Color shade = C(Shade), orb = C(Orb), fill = C(Fill), button = C(Button), gloss = C(Gloss);
        bool flat = gloss.A == 0;

        r["BackBrush"] = Vertical([.. Back.Select((hex, i) => (C(hex), Back.Length == 1 ? 0 : (double)i / (Back.Length - 1)))]);
        r["EdgeBrush"] = Solid(C(Edge));
        r["GlossBrush"] = Vertical((gloss, 0), (A(gloss, (byte)(gloss.A / 6)), 1));
        r["CardBrush"] = Solid(C(Card));
        r["CardEdgeBrush"] = Solid(C(CardEdge));
        r["TextBrush"] = Solid(C(Text));
        r["TextGlow"] = Frozen(new DropShadowEffect { Color = A(shade, 255), BlurRadius = 4, ShadowDepth = 1, Opacity = shade.A / 255.0 });
        r["OrbBrush"] = Radial(new(0.35, 0.25), (orb, 0), (A(shade, 0x30), 1));
        r["BubbleBrush"] = Radial(new(0.35, 0.3), (A(orb, 0x70), 0), (A(orb, 0x10), 0.8), (A(orb, 0x50), 1));
        r["TrackBrush"] = Solid(C(Track));
        r["GrooveBrush"] = Solid(A(shade, 0x40));
        r["RingPlain"] = Solid(C(Plain));
        r["RingCool"] = Solid(C(Cool));
        r["RingWarm"] = Solid(C(Warm));
        r["RingHot"] = Solid(C(Hot));
        r["FillBrush"] = Solid(fill);
        r["SparkStroke"] = Solid(A(fill, 0xE0));
        r["SparkFill"] = Vertical((A(fill, 0x90), 0), (A(fill, 0x10), 1));
        r["ButtonBrush"] = flat
            ? Solid(A(button, 0x30))
            : Vertical((A(button, 0x80), 0), (A(button, 0x38), 0.5), (A(button, 0x18), 0.5), (A(button, 0x40), 1)); // bright top half, darker bottom half
        r["ButtonHoverBrush"] = Solid(A(button, 0x80));
        r["ButtonEdgeBrush"] = Solid(A(button, flat ? (byte)0 : (byte)0xA0));
        r["AccentBrush"] = Solid(C(Accent));
        r["CardCorner"] = new CornerRadius(Corner * 2 / 3);
        r["ButtonCorner"] = new CornerRadius(Corner / 2);
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    static Brush Solid(Color c) => Frozen(new SolidColorBrush(c));
    static GradientStopCollection Stops((Color Color, double Offset)[] stops) => new(stops.Select(s => new GradientStop(s.Color, s.Offset)));
    static Brush Vertical(params (Color, double)[] stops) => Frozen(new LinearGradientBrush(Stops(stops), new Point(0, 0), new Point(0, 1)));
    static Brush Radial(Point origin, params (Color, double)[] stops) => Frozen(new RadialGradientBrush(Stops(stops)) { GradientOrigin = origin });
}
