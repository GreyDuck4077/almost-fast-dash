using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace AlmostFastDash.IRacing
{
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
}
