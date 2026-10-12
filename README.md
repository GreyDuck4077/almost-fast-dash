# Almost Fast Overlays

A small, standalone dashboard manager for sim racing wheel screens, with LED
control. It drives the SSG DV-5 wheel screen and its LEDs and the SSG LB-10
light bar from iRacing telemetry, and lets you design exactly what the screen
shows.

## Scope

In scope:

- **Dashboards** on the wheel screen, and a designer to build them.
- **LEDs** on the screen and on light bars: rev lights, flags, blind spot, alerts.
- **Devices**: finding, testing and configuring the screen and LED hardware.
- **iRacing** telemetry, a small footprint, and one standalone exe.

Out of scope: on-monitor overlays (standings, relative and the like) and
anything else that isn't the wheel screen or its lights. New ideas go on a list
until the core is proven on real hardware.

## Using it

Double-click `App\AlmostFastOverlays.exe` to open the settings app. The same
executable runs the dashboard with `--run`; the Windows Startup shortcut uses
that quiet mode automatically. Preferences are stored in `App\settings.json`
and save automatically as you edit.

## Dashboards

Every dashboard is a design made in the dashboard designer, in one Liquid Glass
style: frosted panels over an aurora background, a header hung from angled
hairlines, readouts on glass tiles and a glowing delta badge. Two come with the
app:

- **Halo** (road and dirt road, the default) – the gear inside a halo of rev
  lights; last, best and predicted laps on the left, fuel, laps of fuel and the
  refuel amount on the right; TC/ABS/bias/incidents in the top notch; the car
  behind bottom-left and the car ahead bottom-right. On pit road the ring becomes
  a pit-speed gauge.
- **Oval** (oval and dirt oval) – a glass spotter dial with your car and cars
  alongside (car left, car right, three wide) and the spotter's call; the car
  behind and ahead with gaps; last/best/current lap and lap speed; laps of fuel
  and whether you make the finish; laps to go and places gained since the start.

Both are ordinary designs: open them in the designer to change anything, or
start new ones from **New dashboard** (Halo, Oval or a blank canvas).

## App layout

The app has three sections: **Dashboards** (the screen designs and their
lights), **Devices** (LED devices and the wheel screen)
and **Settings** (units, startup, diagnostics). On Windows 11 the windows use the
acrylic glass backdrop.

Dashboards lists each dashboard under it in the sidebar; the section itself opens
an overview of cards, and a dashboard opens its own page with an "‹ All
dashboards" link back. The header shows whether the dashboard is running on the
DV-5, with start, restart and stop. Everything saves as you go, including the
screen and light designers; there is no Save button anywhere.

While iRacing isn't running, the dashboard checks the screen 4 times a second and
the lights run at 10 frames a second, instead of 20 and 60.
## Devices

Every LED device is set up once, on the **Devices** page, with **Add device**:

1. **Find**: the app asks each USB serial port whether it is an LED board (only
   when you click; ports already set up and Bluetooth ports are left alone), or
   looks up a WLED controller by its address.
2. **Name**: the name the device reports is filled in; change it to anything.
3. **Count**: a device the app knows (the DV-5 and LB-10, by USB ID or name, or
   a WLED controller) has its count filled in and lit to confirm. Otherwise you
   count together: only LED N is lit, and you move N until it's the last LED.
4. **Map**: *Show the order* lights each LED in turn with its number. Then split
   the LEDs into **groups** (name, purpose, LEDs from–to, reverse); the group you
   are editing lights on the device, brightest where it starts.

Each group has a **purpose**: Shift lights, Left side, Right side, Light bar or
Other. Light layers aimed at a purpose light every group with it on every
device, so light profiles work with whatever is plugged in; a layer can also
aim at one device's group. The DV-5 is set up as Left side 1–4, Shift lights
5–12 and Right side 13–16 (reversed, so it fills bottom to top), which is the
same layout as before. Setup pauses a running dashboard while it uses the
devices and starts it again afterwards.

Supported devices:

- **Arduino LED boards** that speak the common sim-racing LED serial protocol, as
  the SSG DV-5 and LB-10 do.
- **Adalight**: an Arduino driving a WS2812/NeoPixel strip with the open Adalight
  protocol. `Firmware\Adalight\Adalight.ino` is a ready-to-flash sketch.
- **WLED** controllers on your network, driven with WLED's real-time UDP protocol.

Devices are found again by USB ID when Windows renumbers COM ports, and they
connect in the background, so one that's unplugged never makes the others
stutter. Code: `LedDevices.cs`, `DeviceSetupWindow.cs`.
## Light layers

