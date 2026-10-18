# Development

How Almost Fast Dash is built, how it's laid out, and how it's checked before
every release.

- [Building](#building)
- [How it fits together](#how-it-fits-together)
- [Project layout](#project-layout)
- [Design principles](#design-principles)
- [Automatic checks](#automatic-checks)
- [Command line](#command-line)
- [Code style](#code-style)

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) on Windows. The
app it builds runs on Windows 10 or 11 with nothing else installed.

```powershell
dotnet build src/AlmostFastDash -c Release
dotnet test
```

`src\AlmostFastDash\bin\Release\net48\AlmostFastDash.exe` is the whole
application: one file, targeting .NET Framework 4.8 (part of Windows) and
referencing only Windows' own assemblies. `dotnet test` builds the app with its
end-to-end checks and runs them, one test per check. `AlmostFastDash.sln` opens
both projects in Visual Studio or Rider.

## How it fits together

The one exe is three programs, chosen by the command line in `Program.cs`:

- **The app's window** (no options): `UI\MainWindow`, where dashboards, devices
  and settings are chosen. It opens the screen designer (`Designer\`) and the
  light designer (`Lights\LightsWindow`).
- **The dashboard** (`--run`): `Runner\DashboardRunner`, which runs in the
  background, started and stopped by the window's start button.
- **Tools** (`--render-templates`, `--live-check` and the rest):
  `CommandLine\`, for development and support.

The window and the dashboard are separate processes. They share `settings.json`
(the window writes it; the dashboard reloads it when it changes) and talk
through small files next to the exe: `preview.request` (show this preview),
`screen.status` (how the screen keeps up), `dashboard.running`,
`dashboard.active` and `dashboard.hidden`.

What the dashboard does, about 60 times a second:

```
iRacing's shared memory
  └─ IRacing\IRacingSdk                reads the raw telemetry and session YAML
      └─ IRacing\IRacingTelemetrySource  turns it into a TelemetrySnapshot: gaps,
                                         fuel, flags, the race order, the radar
          ├─ Runner\DashboardRunner      the loop: which dashboard, when to draw
          │   └─ Rendering\LayoutRenderer   draws the dashboard's DashLayout
          │       │   (Dashboards\): widgets, each value worked out by a formula
          │       │   (Formulas\Expr) and painted by Rendering\WidgetPainters
          │       ├─ Output\ScreenSender    sends the changed areas to the wheel
          │       │                         screen (Output\WheelScreen, USB)
          │       └─ Output\MonitorOutput   or shows it on the PC
          └─ Lights\LightLoop           works out every LED from the dashboard's
              └─ Devices\LedDeviceLink  light layers and sends them to each device
```

Good places to start reading:

- `TelemetrySnapshot` (`IRacing\`): everything a dashboard can show.
- `DashLayout` and `WidgetCatalog` (`Dashboards\`): what a dashboard is made
  of, and every widget with its settings.
- `LayoutRenderer.Render` (`Rendering\`): how a frame is drawn, and
  `LayoutRenderer.Changes.cs` for how only what changed is redrawn.
- `DataCatalog` and `Functions` (`Formulas\`): every value and function a
  formula can use.
- `Dashboards\Templates\`: the starting dashboards, built with the same calls a
  user's design is made of.

## Project layout

Everything the app is made of is in `src\AlmostFastDash`, one folder (and
namespace) per part. `GlobalUsings.cs` opens every part to every file.

| Folder | What's there |
| --- | --- |
| `IRacing\` | Reading iRacing: shared memory, the session YAML, `TelemetrySnapshot`, gaps, fuel, laps, the radar's lanes, track maps and the made-up lap previews use. |
| `Formulas\` | The formula language (`Expr`), its functions, and the live values it can read. |
| `Dashboards\` | What a dashboard is (`DashLayout`, widgets and their settings), the templates, blocks, modules, the readability audit and export files. |
| `Rendering\` | Drawing a dashboard: `LayoutRenderer`, the widget painters, fills, layer effects, Liquid Glass, flags and the radar. |
| `Output\` | Where a frame goes: the wheel screen over USB (`WheelScreen`, `ScreenSender`, `FrameEncoder`) or a window on the PC (`MonitorOutput`). |
| `Devices\` | LED devices (serial, Adalight, WLED), their shapes and mapping, and wheel buttons. |
| `Lights\` | Light layers and effects, the LED loop, and the light designer. |
| `Designer\` | The screen designer window, split by part: `DesignerWindow.Tools.cs`, `.Inspector.cs`, `.Layers.cs` and so on. |
| `UI\` | The main window (split by page), the device wizard, the look (`Theme.xaml`) and shared window pieces. |
| `Settings\` | `settings.json`: what's in it, saving with backups, and upgrading settings from earlier versions. |
| `Runner\` | The dashboard's loop, its precise timer, previews and diagnostic logs. |
| `CommandLine\` | The developer and support tools. |
| `Checks\` | The end-to-end checks, built only into the Checks configuration. |

Outside `src`: `tests\AlmostFastDash.Tests` runs the checks,
`Firmware\Adalight\` holds a ready-to-flash Arduino sketch, `docs\` the guides,
and `release-check.ps1` the whole pre-release check.

## Design principles

- **One standalone exe.** No installer, nothing in the app from NuGet, and no
  other software required (the build uses Microsoft's reference-assembly
  package only to compile). Everything uses Windows and .NET Framework 4.8
  built-ins:
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
- **Keep time precisely.** Windows wakes a waiting thread only on its timer
  tick, 15.6 ms by default, so a 16 ms wait often lasts 31 ms. `FineTimer`
  asks for 1 ms ticks while the dashboard runs. The loop keeps a steady beat:
  each frame is due one period after the last was due, so a late wake-up is
  made up on the next frame. Check frame rates on the wheel (`screen.status`),
  not only with the profiler, which doesn't wait.
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
    per frame. It copies only the areas it redrew into that bitmap, and
    reports them as `LastChanged`.
- **On the PC, the graphics card does the work.** `MonitorOutput` shows frames
  in a WPF window on its own thread. Each frame, only `LastChanged` is copied
  and uploaded; WPF scales and composes the window on the graphics card.
  - Over the game, the window is layered and click-through. It isn't a WPF
    see-through window, which would make WPF copy every frame back from the
    graphics card. Instead WPF draws normally, and a window procedure in front
    of WPF's keeps the layered style on. `DwmExtendFrameIntoClientArea` makes
    Windows compose the dashboard's see-through pixels.
  - The dashboard shows in one place: `ScreenSender` blanks and releases the
    wheel screen while `PcOutput` is anything but Off, and `MonitorOutput`
    closes when it's Off.
  - Show when: `IRacingTelemetrySource.CarStateOf` reads where the driver is
    from `IsInGarage`, `IsOnTrack`, `IsReplayPlaying` and `CamCarIdx`.
    `DashboardRunner.Shown` decides with `GeneralSettings.ShowsAt`. When hidden, the
    loop sends the wheel screen one black frame and draws nothing, and
    `MonitorOutput.Update` hides the window (full screen goes black). The run
    process notes the hidden point in `dashboard.hidden` for the header.
  - Only the running dashboard writes `dashboard.window`. The main window
    unlocks the overlay by keeping `overlay.unlock` fresh, the way
    `preview.request` works, so the overlay can't be left unlocked.
- **Readable at a glance.** Text is at least 16 px on an 854×480 screen (14 in
  dense tables), at least 4.5:1 contrast against what's behind it, and never
  runs into other text. The dashboard audit enforces this as drawn.
- **Device-agnostic.** Nothing user-facing names a particular wheel or light
  bar. Devices are described by shape, role and protocol.

## Automatic checks

The end-to-end checks drive the real app: they build every template and draw
it in every state, compare redrawing only what changed with a full redraw, step
flags through their timeline, play pack racing through the radar with
known lanes and count the cars it draws on the wrong side, edit designs through the designer's own commands
(draw, mask, group, repeat, undo), save and reload, and run the light layers.
Each one prints PASS or FAIL with what it found.

They need the designer's private members, so they're compiled into the app
itself, in a configuration of its own: `Checks` is Release plus the
`src\AlmostFastDash\Checks` folder and the `--designer-selftest` option. The
exe you ship is built without them. `tests\AlmostFastDash.Tests` builds the
Checks exe, starts it in an empty folder as a first run would, and turns each
line of its report into a test:

```powershell
dotnet test
```

GitHub runs the same build and checks on every push and pull request
(`.github\workflows\build.yml`).

Before a release, `release-check.ps1` does all of it on a clean copy:

- builds the app;
- runs the end-to-end checks, including a readability audit of every template;
- renders every template;
- writes the light frames;
- replays your latest finished iRacing recording.

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
| `--render-library <folder>` | Saves contact sheets of every Library item and a list of every setting. |
| `--audit-dashboards <report.txt>` | Checks every template and saved dashboard, as drawn on every screen and tap view, for text that's too small, too faint, runs into other text, reaches past its widget's edge or is taller than its space. Exits with 1 when it finds anything. |
| `--profile-dashboards <report.txt>` | Times every template and saved dashboard drawing 480 frames of the animated lap, as the wheel draws them. Reports each one's median, mean and slowest frames, and the widgets that took the time. |
| `--ui-snapshot <scenario> <file.png>` | Opens a window in a scripted state and saves a picture of it. |
| `--screen-test [report.txt]` | With the dashboard stopped: a test pattern on the wheel screen (a whole frame, numbered squares sent as areas, then a sliding bar sent as whole frames and as only what changed). Reports the screen's model and both frame rates. |
| `--led-soak <COM port> <LED count> [--fast]` | Streams fast-changing frames to an LED board for 90 seconds and reports whether it kept up. |
| `--light-frames <report.txt>` | Writes each dashboard's LED colors through a rev sweep and the preview states. |
| `--controllers <report.txt>` | Lists the game controllers Windows sees and the buttons held on each. |
| `--designer-selftest <report.txt>` | Checks build only: runs the end-to-end checks. They write settings next to the exe, so run them from a copy (`dotnet test` does). |

The render, audit and snapshot options read `settings.json` next to the exe,
so run them from a scratch copy rather than your real install.

## Code style

`.editorconfig` sets one style for every file: four spaces, Windows line
endings, UTF-8, no trailing whitespace, braces on their own lines and lines of
at most 140 characters. It also flags unused usings, members, parameters and
assignments, and fields that could be `readonly`. Visual Studio, Rider and VS
Code show these as you type. To check the whole project in one go:

```powershell
dotnet build src/AlmostFastDash -c Release -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true
```

(The compiler reports unused usings only in a build that also generates
documentation.)

- **One main type per file**, named after it; small types that only serve it
  (an enum, a little record) can sit with it. A large class is split into
  partial files named after the part they hold: `DesignerWindow.Tools.cs`,
  `LayoutRenderer.Changes.cs`, `MainWindow.Settings.cs`.
- **A folder per part, and a namespace per folder** (`AlmostFastDash.Rendering`
  for `Rendering\`). Two kinds of partial file keep their class's namespace in a
  folder of their own: the checks (`Checks\`) and the templates
  (`Dashboards\Templates\`).
- **Comments explain why**, in plain English. Most classes and members have a
  summary.
- **Calls that take settings as name and value pairs**, as the templates do,
  keep each pair on one line.
- On-screen text is short and in US English. Explanations belong in the
  [user guide](guide.md), not on the screen.

Write code the way the code around it is written.
