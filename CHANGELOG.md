# Changelog

What changed in each version of Almost Fast Dash, newest first, in plain
English. Every version is on the
[releases page](https://github.com/Sota4077/almost-fast-dash/releases) with
its exe and checksum.

## 3.60 · 2026-10-15

A formula reference to keep open while you build.

- **Formula reference.** **Formulas** in the designer's toolbar (or F1)
  opens a window to keep beside the designer, or on another display, while
  you build. It covers everything ƒx can use:
  - the language, step by step, with its order of operations;
  - every theme colour, with its swatch;
  - all 135 data fields by category, each with a live value from the
    preview;
  - all 37 functions, each with a worked example and its live answer;
  - every leaderboard and tyre field;
  - groups, pages and taps;
  - recipes.
- **Always complete.** It's built from the same lists the formulas run on, so
  a new field or function shows up in it by itself. Self-tests check every
  function has a working example and every field a description.
- **Search** across all of it. **Insert** puts an entry into the formula you
  last clicked, and **Copy** puts it on the clipboard. The quick picker beside
  each formula links to it.
- **Fixed:** the designer's Add, Layers and Theme tabs overlapped and were cut
  off in the narrow left panel.

## 3.59 · 2026-10-15

Show the dashboard only when you want it.

- **Show when**, at the top next to Show on, has four on/off choices, matched
  to what iRacing reports:
  - **In car:** driving, or sitting in the car (in the pits too).
  - **Out of car:** after you've got out of the car.
  - **Garage:** on the garage and setup screen.
  - **Spectating:** watching another driver, live or in a replay.
- **Hidden means hidden.** Wherever a choice is off:
  - the wheel screen goes black;
  - a window or the dashboard over the game goes away;
  - full screen on another display goes black.

  Nothing is drawn meanwhile, so it costs nothing. The Dashboard control
  says so, for example *Hidden · out of the car*.
- All four start on, so nothing changes until you turn one off. Before
  iRacing is running, and in previews, the dashboard always shows.
- On a narrow window, the top controls wrap onto a second row instead of
  being cut off.

## 3.58 · 2026-10-15

Choose where the dashboard shows, from the top of the window.

- **Show on**, next to the Dashboard control at the top: *Wheel screen*,
  *Window*, *Over the game* or *Full screen*. It used to be at the bottom of
  the Devices page.
- **One place at a time.** The dashboard no longer shows on the wheel screen
  and the PC together.
  - On the PC, the wheel screen goes black (rather than freezing on the last
    frame) and is let go for other programs.
  - Back on the wheel screen, the window or overlay closes.
  - The LEDs keep working either way.
- **Only what's there can be chosen.** *Wheel screen* is greyed out while
  none is plugged in, and *Full screen* without a second display. Each says
  why when you point at it.
- Over the game, a **move button** appears next to Show on: it unlocks the
  dashboard to be moved and resized, and locks it again.
- The Devices page keeps the settings for each place, and the wheel screen's
  status says when it isn't in use because the dashboard is on the PC.

## 3.57 · 2026-10-15

No wheel screen needed: the dashboard in a window, or over the game.

- **Dashboard on your PC.** *Devices › Dashboard on your PC › Show it* puts
  the running dashboard in a window, over the game, or full screen on another
  display (the old "Dashboard on a monitor"). Any dashboard works.
- **In a window:** a normal window on any display. Move and size it like any
  window. It keeps the dashboard's shape and opens where you left it. Clicks
  on the dashboard work like taps, and the game stays the active window.
- **Over the game:** the dashboard floats on top of iRacing.
  - Clicks go through it to the game.
  - **Move and resize** shows a frame: drag it to move it, and its corner to
    resize it. It locks again on Done, or when the settings window closes.
  - **Opacity** goes from 30% to 100%.
  - **Only while racing** (on by default) hides it while iRacing isn't
    running.
  - iRacing needs to run in a window or borderless window.
- **A see-through background.** In the designer, choose **None** as a
  dashboard's background and only its widgets are drawn: over the game, the
  game shows between them. The designer shows the see-through parts as a
  checkerboard, and a wheel screen shows them as black.
- **Light on your PC.** The graphics card scales and composes the window, and
  only the parts of the dashboard that changed are copied each frame.
  - Measured at 60 frames a second: a window adds about a tenth of one
    processor core, and over the game about a seventh. That's under 1% of a
    modern processor.
  - A minimised window, or the overlay hidden while you're not racing, costs
    nothing: nothing is drawn for it.
  - The renderer also copies only what changed into its finished frame.
- **The welcome card** asks where the dashboard should show (a wheel screen
  or your PC) instead of assuming a wheel screen.
- Seven new self-test checks (323), including one that the frame copied only
  where things changed matches a whole frame, flags and all.

## 3.56 · 2026-10-15

A real 60 frames a second on the wheel.

- **60 frames a second, measured on the wheel.** 3.55 asked for 60 but got
  about 34: Windows wakes a waiting program only every 15.6 ms unless it asks
  for finer timing, so most 16 ms waits lasted 31 ms. While the dashboard
  runs, it now asks Windows for 1 ms timing, and frames follow a steady beat,
  so a wait that runs a little long is made up on the next frame. With the
  animated lap on a 5" screen, Oval, Neon and Roady measured 60 frames a
  second, up from 33 to 34.
- **The lights keep pace too.** The LED loop was meant to update 60 times a
  second and had the same 31 ms waits.
- **No flash of the default dashboard while previewing.** If the dashboard
  read the preview request while the settings window was rewriting it, the
  preview dropped for a quarter of a second. The last request now holds.

## 3.55 · 2026-10-15

Faster drawing, 60 frames a second, and a polish pass over every template.

- **Dashboards draw about 40% faster.** Across the ten templates, a frame of
  the animated lap now takes 3.3 ms on average, down from 5.6 ms. Neon went
  from 9.7 to 6.2 ms and Oval from 8.4 to 4.6. Where the time went:
  - a widget's state is worked out once per frame, not twice;
  - the background under changed widgets is copied back directly;
  - a widget repainted only because a neighbour changed is copied from its
    last picture;
  - shadows, glows and outlines are mixed by the app's own compositor;
  - gauge rims are built once and reused, and their ticks drawn in a few
    batches instead of one at a time.
- **Up to 60 frames a second** while racing (it was 30), still only when
  something on the dashboard changed. *Devices › Wheel screen › Frame rate*
  sets 60 or 30; 30 uses less of your PC.
- **Glows sit where they should.** Every glow was drawn a few pixels down and
  to the right of what lit it, so lit text and pills looked lopsided. Glows
  are now centred, and the big gear number lost a faint grey ghost beside it.
- **Tyre cells fit on small panels.** When there isn't room for the wear and
  pressure line, it's left out instead of being cut off at the bottom.
- **Full race order in previews.** The designer's preview race now has the
  whole 22-car field it claims, so leaderboards fill their panels and track
  maps show the whole field.
- **Leaderboards say LEADER** in the interval column for P1, instead of
  leaving it blank.
- **The readability audit checks more:** it now also catches text that
  reaches past its widget's edge, and text taller than the space it was given.
  Every template and saved dashboard passes.
- `--profile-dashboards` times every dashboard through the animated lap.

## 3.54 · 2026-10-13

The screen pipeline, rebuilt for speed.

- **Only what changed goes to the wheel screen.** Each frame is compared with
  the last in small tiles, and only the changed areas are sent, using VoCore's
  draw command that names an area. On the Oval dashboard with the radar
  moving, that's about a tenth of the screen per frame instead of all of it.
  Checked on a 5" screen with a recorded test.
- **Drawing and sending happen at once.** The screen sends on its own thread,
  so the next frame is drawn while the last one is still going down the cable.
  Even whole frames now go at 43 a second in the screen test. Sending one used
  to take about 31 ms, so 32 a second at best.
- **Up to 30 frames a second** while racing (it was 20), still only when
  something on the dashboard changed.
- **The radar draws about 30% faster**, looking the same:
  - the soft pool under each car is drawn once and reused;
  - the road, rings and edge lines are drawn once for the radar's box.
- **No new 1.6 MB picture every frame.** The renderer reuses one, so memory
  stays flat while racing.
- **Every M-PRO screen.** The 4", 4.3", 5" OLED, 6.8", 3.4" and 10" models
  are recognised from what the screen reports, at their own sizes, with touch.
  Only the 5" has been tested so far.
- **Dashboard on a monitor.** Show the running dashboard full screen on any
  other display, such as an HDMI dash screen or a spare monitor. The graphics
  card scales it, it never takes the keyboard or mouse from the game, and taps
  work on it.
- **Devices page:** a *Send only what changed* switch (on by default), the
  monitor choice, and the screen's model in its status.
- `--screen-test` shows a test pattern on the wheel screen and measures it.

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