Each dashboard can use a layered light profile instead of the classic light
settings: layers with an effect (car-accurate rev lights, meters, blink, breathe,
sweep, flags, blind spot...), a set of LEDs, a Show-when condition and a blend
mode, top of the list winning. LEDs run on their own loop at up to 60 frames a
second with gamma and fade-out. Open **Light designer** from a dashboard's page.
Code: `Lights\`.

## Dashboard designer

Designed dashboards are data, not code: a theme plus one or more screens of
widgets, stored in `settings.json` (code in `Designer\`).

Start one from **New dashboard** (pick Halo, Oval or a blank canvas), or press
**Design** on any dashboard. Panels default to the **Liquid** glass style and
backgrounds to **Aurora**.

- **Canvas** – the DV-5 screen at 854×480. Drag to move, use the handles to
  resize (Shift keeps proportions), drag across empty space to select several.
  Snapping lines up edges and centers with the screen and other widgets; Alt
  turns it off. Ctrl+wheel zooms.
- **Add** – 16 widget types (panel, text, value, gear, bar, arc gauge, on-screen
  shift lights, relative, blind spot, flag, session progress, pedal trace, delta
  trace, lap history, shape, gradient, image) and ready-made blocks (stat card,
  glass card, gear cluster, delta card, fuel card, lap times, relative card, car
  settings strip, LED strip, spotter bars, low-fuel warning, pit limiter banner,
  session header). Select widgets and right-click › **Save as reusable block**
  to add your own (stored in `App\Blocks`).
- **Layers** – stacking order (drag to reorder), hide, lock and rename.
- **Theme** – presets (Midnight, Carbon, Deep ocean, Neon, Le Mans, Daylight),
  accent, font, background style and every theme color. Widgets that use theme
  colors (`accent`, `panel`, `good`...) restyle together.
- **Inspector** – every property of the selected widget. The **ƒx** button turns
  any property into a live formula; **Show when** hides or shows a widget by
  condition. The + button beside a formula lists every data field (with its
  current value), every function, and ready-made recipes.
- **Screens** – a design can have several screens. One with a condition (such as
  `OnPitRoad` or `Caution`) takes over while it is true.
- **Preview** – an animated lap, live iRacing data, or fixed states (shift point,
  pit lane, cars alongside, not connected, each flag). **Live on DV-5** saves
  every change so a running dashboard updates within a second.
- **Undo/redo**, copy/paste, duplicate, align and distribute, keyboard nudging.
- **Export / Import** share a design as one `.afodash` file, images included.

Formulas read like `FuelLapsLeft < 2 ? $bad : $text`. They support
`+ - * / %`, comparisons, `and or not`, `cond ? a : b`, text in quotes, colors
as `#RRGGBB` or theme colors such as `$accent`, about 80 data fields and
functions including `format`, `laptime`, `signed`, `fuel`, `gear`, `clock`,
`time`, `steps`, `deltacolor`, `alpha`, `mix`, `blink`, `clamp` and `norm`.

On the device, a designed dashboard redraws only when something it shows
changes. Everything that never changes is drawn once into a cached base image,
so Halo and Oval draw in about 10–12 ms.

`--render-templates <folder>` writes every template as PNGs.
`--designer-selftest <report.txt>` runs the designer's end-to-end checks; it
saves into the settings next to the exe, so run it from a copy.

## LB-10 brow light bar

The SSG LB-10 (usually COM6) is driven alongside the DV-5. Each dashboard picks
what it shows: shift lights, blind spot, flags only, brake and throttle, brake,
throttle, lap delta, fuel level or off. With "Flash flags" on, any flag takes
over the bar in its own color.

While the dashboard runs it reloads `settings.json` within a second of any
change, so edits apply without a restart. When automatic switching is on, the
dashboard follows iRacing's track type: the default dashboard is used when it is
assigned to that type, otherwise the first dashboard assigned to it, otherwise
the default.

Use `--render-previews <folder>` to write a PNG of every dashboard without
touching the DV-5, which is safe while the dashboard is running.

This is a minimal direct-control dashboard proof for the SSG DV-5 and iRacing.

It currently:

- connects directly to the 854x480 M-PRO WinUSB screen;
- reads live `Gear` and `Speed` values from iRacing's local SDK;
- displays a large gear and whole-number speed in MPH;
- uses the four LEDs on each side as a spatial blind-spot indicator;
- fills the eight upper LEDs symmetrically as an RPM/shift gauge;
- shows session progress in a slim white strip at the top of the screen;
- does not modify or flash device firmware.

Only one program can drive the DV-5 at a time, so close any other dashboard software while this runs. Almost Fast Overlays runs until stopped, reconnects automatically when the DV-5 is unplugged and restored, and continues waiting when iRacing closes or changes sessions.

On a normal shutdown, Almost Fast Overlays sends a black frame before releasing the display so the controller does not retain the last dashboard or flag image.

Build (needs the .NET SDK; the result needs only Windows) and run:

```powershell
dotnet build -c Release
& '.\bin\Release\net48\AlmostFastOverlays.exe'
```

Press Ctrl+C for a clean shutdown. Use `--seconds 120` for a time-limited run.

Use `--demo` to cycle from reverse through sixth gear while speed rises, followed by animated green, white, yellow, red, black, meatball, blue/yellow, safety-car, surface-condition, and checkered flags. It then sweeps the left and right blind-spot LEDs from front to beside to rear and finishes with an RPM/shift-light sweep. The sequence repeats until stopped.

