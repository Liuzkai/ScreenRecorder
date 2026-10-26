# Region selection regression checks

Run the geometry and saved-value checks on any .NET 9 host:

```sh
dotnet run --project ScreenRecorder.RegionTests/ScreenRecorder.RegionTests.csproj
```

The console runner exits nonzero on failure and prints the executed check count.
It links the production geometry code; it does not simulate WPF input or Windows capture.

The Windows runner also exercises real desktop border windows:

```powershell
dotnet run --project ScreenRecorder.DesktopTests/ScreenRecorder.DesktopTests.csproj -c Release
```

These checks verify physical placement, dashed rendering, mouse pass-through styles,
capture exclusion (or safe placement outside the capture), focus, pause/resume, stop,
and cancellation/shutdown cleanup. Full WGC screen capture still needs the interaction checks below.

Windows interaction checks (required before shipping):

- Open custom region selection with no saved region: a centered 640 × 320 pixel frame appears.
- Drag outside the frame in either direction; release, then drag inside to move. Releasing does not start recording.
- Enter 640 × 320 and apply, including near a monitor edge. Invalid, empty, fractional, too-small or oversized inputs show an error and cannot be confirmed.
- Confirm by button, Enter outside the size fields, or double-click inside the frame. Enter within a size field only applies dimensions. Esc/cancel leaves the previous saved region unchanged.
- Reopen, then restart the application and reopen: the last confirmed custom region and monitor are restored. Selecting a window/display must not overwrite it.
- Repeat on a secondary monitor at 150%/200% scaling (including a monitor left of the primary); verify a 640 × 320 input produces a 640 × 320 recording. Move between monitors and confirm only one custom region is active.
- Disconnect the saved monitor or reduce its resolution; the restored frame must fit the available monitor.
- Switch between region/window/display modes; window/display selection and cancellation still work.
