using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

/// <summary>Draws a percentage as a tray icon: coloured tile, white number scaled to fill it.</summary>
internal static partial class IconRenderer
{
    /// <param name="percent">The number to draw; null draws "?".</param>
    /// <param name="stale">Grey the tile out: the number is old and may no longer be true.</param>
    public static Icon Render(int? percent, bool stale)
    {
        // Draw at the tray's real pixel size so Windows does not downscale (and shrink) the number.
        var size = TrayIconSize();

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var radius = Math.Max(2, size / 8);
            using (var background = new SolidBrush(stale ? StaleColor : ColorFor(percent)))
            using (var tile = RoundedRect(new Rectangle(0, 0, size, size), radius))
                g.FillPath(background, tile);

            var text = percent?.ToString(CultureInfo.InvariantCulture) ?? "?";
            using var glyphs = TextPath(text);
            FitInto(glyphs, new RectangleF(1, 1, size - 2, size - 2));
            g.FillPath(Brushes.White, glyphs);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Clone so the GDI handle can be released straight away.
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static readonly Color StaleColor = Color.FromArgb(110, 110, 110);

    private static Color ColorFor(int? percent) => percent switch
    {
        null => StaleColor,
        >= 90 => Color.FromArgb(200, 40, 40),
        >= 70 => Color.FromArgb(205, 140, 0),
        _ => Color.FromArgb(30, 140, 70),
    };

    private static GraphicsPath TextPath(string text)
    {
        var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        path.AddString(text, family, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
        return path;
    }

    /// <summary>Scales the glyph outlines to the largest size that fits, keeping aspect ratio, centred.</summary>
    private static void FitInto(GraphicsPath path, RectangleF target)
    {
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
        using var matrix = new Matrix();
        matrix.Translate(
            target.X + (target.Width - bounds.Width * scale) / 2,
            target.Y + (target.Height - bounds.Height * scale) / 2);
        matrix.Scale(scale, scale);
        matrix.Translate(-bounds.X, -bounds.Y);
        path.Transform(matrix);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Small-icon size at the primary monitor's current DPI. SystemInformation.SmallIconSize is frozen at
    /// the DPI the process started with, so it goes blurry after a scaling change.
    /// </summary>
    private static int TrayIconSize()
    {
        const int smCxSmIcon = 49;
        const uint monitorDefaultToPrimary = 1;
        const int mdtEffectiveDpi = 0;

        try
        {
            var monitor = MonitorFromPoint(default, monitorDefaultToPrimary);
            if (GetDpiForMonitor(monitor, mdtEffectiveDpi, out var dpi, out _) == 0)
            {
                var size = GetSystemMetricsForDpi(smCxSmIcon, dpi);
                if (size > 0)
                    return size;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return SystemInformation.SmallIconSize.Width;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);

    /// <remarks>(0,0) is always on the primary monitor, which is where the tray is.</remarks>
    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);
}
