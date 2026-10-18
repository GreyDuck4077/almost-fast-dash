using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace AlmostFastDash.IRacing
{
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
}
