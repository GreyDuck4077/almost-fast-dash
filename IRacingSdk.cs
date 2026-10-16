using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace AlmostFastOverlays
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

    internal sealed class DataSample
    {
        public bool IsConnected { get; set; }
        public Telemetry Telemetry { get; set; }
    }

    internal static class iRacing
    {
        private const string MapName = @"Local\IRSDKMemMapFileName";
        private const string DataEventName = @"Local\IRSDKDataValidEvent";
        private static readonly object Gate = new object();
        private static Thread _thread;
        private static volatile bool _stop;
        // How many sources are listening: the reader stops only when the last one does (the settings
        // window and the light designer can both show live data).
        private static int _listeners;

        public static event Action<DataSample> NewData;

        public static void StartListening()
        {
            lock (Gate)
            {
                _listeners++;
                if (_thread != null) return;
                _stop = false;
                _thread = new Thread(Run) { IsBackground = true, Name = "iRacing telemetry" };
                _thread.Start();
            }
        }

        public static void StopListening()
        {
            Thread thread;
            lock (Gate)
            {
                if (_listeners > 0) _listeners--;
                if (_listeners > 0) return;
                thread = _thread;
                _thread = null;
                _stop = true;
            }
            thread?.Join(1000);
        }

        private static void Run()
        {
            var disconnected = new DataSample();
            while (!_stop)
            {
                MemoryMappedFile map;
                try { map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read); }
                catch
                {
                    Publish(disconnected);
                    Sleep(1000);
                    continue;
                }

                using (map)
                using (MemoryMappedViewAccessor view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                using (EventWaitHandle dataReady = OpenDataEvent())
                {
                    var session = new SessionCache();
                    int lastTick = -1;
                    while (!_stop)
                    {
                        if (dataReady != null) dataReady.WaitOne(100);
                        else Sleep(16);

                        // Status bit 1 is "connected"; iRacing clears it when the sim closes.
                        if ((view.ReadInt32(4) & 1) == 0)
                        {
                            Publish(disconnected);
                            Sleep(500);
                            if (!StillOpen()) break;
                            continue;
                        }

                        Telemetry telemetry = Telemetry.Read(view, session, ref lastTick);
                        if (telemetry != null) Publish(new DataSample { IsConnected = true, Telemetry = telemetry });
                    }
                }
            }
        }

        /// <summary>Hands a sample to every listener; one that fails is logged, never allowed to stop the reader.</summary>
        private static void Publish(DataSample sample)
        {
            try { NewData?.Invoke(sample); }
            catch (Exception exception) { Console.Error.WriteLine("Telemetry: " + exception); }
        }

        private static bool StillOpen()
        {
            try { using (MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read)) return true; }
            catch { return false; }
        }

        private static EventWaitHandle OpenDataEvent()
        {
            try { return EventWaitHandle.OpenExisting(DataEventName); }
            catch { return null; }
        }

        private static void Sleep(int milliseconds)
        {
            for (int waited = 0; waited < milliseconds && !_stop; waited += 50) Thread.Sleep(50);
        }
    }

    /// <summary>
    /// Re-reads the variable list and session YAML only when iRacing reports a
    /// new session-info version (a new session, car or driver change).
    /// </summary>
    internal sealed class SessionCache
    {
        private int _update = -1;
        private int _varOffset = -1;
        private int _varCount = -1;
        public SessionData Data { get; private set; }
        public Dictionary<string, VarHeader> Vars { get; private set; }

        public void Refresh(MemoryMappedViewAccessor view, int update, int offset, int length, int varOffset, int varCount)
        {
            if (Vars == null || varOffset != _varOffset || varCount != _varCount || update != _update)
            {
                Vars = VarHeader.ReadAll(view, varOffset, varCount);
                _varOffset = varOffset;
                _varCount = varCount;
            }
            if (update == _update && Data != null) return;
            var bytes = new byte[length];
            view.ReadArray(offset, bytes, 0, length);
            int end = Array.IndexOf(bytes, (byte)0);
            string yaml = Encoding.GetEncoding(1252).GetString(bytes, 0, end < 0 ? length : end);
            Data = new SessionData(Yaml.Parse(yaml));
            _update = update;
        }
    }

    internal sealed class Telemetry
    {
        private const int HeaderVarBuffers = 48;
        private const int VarHeaderSize = 144;
        private readonly Dictionary<string, VarHeader> _vars;
        private readonly byte[] _buffer;

        private Telemetry(Dictionary<string, VarHeader> vars, byte[] buffer, SessionData session)
        {
            _vars = vars;
            _buffer = buffer;
            SessionData = session;
        }

        public SessionData SessionData { get; }

        public object this[string name]
        {
            get
            {
                if (!_vars.TryGetValue(name, out VarHeader v)) return null;
                if (v.Count == 1) return ReadValue(v, 0);
                var values = Array.CreateInstance(ElementType(v.Type), v.Count);
                for (int i = 0; i < v.Count; i++) values.SetValue(ReadValue(v, i), i);
                return values;
            }
        }

        public SessionFlags SessionFlags => (SessionFlags)(uint)Int("SessionFlags");
        public int EnterExitReset => Int("EnterExitReset");
        public int Gear => Int("Gear");
        public float Speed => Float("Speed");
        public float RPM => Float("RPM");
        public float ShiftIndicatorPct => Float("ShiftIndicatorPct");
        public double SessionTimeRemain => Double("SessionTimeRemain");
        public int Lap => Int("Lap");
        public int LapCompleted => Int("LapCompleted");
        public int PlayerCarPosition => Int("PlayerCarPosition");
        public float FuelLevel => Float("FuelLevel");
        public float LapCurrentLapTime => Float("LapCurrentLapTime");
        public float LapLastLapTime => Float("LapLastLapTime");
        public float LapBestLapTime => Float("LapBestLapTime");
        public float LapDeltaToBestLap => Float("LapDeltaToBestLap");
        public bool LapDeltaToBestLap_OK => Bool("LapDeltaToBestLap_OK");
        public float LapDeltaToSessionBestLap => Float("LapDeltaToSessionBestLap");
        public bool LapDeltaToSessionBestLap_OK => Bool("LapDeltaToSessionBestLap_OK");
        public bool OnPitRoad => Bool("OnPitRoad");
        public int SessionUniqueID => Int("SessionUniqueID");
        public float[] CarIdxLapDistPct => ReadArray<float>("CarIdxLapDistPct", 4, 4);
        public int[] CarIdxLap => ReadArray<int>("CarIdxLap", 2, 4);
        public int[] CarIdxPosition => ReadArray<int>("CarIdxPosition", 2, 4);
        public bool[] CarIdxOnPitRoad => ReadBoolArray("CarIdxOnPitRoad");

        /// <summary>The session currently running (practice, qualifying, race...).</summary>
        public SessionEntry Session => SessionData?.Session(Int("SessionNum"));

        public CarDetails[] CarDetails => SessionData?.Cars;

        internal static Telemetry Read(MemoryMappedViewAccessor view, SessionCache session, ref int lastTick)
        {
            int numVars = view.ReadInt32(24);
            int varHeaderOffset = view.ReadInt32(28);
            int numBuf = Math.Max(1, Math.Min(4, view.ReadInt32(32)));
            int bufLen = view.ReadInt32(36);

            int latest = 0;
            for (int i = 1; i < numBuf; i++)
                if (view.ReadInt32(HeaderVarBuffers + i * 16) > view.ReadInt32(HeaderVarBuffers + latest * 16)) latest = i;
            int tick = view.ReadInt32(HeaderVarBuffers + latest * 16);
            if (tick == lastTick) return null;

            var buffer = new byte[bufLen];
            for (int attempt = 0; attempt < 2; attempt++)
            {
                view.ReadArray(view.ReadInt32(HeaderVarBuffers + latest * 16 + 4), buffer, 0, bufLen);
                // iRacing may overwrite the buffer while it is copied; keep the copy only if the tick held.
                if (view.ReadInt32(HeaderVarBuffers + latest * 16) == tick) break;
            }
            lastTick = tick;

            session.Refresh(view, view.ReadInt32(12), view.ReadInt32(20), view.ReadInt32(16), varHeaderOffset, numVars);
            return new Telemetry(session.Vars, buffer, session.Data);
        }

        /// <summary>
        /// Every sample of an iRacing telemetry recording (.ibt), as live telemetry, so a
        /// race can be replayed through the dashboard and lights after the fact.
        /// </summary>
        internal static IEnumerable<Telemetry> ReadRecording(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            int sessionLength = BitConverter.ToInt32(file, 16), sessionOffset = BitConverter.ToInt32(file, 20);
            int numVars = BitConverter.ToInt32(file, 24), varHeaderOffset = BitConverter.ToInt32(file, 28);
            int bufLen = BitConverter.ToInt32(file, 36), dataOffset = BitConverter.ToInt32(file, HeaderVarBuffers + 4);
            var vars = new Dictionary<string, VarHeader>(numVars, StringComparer.Ordinal);
            for (int i = 0; i < numVars; i++)
            {
                int at = varHeaderOffset + i * VarHeaderSize;
                int length = Array.IndexOf(file, (byte)0, at + 16, 32) - (at + 16);
                vars[Encoding.ASCII.GetString(file, at + 16, length < 0 ? 32 : length)] = new VarHeader
                {
                    Type = BitConverter.ToInt32(file, at),
                    Offset = BitConverter.ToInt32(file, at + 4),
                    Count = Math.Max(1, BitConverter.ToInt32(file, at + 8))
                };
            }
            int end = Array.IndexOf(file, (byte)0, sessionOffset, sessionLength);
            var session = new SessionData(Yaml.Parse(Encoding.GetEncoding(1252).GetString(file, sessionOffset, (end < 0 ? sessionOffset + sessionLength : end) - sessionOffset)));
            for (long at = dataOffset; at + bufLen <= file.Length; at += bufLen)
            {
                var buffer = new byte[bufLen];
                Buffer.BlockCopy(file, (int)at, buffer, 0, bufLen);
                yield return new Telemetry(vars, buffer, session);
            }
        }

        private int Int(string name) => this[name] is object value && !(value is Array) ? Convert.ToInt32(value) : 0;
        private float Float(string name) => this[name] is object value && !(value is Array) ? Convert.ToSingle(value) : 0f;
        private double Double(string name) => this[name] is object value && !(value is Array) ? Convert.ToDouble(value) : 0d;
        private bool Bool(string name) => this[name] is bool value && value;

        private object ReadValue(VarHeader v, int index)
        {
            switch (v.Type)
            {
                case 0: return (char)_buffer[v.Offset + index];
                case 1: return _buffer[v.Offset + index] != 0;
                case 2:
                case 3: return BitConverter.ToInt32(_buffer, v.Offset + index * 4);
                case 4: return BitConverter.ToSingle(_buffer, v.Offset + index * 4);
                default: return BitConverter.ToDouble(_buffer, v.Offset + index * 8);
            }
        }

        private static Type ElementType(int type)
        {
            switch (type)
            {
                case 0: return typeof(char);
                case 1: return typeof(bool);
                case 2:
                case 3: return typeof(int);
                case 4: return typeof(float);
                default: return typeof(double);
            }
        }

        private T[] ReadArray<T>(string name, int type, int elementSize) where T : struct
        {
            if (!_vars.TryGetValue(name, out VarHeader v) || v.Type != type) return null;
            var values = new T[v.Count];
            Buffer.BlockCopy(_buffer, v.Offset, values, 0, v.Count * elementSize);
            return values;
        }

        private bool[] ReadBoolArray(string name)
        {
            if (!_vars.TryGetValue(name, out VarHeader v) || v.Type != 1) return null;
            var values = new bool[v.Count];
            for (int i = 0; i < v.Count; i++) values[i] = _buffer[v.Offset + i] != 0;
            return values;
        }
    }

    internal struct VarHeader
    {
        private const int Size = 144;
        public int Type;
        public int Offset;
        public int Count;

        public static Dictionary<string, VarHeader> ReadAll(MemoryMappedViewAccessor view, int offset, int count)
        {
            var vars = new Dictionary<string, VarHeader>(count, StringComparer.Ordinal);
            var name = new byte[32];
            for (int i = 0; i < count; i++)
            {
                long at = offset + (long)i * Size;
                view.ReadArray(at + 16, name, 0, 32);
                int length = Array.IndexOf(name, (byte)0);
                vars[Encoding.ASCII.GetString(name, 0, length < 0 ? 32 : length)] = new VarHeader
                {
                    Type = view.ReadInt32(at),
                    Offset = view.ReadInt32(at + 4),
                    Count = Math.Max(1, view.ReadInt32(at + 8))
                };
            }
            return vars;
        }
    }

    internal sealed class SessionData
    {
        private readonly Dictionary<string, object> _root;

        public SessionData(Dictionary<string, object> root)
        {
            _root = root ?? new Dictionary<string, object>();
            WeekendInfo = new WeekendInfo(Yaml.Map(_root, "WeekendInfo"));
            var cars = new List<CarDetails>();
            foreach (Dictionary<string, object> driver in Yaml.Maps(Yaml.Map(_root, "DriverInfo"), "Drivers"))
                cars.Add(new CarDetails(new DriverEntry(driver)));
            Cars = cars.ToArray();

            // The player car's own shift-light points, as iRacing's in-car lights use them.
            Dictionary<string, object> driverInfo = Yaml.Map(_root, "DriverInfo");
            ShiftFirstRpm = Yaml.Number(Yaml.Text(driverInfo, "DriverCarSLFirstRPM"));
            ShiftRpm = Yaml.Number(Yaml.Text(driverInfo, "DriverCarSLShiftRPM"));
            ShiftLastRpm = Yaml.Number(Yaml.Text(driverInfo, "DriverCarSLLastRPM"));
            ShiftBlinkRpm = Yaml.Number(Yaml.Text(driverInfo, "DriverCarSLBlinkRPM"));
            RedlineRpm = Yaml.Number(Yaml.Text(driverInfo, "DriverCarRedLine"));

            // The usable tank: the car's capacity times the series' fuel limit (1.0 when there is none).
            double maxFuel = Yaml.Number(Yaml.Text(driverInfo, "DriverCarFuelMaxLtr"));
            double maxPct = Yaml.Number(Yaml.Text(driverInfo, "DriverCarMaxFuelPct"));
            FuelCapacityLiters = maxFuel * (maxPct > 0 && maxPct <= 1 ? maxPct : 1);

            // Where each timing sector starts, as a fraction of the lap.
            var starts = new List<double>();
            foreach (Dictionary<string, object> sector in Yaml.Maps(Yaml.Map(_root, "SplitTimeInfo"), "Sectors"))
                starts.Add(Yaml.Number(Yaml.Text(sector, "SectorStartPct")));
            starts.Sort();
            SectorStarts = starts.ToArray();
        }

        public double FuelCapacityLiters { get; }
        /// <summary>Lap fractions where the track's timing sectors start; the first is 0.</summary>
        public double[] SectorStarts { get; }

        public WeekendInfo WeekendInfo { get; }
        public CarDetails[] Cars { get; }
        public double ShiftFirstRpm { get; }
        public double ShiftRpm { get; }
        public double ShiftLastRpm { get; }
        public double ShiftBlinkRpm { get; }
        public double RedlineRpm { get; }

        public SessionEntry Session(int sessionNum)
        {
            foreach (Dictionary<string, object> session in Yaml.Maps(Yaml.Map(_root, "SessionInfo"), "Sessions"))
                if (Yaml.Int(session, "SessionNum") == sessionNum)
                    return new SessionEntry(session);
            return null;
        }
    }

    internal sealed class WeekendInfo
    {
        public WeekendInfo(Dictionary<string, object> map)
        {
            TrackLength = Yaml.Text(map, "TrackLength");
            TrackPitSpeedLimit = Yaml.Text(map, "TrackPitSpeedLimit");
            Category = Yaml.Text(map, "Category");
            TrackType = Yaml.Text(map, "TrackType");
            TrackId = Yaml.Int(map, "TrackID");
            TrackName = Yaml.Text(map, "TrackDisplayName") ?? Yaml.Text(map, "TrackName") ?? "";
            TrackConfig = Yaml.Text(map, "TrackConfigName") ?? "";
            WeekendOptions = new WeekendOptions { NumStarters = Yaml.Int(Yaml.Map(map, "WeekendOptions"), "NumStarters") };
        }

        public string TrackLength { get; }
        public string TrackPitSpeedLimit { get; }
        public string Category { get; }
        public string TrackType { get; }
        /// <summary>iRacing's id for the track layout, its name and its configuration (such as "Oval" or "International").</summary>
        public int TrackId { get; }
        public string TrackName { get; }
        public string TrackConfig { get; }
        public WeekendOptions WeekendOptions { get; }
    }

    internal sealed class WeekendOptions
    {
        public int NumStarters { get; set; }
    }

    internal sealed class SessionEntry
    {
        private readonly string _laps;
        private readonly string _time;

        public SessionEntry(Dictionary<string, object> map)
        {
            _laps = Yaml.Text(map, "SessionLaps") ?? "unlimited";
            _time = Yaml.Text(map, "SessionTime") ?? "unlimited";
            SessionType = Yaml.Text(map, "SessionType") ?? "";
            IsRace = string.Equals(SessionType, "Race", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"Practice", "Lone Qualify", "Open Qualify", "Race", "Warmup"...</summary>
        public string SessionType { get; }
        public bool IsRace { get; }
        public bool IsLimitedSessionLaps => !_laps.StartsWith("unlimited", StringComparison.OrdinalIgnoreCase);
        public bool IsLimitedTime => !_time.StartsWith("unlimited", StringComparison.OrdinalIgnoreCase);
        public int _SessionLaps => (int)Yaml.Number(_laps);
        /// <summary>Session length in seconds, reported as text such as "1800.0000 sec".</summary>
        public double _SessionTime => Yaml.Number(_time);
    }

    internal sealed class CarDetails
    {
        public CarDetails(DriverEntry driver)
        {
            Driver = driver;
        }

        public DriverEntry Driver { get; }
        public int CarIdx => Driver.CarIdx;
        public string UserName => Driver.UserName;
        public string CarNumberDisplay => Driver.CarNumber;
        public bool IsPaceCar => Driver.CarIsPaceCar;
    }

    internal sealed class DriverEntry
    {
        public DriverEntry(Dictionary<string, object> map)
        {
            CarIdx = Yaml.Int(map, "CarIdx");
            UserName = Yaml.Text(map, "UserName");
            CarNumber = Yaml.Text(map, "CarNumber");
            CarScreenName = Yaml.Text(map, "CarScreenName");
            CarScreenNameShort = Yaml.Text(map, "CarScreenNameShort");
            CarClassEstLapTime = Yaml.Number(Yaml.Text(map, "CarClassEstLapTime"));
            IRating = Yaml.Int(map, "IRating");
            LicString = Yaml.Text(map, "LicString");
            LicColor = Yaml.Text(map, "LicColor");
            CarIsPaceCar = Yaml.Int(map, "CarIsPaceCar") == 1;
            IsSpectator = Yaml.Int(map, "IsSpectator") == 1;
            CarClassId = Yaml.Int(map, "CarClassID");
            CarClassShortName = Yaml.Text(map, "CarClassShortName");
            CarClassColor = Yaml.Text(map, "CarClassColor");
            CarClassRelSpeed = Yaml.Number(Yaml.Text(map, "CarClassRelSpeed"));
            CarId = Yaml.Int(map, "CarID");
            TeamName = Yaml.Text(map, "TeamName");
            Incidents = Yaml.Int(map, "CurDriverIncidentCount");
        }

        public bool IsSpectator { get; }
        public int CarClassId { get; }
        public string CarClassShortName { get; }
        /// <summary>Class color as iRacing reports it, such as "0xffda59".</summary>
        public string CarClassColor { get; }
        public double CarClassRelSpeed { get; }
        public int CarId { get; }
        public string TeamName { get; }
        public int Incidents { get; }

        public int CarIdx { get; }
        public string UserName { get; }
        public string CarNumber { get; }
        public string CarScreenName { get; }
        public string CarScreenNameShort { get; }
        public double CarClassEstLapTime { get; }
        public long IRating { get; }
        public string LicString { get; }
        public string LicColor { get; }
        public bool CarIsPaceCar { get; }
    }

    /// <summary>
    /// Just enough YAML for iRacing's session string: indented maps, "- " lists
    /// and plain scalars. Values are kept as text.
    /// </summary>
    internal static class Yaml
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var root = new Dictionary<string, object>();
            // Each open container with the indent of its keys; lists track the indent of their dashes.
            var stack = new List<KeyValuePair<int, object>> { new KeyValuePair<int, object>(-1, root) };
            string pendingKey = null;
            int pendingIndent = 0;
            Dictionary<string, object> pendingParent = null;

            foreach (string rawLine in text.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed == "---" || trimmed == "..." || trimmed.StartsWith("#")) continue;
                int indent = line.Length - trimmed.Length;
                bool item = trimmed.StartsWith("- ") || trimmed == "-";

                if (pendingKey != null)
                {
                    // A "key:" line opens a list or map, depending on the next line.
                    object child = item ? (object)new List<object>() : new Dictionary<string, object>();
                    pendingParent[pendingKey] = child;
                    stack.Add(new KeyValuePair<int, object>(item ? indent : Math.Max(indent, pendingIndent + 1), child));
                    pendingKey = null;
                }

                while (stack.Count > 1 && (indent < stack[stack.Count - 1].Key ||
                    (indent == stack[stack.Count - 1].Key && !item && stack[stack.Count - 1].Value is List<object>)))
                    stack.RemoveAt(stack.Count - 1);

                Dictionary<string, object> target;
                if (item)
                {
                    var list = stack[stack.Count - 1].Value as List<object>;
                    if (list == null) continue;
                    target = new Dictionary<string, object>();
                    list.Add(target);
                    trimmed = trimmed.Length > 2 ? trimmed.Substring(2).TrimStart() : "";
                    indent += 2;
                    stack.Add(new KeyValuePair<int, object>(indent, target));
                    if (trimmed.Length == 0) continue;
                }
                else
                {
                    target = stack[stack.Count - 1].Value as Dictionary<string, object>;
                    if (target == null) continue;
                }

                int colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;
                string key = trimmed.Substring(0, colon).Trim();
                string value = trimmed.Substring(colon + 1).Trim();
                if (value.Length == 0)
                {
                    pendingKey = key;
                    pendingIndent = indent;
                    pendingParent = target;
                    target[key] = null;
                }
                else
                {
                    target[key] = Unquote(value);
                }
            }
            return root;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                return value.Substring(1, value.Length - 2);
            return value;
        }

        public static Dictionary<string, object> Map(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;

        public static IEnumerable<Dictionary<string, object>> Maps(Dictionary<string, object> map, string key)
        {
            if (map != null && map.TryGetValue(key, out object value) && value is List<object> list)
                foreach (object entry in list)
                    if (entry is Dictionary<string, object> child)
                        yield return child;
        }

        public static string Text(Dictionary<string, object> map, string key) =>
            map != null && map.TryGetValue(key, out object value) ? value as string : null;

        public static int Int(Dictionary<string, object> map, string key) => (int)Number(Text(map, key));

        /// <summary>The leading number of a value such as "2.50 km" or "1800.0000 sec"; 0 when there is none.</summary>
        public static double Number(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string first = text.Trim().Split(' ')[0];
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
        }
    }
}
