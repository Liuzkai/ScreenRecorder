using System;
using System.Drawing;

namespace ScreenRecorder.Region
{
    internal static class RecordingRegionGeometry
    {
        // Physical desktop pixels. Prefer the outside of the recording; at a monitor edge,
        // an excluded window can safely move just inside so all four sides remain visible.
        internal static Rectangle[] Borders(Rectangle capture, Rectangle monitor, bool excluded)
        {
            const int thickness = 2;
            int left = capture.Left - thickness;
            int top = capture.Top - thickness;
            int right = capture.Right;
            int bottom = capture.Bottom;
            if (excluded)
            {
                left = Math.Max(monitor.Left, left);
                top = Math.Max(monitor.Top, top);
                right = Math.Min(monitor.Right - thickness, right);
                bottom = Math.Min(monitor.Bottom - thickness, bottom);
            }
            return new[]
            {
                new Rectangle(capture.Left, top, capture.Width, thickness),
                new Rectangle(capture.Left, bottom, capture.Width, thickness),
                new Rectangle(left, capture.Top, thickness, capture.Height),
                new Rectangle(right, capture.Top, thickness, capture.Height),
            };
        }
    }
}
