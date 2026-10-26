using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using ScreenRecorder.DirectX;
using ScreenRecorder.Region;
using ScreenRecorder.VideoSource;
using Forms = System.Windows.Forms;

internal static class Program
{
    private static int _passed;

    [STAThread]
    private static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("::error::" + error.ToString().Replace("%", "%25")
                .Replace("\r", "%0D").Replace("\n", "%0A"));
            return 1;
        }
    }

    private static void Run()
    {
        var monitor = MonitorInfo.GetPrimaryMonitorInfo()
            ?? throw new InvalidOperationException("No desktop monitor is available.");
        using var encoder = new ScreenRecorder.Encoder.Encoder();
        using var indicator = new RecordingRegionIndicator(encoder);
        var physical = new Rectangle(monitor.PhysicalLeft + 100, monitor.PhysicalTop + 100, 640, 320);
        var region = new System.Windows.Rect(100.0 * monitor.Width / monitor.PhysicalWidth,
            100.0 * monitor.Height / monitor.PhysicalHeight,
            640.0 * monitor.Width / monitor.PhysicalWidth, 320.0 * monitor.Height / monitor.PhysicalHeight);
        var foreground = GetForegroundWindow();
        indicator.Show(monitor.DeviceName, region);
        Forms.Application.DoEvents();
        Check("showing the recording border does not activate it", GetForegroundWindow() == foreground);
        var strips = Borders();
        Check("confirmation/countdown shows four border windows", strips.Length == 4);
        var display = new Rectangle(monitor.PhysicalLeft, monitor.PhysicalTop, monitor.PhysicalWidth, monitor.PhysicalHeight);
        foreach (var strip in strips)
        {
            long styles = GetWindowLongPtr(strip.Handle, -20).ToInt64();
            Check("border is layered, click-through, no-activate and hidden from task switching",
                (styles & 0x080800A0) == 0x080800A0 && !strip.ShowInTaskbar);
            bool excluded = GetWindowDisplayAffinity(strip.Handle, out uint affinity) && affinity == 0x11;
            var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            NativeRect bounds;
            try { Check("read native recording border bounds", GetWindowRect(strip.Handle, out bounds)); }
            finally { SetThreadDpiAwarenessContext(previous); }
            var rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            Check("native outline follows the actual capture pixels",
                RecordingRegionGeometry.Borders(physical, display, excluded).Contains(rectangle));
            Check("border is excluded from capture or entirely outside recorded pixels",
                excluded || !rectangle.IntersectsWith(physical));
            using var image = new Bitmap(strip.ClientSize.Width, strip.ClientSize.Height);
            strip.DrawToBitmap(image, strip.ClientRectangle);
            bool horizontal = image.Width >= image.Height;
            var white = image.GetPixel(1, 1);
            var black = image.GetPixel(horizontal ? 5 : 1, horizontal ? 1 : 5);
            Check("rendered border alternates white dashes and black gaps",
                white.R > 240 && white.G > 240 && white.B > 240
                && black.R < 15 && black.G < 15 && black.B < 15);
        }

        encoder.Start("mp4", "unused.mp4", MediaEncoder.VideoCodec.H264, 5000000,
            new VideoSize(640, 320), MediaEncoder.AudioCodec.None, 0);
        encoder.Pause();
        Check("pause preserves the recording outline", Borders().Length == 4);
        encoder.Resume();
        Check("resume preserves the same outline windows", Borders().SequenceEqual(strips));
        encoder.Stop();
        Check("stop removes every border window", Borders().Length == 0);
        indicator.Show(monitor.DeviceName, region);
        indicator.Hide();
        Check("countdown cancellation/startup failure cleanup removes every border", Borders().Length == 0);
        indicator.Show(monitor.DeviceName, region);
        indicator.Show(monitor.DeviceName, region);
        Check("a new selection replaces the previous outline", Borders().Length == 4);
        indicator.Dispose();
        Check("application shutdown removes every border", Borders().Length == 0);
        Console.WriteLine($"{_passed} desktop checks passed.");
    }

    private static Forms.Form[] Borders() => Forms.Application.OpenForms.Cast<Forms.Form>()
        .Where(f => f.Name == "RecordingRegionBorder").ToArray();

    private static void Check(string name, bool success)
    {
        if (!success) throw new InvalidOperationException("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
