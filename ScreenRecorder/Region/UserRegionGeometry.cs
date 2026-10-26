using System;
using System.Drawing;
using System.Globalization;

namespace ScreenRecorder.Region
{
    // Monitor-local physical pixels, independent of WPF/DPI and desktop monitor offsets.
    internal static class UserRegionGeometry
    {
        internal const int MinimumSize = 100;

        internal static Rectangle Restore(string saved, Size display)
        {
            var values = (saved ?? "").Split(',');
            if (values.Length == 4
                && int.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                && int.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
                && int.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                && int.TryParse(values[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                && w >= MinimumSize && h >= MinimumSize)
                return Fit(new Rectangle(x, y, w, h), display);

            int width = Math.Min(640, display.Width), height = Math.Min(320, display.Height);
            return new Rectangle((display.Width - width) / 2, (display.Height - height) / 2, width, height);
        }

        internal static Rectangle Fit(Rectangle region, Size display)
        {
            int width = Math.Clamp(region.Width, 0, display.Width);
            int height = Math.Clamp(region.Height, 0, display.Height);
            return new Rectangle(Math.Clamp(region.X, 0, display.Width - width),
                Math.Clamp(region.Y, 0, display.Height - height), width, height);
        }

        internal static string Serialize(Rectangle region) =>
            FormattableString.Invariant($"{region.X},{region.Y},{region.Width},{region.Height}");

        internal static Rectangle Move(Rectangle region, Point start, Point end, Size display) =>
            Fit(new Rectangle(region.X + end.X - start.X, region.Y + end.Y - start.Y,
                region.Width, region.Height), display);

        internal static Rectangle Draw(Point start, Point end) =>
            new Rectangle(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

        internal static bool TrySize(string widthText, string heightText, Size display, out Size size)
        {
            size = Size.Empty;
            if (!int.TryParse(widthText, out int width) || !int.TryParse(heightText, out int height)
                || width < MinimumSize || height < MinimumSize || width > display.Width || height > display.Height)
                return false;
            size = new Size(width, height);
            return true;
        }
    }
}
