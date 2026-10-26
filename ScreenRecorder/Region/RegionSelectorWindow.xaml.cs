using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenRecorder.Region
{
    /// <summary>
    /// One selection overlay covering exactly ONE monitor, created and coordinated by
    /// <see cref="RegionSelectorSession"/>. Per-monitor windows are required for mixed-DPI
    /// setups: DWM scales a DPI-unaware window by a single monitor's factor, so one
    /// desktop-spanning window cannot cover a 100%+200% layout.
    /// </summary>
    public partial class RegionSelectorWindow : Window
    {
        public RegionSelectionMode RegionSelectionMode
        {
            get { return (RegionSelectionMode)GetValue(RegionSelectionModeProperty); }
            set { SetValue(RegionSelectionModeProperty, value); }
        }
        public static readonly DependencyProperty RegionSelectionModeProperty =
            DependencyProperty.Register("RegionSelectionMode", typeof(RegionSelectionMode), typeof(RegionSelectorWindow),
            new PropertyMetadata(RegionSelectionMode.UserRegion, OnRegionSelectionModePropertyChanged));

        private static void OnRegionSelectionModePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is RegionSelectorWindow window)
            {
                window._session?.OnModeChanged((RegionSelectionMode)e.NewValue);
                window.UpdateRegionEditor();
            }
        }

        private readonly RegionSelectorSession _session;

        /// <summary>The mode-switch menu is shown on the primary monitor's window only.</summary>
        internal bool ShowsMenu { get; }
        internal string DeviceName { get; }
        internal System.Drawing.Rectangle UserRegion => regionSelector.UserRegion;
        private bool _updatingSize;

        internal void RestoreUserRegion(string saved) => regionSelector.SetUserRegion(
            UserRegionGeometry.Restore(saved, regionSelector.PhysicalSize));

        internal void ClearUserRegion() => regionSelector.SetUserRegion(System.Drawing.Rectangle.Empty);

        internal RegionSelectorWindow(RegionSelectorSession session, System.Windows.Forms.Screen screen, bool showMenu)
        {
            _session = session;
            DeviceName = screen.DeviceName;
            ShowsMenu = showMenu;

            InitializeComponent();

            RegionSelectionMode = session.RegionSelectionMode;

            var bounds = screen.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("width or height is zero");
            }

            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;

            if (showMenu)
            {
                Canvas.SetLeft(regionMenuRoot, (bounds.Width / 2.0d) - (regionMenuRoot.Width / 2.0));
                Canvas.SetTop(regionMenuRoot, 0);
            }
            else
            {
                regionMenuRoot.Visibility = Visibility.Collapsed;
            }

            // GDI screen reads are physical pixels (never DPI-virtualized): capture the monitor's
            // PHYSICAL rect and scale it to the window's (virtualized) size. Capturing the virtual
            // rect on a scaled monitor grabs only its top-left portion (200% → top-left quarter).
            var monitorInfo = ScreenRecorder.DirectX.MonitorInfo.GetMonitorInfo(screen.DeviceName);
            int physicalLeft = monitorInfo?.PhysicalLeft ?? bounds.Left;
            int physicalTop = monitorInfo?.PhysicalTop ?? bounds.Top;
            int physicalWidth = monitorInfo?.PhysicalWidth ?? bounds.Width;
            int physicalHeight = monitorInfo?.PhysicalHeight ?? bounds.Height;

            using (var physicalBitmap = new System.Drawing.Bitmap(physicalWidth, physicalHeight))
            {
                using (var g = System.Drawing.Graphics.FromImage(physicalBitmap))
                {
                    g.CopyFromScreen(physicalLeft, physicalTop, 0, 0, new System.Drawing.Size(physicalWidth, physicalHeight), System.Drawing.CopyPixelOperation.SourceCopy);
                }

                var displayBitmap = physicalBitmap;
                if (physicalWidth != bounds.Width || physicalHeight != bounds.Height)
                {
                    displayBitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                    using (var g = System.Drawing.Graphics.FromImage(displayBitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(physicalBitmap, new System.Drawing.Rectangle(0, 0, bounds.Width, bounds.Height));
                    }
                }

                try
                {
                    ImageBrush imageBrush = new ImageBrush(Utils.ImageSourceFromBitmap(displayBitmap));
                    imageBrush.Freeze();
                    Background = imageBrush;
                }
                finally
                {
                    if (!ReferenceEquals(displayBitmap, physicalBitmap))
                    {
                        displayBitmap.Dispose();
                    }
                }
            }

            regionSelector.Attach(screen.DeviceName, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                new System.Drawing.Size(physicalWidth, physicalHeight));
            regionSelector.UserRegionChanged += (_, _) =>
            {
                _session.OnUserRegionChanged(this);
                UpdateRegionEditor();
            };
            regionSelector.UserRegionConfirmationRequested += (_, _) => ConfirmRegion_Click(this, new RoutedEventArgs());
            regionSelector.StartSelection();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (ShowsMenu)
            {
                Focus();
            }
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);

            // Focus hopping between the session's overlays (clicking another monitor) must not
            // cancel; focus leaving the session (alt-tab, another app window) must.
            if (!_session.ContainsFocus(e.NewFocus))
            {
                _session.Cancel();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.Key)
            {
                case Key.Escape:
                    _session.Cancel();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (RegionSelectionMode == RegionSelectionMode.UserRegion)
                    {
                        if (e.OriginalSource is TextBox)
                            ApplyRegionSize();
                        else if (regionEditor.Visibility == Visibility.Visible)
                            ConfirmRegion_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void regionSelector_RegionSelected(object sender, RegionSelectedEventArgs e)
        {
            if (e.IsCancelled)
            {
                _session.Cancel();
            }
            else
            {
                if (e.RegionSelectionMode == RegionSelectionMode.UserRegion)
                {
                    AppConfig.Instance.LastUserRegionDevice = DeviceName;
                    AppConfig.Instance.LastUserRegion = UserRegionGeometry.Serialize(UserRegion);
                }
                _session.Complete(new RegionSelectionResult(e.DeviceName, Rect.Offset(e.Region, -e.DisplayBounds.X, -e.DisplayBounds.Y)));
            }
        }

        private void UpdateRegionEditor()
        {
            if (regionEditor == null) return;
            regionEditor.Visibility = RegionSelectionMode == RegionSelectionMode.UserRegion
                && UserRegion.Width > 0 && UserRegion.Height > 0 ? Visibility.Visible : Visibility.Collapsed;
            _updatingSize = true;
            regionWidth.Text = UserRegion.Width.ToString();
            regionHeight.Text = UserRegion.Height.ToString();
            _updatingSize = false;
            ValidateRegionSize();
        }

        private bool ValidateRegionSize()
        {
            if (confirmRegion == null) return false;
            bool valid = UserRegionGeometry.TrySize(regionWidth.Text, regionHeight.Text,
                regionSelector.PhysicalSize, out _);
            confirmRegion.IsEnabled = applyRegionSize.IsEnabled = valid;
            regionSizeError.Text = valid ? "" : string.Format(Properties.Resources.RegionSizeRange,
                UserRegionGeometry.MinimumSize, regionSelector.PhysicalSize.Width, regionSelector.PhysicalSize.Height);
            return valid;
        }

        private void RegionSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_updatingSize) ValidateRegionSize();
        }

        private bool ApplyRegionSize()
        {
            if (!ValidateRegionSize() || !UserRegionGeometry.TrySize(regionWidth.Text, regionHeight.Text,
                regionSelector.PhysicalSize, out var size)) return false;
            regionSelector.SetUserRegion(new System.Drawing.Rectangle(UserRegion.Location, size));
            return true;
        }

        private void ApplyRegionSize_Click(object sender, RoutedEventArgs e) => ApplyRegionSize();

        private void ConfirmRegion_Click(object sender, RoutedEventArgs e)
        {
            if (ApplyRegionSize()) regionSelector.ConfirmUserRegion();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _session.Cancel();
        }
    }
}
