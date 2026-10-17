# Changelog

What changed in each version of Almost Fast Dash, newest first, in plain
English. Every version is on the
[releases page](https://github.com/Sota4077/almost-fast-dash/releases) with
its exe and checksum.

## 3.71 · 2026-10-17

A polish pass over the whole app.

- **The Font list shows every font in its own typeface,** and "theme" now
  says which font it is: "Theme font (Bahnschrift)". The list opens at once
  even with hundreds of fonts installed.
- **Tap views no longer pile up in the designer.** A card's other views (the
  ones a tap switches to) used to show faded on top of the view in front. Now
  only the view you're on shows; Next in Properties steps through the rest.
- **An About card in Settings:** the version, and links to the user guide,
  what's new, reporting a problem, and credits and license.
- **Settings line up:** the unit choices sit on the right like every other
  setting, each with a line saying what it changes.
- **The light designer's Effect and LEDs pickers run full width,** so long
  choices such as "Every LED on shift-light devices" aren't cut off.
- **Device setup** shows what to type for a WLED address, and "LED #n didn't
  light up" now looks like the link it is.
- **The formula reference's search box** says what you can search for.
- US English everywhere: "around", "color theme", "parentheses", and
  pressures in bar or psi.

## 3.70 · 2026-10-16

- **Every button has a name** screen readers and Windows automation can read:
  the designer's tool rail, toolbar, align and layer buttons, the sidebar and
  New dashboard. (Icon buttons used to come up blank.)
- The README shows off the designer: masks, Repeat, modules and drawing.
- The last UK spellings are gone (kilometers, labeled).
- Checked by hand, live: guides dragged out of a ruler and back, the
  eyedropper picking from the canvas, and Repeat making copies.

## 3.69 · 2026-10-16

The app comes alive.

- **Point at a dashboard's card and it plays a lap:** revs sweep, the gear
  rolls over, the traces run. It settles as soon as you move on, and only
  plays while the app is the window you're using.
- **The dashboard page's screen plays too,** in step with its lights, for the
  animated lap and a live session (it used to show a still).
- **What's new:** after an update, the Dashboards page says once what's new,
  with a link to every change. A new install sees its welcome steps instead.

## 3.68 · 2026-10-16

A second round of polish.

- **Dark title bars on every window**, the designer's dialogs included, in
  the app's own background color.
- **Dashboard cards glow** faintly in their own accent color as you point at
  them.
- **The designer's pasteboard** around the screen has a faint dot grid, as
  design tools do.
- **Tires**, not tyres, everywhere: the widget, the Library, help and the
  pit-lane page's label (saved dashboards are updated; your own words stay).
  Formulas gain `tire("LF", "Temp")`; `tyre()` still works.
- **Cleaner setting names:** "Unit size (0 auto)" and the like are plain
  names now, with what the brackets said in their help.
- **More room for Properties:** Layers starts shorter (drag the line between
  them to change it).
- Starting a new design asks for the size to build for in plain words, rather
  than warning that no wheel screen was found.
- The Preview menu explains itself, and the designer's hint line points to
  Ctrl+/ for every shortcut. The shortcuts sheet is laid out in three columns.
- The release check now fails if any template render goes missing.

## 3.67 · 2026-10-16

A first round of polish for anyone opening the designer for the first time.

- **Right-click a dashboard in the sidebar:** Set as default, Open, Edit the
  screen design, Duplicate. Setting the default from a card now updates the
  sidebar's Default mark too.
- **What only shows at times is drawn faintly in the designer:** a pit-lane
  banner, a low-fuel warning, a flag, the blind-spot bars, anything with a
  Show when that isn't true right now. Point at one to see when it shows;
  select one and Properties explains it, with a button to preview that moment.
  On the wheel and the PC nothing changes.
- **An empty screen starts with a card** of one-click starters: a gear and
  revs, shift lights, lap times.
- **Every setting explains itself** when you point at its name: all 1,084 of
  them, kept that way by a self-test.
- **Layer thumbnails:** each layer shows a small picture of itself.
- **Keyboard shortcuts** (Ctrl+/, or the keyboard button at the top).
- The Library's Quick values are now **Live data**, and My modules waits at
  the end until you've saved one. A search that finds nothing says so.
- Spelling is consistently US English (color, center, license).
- The light designer's layers turn on and off with an eye, like the screen
  designer's.

## 3.66 · 2026-10-16

Rulers and guides, and options for modules.

- **Rulers** along the canvas (Rulers below it, or Ctrl+R), marking the
  selection and the pointer. **Guides** drag out of them, snap as they go, and
  widgets snap to them. Drag one back to its ruler to remove it. Guides are
  kept with the design and follow it to a new screen size.
- **Module options:** right-click a setting's name › Make this an option…, and
  link other settings to it. With the group selected, its options are at the
  top of Properties, and every linked setting follows a change. Formulas read
  them as `option("Name")`. An option that's a size scales with the module.

## 3.65 · 2026-10-16

Repeat, zoom to the selection, and an eyedropper.

- **Repeat** (Ctrl+Shift+D, right-click, or the button in Properties): copies
  of the selection in rows and columns, or around a circle, shown on the
  canvas as you set them up.
  - Around a circle: how many, the angle they cover (360 all the way round,
    180 half), the radius and where the original sits. Copies follow the
    circle like a dial's ticks or keep upright like its numbers, with an
    extra turn each if you like.
  - **Number them as Slot 1, 2, 3…**: each copy gets its own group and the
    next number, so one light with `=RevPct * 12 >= Slot` becomes a shift
    light row, and one `=Slot - 1` text becomes a dial's numbers.
