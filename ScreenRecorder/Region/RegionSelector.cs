using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenRecorder.Region
{
    /// <summary>
    /// Selection surface for ONE monitor's overlay window (one instance per monitor, kept in
    /// sync by <see cref="RegionSelectorSession"/>). Mouse input arrives window-local and is
    /// converted to desktop coordinates against the owning monitor's bounds, so results stay in
    /// the same (possibly DPI-virtualized) desktop space the recorder maps from.
    /// </summary>
    public class RegionSelector : FrameworkElement
    {
        public RegionSelectionMode RegionSelectionMode
        {
            get { return (RegionSelectionMode)GetValue(RegionSelectionModeProperty); }
            set { SetValue(RegionSelectionModeProperty, value); }
        }

        public static readonly DependencyProperty RegionSelectionModeProperty = DependencyProperty.Register
        (
            name: "RegionSelectionMode",
            propertyType: typeof(RegionSelectionMode),
            ownerType: typeof(RegionSelector),
            typeMetadata: new FrameworkPropertyMetadata(RegionSelectionMode.WindowRegion, FrameworkPropertyMetadataOptions.AffectsRender, OnRegionSelectionModePropertyChanged)
        );

        private static void OnRegionSelectionModePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is RegionSelector regionSelector)
            {
                regionSelector.StartSelection();
            }
        }

        public event RegionSelectedHandler RegionSelected;
        public event EventHandler UserRegionChanged;
        public event EventHandler UserRegionConfirmationRequested;
        private System.Drawing.Rectangle _userRegion;
        private System.Drawing.Rectangle _dragRegion;
        private bool _movingRegion;
        private System.Drawing.Size _physicalSize;
        public System.Drawing.Rectangle UserRegion => _userRegion;
        public System.Drawing.Size PhysicalSize => _physicalSize;

        public void SetUserRegion(System.Drawing.Rectangle region)
        {
            _userRegion = UserRegionGeometry.Fit(region, _physicalSize);
            UserRegionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        public void ConfirmUserRegion()
        {
            if (RegionSelectionMode != RegionSelectionMode.UserRegion || IsMouseCaptured
                || _userRegion.Width < UserRegionGeometry.MinimumSize || _userRegion.Height < UserRegionGeometry.MinimumSize)
                return;
            _selectionStarted = false;
            RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _deviceName,
                _deviceBounds, GetUserRegion(), false));
        }

        private System.Drawing.Point ToPhysical(Point local) => new System.Drawing.Point(
            (int)Math.Round(Math.Clamp(local.X / _deviceBounds.Width, 0, 1) * _physicalSize.Width),
            (int)Math.Round(Math.Clamp(local.Y / _deviceBounds.Height, 0, 1) * _physicalSize.Height));

        #region Private Fields
        private Point _downPoint, _movePoint;   // desktop coordinates
        private Rect _selectedTargetBounds;     // desktop coordinates
        private string _selectedTargetDevice;
        private bool _selectionStarted = false;
        private WindowRegion[] _windowRegions;
        private string _deviceName;             // owning monitor
        private Rect _deviceBounds;             // owning monitor, desktop coordinates
        #endregion

        public RegionSelector()
        {
            Focusable = true;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _windowRegions = WindowRegion.GetWindowRegions() ?? Array.Empty<WindowRegion>();
        }

        /// <summary>Binds this selector to the monitor its overlay window covers.</summary>
        public void Attach(string deviceName, Rect deviceBounds, System.Drawing.Size physicalSize)
        {
            _deviceName = deviceName;
            _deviceBounds = deviceBounds;
            _physicalSize = physicalSize;
        }

        private Point ToDesktop(Point localPoint)
        {
            return new Point(localPoint.X + _deviceBounds.X, localPoint.Y + _deviceBounds.Y);
        }

        private Rect ToLocal(Rect desktopRect)
        {
            if (desktopRect.IsEmpty)
                return Rect.Empty;

            return new Rect(desktopRect.X - _deviceBounds.X, desktopRect.Y - _deviceBounds.Y, desktopRect.Width, desktopRect.Height);
        }

        #region Mouse Event Handlers

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (!_selectionStarted || e.ChangedButton != MouseButton.Left)
                return;

            Focus();
            _downPoint = _movePoint = ToDesktop(e.GetPosition(this));

            CaptureMouse();

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    // A drag inside moves the existing rectangle; outside draws a replacement.
                    _selectedTargetDevice = _deviceName;
                    _dragRegion = _userRegion;
                    _movingRegion = _userRegion.Contains(ToPhysical(e.GetPosition(this)));
                    if (e.ClickCount == 2 && _movingRegion)
                    {
                        ReleaseMouseCapture();
                        UserRegionConfirmationRequested?.Invoke(this, EventArgs.Empty);
                    }
                    break;
                case RegionSelectionMode.WindowRegion:
                case RegionSelectionMode.DisplayRegion:
                    if (!string.IsNullOrWhiteSpace(_selectedTargetDevice) && _selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)
                    {
                        _selectionStarted = false;
                        RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, _selectedTargetDevice, _deviceBounds, _selectedTargetBounds, !(_selectedTargetBounds.Width > 0 && _selectedTargetBounds.Height > 0)));
                    }
                    break;
            }

            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_selectionStarted)
                return;

            _movePoint = ToDesktop(e.GetPosition(this));

            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    Cursor = _userRegion.Contains(ToPhysical(e.GetPosition(this))) ? Cursors.SizeAll : Cursors.Cross;
                    if (IsMouseCaptured)
                        UpdateUserDrag(e.GetPosition(this));
                    break;
                case RegionSelectionMode.WindowRegion:
                    Cursor = Cursors.Arrow;

                    _selectedTargetBounds = Rect.Empty;
                    _selectedTargetDevice = null;
                    foreach (var windowRegion in _windowRegions)
                    {
                        if (windowRegion.Region.Contains(_movePoint))
                        {
                            _selectedTargetBounds = windowRegion.Region;
                            // The pointer is on this monitor by construction.
                            _selectedTargetDevice = _deviceName;
                            break;
                        }
                    }
                    InvalidateVisual();
                    break;
                case RegionSelectionMode.DisplayRegion:
                    Cursor = Cursors.Arrow;

                    // Hovering this overlay means hovering this monitor.
                    if (_selectedTargetBounds != _deviceBounds)
                    {
                        _selectedTargetBounds = _deviceBounds;
                        _selectedTargetDevice = _deviceName;
                        InvalidateVisual();
                    }
                    break;
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            if (!_selectionStarted || IsMouseCaptured)
                return;

            // The pointer moved off to another monitor's overlay — drop this one's highlight.
            if ((RegionSelectionMode == RegionSelectionMode.WindowRegion || RegionSelectionMode == RegionSelectionMode.DisplayRegion)
                && _selectedTargetBounds != Rect.Empty)
            {
                _selectedTargetBounds = Rect.Empty;
                _selectedTargetDevice = null;
                InvalidateVisual();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!_selectionStarted || e.ChangedButton != MouseButton.Left || !IsMouseCaptured)
                return;
            if (RegionSelectionMode == RegionSelectionMode.UserRegion)
                UpdateUserDrag(e.GetPosition(this));
            ReleaseMouseCapture();
        }

        private void UpdateUserDrag(Point local)
        {
            var start = ToPhysical(new Point(_downPoint.X - _deviceBounds.X, _downPoint.Y - _deviceBounds.Y));
            var end = ToPhysical(local);
            if (_movingRegion)
                SetUserRegion(UserRegionGeometry.Move(_dragRegion, start, end, _physicalSize));
            else
            {
                var region = UserRegionGeometry.Draw(start, end);
                if (region.Width > 0 && region.Height > 0)
                    SetUserRegion(region);
            }
        }
        #endregion

        #region OnRender

        private readonly Pen _selectorPen = new Pen(Brushes.White, 2);
        private readonly Brush _dimBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        protected override void OnRender(DrawingContext dc)
        {
            if (!_selectionStarted)
                return;

            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

            dc.DrawRectangle(Brushes.Transparent, null, bounds);

            var pathGeometry = new PathGeometry();
            pathGeometry.AddGeometry(new RectangleGeometry(bounds));

            Rect highlight = Rect.Empty;
            switch (RegionSelectionMode)
            {
                case RegionSelectionMode.UserRegion:
                    highlight = ToLocal(GetUserRegion());
                    break;
                case RegionSelectionMode.WindowRegion:
                    highlight = _selectedTargetBounds.IsEmpty ? Rect.Empty : ToLocal(Rect.Intersect(_deviceBounds, _selectedTargetBounds));
                    break;
                case RegionSelectionMode.DisplayRegion:
                    highlight = ToLocal(_selectedTargetBounds);
                    break;
            }

            if (!highlight.IsEmpty)
            {
                pathGeometry.AddGeometry(new RectangleGeometry(highlight));
                dc.DrawRectangle(null, _selectorPen, highlight);
            }

            dc.DrawGeometry(_dimBrush, null, pathGeometry);
        }

        #endregion

        #region Private Methods

        private Rect GetUserRegion()
        {
            if (_userRegion.Width <= 0 || _userRegion.Height <= 0)
                return Rect.Empty;
            double scaleX = _deviceBounds.Width / _physicalSize.Width;
            double scaleY = _deviceBounds.Height / _physicalSize.Height;
            return new Rect(_deviceBounds.X + _userRegion.X * scaleX, _deviceBounds.Y + _userRegion.Y * scaleY,
                _userRegion.Width * scaleX, _userRegion.Height * scaleY);
        }

        #endregion

        #region Public Methods
        public void StartSelection()
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            _selectionStarted = true;
            _downPoint = _movePoint = new Point(0, 0);
            _selectedTargetBounds = Rect.Empty;
            _selectedTargetDevice = null;
            InvalidateVisual();
        }

        public void CancelSelection()
        {
            if (_selectionStarted)
            {
                _selectionStarted = false;
                RegionSelected?.Invoke(this, new RegionSelectedEventArgs(RegionSelectionMode, null, Rect.Empty, Rect.Empty, true));
                InvalidateVisual();
            }
        }
        #endregion
    }
}
