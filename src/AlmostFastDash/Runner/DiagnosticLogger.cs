using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AlmostFastDash.Runner
{
    internal sealed class DiagnosticLogger : IDisposable
    {
        private const long MaximumFileBytes = 2 * 1024 * 1024;
        private const int MaximumFiles = 8;
        private readonly bool _enabled;
        private readonly string _directory;
        private StreamWriter _writer;
        private DateTime _lastWriteUtc;
        private DateTime _lastFlushUtc;
        private readonly object _lightGate = new object();
        private StreamWriter _lights;

        public DiagnosticLogger(bool enabled)
        {
            _enabled = enabled;
            _directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        }

        public string DirectoryPath => _enabled ? _directory : null;

        /// <summary>Where the radar last drew the cars near you, written with each line of the radar log.</summary>
        private string _radarCars = "";

        /// <summary>
        /// Notes where the radar placed the cars within 25 metres: number, metres ahead (+) or behind (-), the lane it's
        /// drawn in (-1 left, 0 in line, 1 right) and A when the spotter has it alongside. "#12@-7.1:0.0;#5@-7.4:1.0A".
        /// </summary>
        public void NoteRadarCars(RadarCar[] cars)
        {
            if (!_enabled) return;
            _radarCars = string.Join(";", (cars ?? new RadarCar[0]).Where(c => Math.Abs(c.Meters) <= 25).Select(c =>
                "#" + (string.IsNullOrEmpty(c.Number) ? "idx" + c.CarIdx.ToString(CultureInfo.InvariantCulture) : c.Number) + "@" +
                c.Meters.ToString("0.0", CultureInfo.InvariantCulture) + ":" + c.Lane.ToString("0.0", CultureInfo.InvariantCulture) +
                (c.Alongside ? "A" : "")));
        }

        public void RecordRadar(
            int rawLeftRight,
            double? nearestDeltaPct,
            BlindSpotZone left,
            BlindSpotZone right)
        {
            if (!_enabled) return;
            DateTime now = DateTime.UtcNow;
            if ((now - _lastWriteUtc).TotalMilliseconds < 100) return;
            _lastWriteUtc = now;

            // A file is started only once there's something to write, so starts without a session don't push out
            // the logs of a race.
            if (_writer == null || _writer.BaseStream.Length >= MaximumFileBytes) OpenNewFile();
            _writer.Write(now.ToString("O", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(rawLeftRight.ToString(CultureInfo.InvariantCulture));
            _writer.Write(',');
            if (nearestDeltaPct.HasValue)
                _writer.Write(nearestDeltaPct.Value.ToString("0.000000", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(left);
            _writer.Write(',');
            _writer.Write(right);
            _writer.Write(',');
            _writer.WriteLine(_radarCars);
            if ((now - _lastFlushUtc).TotalSeconds >= 1)
            {
                _writer.Flush();
                _lastFlushUtc = now;
            }
        }

        /// <summary>
        /// Writes one stretch of a light layer lighting an LED group to the light log, so
        /// a light seen during a race can be looked up afterwards. Called from the LED loop.
        /// </summary>
        public void RecordLight(LightSpan span)
        {
            if (!_enabled) return;
            lock (_lightGate)
            {
                try
                {
                    if (_lights == null || _lights.BaseStream.Length >= MaximumFileBytes)
                    {
                        _lights?.Dispose();
                        Directory.CreateDirectory(_directory);
                        _lights = new StreamWriter(Path.Combine(_directory, "lights-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") +
                            ".csv"),
                            false) { AutoFlush = true };
                        _lights.WriteLine("started_local,seconds,device,group,layer,color,while");
                        PruneOldFiles("lights-*.csv");
                    }
                    _lights.WriteLine(string.Join(",",
                        span.Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        span.Seconds.ToString("0.0", CultureInfo.InvariantCulture),
                        Csv(span.Device), Csv(span.Group), Csv(span.Layer), span.Color, Csv(span.Context)));
                }
                catch (IOException) { }
            }
        }

        private static string Csv(string text) =>
            text == null ? "" : text.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

        private void OpenNewFile()
        {
            _writer?.Dispose();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "radar-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".csv");
            _writer = new StreamWriter(path, false) { AutoFlush = false };
            _writer.WriteLine("timestamp_utc,car_left_right,nearest_delta_lap_pct,left_led_zone,right_led_zone,radar_cars");
            _lastFlushUtc = DateTime.UtcNow;
            PruneOldFiles("radar-*.csv");
        }

        private void PruneOldFiles(string pattern)
        {
            FileInfo[] files = new DirectoryInfo(_directory)
                .GetFiles(pattern)
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToArray();
            foreach (FileInfo file in files.Skip(MaximumFiles)) file.Delete();
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _writer = null;
            lock (_lightGate)
            {
                _lights?.Dispose();
                _lights = null;
            }
        }
    }
}
