# DV5Direct

Double-click `App\DV5Direct.exe` to open the settings app. The same
executable runs the dashboard with `--run`; the Windows Startup shortcut uses
that quiet mode automatically. Preferences are stored in `App\settings.json`
and save automatically as you edit.

The settings app has three areas:

- **Dashboards** shows a live thumbnail of every dashboard, which one is the
  default, and which track types each one is assigned to. New dashboards can be
  created from any layout (Velocity, Endurance or Apex) and duplicated, renamed,
  reset or deleted.
- **Dashboard editor** shows a live DV-5 preview with animated LEDs and a
  selectable screen state (racing, shift point, flags). It edits the name,
  accent color, track-type assignment, shift lights (pattern, color preset or
  custom colors, brightness), side lights (blind spot, RPM, brake pressure,
  throttle or off, optionally the same on both sides) and flag alert timing.
- **Device & app** sets the LED controller port (with a light test), screen
  brightness, speed and fuel units, automatic switching by track type, start
  with Windows, and radar diagnostics.

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

SimHub must be closed while this program runs because both programs require exclusive access to the screen. DV5Direct now runs until stopped, reconnects automatically when the DV-5 is unplugged and restored, and continues waiting when iRacing closes or changes sessions.

On a normal shutdown, DV5Direct sends a black frame before releasing the display so the controller does not retain the last dashboard or flag image.

Build and run from an x86-capable Windows shell:

```powershell
dotnet build -c Release
& '.\bin\Release\net48\DV5Direct.exe'
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

The Endurance dashboard tracks fuel used on each clean lap (laps that touch pit road or include a refuel are skipped) and averages the last five. From that it shows laps of fuel left, laps to go (estimated from average lap time in timed races), fuel to add to reach the finish, brake bias, stint length since the last pit stop, and a bar chart of the last five lap times.

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

Run `Install-Autostart.ps1` once to build a compact Windows application under this project's `App` folder, add it to the current Windows user's Startup folder, and create desktop and Start Menu shortcuts. Diagnostics are stored under `App\Logs`, keeping the application data inside the DV5Direct folder. All shortcuts target the windowless executable directly; Windows Script Host is not used. The Start Menu also receives `Stop DV5Direct`, which requests a clean shutdown and clears the LCD. Run `Remove-Autostart.ps1` to remove the shortcuts. Only one DV5Direct instance is allowed at a time.

The proof currently references display-driver assemblies from the local SimHub installation. Replacing or isolating that dependency is a later packaging decision; it is intentionally outside this first four-step milestone.
