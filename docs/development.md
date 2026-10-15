# Development

How Almost Fast Dash is built, how it's laid out, and how it's checked before
every release.

- [Building](#building)
- [Design principles](#design-principles)
- [Project layout](#project-layout)
- [Automatic checks](#automatic-checks)
- [Command line](#command-line)
- [Code style](#code-style)

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download). The app it
builds runs on Windows 10 or 11 with nothing else installed.

```powershell
dotnet build -c Release
```

`bin\Release\net48\AlmostFastDash.exe` is the whole application: one file,
targeting .NET Framework 4.8 (part of Windows) and referencing only Windows'
own assemblies.

## Design principles

- **One standalone exe.** No installer, no NuGet packages and no other
  software required. Everything uses Windows and .NET Framework 4.8 built-ins:
  - WinUSB for the wheel screen;
  - shared memory for iRacing's telemetry;
  - serial ports and UDP for LED devices;
  - `winmm` for wheel buttons.
- **Dashboards are data.** Every template is built only from the designer's
  own widgets and stored in `settings.json`, so anything a template does, a user
  can do too.
- **Draw only what changed.** The renderer keeps a static base layer and
  redraws only widgets whose state changed. Painters must stay inside their
  widget's bounds and only narrow the clip, never replace it. The self-test
  compares incremental frames with full redraws.
- **Draw each frame cheaply.** A frame has a few milliseconds at 60 a second,
  so `LayoutRenderer`:
  - works out each widget's state fingerprint once per frame and reuses it to
    decide what changed;
  - copies the background back under changed widgets with a plain memory copy;
  - redraws a widget that only overlaps a changed one from its finished
    picture instead of painting it again;
  - mixes layer effects (shadows, glows, outlines) with its own compositor in
    `LayerStyle.Compose`, not GDI+.

  Painters can keep dense, unchanging artwork with `Paint.Still` and reusable
  geometry with `Renderer.Shape`. Measure with `--profile-dashboards` before and
  after: drawing a large cached picture that's mostly empty can cost as much as
  drawing the shapes, so a cache only pays off for dense content.
- **Send as little as possible, and in parallel.** USB is the wheel screen's
  bottleneck: a whole 854×480 frame is about 820 KB. So:
  - The dashboard loop draws on its own thread.
  - `ScreenSender` sends on another, so the next frame is drawn while the last
    one is still going down the cable.
  - `FrameEncoder` compares each frame with the last one sent in 16-pixel
    tiles, and joins the changed tiles into a few rectangles where one costs
    less to send than two.
  - Only those rectangles are encoded (RGB565, turned a quarter onto portrait
    panels) and sent, with VoCore's draw command that names an area. When most
    of the screen changed, a whole frame goes instead.
  - The renderer hands back one reused bitmap instead of a new 1.6 MB copy
    per frame.
- **Readable at a glance.** Text is at least 16 px on an 854×480 screen (14 in
  dense tables), at least 4.5:1 contrast against what's behind it, and never
  runs into other text. The dashboard audit enforces this as drawn.
- **Device-agnostic.** Nothing user-facing names a particular wheel or light
  bar. Devices are described by shape, role and protocol.

## Project layout

| Path | What's there |
| --- | --- |
| `Program.cs` | Entry point, the command line, and the loop that drives the wheel screen. |
| `IRacingSdk.cs` | Reads iRacing's shared-memory telemetry and YAML session string. |
| `IRacingTelemetrySource.cs` | Turns iRacing's data into a `TelemetrySnapshot`: gaps, fuel, the race order, and the preview snapshots. |
| `Radar.cs` | `RadarTracker`: places nearby cars along the track and works out their lanes. |
| `WheelScreen.cs` | Drives VoCore's M-PRO screens through WinUSB: every model's size, whole frames and areas, touch. |
| `ScreenSender.cs`, `FrameEncoder.cs` | Sending to the wheel screen on its own thread: finding what changed, encoding it and reconnecting. |
| `MonitorOutput.cs` | The dashboard full screen on another display. |
| `ScreenStats.cs`, `ScreenTest.cs` | The screen's measured performance, and the `--screen-test` pattern. |
| `LedDevices.cs`, `ArduinoLedController.cs`, `LedLayout.cs` | LED devices: the serial, Adalight and WLED protocols, and each device's shape. |
| `WheelButtons.cs` | Wheel and button-box buttons through Windows' joystick interface. |
| `AppSettings.cs` | Settings, schema migrations, and saving with backups. |
| `SettingsWindow.*`, `Theme.xaml` | The main window and the app's look. |
| `Designer\` | The dashboard designer, the widget painters, `LayoutRenderer`, the templates (`DesignTemplates.cs`) and the readability audit (`DashboardAudit.cs`). |
| `Lights\` | Light profiles, effects, themes, the LED loop and the light designer. |
| `Firmware\Adalight\` | A ready-to-flash Arduino sketch for Adalight strips. |
| `release-check.ps1` | Builds the app and runs every automatic check. |

## Automatic checks

`release-check.ps1` builds the app and runs every check on a clean copy:

- the designer and light self-tests, including a readability audit of every
  template;
- a render of every template;
- the light frames;
- a replay of your latest finished iRacing recording.

```powershell
powershell -ExecutionPolicy Bypass -File .\release-check.ps1
```

It ends by printing the exe's SHA-256, which goes in the release notes.

iRacing recordings (`.ibt`) hold only your own car, so gaps to other cars and
the radar can only be checked in a live session with `--live-check`.

## Command line

| Option | What it does |
| --- | --- |
| *(none)* | Opens the app. |
| `--run` | Runs the dashboard (what the start button and Start with Windows use). |
| `--stop` | Asks a running dashboard to stop. |
| `--replay-ibt <file.ibt> <report.txt>` | Replays an iRacing telemetry recording through the app: every flag, each lap's times, falloff and fuel, the traced track map, and every light that stayed on longer than a second. |
| `--live-check <report.txt> [seconds]` | Run while you're in a session. Records what iRacing sends for every value the dashboards use and what the app makes of it: gaps by both methods, fuel, the race order, the radar compared with iRacing's spotter, and the screen's measured performance. |
| `--render-templates <folder>` | Saves a picture of every template in each of its states, plus the second views and other screen sizes. |
| `--render-dashboards <folder>` | Saves a picture of every dashboard in `settings.json`, in each state and on each page. |
| `--render-flags <folder>` | Saves every flag screen, each flag that stays out stepping aside to its frame, and every dashboard framed for a caution and for the final lap. |
| `--audit-dashboards <report.txt>` | Checks every template and saved dashboard, as drawn on every screen and tap view, for text that's too small, too faint, runs into other text, reaches past its widget's edge or is taller than its space. Exits with 1 when it finds anything. |
| `--profile-dashboards <report.txt>` | Times every template and saved dashboard drawing 480 frames of the animated lap, as the wheel draws them. Reports each one's median, mean and slowest frames, and the widgets that took the time. |
| `--ui-snapshot <scenario> <file.png>` | Opens a window in a scripted state and saves a picture of it. |
| `--screen-test [report.txt]` | With the dashboard stopped: a test pattern on the wheel screen (a whole frame, numbered squares sent as areas, then a sliding bar sent as whole frames and as only what changed). Reports the screen's model and both frame rates. |
| `--led-soak <COM port> <LED count> [--fast]` | Streams fast-changing frames to an LED board for 90 seconds and reports whether it kept up. |
| `--light-frames <report.txt>` | Writes each dashboard's LED colours through a rev sweep and the preview states. |
| `--designer-selftest <report.txt>` | Runs the end-to-end checks. They write settings next to the exe, so run them from a copy. |

The render, audit and snapshot options read `settings.json` next to the exe,
so run them from a scratch copy rather than your real install.

## Code style

`.editorconfig` sets one style for every file: four spaces, Windows line
endings, UTF-8 and no trailing whitespace. It also flags unused usings,
members, parameters and assignments, and fields that could be `readonly`.
Visual Studio and VS Code show these as you type. To check the whole project
in one go:

```powershell
dotnet build -c Release -p:EnforceCodeStyleInBuild=true
```

The compiler reports unused usings only in a build that also generates
documentation (`-p:GenerateDocumentationFile=true`).

Write code the way the code around it is written. Comments explain *why*,
in plain English.
