using System.IO;

namespace AlmostFastDash.Dashboards
{
    /// <summary>The shareable .afodash file: one designed dashboard, with its images and LED lights.</summary>
    internal sealed class DesignPackage
    {
        /// <summary>The file's identity, from before the rename to Almost Fast Dash; kept so every .afodash file stays readable.</summary>
        public string Format { get; set; } = "AlmostFastOverlays.Dashboard";
        /// <summary>2 added the LED lights; files without them still import.</summary>
        public int Version { get; set; } = 2;
        public string Name { get; set; }
        public string AccentColor { get; set; }
        public DashLayout Design { get; set; }
        public LightProfile Lights { get; set; }

        public static DesignPackage From(DashboardSettings dashboard, DashLayout design = null) => new DesignPackage
        {
            Name = dashboard.Name, AccentColor = dashboard.AccentColor, Design = design ?? dashboard.Design,
            Lights = dashboard.Lights == null ? null : SettingsStore.Clone(dashboard.Lights)
        };

        public static void Write(string path, DesignPackage package) =>
            File.WriteAllText(path, SettingsStore.ToJson(package));

        public static DesignPackage Read(string path)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue,
                RecursionLimit = 256 };
            DesignPackage package = serializer.Deserialize<DesignPackage>(File.ReadAllText(path));
            if (package?.Design == null || package.Format != "AlmostFastOverlays.Dashboard")
                throw new InvalidDataException("This is not an Almost Fast Dash dashboard file.");
            package.Design.Normalize();
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            return package;
        }
    }
}
