# Credits and notices

Almost Fast Dash is free software under the GNU General Public License v3.0 (see
[LICENSE](LICENSE)). It includes no third-party code or libraries: it is built
only on Windows and the .NET Framework that ships with it. It does talk to other
people's software and hardware, and it was built on their published work. This
page credits them and explains exactly what was used.

## iRacing SDK

The telemetry reader (`IRacingSdk.cs`) is a new C# implementation of the
iRacing SDK's shared-memory telemetry format (`Local\IRSDKMemMapFileName`) and
its YAML session string. Its flag, engine-warning and spotter definitions (names
and values, such as `servicible` and `oneLapToGreen`) and the memory layout
follow the SDK's `irsdk_defines.h`. That header's notice is reproduced here as
its licence requires:

```
Copyright (c) 2013, iRacing.com Motorsport Simulations, LLC.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:
    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.
    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.
    * Neither the name of iRacing.com Motorsport Simulations nor the
      names of its contributors may be used to endorse or promote products
      derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## iRacingSDK.Net

Before it became a standalone app, this project (then called DV5Direct) used
[iRacingSDK.Net](https://github.com/vipoo/iRacingSDK.Net) by Dean Netherton
(GPL-3.0). The built-in reader that replaced it keeps that library's shape, with
names such as `iRacing.NewData`, `DataSample`, `Telemetry` and `SessionData`, so
the rest of the app didn't need rewriting. Its code is newly written; nothing
from iRacingSDK.Net is included.

## VoCore M-PRO screens

The wheel-screen driver (`WheelScreen.cs`) speaks the USB protocol of VoCore's
M-PRO screens (USB C872:1004) through Windows' built-in WinUSB driver.

**How the protocol was worked out.** When the app first drove the screen
directly, the commands were learned by inspecting how SimHub's installed screen
driver talks to the screen. Since then, every command the driver sends has been
checked against VoCore's own open-source M-PRO driver,
[`mpro.c` in Vonger/mpro_drm](https://github.com/Vonger/mpro_drm)
(GPL-2.0-or-later), and VoCore's published notes at [vonger.cn](https://vonger.cn)
and [vocore.io](https://vocore.io/screen.html). Those sources cover:

- the vendor requests (0xB0 commands; 0xB5, 0xB6 and 0xB7 queries);
- the model and panel queries;
- wake (`00 29`), frame write (`00 2C`) and backlight (`00 51`);
- the 320 bytes of padding that most 5-inch panels expect after a frame.

No code from SimHub or from VoCore's driver is included.

## LED protocols

- **Arduino LED boards** speak the serial protocol of SimHub's Arduino firmware,
  which many sim-racing LED boards run: packets of `01 01 FF`, a length, the
  payload and a CRC-8. No SimHub code or files are included.
- **Adalight** is the LED-strip protocol by
  [Adafruit](https://github.com/adafruit/Adalight) (GPL-3.0). Its frames are
  "Ada", the LED count less one, a checksum and then the colours.
  `Firmware/Adalight/Adalight.ino` is a sketch written for this project that
  receives it.
- **FastLED** ([MIT](https://github.com/FastLED/FastLED)) drives the strip in
  that sketch. You install it separately in the Arduino IDE; it isn't included
  here.
- **WLED** controllers are driven through WLED's documented real-time UDP
  protocol (DRGB and DNRGB) and its `/json/info` page
  ([WLED](https://github.com/wled/WLED), EUPL-1.2). Nothing from WLED is
  included.

## Design

- The "Liquid" glass panels are inspired by Apple's Liquid Glass design
  language.
- The dashboards are inspired by modern production-car instrument clusters and
  driver-assist displays.
- Every dashboard, flag, car drawing and the app icon are drawn by this
  project's own code. No artwork is copied.
- Bahnschrift and Segoe UI are Windows fonts. The app uses them from Windows and
  doesn't include them.

## Trademarks

iRacing is a registered trademark of iRacing.com Motorsport Simulations, LLC.
SimHub, VoCore, SSG, Adafruit, Adalight, FastLED, WLED and Apple are trademarks
of their respective owners. They are named only to say what this app works with
or was inspired by. Almost Fast Dash is an independent project, not affiliated
with or endorsed by any of them.

## How it was made

Much of the code was written with Claude (Anthropic), an AI coding assistant,
under the project author's direction. From version 3.49 on, commits made that
way carry a `Co-Authored-By` line.
