using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace AlmostFastDash.IRacing
{
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
}
