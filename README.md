# Almost Fast Dash

A small, standalone dashboard manager for sim-racing wheel screens, with LED
control, for iRacing. Design exactly what your wheel screen shows, set up any
LED device once, and build light profiles for rev lights, flags, blind spots and
alerts. One exe, no installer, no other software required.

## What it does

- **Dashboards** on a wheel screen, made in a built-in designer. Two flagships
  lead the set:
  - **Roady**, for road courses: a sweep of revs arched over a huge gear, shift
    colours from the car's own shift points, plus delta, pedals, bias and fuel.
  - **Roundy**, for ovals: curved blind-spot arcs, speed and gear in a rev ring,
    the cars around you, and a lap-falloff chart of your stint.

  **Apex**, **Twin Dial**, **Neon**, **Ribbon**, **Spotter** and **Race Display**
  are also included. Every one is built only from the designer's own widgets,
  so any of them can be changed or rebuilt by hand. The dashboard can switch by
  the session's track type, and a wheel button can page through screens.
- **Designs for any resolution.** A new dashboard is built for the wheel screen
  that's plugged in, or for the resolution you choose (common wheel and dash
  sizes, or any width and height). Templates scale to it, and the designer
  can move a dashboard to another size later. Which screens the app can
  actually *drive* is a shorter list: see [Compatibility](#compatibility).
- **Touch**: on a touch wheel screen, tapping a panel switches what it shows.
  In the designer, drop something onto a panel to add it to the panel or to a
  new view of it.
- **LED devices** of any shape: the LEDs around a wheel screen, light bars, LED
  strips. Arduino boards on the common sim-racing LED protocol, Adalight strips
  and WLED controllers.
- **Light profiles** per dashboard: layers of effects (rev lights, flags, blind
  spot, pit limiter, low fuel, ABS…) with conditions, priorities and a colour
  theme.
- **iRacing** telemetry read straight from iRacing's shared memory.

It does not do on-monitor overlays (standings, relative and the like).

## Getting started

1. Download `AlmostFastDash.exe` from the
   [latest release](https://github.com/Sota4077/almost-fast-dash/releases/latest),
   put it in a folder of its own and run it. Settings (`settings.json`), logs and
   track maps are kept next to it. Windows 10 or 11; nothing else to install.
   **Settings › Check for new versions** shows a link here when a newer one is out.
2. **Devices › Add device** for each LED device (see below).
3. **Dashboards**: pick or customise a dashboard and press the start button in
   the header. The dashboard runs in the background and keeps going when the
   window is closed; **Settings › Start with Windows** starts it at sign-in.

Only one program can drive a wheel screen or an LED board at a time, so close
other dashboard software (SimHub and the like) while this runs. The Devices page
says when another program has a device's port.

## Compatibility

What has been tested on real hardware, what is supported by protocol but not
yet tried, and what you can only design for:

| | Status |
| --- | --- |
| **Sim** | iRacing only. |
| **Wheel screens driven** | VoCore-family "M-PRO" USB screens at 854×480 (USB C872:1004), including touch. Tested on one such wheel screen. No other screen is driven yet. |
| **Designing for other sizes** | Any resolution from 64×64 to 3840×3840. A design for another size is fitted onto the driven screen without stretching; it does not make the app drive a screen of that size. |
| **LED boards tested** | The LEDs around a wheel screen and a light bar, both on SSG firmware (sim-racing LED serial protocol). |
| **LED devices supported, not yet tested here** | Other Arduino boards on the same serial protocol, Adalight strips (`Firmware\Adalight`), WLED controllers over UDP. |
| **Windows** | 10 and 11, .NET Framework 4.8 (built in). |

If you try it on something not in the tested rows, please open an issue with
what worked and what didn't.

## How the numbers are worked out

Race information is only useful if it can be trusted, so here is what the
dashboard does behind the main figures:

- **Gaps to the cars around you** (relative, ahead/behind) come from iRacing's
  own estimate of each car's time to reach its spot on the lap, which follows
  the track's fast and slow parts. If an estimate is missing or doesn't agree
  with where the cars are, the gap falls back to distance × lap time. Gaps in
  the race order come from iRacing's official timing.
- **Fuel per lap** is the average of your last five clean laps: laps that
  didn't touch pit road, weren't under caution and weren't refuelled. Until
  three such laps exist, fuel figures are marked **EST**.
- **Fuel to add** counts only what's left of the current lap plus the laps to
  go, and adds the **safety margin** from Settings (none, ½, 1 or 2 laps). In a
  timed race it assumes the race ends when the leader next crosses the line
  after the clock runs out, worked out at your pace (slightly generous if you're
  behind the leader). It shows the amount with its unit, or ENOUGH.
- **Lap falloff** compares your last green-flag lap of the stint with the
  stint's best; caution and pit laps are left out.

## Tests and releases

`release-check.ps1` builds the app and runs every automatic check on a clean
copy before a release: the designer and light self-tests, a render of every
template, the light frames, and a replay of your latest iRacing recording.

```powershell
powershell -ExecutionPolicy Bypass -File .\release-check.ps1
```

Settings are saved whole or not at all, the previous copy is kept as
`settings.json.bak`, and a dated copy goes into `SettingsBackups` every ten
minutes of changes (the last twelve are kept). A damaged settings file is
replaced by the newest backup that reads, and the damaged one is kept beside
it. **Devices › Wheel screen › Performance** shows the frames a second, drawing
time and sending time measured on the wheel.

## Devices

**Add device** walks through four steps:

1. **Find**: the app asks each USB serial port whether it's an LED board (only
   when you ask; ports already set up and Bluetooth ports are left alone), or
   looks up a WLED controller by its address.
2. **Name** it.
3. **Shape**: what the device looks like and how many LEDs each part has:
   - *Screen with LEDs*: LEDs down the left side, across the top, down the right
     side and along the bottom.
   - *Light bars*: how many bars, LEDs on each, and whether they're joined end
     to end into one long bar or work separately.
   - *LED strip*: its length.

   Each part gets a role (Shift lights, Left side, Right side, Light bar or
   Other); light layers aimed at a role light every part with it, on every
   device.
4. **Map**: one LED at a time lights on the device and you click where it is on
   a drawing of the shape. After two neighbours the app follows the chain on its
   own, so most devices take two clicks; wiring that jumps is asked for LED by
   LED. Then you click a few LEDs to check the right ones light, or place every
   LED by hand if something's off. If an LED doesn't light, the shape has more
   LEDs than the device and the app takes you back to fix the count.

From then on a device is drawn in its real shape everywhere, and fills run the
way each part runs: sides bottom to top, rows and bars left to right.

Each device's row shows its live state: connected (and how), connecting, not
found, in use by another program, or keeps disconnecting. **Settings** on a row
changes its name, power and on/off, sets its LEDs up again, tests it or removes
it.

**Power**: a device powered from the PC's USB port is held to about 250 mA, and
bright frames are dimmed to fit, so a whole bar lit white can't overload the
port (which drops the device off USB with its LEDs stuck on). Choose *a powered
hub or its own power supply* to remove the limit.

**Fast updates** (several colour changes per message) are used automatically
when a board accepts them. Messages are kept within what these boards can take
(26 bytes), each waits for the board's acknowledgement, and a device that keeps
disconnecting is retried less and less often.

Supported devices:

- **Arduino LED boards** on the common sim-racing LED serial protocol, such as
  the LEDs on many wheel screens and light bars.
- **Adalight**: an Arduino driving a WS2812/NeoPixel strip with the open Adalight
  protocol. `Firmware\Adalight\Adalight.ino` is a ready-to-flash sketch.
- **WLED** controllers on your network, over WLED's real-time UDP protocol.

Devices are found again by USB ID when Windows renumbers COM ports.

## The designer

Open **Design** on a dashboard. Drag widgets and ready-made blocks onto the
screen; every setting can be a fixed value or a live formula (`ƒx`).

- **Widgets**: text and values, the gear, bars and arc gauges (needles, ticks,
  numbers), shift lights, the leaderboard, relative, tyres, a self-tracing track
  map, live graphs of any value, flags, blind-spot bars, panels and shapes.
- **Quick values**: one-click items for engine, lap, sectors, tyres, fuel, race,
  leaderboard, notices and the PC.
- **Shared tools** on every widget where they fit: shape (bend, taper, bulge,
  slant), fill direction, flip and rotate, and style (recolour with a colour,
  gradient or texture, outline, drop shadow, outer glow).
- **Groups** (Ctrl+G) with a number formulas read as `Slot`: build one driver row
  with `standing(Slot, "Name")`, then Ctrl+D copies it for the next place.
- **Several widgets** selected show their shared settings, edited together;
  Ctrl+Shift+C / Ctrl+Shift+V copies and pastes a widget's look.
- **Screens**: a screen with a *Show when* condition takes over when it's true
  (pit lane, caution); the others are pages a wheel button steps through.
- **Export and import** a whole dashboard, with its images and lights, as one
  `.afodash` file.

The **track map** draws itself: drive one clean lap at a track and the app
traces it from your speed and heading, then keeps it in `TrackMaps` for next
time.

## Light profiles

Open **Light designer** from a dashboard's page. A profile is a stack of layers,
the top of the list winning where layers overlap. Each layer has:

- an **effect**: rev lights (from the car's own shift-light RPMs), meter, centre
  meter, solid, blink, breathe, sweep, flag colours, blind spot, gradient, blank;
- the **LEDs** it lights: a role on every device (shift lights, sides, light
  bar, the light bar split down the middle…) or one device or part;
- **Show when**: a condition such as `OnPitRoad && PitLimiter`;
- a **blend** (over, or replace the whole target) and brightness.

The **colour theme** sets what `good`, `warn` and `bad` mean on the LEDs: the
low, middle and high of rev lights and meters, the shift flash, blind-spot
warnings. Pick one of 24 themes or your own three colours.

**Preview** plays any moment of a race: an animated lap, live iRacing, the shift
point, cars alongside, the pit limiter, each flag. The same states are on a
dashboard's page, where **Show on my wheel** puts the preview on the real
screen and LEDs (through the running dashboard while iRacing isn't connected).

Out of the car or in a replay, the LEDs go dark. The checkered flag shows once,
for ten seconds.

## Dashboard designer

Dashboards are data, not code: a theme plus one or more screens of widgets,
stored in `settings.json`.

- **Canvas**: the screen at the dashboard's own resolution (Display resolution
  changes it). Drag, resize, multi-select, snap to edges and centres (Alt turns
  snapping off), Ctrl+wheel to zoom.
- **Add**: widgets (panel, text, value, gear, bar, arc gauge, on-screen shift
  lights, relative, blind spot, flag, session progress, pedal and delta traces,
  lap history, shape, gradient, image) and ready-made blocks. Save your own
  selection as a reusable block.
- **Theme**: presets, accent, font, background and every theme colour.
- **Inspector**: every property of the selected widget. **ƒx** turns any
  property into a live formula; **Show when** shows a widget only when a
  condition is true.
- **Screens**: a screen with a condition (such as `OnPitRoad`) takes over while
  it's true.
- **Undo/redo**, copy/paste, align and distribute, and **Export/Import** of a
  design as one `.afodash` file.

Formulas read like `FuelLapsLeft < 2 ? $bad : $text`: arithmetic, comparisons,
`and or not`, `cond ? a : b`, colours as `#RRGGBB` or `$theme` names, about 80
data fields and functions such as `format`, `laptime`, `fuel`, `steps`, `mix`
and `blink`.

## Settings

Units (speed, fuel, temperature), the fuel safety margin, the page the app
opens on, minimising to the system tray, switching dashboards by track type,
starting with Windows, checking for new versions, and diagnostic logs.

**Diagnostic logs** (in `Logs` next to the exe) record blind-spot decisions and
which light layer lit which LEDs, so an unexpected light can be looked up after
a race. No names or account details are recorded.

## Command line

| Option | Does |
| --- | --- |
| *(none)* | Opens the app. |
| `--run` | Runs the dashboard (what the start button and Start with Windows use). |
| `--stop` | Asks a running dashboard to stop. |
| `--replay-ibt <file.ibt> <report.txt>` | Replays an iRacing telemetry recording through the app: every flag, each lap's times, falloff and fuel, the traced track map, and every light longer than a second. |
| `--live-check <report.txt> [seconds]` | While you're in a session: what iRacing sends for every value the dashboards use, what the app makes of it (gaps by both methods, fuel, the race order) and the screen's measured performance. |
| `--render-templates <folder>` | Saves a picture of every template in its states, the second views and other screen sizes. |
| `--led-soak <COM port> <LED count> [--fast]` | Streams fast-changing frames to an LED board for 90 seconds and reports whether it kept up. |
| `--light-frames <report.txt>` | Writes each dashboard's LED colours through a rev sweep and the preview states. |
| `--designer-selftest <report.txt>` | Runs the end-to-end checks (they write settings next to the exe, so run them from a copy). |

## Building

Needs the .NET SDK; the result needs only Windows 10 or 11.

```powershell
dotnet build -c Release
```

`bin\Release\net48\AlmostFastDash.exe` is the whole application: one file,
targeting .NET Framework 4.8 (part of Windows) and referencing only Windows' own
assemblies. The wheel screen (a VoCore-family USB panel) is driven through
Windows' built-in WinUSB driver (`Dv5Screen.cs`), iRacing's telemetry is read
from its shared memory (`IRacingSdk.cs`), and LED devices are in
`LedDevices.cs`, `ArduinoLedController.cs` and `LedLayout.cs`.

## License

Copyright (C) 2026 adam-paul (https://github.com/Sota4077)

Almost Fast Dash is free and open-source software under the
[GNU General Public License v3.0](LICENSE). You may use, study, change and share
it, including commercially. If you distribute it or anything built from it, you
must release that complete source code under the same license, so it stays open
for everyone.
