using System.Drawing;
using System.Globalization;
using ScreenRecorder.Region;

int passed = 0;
var display = new Size(1920, 1080);
var defaultRegion = new Rectangle(640, 380, 640, 320);

void Check(string name, bool success)
{
    if (!success) throw new InvalidOperationException("FAIL: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}

Check("first use is centered 640 x 320", UserRegionGeometry.Restore(null, display) == defaultRegion);
foreach (string saved in new[] { "", "bad", "0,0,0,320", "0,0,640,-1", "0,0,99,320", "NaN,0,640,320", "0,0,9999999999,320" })
    Check("invalid saved region falls back: " + saved, UserRegionGeometry.Restore(saved, display) == defaultRegion);

var previous = new Rectangle(1100, 700, 640, 320);
foreach (string culture in new[] { "en-US", "ko-KR", "de-DE", "zh-CN" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    string saved = UserRegionGeometry.Serialize(previous);
    Check("settings round trip: " + culture, saved == "1100,700,640,320" && UserRegionGeometry.Restore(saved, display) == previous);
}

Check("smaller display clamps position without losing size",
    UserRegionGeometry.Restore("1800,900,640,320", display) == new Rectangle(1280, 760, 640, 320));
Check("oversized previous region fits new display",
    UserRegionGeometry.Restore("100,200,3840,2160", display) == new Rectangle(0, 0, 1920, 1080));
Check("negative saved coordinates are recovered",
    UserRegionGeometry.Restore("-200,-100,640,320", display) == new Rectangle(0, 0, 640, 320));
Check("small display limits default dimensions",
    UserRegionGeometry.Restore(null, new Size(400, 240)) == new Rectangle(0, 0, 400, 240));
Check("size stays in physical pixels on a 4K display",
    UserRegionGeometry.Restore(null, new Size(3840, 2160)) == new Rectangle(1600, 920, 640, 320));
Check("input size accepted", UserRegionGeometry.TrySize("640", "320", display, out var size) && size == new Size(640, 320));
foreach (var invalid in new[] { ("", "320"), ("abc", "320"), ("640.5", "320"), ("0", "320"), ("640", "-1"), ("99", "320"), ("1921", "320"), ("640", "1081"), ("99999999999", "320") })
    Check("invalid dimensions rejected: " + invalid, !UserRegionGeometry.TrySize(invalid.Item1, invalid.Item2, display, out _));
Check("display-sized input accepted", UserRegionGeometry.TrySize("1920", "1080", display, out _));
Check("moving preserves size", UserRegionGeometry.Move(previous, new Point(1200, 800), new Point(1250, 825), display) == new Rectangle(1150, 725, 640, 320));
Check("moving stops at bottom-right edge", UserRegionGeometry.Move(previous, new Point(1100, 700), new Point(1920, 1080), display) == new Rectangle(1280, 760, 640, 320));
Check("moving stops at top-left edge", UserRegionGeometry.Move(new Rectangle(50, 50, 640, 320), new Point(100, 100), Point.Empty, display) == new Rectangle(0, 0, 640, 320));
Check("resizing near edge shifts position into view", UserRegionGeometry.Fit(new Rectangle(1200, 700, 1000, 600), display) == new Rectangle(920, 480, 1000, 600));
Check("reverse drag creates the requested rectangle", UserRegionGeometry.Draw(new Point(740, 420), new Point(100, 100)) == new Rectangle(100, 100, 640, 320));
Check("forward drag creates the same rectangle", UserRegionGeometry.Draw(new Point(100, 100), new Point(740, 420)) == new Rectangle(100, 100, 640, 320));
Check("click without dragging has no area", UserRegionGeometry.Draw(new Point(100, 100), new Point(100, 100)).Size == Size.Empty);

var capture = new Rectangle(100, 100, 640, 320);
var displayBounds = new Rectangle(Point.Empty, display);
var borders = RecordingRegionGeometry.Borders(capture, displayBounds, excluded: false);
Check("recording outline has four sides", borders.Length == 4);
Check("fallback outline never enters the recorded pixels", borders.All(b => !b.IntersectsWith(capture)));
Check("top outline matches the recording width", borders[0] == new Rectangle(100, 98, 640, 2));
Check("bottom outline follows the recorded bottom edge", borders[1] == new Rectangle(100, 420, 640, 2));
Check("left outline matches the recording height", borders[2] == new Rectangle(98, 100, 2, 320));
Check("right outline follows the recorded right edge", borders[3] == new Rectangle(740, 100, 2, 320));
var edgeBorders = RecordingRegionGeometry.Borders(displayBounds, displayBounds, excluded: true);
Check("excluded outline is visible at all screen edges", edgeBorders.All(displayBounds.Contains));
var fallbackEdges = RecordingRegionGeometry.Borders(displayBounds, displayBounds, excluded: false);
Check("screen-edge fallback stays outside the recording", fallbackEdges.All(b => !b.IntersectsWith(displayBounds)));
var negativeMonitor = new Rectangle(-2560, -200, 2560, 1440);
var negativeCapture = new Rectangle(-2400, -100, 640, 320);
Check("outline supports monitors left of and above primary",
    RecordingRegionGeometry.Borders(negativeCapture, negativeMonitor, excluded: false)[0] == new Rectangle(-2400, -102, 640, 2));
Console.WriteLine($"{passed} checks passed.");