- **Zoom to selection** (Shift+2, or right-click); Shift+1 fits the screen.
- **Eyedropper:** Pick from the screen in every color picker, then click any
  color in the designer.

## 3.64 · 2026-10-16

Modules: keep your own creations, place them anywhere, and share them as a
file.

- **Save as module** (Ctrl+M, or right-click): any selection, group or widget,
  with a name, a category, what it shows and who made it. It's kept in the
  Library under **My modules** with a picture of it and the images it uses.
- **Placing a module** brings it in as one group named after it. A module
  built on a screen of another size is scaled to fit, evenly, so round gauges
  stay round.
- **Sharing:** right-click a module › Export to a file to share. **Import…**
  in My modules, or dropping an `.afomodule` file on the screen, adds one.
  Modules are widgets and formulas only, so they're safe to pass around.
- **Colors**, for several widgets, a group or a placed module: every color
  they use, once each. Change one and it changes everywhere it's used.
- **Scale**, for the same: resizes everything about its middle, text, lines
  and glows included.
- Blocks saved by earlier versions are listed in My modules, and deleting a
  module sends it to the Recycle Bin.

## 3.63 · 2026-10-16

Masks: clip one layer to another, fade widgets out, and blend them with what's
beneath.

- **Clipping masks:** Alt-click a layer (or Ctrl+Alt+G, or right-click › Clip
  to layer below) and it shows only where the layer beneath it is drawn, as in
  an image editor. A striped rectangle clipped to a rectangle outline makes a
  kerb; a gradient clipped to a gear digit fills the digit. Several layers can
  clip to the same layer, and they hide and show with it.
- **Fade** in Style: fades a widget out towards an edge, both sides, top and
  bottom, every edge or from the middle, over the length you set.
- **Blend** in Style: Multiply, Screen, Add and Overlay mix a widget with
  what's beneath it, for tints, glows and light spills.
- **Radial gradients** on ellipses, polygons and stars now reach the shape's
  own edge, so a glow fades out softly instead of stopping at a rim.
- Every existing dashboard draws exactly as before: all 58 template renders
  are identical pixel for pixel.

## 3.62 · 2026-10-16

Drawing tools: the Pen, polygons and stars, gradient and pattern fills, and
patterns for any widget.

- **Pen** (P): click for corners and drag for curves. Click the first point to
  close the shape; double-click, Enter or Esc to leave it open.
- **Edit points:** double-click a path (or Enter) to drag its points and
  handles. Alt-click switches a point between corner and curve, and Delete
  removes one.
- **Convert to path:** turns a rectangle, ellipse, polygon or star into a path
  to reshape.
- **Polygon** on the rail (Shift+U steps to it), and **Star** among the
  shapes: any number of sides, and a star's inner size.
- **Fills** for shapes and paths: a color, a linear or radial gradient, or a
  pattern in two colors. The patterns are stripes, checks, dots, carbon fibre
  (a proper 2×2 twill), honeycomb, grid and brushed metal, at any size and
  angle.
- **Lines:** a color or a gradient, solid, dashed, dotted or dash-dot, with
  round, flat or square ends and round, sharp or bevelled corners.
- **Recolor › Pattern** in every widget's Style paints it in a pattern. A
  rectangle outline in red and white stripes makes a kerb; digits in brushed
  metal look machined.
- **A compact Library:** widgets are rows of icon and name instead of big
  tiles, so the whole list scrolls about half as far.
- Every existing dashboard draws exactly as before: all 58 template renders
  are identical pixel for pixel.

## 3.61 · 2026-10-15

The designer's new workspace, laid out like an image editor.

- **Tools on the left rail:**
  - **Move** (V);
  - **Rectangle** (U), **Ellipse** and **Line** (Shift+U steps through them);
  - **Text** (T);
  - **Hand** (H, or hold Space);
  - **Zoom** (Z, Alt+click to zoom out).

  Draw a shape or a text straight onto the screen: it becomes a widget,
  selected and ready to style, and the rail goes back to Move. Shift keeps a
  shape square, round, level or at 45°, Alt draws from the middle, and a click
  makes one of the usual size. Each drawing is its own step to undo.
- **Layers always in view.** Layers sit on the right below Properties instead
  of on a tab, with a count, and buttons to bring forward, send back, group,
  duplicate and delete. The line between them drags to share the room.
- **Properties and Theme** share the top of the right side.
- **The Library** (what was Add) sits beside the tools, with everything it
  had. Hide it for more canvas, and the grid button on the rail brings it
  back.
- Nothing was taken away: every widget, block, template, shortcut and setting
  is where you'd look for it.

## 3.60 · 2026-10-15

A formula reference to keep open while you build.

- **Formula reference.** **Formulas** in the designer's toolbar (or F1)
  opens a window to keep beside the designer, or on another display, while
  you build. It covers everything ƒx can use:
  - the language, step by step, with its order of operations;
  - every theme color, with its swatch;
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
  - a widget repainted only because a neighbor changed is copied from its
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
  are recognized from what the screen reports, at their own sizes, with touch.
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
  the live delta, plus traffic in class colors.

## 3.47 · 2026-10-13

- **Flags** fill the screen as they come out, then step aside to a colored
  frame round the edge of the screen. The screen stays steady while the lights
  flash, and the checkered flag shows your result.

## 3.46 · 2026-10-13

- Neon rebuilt from the ground up. Halo and Oval get rounded corners.

## 3.45 · 2026-10-13

- An instrument finish for the flagship dashboards, and choices in the app take
  the accent color.

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
- Shared Style tools for every widget: recolor, outline, drop shadow and outer
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
- LED color themes, and deeper reds and truer greens.

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
