<p align="center">
  <img src="docs/images/logo.png" width="104" alt="Almost Fast Dash icon">
</p>

<h1 align="center">Almost Fast Dash</h1>

<p align="center">
  <strong>Dashboards and LED control for iRacing.</strong><br>
  Design exactly what your dashboard shows and light every LED you own, from one small app.<br>
  On your wheel screen, in a window, or over the game: no wheel screen needed.
</p>

<p align="center">
  <a href="https://github.com/Sota4077/almost-fast-dash/releases/latest"><img src="https://img.shields.io/github/v/release/Sota4077/almost-fast-dash?style=flat-square&label=download&color=2dd4bf" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-2dd4bf?style=flat-square" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/sim-iRacing-2dd4bf?style=flat-square" alt="For iRacing">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Sota4077/almost-fast-dash?style=flat-square&color=2dd4bf" alt="GPL-3.0 license"></a>
</p>

<p align="center">
  <a href="https://github.com/Sota4077/almost-fast-dash/releases/latest"><strong>Download</strong></a>
  &nbsp;·&nbsp; <a href="#installing">Install</a>
  &nbsp;·&nbsp; <a href="#dashboards">Dashboards</a>
  &nbsp;·&nbsp; <a href="docs/guide.md">User guide</a>
  &nbsp;·&nbsp; <a href="docs/development.md">Development</a>
</p>

<p align="center">
  <img src="docs/images/hero.png" alt="The Roady dashboard on a wheel screen, with Roundy and Neon behind it">
</p>

## Highlights

- **Made for the screen on your wheel.** Ten ready-made dashboards, each built
  with the same designer you get, so you can change any of them or build your
  own from scratch.
- **A designer you already know how to use.** Laid out like an image editor:
  tools on the left, Properties and Layers always in view. Draw with a pen,
  shapes, gradients and patterns; mask with clipping, fades and blend modes;
  repeat anything in rows or around a dial. Keep what you build as modules to
  reuse and share, and drive any setting with live data.
- **Lights that make sense.** Rev lights from each car's own shift points,
  flags, blind spots, the pit limiter and alerts. They're layered with
  priorities and conditions, on any LED device you own.
