using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace AlmostFastDash.IRacing
{
    // A self-contained reader for iRacing's shared-memory telemetry
    // (Local\IRSDKMemMapFileName) and its YAML session string.
    //
    // The memory layout and the flag, warning and spotter definitions below follow the iRacing SDK's
    // irsdk_defines.h, Copyright (c) 2013, iRacing.com Motorsport Simulations, LLC, under the BSD
    // 3-Clause license reproduced in CREDITS.md. The class names keep the shape of iRacingSDK.Net
    // (Dean Netherton, GPL-3.0), which the app used before it was standalone; the code is new.

    [Flags]
    internal enum SessionFlags : uint
    {
        checkered = 0x1,
        white = 0x2,
        green = 0x4,
        yellow = 0x8,
        red = 0x10,
        blue = 0x20,
        debris = 0x40,
        crossed = 0x80,
        yellowWaving = 0x100,
        oneLapToGreen = 0x200,
        greenHeld = 0x400,
        tenToGo = 0x800,
        fiveToGo = 0x1000,
        randomWaving = 0x2000,
        caution = 0x4000,
        cautionWaving = 0x8000,
        black = 0x10000,
        disqualify = 0x20000,
        servicible = 0x40000,
        furled = 0x80000,
        repair = 0x100000
    }

    [Flags]
    internal enum EngineWarnings
    {
        WaterTempWarning = 0x01,
        FuelPressureWarning = 0x02,
        OilPressureWarning = 0x04,
        EngineStalled = 0x08,
        PitSpeedLimiter = 0x10,
        RevLimiterActive = 0x20,
        OilTempWarning = 0x40
    }
}
