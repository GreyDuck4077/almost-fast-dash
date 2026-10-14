# Changelog

What changed in each version of Almost Fast Dash, newest first, in plain
English. Every version is on the
[releases page](https://github.com/Sota4077/almost-fast-dash/releases) with
its exe and checksum.

## 3.53 · 2026-10-13

- **A welcome card for new installs.** The Dashboards page lists the three
  first steps: plug in the wheel screen, add LED devices, start the dashboard.
  Each one is ticked off as it's done, and the card goes once the dashboard
  runs. Existing installs don't see it.
- **The wheel screen's status** on the Devices page: showing the dashboard,
  plugged in, or not found.
- **Units to match your region** on a new install: mph, gallons and °F in the
  US, mph with liters and °C in the UK, and km/h, liters and °C elsewhere. New
  installs used to start with mph and liters mixed.
- **Choosing a screen size** for a new dashboard, with no wheel screen plugged
  in, now says *Choose your screen size…* instead of showing an empty box.
- A changelog, issue forms for bugs, hardware and ideas, and a picture for
  sharing links.

## 3.52 · 2026-10-13

- **Leaderboard:** cars on the lead lap no longer show "+1 LAP" between the
  leader crossing the line and crossing it themselves. A car is now a lap down
  only when it's a whole lap behind on track.
- Checked in a live 39-car race: the radar matched iRacing's spotter every time
  a car came alongside, and the gaps agreed with a second method to within
  0.3 s.

## 3.51 · 2026-10-13

- **Every piece of text is checked as it's drawn.** The new dashboard audit
  checks every screen for text under 16 px (14 in dense tables), text that
  doesn't stand out from its background (WCAG 4.5:1), and text running into
  other text. All 106 problems it found are fixed.
- Labels in the theme's dim grey, such as Roady's *RPM x1000*, now use the
  readable grey. Red-zone numbers on rev scales are a brighter red.
- Brighter secondary text and larger captions in the app.
- A code cleanup: unused code removed, large files split, and one code style
  for every file.

## 3.50 · 2026-10-13

- **Radar:** a chase camera, tilted 70° and set just behind your car, with
  bigger cars and readable numbers. Two-by-two formations show two wide, and
  cars in your bubble keep their lane.
- The text floor rises to 14 px on every dashboard.

## 3.49 · 2026-10-13

- Brighter labels on every dashboard.
- Full credits for the work this app builds on ([CREDITS.md](CREDITS.md)).

## 3.48 · 2026-10-13

- **Oval** becomes a pack-racing dashboard with a live radar of the cars
  around you.
- **Halo** becomes an endurance and multiclass dashboard: one ring for revs and
  the live delta, plus traffic in class colours.

## 3.47 · 2026-10-13

- **Flags** fill the screen as they come out, then step aside to a coloured
  frame round the edge of the screen. The screen stays steady while the lights
  flash, and the checkered flag shows your result.

## 3.46 · 2026-10-13

- Neon rebuilt from the ground up. Halo and Oval get rounded corners.

## 3.45 · 2026-10-13

- An instrument finish for the flagship dashboards, and choices in the app take
  the accent colour.

## 3.44 · 2026-10-13

- **Numbers you can trust:**
  - gaps from iRacing's own timing estimates;
  - fuel per lap from clean green-flag laps only;
  - fuel to add with a safety margin.
- Settings are saved whole or not at all, with automatic backups and recovery.
- The wheel screen's real frame rate, drawing time and sending time are shown
  in the app.
- A release check runs every automatic test before each release.

## 3.41 – 3.43 · 2026-10-13

- Dashboards for any screen resolution.
- Touch screens: tap a glass panel to change what it shows. In the designer,
  drop something onto a panel to build its tap views.

## 3.35 – 3.40 · 2026-10-12

- The flagship dashboards, **Roady** for road courses and **Roundy** for ovals,
  with a lap-falloff chart.
- Liquid Glass panels.

## 3.29 – 3.34 · 2026-10-12

- Renamed **Almost Fast Dash**.
- Six new dashboards built only from the designer's own tools.
- Shared Style tools for every widget: recolour, outline, drop shadow and outer
  glow.
- An opt-in update check.
- Faster frames: only changed widgets are redrawn.
- Several selected widgets, or a group, resize together.

## 3.23 – 3.28 · 2026-10-12

- **Designer:**
  - shaped gauges (bend, taper, bulge, slant), and needles, ticks and numbers
    on arcs;
  - flip and rotate for every widget, and one fill-direction tool;
  - quick values for the engine, sectors, tyres, leaderboard and more.
- Export a dashboard together with its lights.
- Smooth page and card transitions.

## 3.13 – 3.22 · 2026-10-11 to 12

- **Device setup by shape:** describe the device, then click where each LED is.
  Most devices take two clicks.
- **Reliability:** each device sends on its own thread, with live connection
  status, automatic fast updates, and backing off when a device keeps
  dropping. Fixed wheel-screen LED boards crashing under fast-changing lights.
- LED colour themes, and deeper reds and truer greens.

## 3.0 – 3.12 · 2026-10-11

- A standalone app: no other software needed. On-monitor overlays were removed
  to focus on the wheel screen and LEDs.
- **Other LED devices:**
  - Arduino boards on the common sim-racing protocol;
  - Adalight strips;
  - WLED controllers.
- **LED safety:** a power limit for USB-powered LEDs, and the LEDs go dark out
  of the car.
- The dashboard preview can be shown on your real devices.

## Before 3.0

The project began in September 2026 as DV5Direct, a dashboard for one wheel
screen. It was then called Almost Fast Overlays, before it became a standalone
app.