- **Numbers you can trust.** Gaps come from iRacing's own timing estimates and
  fuel from clean green-flag laps. Every piece of text is checked for size and
  contrast. See [how the numbers are worked out](#how-the-numbers-are-worked-out).
- **Fast and light on your PC.** One small exe, with no installer, no
  background service and no account. The dashboard redraws only what changed,
  and sends the wheel screen only those parts. It draws the next frame while
  the last one is still being sent, at up to 60 frames a second.
- **Any screen, no wheel screen needed.** Drive a VoCore M-PRO wheel screen
  directly, show the dashboard in a window on any monitor, float it over the
  game, or put it full screen on an HDMI dash screen. On the PC, the graphics
  card scales and composes it, and only the changed parts are copied. Show it
  only when you want it: in the car, out of it, in the garage or spectating.
- **Free and open.** GPL-3.0, with no account, no ads and no tracking.

## Dashboards

Every dashboard is built only from the designer's own widgets. Switch them by
the session's track type, page through screens with a wheel button, and on a
touch screen tap a panel to change what it shows.

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/dash-roady.png" alt="Roady dashboard"><br>
      <strong>Roady</strong> &nbsp;·&nbsp; road courses<br>
      A sweep of revs over a huge gear, with shift colors from the car's own
      shift points, plus delta, pedals, brake bias and fuel.
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/dash-roundy.png" alt="Roundy dashboard"><br>
      <strong>Roundy</strong> &nbsp;·&nbsp; ovals<br>
      Curved blind-spot arcs, speed and gear in a rev ring, the cars around you
      and a lap-falloff chart of your stint.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="docs/images/dash-oval.png" alt="Oval dashboard with the radar"><br>
      <strong>Oval</strong> &nbsp;·&nbsp; pack racing<br>
      A live radar of the cars around you, seen from just above and behind your
      car, with the gaps ahead and behind in car lengths and how fast they're
      closing.
    </td>
    <td width="50%" valign="top">
      <img src="docs/images/dash-halo.png" alt="Halo dashboard"><br>
      <strong>Halo</strong> &nbsp;·&nbsp; endurance and multiclass<br>
      Revs and the live delta in one ring, your stint and fuel, and traffic
      within eight seconds in class colors.
    </td>
  </tr>
</table>

<table>
  <tr>
    <td width="33%" valign="top"><img src="docs/images/dash-apex.png" alt="Apex dashboard"><br><strong>Apex</strong></td>
    <td width="33%" valign="top"><img src="docs/images/dash-twin-dial.png" alt="Twin Dial dashboard"><br><strong>Twin Dial</strong></td>
    <td width="33%" valign="top"><img src="docs/images/dash-neon.png" alt="Neon dashboard"><br><strong>Neon</strong></td>
  </tr>
  <tr>
    <td width="33%" valign="top"><img src="docs/images/dash-ribbon.png" alt="Ribbon dashboard"><br><strong>Ribbon</strong></td>
    <td width="33%" valign="top"><img src="docs/images/dash-spotter.png" alt="Spotter dashboard"><br><strong>Spotter</strong></td>
    <td width="33%" valign="top"><img src="docs/images/dash-race-display.png" alt="Race Display dashboard"><br><strong>Race Display</strong></td>
  </tr>
</table>

Dashboards are designed for the wheel screen you have plugged in, or for any
resolution from 64×64 to 3840×3840. Templates scale to fit.

### Flags

A flag fills the screen as it comes out and then hands the dashboard back.
While a flag stays out, a glow in its color runs around the edge of the screen,
with a tab that names it. The checkered flag shows your result.

<table>
  <tr>
    <td width="33%" valign="top"><img src="docs/images/flag-caution.png" alt="Caution flag filling the screen"><br>The flag comes out</td>
    <td width="33%" valign="top"><img src="docs/images/flag-frame.png" alt="Roady with a yellow caution frame"><br>It steps aside to a frame</td>
    <td width="33%" valign="top"><img src="docs/images/flag-checkered.png" alt="Checkered flag with the result"><br>Your result at the finish</td>
  </tr>
</table>

## The app

<p align="center">
  <img src="docs/images/app-designer.png" alt="The dashboard designer">
</p>
<p align="center"><em>The designer: drag widgets onto the screen, or draw your own. Any setting can be a fixed value or a live formula.</em></p>

<table>
  <tr>
    <td width="50%" valign="top"><img src="docs/images/app-masks.png" alt="Clipping masks, fades and blends"><br><strong>Masks</strong>: stripes clipped to an outline make a kerb; a gradient fills a gear digit; light spills, fades and blends.</td>
    <td width="50%" valign="top"><img src="docs/images/app-repeat.png" alt="Repeat in rows and around a circle"><br><strong>Repeat</strong>: one tick becomes a dial, one light a shift-light row, one number a dial's scale.</td>
  </tr>
  <tr>
    <td width="50%" valign="top"><img src="docs/images/app-modules.png" alt="Modules in the Library"><br><strong>Modules</strong>: keep what you build, place it anywhere, share it as a file, and recolor it in a click.</td>
    <td width="50%" valign="top"><img src="docs/images/app-drawing.png" alt="Drawing tools"><br><strong>Drawing</strong>: a pen, polygons and stars, gradient and pattern fills, carbon fiber and brushed metal.</td>
  </tr>
</table>

<table>
  <tr>
    <td width="50%" valign="top"><img src="docs/images/app-dashboards.png" alt="Dashboards page"><br><strong>Dashboards</strong>: pick, customize and start a dashboard.</td>
    <td width="50%" valign="top"><img src="docs/images/app-lights.png" alt="Light designer"><br><strong>Light designer</strong>: layers of effects with conditions and priorities.</td>
  </tr>
  <tr>
    <td width="50%" valign="top"><img src="docs/images/app-devices.png" alt="Devices page"><br><strong>Devices</strong>: set up each LED device once, in its real shape, and choose your screens.</td>
    <td width="50%" valign="top"><img src="docs/images/app-dashboard-page.png" alt="A dashboard's page with the live preview"><br><strong>Live preview</strong>: see any moment of a race on screen and on your LEDs.</td>
  </tr>
</table>

## Installing

1. Download `AlmostFastDash.exe` from the
   [latest release](https://github.com/Sota4077/almost-fast-dash/releases/latest).
2. Put it in a folder of its own, such as `Documents\Almost Fast Dash`. The app
   keeps its settings, logs and track maps next to the exe, so don't put it
   anywhere that needs administrator rights to write, such as `Program Files`.
3. Run it. The first time, Windows will probably show a warning (see below).

You need Windows 10 or 11. Nothing else needs to be installed.

### "Windows protected your PC"

Almost Fast Dash isn't code-signed. Signing needs a paid certificate, renewed
every year, and this free project doesn't have one. Without one, Windows
doesn't know who published the app, and Microsoft Defender SmartScreen warns
about any download it hasn't seen widely yet. Each new version starts
again from zero. The warning comes from a reputation check, not a virus scan.
It doesn't mean anything was found in the file.

To run it:

1. Click **More info**.
2. Check that the app is `AlmostFastDash.exe`. The publisher shows as
   *Unknown publisher*.
3. Click **Run anyway**. Windows remembers your choice for that file.

You can also do this before the first run: right-click the exe, choose
**Properties**, tick **Unblock** and click **OK**.

Your browser may also warn that the file isn't commonly downloaded. Choose to
keep it from the download's menu.

> [!NOTE]
> On Windows 11 PCs with **Smart App Control** turned on (*Windows Security ›
> App & browser control*), Windows blocks unsigned apps outright. There's no
> **Run anyway** button and no way to allow a single app. Using Almost Fast Dash
> on that PC means turning Smart App Control off. On older Windows 11 versions
> it can't be turned back on afterwards without resetting Windows, so check
> before you change it.

### Checking your download

Each release lists the SHA-256 checksum of the exe. In PowerShell, in the
folder with the exe, run:

```powershell
Get-FileHash .\AlmostFastDash.exe
```

The hash must match the one on the release page. You can also scan the file
with your antivirus or at [VirusTotal](https://www.virustotal.com), or
[build it yourself](docs/development.md#building) from this source code.

Some antivirus programs flag new, unsigned apps on reputation alone. If yours
quarantines the exe, check the hash first, then restore the file. Please also
[open an issue](https://github.com/Sota4077/almost-fast-dash/issues) so the
false positive can be reported to the antivirus maker.

### First run

A welcome card on the Dashboards page walks you through these steps and ticks
each one off as you go. Units start out to match your Windows region; change
them under **Settings**.

1. Close other dashboard software, such as SimHub. Only one program at a time
   can drive a wheel screen or an LED board. The Devices page tells you when
   another program has a device's port.
2. **Devices › Add device** for each LED device. Setup takes four short steps:
   find, name, shape and map. See the
   [user guide](docs/guide.md#devices) for details.
3. **Dashboards**: pick a dashboard and press the start button in the header.
   The dashboard keeps running in the background when you close the window.
   **Settings › Start with Windows** starts it when you sign in.
4. No wheel screen? **Show on**, at the top of the window, puts the dashboard
   in a window, over the game, or full screen on another display instead.

**Updating:** turn on **Settings › Check for new versions** and the app shows a
link when a new version is out. To update, stop the dashboard, close the app
and replace `AlmostFastDash.exe` with the new one. Your settings and
dashboards stay in the folder.

## Compatibility

What has been tested on real hardware, what should work from the protocol but
hasn't been tried yet, and what you can only design for:

| | Status |
| --- | --- |
| **Sim** | iRacing only. |
| **Wheel screens tested** | VoCore's 5" M-PRO USB screen at 854×480 (USB C872:1004), including touch, sending whole frames and changed areas. |
| **Wheel screens supported, not yet tested here** | VoCore's other M-PRO screens: 4", 4.3", 5" OLED, 6.8", 3.4" and 10". Each reports its model, and the app uses the sizes from VoCore's own driver. |
| **Your PC's screens** | In a window on any display, or over the game (with iRacing in a window or borderless window): both tested on a 3440×1440 monitor. Full screen on any display other than your main one, such as an HDMI dash screen: not yet tested on a second display. Scaled and composed by the graphics card. |
| **Designing for other sizes** | Any resolution from 64×64 to 3840×3840. A design for another size is fitted onto the screen it's shown on without stretching. |
| **LED boards tested** | The LEDs around a wheel screen and a light bar, both on SSG firmware (the sim-racing LED serial protocol). |
| **LED devices supported, not yet tested here** | Other Arduino boards on the same serial protocol, Adalight strips (`Firmware\Adalight`), WLED controllers over UDP. |
| **Windows** | 10 and 11, with .NET Framework 4.8 (built into Windows). |

Tried it on something that isn't in the tested rows? Please send a
[hardware report](https://github.com/Sota4077/almost-fast-dash/issues/new?template=hardware_report.yml)
with what worked and what didn't. With the dashboard stopped,
`AlmostFastDash.exe --screen-test report.txt` shows a test pattern on a wheel
screen and notes its model.

## How the numbers are worked out

Race information is only useful if you can trust it. Here is what the
dashboard does behind the main figures:

- **Gaps to the cars around you** come from iRacing's estimate of how long each
  car takes to reach its spot on the lap, which follows the track's fast and
  slow parts. If an estimate is missing or doesn't agree with where the cars
  are, the gap falls back to distance × lap time. Gaps in the race order come
  from iRacing's official timing.
- **Fuel per lap** is the average of your last five clean laps: laps that
  didn't touch pit road, weren't under caution and weren't refuelled. Until
  three such laps exist, fuel figures are marked **EST**.
- **Fuel to add** counts what's left of the current lap plus the laps to go,
  then adds the **safety margin** from Settings (none, ½, 1 or 2 laps). In a
  timed race, it assumes the race ends when the leader next crosses the line
  after the clock runs out, worked out at your pace.
- **Lap falloff** compares your last green-flag lap of the stint with the
  stint's best. Caution and pit laps are left out.
- **The radar** places every car at its exact distance ahead or behind. iRacing
  doesn't say where cars are *across* the track, so lanes are worked out from
  the spotter's count of cars alongside you, and from the fact that two cars
  less than a car length apart can't share a lane. That keeps two-by-two
  formations two wide. Gaps in car lengths are bumper to bumper, using a
  nominal 5 m car.
- **Text** on every dashboard is at least 16 px on an 854×480 screen (14 px in
  dense tables). It stands out from what's behind it by at least 4.5 to 1, the
  WCAG level for readable text, and never runs into other text. An automatic
  audit checks this on every screen, exactly as drawn.

## Privacy

- No account, no ads, no analytics.
- The app connects to the internet only to check for new versions, if you turn
  that on (it asks GitHub for the latest release). It also talks to any WLED
  controllers you add, on your own network.
- Diagnostic logs (on unless you turn them off) stay in the `Logs` folder next
  to the exe and contain no names or account details.

## Documentation

- **[User guide](docs/guide.md)**: dashboards, devices, the designer, light
  profiles and settings.
- **[Development](docs/development.md)**: building, the project layout, the
  automatic checks and the command line.
- **[Credits](CREDITS.md)**: whose published work this builds on, and how.

## Building

You need the .NET SDK. The app it builds needs only Windows 10 or 11.

```powershell
dotnet build -c Release
```

The result is `bin\Release\net48\AlmostFastDash.exe`: the whole application in
one file. See [Development](docs/development.md) for the rest.

## License

Copyright (C) 2026 adam-paul (https://github.com/Sota4077)

Almost Fast Dash is free and open-source software under the
[GNU General Public License v3.0](LICENSE). You may use, study, change and share
it, including commercially. If you distribute it or anything built from it, you
must release the complete source code under the same license, so it stays open
for everyone.

## Credits

Almost Fast Dash includes no third-party code, but it builds on other people's
published work:
- the iRacing SDK's telemetry format;
- iRacingSDK.Net, which the app used before it became standalone;
- VoCore's open M-PRO screen driver;
- the SimHub, Adalight and WLED LED protocols.

[CREDITS.md](CREDITS.md) says exactly what was used from each, explains how the
screen protocol was first worked out, and reproduces the iRacing SDK's license
notice.

iRacing is a registered trademark of iRacing.com Motorsport Simulations, LLC.
Almost Fast Dash is an independent project. It isn't affiliated with or
endorsed by iRacing, SimHub, VoCore or any hardware maker named here.
