using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DV5Direct
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

        public DiagnosticLogger(bool enabled)
        {
            _enabled = enabled;
            _directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (_enabled) OpenNewFile();
        }

        public string DirectoryPath => _enabled ? _directory : null;

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

            if (_writer.BaseStream.Length >= MaximumFileBytes) OpenNewFile();
            _writer.Write(now.ToString("O", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(rawLeftRight.ToString(CultureInfo.InvariantCulture));
            _writer.Write(',');
            if (nearestDeltaPct.HasValue)
                _writer.Write(nearestDeltaPct.Value.ToString("0.000000", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(left);
            _writer.Write(',');
            _writer.WriteLine(right);
            if ((now - _lastFlushUtc).TotalSeconds >= 1)
            {
                _writer.Flush();
                _lastFlushUtc = now;
            }
        }

        private void OpenNewFile()
        {
            _writer?.Dispose();
            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "radar-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".csv");
            _writer = new StreamWriter(path, false) { AutoFlush = false };
            _writer.WriteLine("timestamp_utc,car_left_right,nearest_delta_lap_pct,left_led_zone,right_led_zone");
            _lastFlushUtc = DateTime.UtcNow;
            PruneOldFiles();
        }

        private void PruneOldFiles()
        {
            FileInfo[] files = new DirectoryInfo(_directory)
                .GetFiles("radar-*.csv")
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToArray();
            foreach (FileInfo file in files.Skip(MaximumFiles)) file.Delete();
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
