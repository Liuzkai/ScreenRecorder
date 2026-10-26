using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenRecorder.DirectX;
using ScreenRecorder.Encoder;
using Forms = System.Windows.Forms;

namespace ScreenRecorder.Region
{
    /// <summary>Click-through recording boundary, independent of the selection overlay.</summary>
    internal sealed class RecordingRegionIndicator : IDisposable
    {
        private readonly ScreenRecorder.Encoder.Encoder _encoder;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly List<BorderStrip> _strips = new List<BorderStrip>();

        internal RecordingRegionIndicator(ScreenRecorder.Encoder.Encoder encoder)
        {
            _encoder = encoder;
            _encoder.EncoderStopped += EncoderStopped;
        }

        internal void Show(string deviceName, System.Windows.Rect region)
        {
            Hide();
            var monitor = MonitorInfo.GetMonitorInfo(deviceName)
                ?? throw new ArgumentException("Recording monitor is unavailable.", nameof(deviceName));
            var physical = monitor.VirtualToPhysical(System.Windows.Rect.Intersect(region,
                new System.Windows.Rect(0, 0, monitor.Width, monitor.Height)));
            if (physical.IsEmpty || physical.Width < 2 || physical.Height < 2)
                throw new ArgumentException("Recording region is empty.", nameof(region));

            // Match the engine's crop, including rounding odd dimensions down for NV12.
            var capture = new Rectangle(monitor.PhysicalLeft + (int)physical.X,
                monitor.PhysicalTop + (int)physical.Y,
                (int)physical.Width & ~1, (int)physical.Height & ~1);
            var display = new Rectangle(monitor.PhysicalLeft, monitor.PhysicalTop,
                monitor.PhysicalWidth, monitor.PhysicalHeight);
            var outside = RecordingRegionGeometry.Borders(capture, display, excluded: false);
            var excluded = RecordingRegionGeometry.Borders(capture, display, excluded: true);
            try
            {
                for (int i = 0; i < outside.Length; i++)
                {
                    var strip = new BorderStrip();
                    _strips.Add(strip);
                    // Seed the correct monitor before creating the HWND. The final placement
                    // below uses native physical pixels and handles negative monitor origins.
                    strip.Bounds = new Rectangle(monitor.Left, monitor.Top, 2, 2);
                    var handle = strip.Handle;
                    bool captureExcluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
                        && Utils.SetWindowDisplayedOnlyMonitor(handle, true);
                    strip.Show();
                    strip.Place(captureExcluded ? excluded[i] : outside[i]);
                }
            }
            catch
            {
                Hide();
                throw;
            }
        }

        private void EncoderStopped(object sender, EncoderStoppedEventArgs e) => Hide();

        internal void Hide()
        {
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.Invoke(Hide);
                return;
            }
            foreach (var strip in _strips) strip.Dispose();
            _strips.Clear();
        }

        public void Dispose()
        {
            _encoder.EncoderStopped -= EncoderStopped;
            Hide();
        }

        // Four thin opaque windows avoid a desktop-sized transparent HWND, which can become
        // a black rectangle in capture on older Windows versions. Constant alpha enables
        // WS_EX_TRANSPARENT mouse pass-through across processes without stealing focus.
        private sealed class BorderStrip : Forms.Form
        {
            internal BorderStrip()
            {
                Name = "RecordingRegionBorder";
                FormBorderStyle = Forms.FormBorderStyle.None;
                StartPosition = Forms.FormStartPosition.Manual;
                AutoScaleMode = Forms.AutoScaleMode.None;
                ShowInTaskbar = false;
                TopMost = true;
                BackColor = Color.Black;
                Opacity = 0.99;
                SetStyle(Forms.ControlStyles.UserPaint | Forms.ControlStyles.AllPaintingInWmPaint
                    | Forms.ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override bool ShowWithoutActivation => true;

            protected override Forms.CreateParams CreateParams
            {
                get
                {
                    var parameters = base.CreateParams;
                    parameters.ExStyle |= 0x00000020 | 0x00000080 | 0x08000000;
                    return parameters;
                }
            }

            internal void Place(Rectangle bounds)
            {
                // Only this native placement call uses PMv2; restore the WPF thread's original
                // DPI context immediately so existing selection/toolbar coordinates stay valid.
                var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    if (!SetWindowPos(Handle, new IntPtr(-1), bounds.X, bounds.Y,
                        bounds.Width, bounds.Height, 0x0010 | 0x0040))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
                }
            }

            protected override void OnPaint(Forms.PaintEventArgs e)
            {
                base.OnPaint(e);
                bool horizontal = ClientSize.Width >= ClientSize.Height;
                int length = horizontal ? ClientSize.Width : ClientSize.Height;
                for (int position = 0; position < length; position += 8)
                {
                    var dash = horizontal
                        ? new Rectangle(position, 0, Math.Min(4, length - position), ClientSize.Height)
                        : new Rectangle(0, position, ClientSize.Width, Math.Min(4, length - position));
                    e.Graphics.FillRectangle(Brushes.White, dash);
                }
            }

            protected override void WndProc(ref Forms.Message message)
            {
                if (message.Msg == 0x0084) // WM_NCHITTEST
                {
                    message.Result = new IntPtr(-1); // HTTRANSPARENT
                    return;
                }
                if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
                {
                    message.Result = new IntPtr(3); // MA_NOACTIVATE
                    return;
                }
                base.WndProc(ref message);
            }

            [DllImport("user32.dll")]
            private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
                int x, int y, int width, int height, uint flags);
        }
    }
}
