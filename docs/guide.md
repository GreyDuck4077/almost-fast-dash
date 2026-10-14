# User guide

How to set up Almost Fast Dash and get the most out of it. If you haven't
installed it yet, start with [Installing](../README.md#installing).

- [Dashboards](#dashboards)
- [Flags](#flags)
- [Devices](#devices)
- [Screens](#screens)
- [The designer](#the-designer)
- [Light profiles](#light-profiles)
- [Settings](#settings)
- [Troubleshooting](#troubleshooting)

## Dashboards

The **Dashboards** page shows every dashboard you have. On a new install, a
welcome card at the top lists the first steps. It goes once you start the
dashboard, or when you click **Got it**.

On each dashboard's card:

- **Customize** opens the dashboard's page, with a live preview of the screen
  and your LEDs, its light profile and its track types.
- **Design** opens the [designer](#the-designer).
- **Set as default** makes it the dashboard used whenever no other one matches.

**Track types.** Under *Use automatically for* on a dashboard's page, choose
the track types it's for (Road, Oval, Dirt Road, Dirt Oval). When a session
starts, the app switches to the matching dashboard. Turn off **Settings ›
Switch dashboards by track type** to always show the default.

**Pages.** A dashboard can have several pages, such as timing, standings or
engine. In the designer, set a **Next page** button (and optionally a
**Previous page** button) on your wheel to step through them while you drive.

**Touch.** On a touch wheel screen, tapping a panel switches what it shows.

**Live preview.** A dashboard's page can play any moment of a race: an
animated lap, live iRacing, the shift point, cars alongside, the pit limiter or
each flag. **Show on my wheel** puts the preview on the real screen and LEDs
while the dashboard is stopped.

**Start and stop** the dashboard with the button in the header. It keeps
running in the background when you close the window. **Settings › Start with
Windows** starts it when you sign in.

## Flags

A flag fills the screen as it comes out (the screen stays steady while the
lights flash), then hands the dashboard back. While a flag stays out (a
caution, the final lap, black, repairs or blue), a glow in its colour runs
round the edge of the screen with a tab that names it. Red and checkered keep
the whole screen. The checkered flag shows your result: position, class, best
lap and places gained.

## Devices

**Devices › Add device** walks through four steps:

1. **Find.** The app asks each USB serial port whether it's an LED board. It
   only does this when you ask, and leaves alone ports already set up and
   Bluetooth ports. For WLED, it looks the controller up by its address.
2. **Name** the device.
3. **Shape.** Choose what the device looks like and how many LEDs each part
   has:
   - *Screen with LEDs*: LEDs down the left side, across the top, down the
     right side and along the bottom.
   - *Light bars*: how many bars, how many LEDs on each, and whether they're
     joined end to end into one long bar or work separately.
   - *LED strip*: its length.

   Each part gets a role: Shift lights, Left side, Right side, Light bar or
   Other. A light layer aimed at a role lights every part with that role, on
   every device.
4. **Map.** One LED at a time lights up on the device, and you click where it
   is on a drawing of the shape. After two neighbours the app follows the
   chain on its own, so most devices take two clicks. Wiring that jumps is
   asked about LED by LED. Then you click a few LEDs to check the right ones
   light, or place every LED by hand if something's off. If an LED doesn't
   light, the shape has more LEDs than the device, and the app takes you back
   to fix the count.

From then on, the device is drawn in its real shape everywhere. Fills run the
way each part runs: sides bottom to top, rows and bars left to right.

Each device's row shows its live state: connected (and how), connecting, not
found, in use by another program, or keeps disconnecting. **Settings** on a
row lets you change its name, power and on/off, set its LEDs up again, test
it or remove it.

**Power.** A device powered from the PC's USB port is held to about 250 mA.
Bright frames are dimmed to fit, so a whole bar lit white can't overload the
port. An overloaded port drops the device off USB with its LEDs stuck on.
Choose *a powered hub or its own power supply* to remove the limit.

**Fast updates** (several colour changes per message) are used automatically
when a board accepts them. Messages are kept within what these boards can take
(26 bytes), and each one waits for the board's acknowledgement. A device that
keeps disconnecting is retried less and less often. Devices are found again by
their USB ID when Windows renumbers COM ports.

### Supported devices

- **Arduino LED boards** on the common sim-racing LED serial protocol, such as
  the LEDs on many wheel screens and light bars.
- **Adalight**: an Arduino driving a WS2812/NeoPixel strip with the open
  Adalight protocol. `Firmware\Adalight\Adalight.ino` is a ready-to-flash
  sketch.
- **WLED** controllers on your network, over WLED's real-time UDP protocol.

See [Compatibility](../README.md#compatibility) for what has been tested on
real hardware.

## Screens

The running dashboard can show on a wheel screen, a monitor, or both at once.
Both are set up at the bottom of the **Devices** page.

### The wheel screen

The app drives VoCore's M-PRO USB screens directly. Each one reports its model
(4", 4.3", 5", 5" OLED, 6.8", 3.4" or 10"), so the dashboard knows its size.
The **Wheel screen** card shows:

- **Status:** showing the dashboard, plugged in, or not found.
- **Screen brightness:** the backlight.
- **Performance:** frames a second, and the time to draw and send each one,
  measured on the wheel.
- **Send only what changed:** each frame sends just the parts of the screen
  that moved, often a tenth of the screen or less, so the screen keeps up at a
  much higher frame rate. It's on unless you turn it off. If parts of your
  screen ever stop updating, turn it off and please send a
  [hardware report](https://github.com/Sota4077/almost-fast-dash/issues/new?template=hardware_report.yml).

The dashboard draws the next frame while the last one is still being sent, at
up to 30 frames a second, and only when something on it changed.

To check a screen, stop the dashboard and run
`AlmostFastDash.exe --screen-test report.txt`. It shows a title, numbered
squares in each corner and the middle, then a sliding bar, sent as whole
frames and then as only what changed. It writes the screen's model and both
frame rates to the report.

### Dashboard on a monitor

**Dashboard on a monitor › Display** shows the running dashboard full screen on
another display, such as an HDMI dash screen or a spare monitor. Any display
except your main one can be chosen.

- It's scaled to fit by the graphics card, without stretching. For the
  sharpest picture, design the dashboard at the display's resolution.
- It never becomes the active window, so the game keeps the keyboard and
  mouse.
- Clicks and touches on it work like taps on a touch wheel screen.
- If the display is unplugged, the dashboard comes back on it when it's
  plugged in again.

## The designer

Dashboards are data, not code: a theme plus one or more screens of widgets. Open
**Design** on any dashboard to change it.

![The designer](images/app-designer.png)

- **Canvas.** The screen at the dashboard's own resolution. Drag, resize and
  multi-select. Widgets snap to edges and centres (hold Alt to stop snapping),
  and Ctrl+wheel zooms.
- **Add.** Drag widgets and ready-made blocks onto the screen:
  - **Widgets**: text and values, the gear, bars and arc gauges (needles,
    ticks, numbers), shift lights, the leaderboard, the relative, tyres, a
    self-tracing track map, a radar of the cars around you, a traffic strip in
    class colours, live graphs of any value, flags, blind-spot bars, panels,
    shapes, gradients and images.
  - **Quick values**: one-click items for the engine, lap, sectors, tyres,
    fuel, race, leaderboard, notices and the PC.
  - **Blocks**: ready-made cards, clusters and strips. Save your own selection
    as a block to reuse it.
- **Inspector.** Every property of the selected widget. **ƒx** turns any
  property into a live formula. **Show when** shows a widget only while a
  condition is true.
- **Shared tools** on every widget where they fit:
  - shape: bend, taper, bulge, slant;
  - fill direction, flip and rotate;
  - style: recolour with a colour, gradient or texture, outline, drop shadow,
    outer glow.
- **Several widgets** selected show the settings they share, edited together.
  Ctrl+Shift+C and Ctrl+Shift+V copy and paste a widget's look.
- **Groups** (Ctrl+G) have a number that formulas read as `Slot`. Build one
  driver row with `standing(Slot, "Name")`, then press Ctrl+D to copy it for the
  next place.
- **Screens.** A screen with a *Show when* condition (such as `OnPitRoad`) takes
  over while it's true. The others are pages that a wheel button steps through.
- **Theme.** Presets, the accent colour, the font, the background and every
  theme colour.
- **Display resolution** moves a dashboard to another screen size.
- **Undo and redo**, copy and paste, align and distribute.
- **Export and Import** a whole dashboard, with its images and lights, as one
  `.afodash` file.

**Formulas** read like `FuelLapsLeft < 2 ? $bad : $text`. They support:
- arithmetic and comparisons;
- `and`, `or`, `not`, and `cond ? a : b`;
- colours as `#RRGGBB` or as `$theme` names;
- about 80 data fields;
- functions such as `format`, `laptime`, `fuel`, `steps`, `mix` and `blink`.

**The track map** draws itself. Drive one clean lap at a track and the app
traces it from your speed and heading, then keeps it in `TrackMaps` for next
time.

**Readable text.** Every template keeps text at 16 px or larger on an 854×480
screen (14 px in dense tables), with strong contrast. Keep to the same sizes in
your own designs and they'll be readable at a glance while you drive.

## Light profiles

Open **Light designer** from a dashboard's page. A profile is a stack of
layers. Where layers overlap, the one higher in the list wins.

![The light designer](images/app-lights.png)

Each layer has:

- an **effect**: rev lights (from the car's own shift-light RPMs), meter,
  centre meter, solid, blink, breathe, sweep, flag colours, blind spot,
  gradient or blank;
- the **LEDs** it lights: a role on every device (shift lights, sides, light
  bar, the light bar split down the middle…) or one device or part;
- **Show when**: a condition such as `OnPitRoad && PitLimiter`;
- a **blend** (over, or replace the whole target) and a brightness.

The **colour theme** sets what `good`, `warn` and `bad` mean on the LEDs: the
low, middle and high of rev lights and meters, the shift flash and blind-spot
warnings. Pick one of 24 themes or choose your own three colours.

Out of the car or in a replay, the LEDs go dark. The checkered flag shows
once, for ten seconds.

## Settings

- **Units** for speed, fuel and temperature. A new install picks them to match
  your Windows region.
- **Fuel safety margin**: none, ½, 1 or 2 laps, added to *fuel to add*.
- The page the app opens on.
- **Minimize to the system tray**.
- **Switch dashboards by track type**.
- **Start with Windows**.
- **Check for new versions**: when the app opens, it asks GitHub whether a
  newer release is out and shows a link to it. This is off until you turn it
  on.
- **Diagnostic logs** (on by default): small logs, in `Logs` next to the exe,
  of blind-spot decisions and which light layer lit which LEDs. Use them to
  look into an odd light after a race. No names or account details are
  recorded.

The wheel screen and the monitor have their own settings on the **Devices**
page (see [Screens](#screens)).

### Your settings are safe

Settings are saved whole or not at all, and the previous copy is kept as
`settings.json.bak`. A dated copy goes into `SettingsBackups` every ten minutes
of changes, and the last twelve are kept. If the settings file is ever
damaged, the newest backup that reads replaces it, and the damaged file is
kept beside it.

## Troubleshooting

**The wheel screen or an LED device isn't found.** Only one program can drive
a device at a time. Close other dashboard software (such as SimHub), then
check the Devices page, which says when another program has a device's port.

**An LED device keeps dropping off USB when lots of LEDs light.** The USB port
can't supply enough power. Leave the device's power setting on the PC's USB
port (the app then limits brightness), or plug the device into a powered hub.

**The wheel screen shows nothing.** Check **Devices › Wheel screen › Status**.
*Not found* means Windows can't see it: check the USB cable, and close other
dashboard software. Only VoCore-family M-PRO screens can be driven for now
(see [Compatibility](../README.md#compatibility)).

**Parts of the wheel screen stop updating.** Turn off **Devices › Wheel
screen › Send only what changed**, so every frame is sent whole. Then please
send a [hardware report](https://github.com/Sota4077/almost-fast-dash/issues/new?template=hardware_report.yml)
with your screen's model (from `--screen-test`).

**A light came on and you don't know why.** Check **Settings › Diagnostic
logs** is on (it is unless you turned it off). After the race, the light log
in `Logs` lists which layer lit which LEDs, and when.

**Windows won't run the app.** See
["Windows protected your PC"](../README.md#windows-protected-your-pc).

Still stuck? [Open an issue](https://github.com/Sota4077/almost-fast-dash/issues)
and describe what you see.