Use `--preview` to hold representative values on the premium dashboard for physical-screen layout review without requiring an active iRacing session.

Use `--preview-timed` to preview the same dashboard in timed-race mode with a continuous remaining-time progress bar.

The timed-race bar is cyan above 60% remaining, yellow from 60% through 25%, and red below 25%.

Subdued gray adjacent-gear hints flank the main gear: previous on the left and next on the right. A dash is shown when no further gear exists.

Gear changes use a 14-step, 420 ms arc transition: the old gear rotates outward and fades while the new gear rotates into the center. Downshifts reverse the direction.

The screen is updated only when the displayed gear, whole-number MPH, connection state, or flag changes. This avoids unnecessary bitmap rendering and USB transfers while the display is unchanged.

The side LEDs show red at the top when the nearest overlapping car is ahead, the two middle LEDs while it is alongside, and the bottom two LEDs as it falls behind. iRacing's native `CarLeftRight` spotter state selects the occupied side; signed opponent lap distance supplies the longitudinal position. The LED port is set in Device & app (COM7 by default) and can be overridden with `--led-port COM8`.

The upper RPM LEDs fill from both outside edges toward the center. At iRacing's shift point, all eight upper LEDs and the gear/speed text flash red together.

The screen's upper strip shrinks as time expires in timed races, fills equal lap blocks from left to right in lap-count races, and displays remaining time as text during practice and other non-race sessions.

Fuel figures come from fuel used on each clean lap (laps that touch pit road or include a refuel are skipped) and averages the last five. From that it shows laps of fuel left, laps to go (estimated from average lap time in timed races), fuel to add to reach the finish, brake bias, stint length since the last pit stop, and a bar chart of the last five lap times.

Speed can be shown in MPH or km/h and fuel in liters or gallons on every dashboard.

The Velocity dashboard uses a graphite/carbon background with cyan structure, white primary values, amber lap emphasis, and a centered delta bar. It shows the nearest drivers ahead and behind, position, lap, incidents, fuel, current lap time, and live comparison to the session-best lap. By design, positive/green/right means faster and negative/red/left means slower.

After each completed lap, a white summary band appears for four seconds with position on the left, completed lap on the right, and best, last, and session-average lap times in the center.

Completed-lap blocks in the top progress strip remain cyan for normal laps and yellow for any lap driven partly or entirely under an iRacing yellow/caution state.

At the checkered transition, the checkered screen shows `FINISH` and the player's finishing position for ten seconds, then restores the normal dashboard even if iRacing continues holding the checkered state.

The live green-flag alert ignores iRacing's pre-start `greenHeld` state. It triggers only on the actual `green` transition, flashes rapidly about five times, and then restores the normal dashboard.

iRacing caution and waving-caution states display as yellow flags. The safety-car graphic remains available in demo mode but is not inferred from a caution state during live driving.

The live yellow alert flashes six times when a yellow or caution state first activates, then restores the dashboard during extended pace laps. It rearms after iRacing clears the caution state.

Compact radar diagnostics are stored in the `Logs` folder next to the executable. Files rotate at 2 MB and only the newest eight are retained. They contain timestamps, raw left/right spotter state, nearest relative lap distance, and the resulting LED zones—no driver names or account information. Turn them off in Device & app, or pass `--no-logging`.

## Auto-start

Run `Install-Autostart.ps1` once to build a compact Windows application under this project's `App` folder, add it to the current Windows user's Startup folder, and create desktop and Start Menu shortcuts. Diagnostics are stored under `App\Logs`, keeping the application data inside the project folder. All shortcuts target the windowless executable directly; Windows Script Host is not used. The Start Menu also receives `Stop Almost Fast Overlays`, which requests a clean shutdown and clears the LCD. Run `Remove-Autostart.ps1` to remove the shortcuts. Only one Almost Fast Overlays instance is allowed at a time.

## Standalone

`AlmostFastOverlays.exe` is the whole application: one file, about 230 KB, with
no DLLs beside it and nothing else to install. It targets .NET Framework 4.8,
which is part of Windows 10 and 11, and references only Windows' own assemblies.

- The DV-5 screen (USB `C872:1004`) is driven directly through Windows' built-in
  WinUSB driver (`Dv5Screen.cs`).
- iRacing telemetry is read straight from iRacing's shared memory, including its
  session YAML (`IRacingSdk.cs`).
- The DV-5 and LB-10 LEDs, and other Arduino LED boards, use serial ports (`ArduinoLedController.cs`); Adalight and WLED devices are in `LedDevices.cs`.
- Settings use Windows' built-in JSON serializer.

Copy the exe to any folder and run it; `settings.json` and `Logs` are created
next to it.

## License

Copyright (C) 2026 adam-paul (https://github.com/Sota4077)

Almost Fast Overlays is free and open-source software under the
[GNU General Public License v3.0](LICENSE). You may use, study, change and share
it, including commercially. If you distribute it or anything built from it, you
must release that complete source code under the same license, so it stays open
for everyone.
