using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Settings
{
    internal sealed partial class AppSettings
    {
        internal const int CurrentSchema = 21;

        public int SchemaVersion { get; set; }
        public string DefaultDashboardId { get; set; } = DashboardSettings.RoadId;
        public GeneralSettings General { get; set; } = new GeneralSettings();
        public List<DashboardSettings> Dashboards { get; set; } = new List<DashboardSettings>();

        /// <summary>A new install's settings: the built-in dashboards, units for the PC's region and the welcome card and the thank-you note.</summary>
        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings();
            settings.General.UseRegionUnits(System.Globalization.RegionInfo.CurrentRegion);
            settings.General.ShowWelcome = true;
            settings.General.ShowThanks = true;
            settings.General.SeenVersion = WhatsNew.Version;
            settings.EnsureBuiltInDashboards();
            return settings;
        }

        public DashboardSettings GetDefaultDashboard()
        {
            EnsureBuiltInDashboards();
            return Dashboards.FirstOrDefault(d => d.Id == DefaultDashboardId) ?? Dashboards.First();
        }

        /// <summary>
        /// Picks the dashboard for a session. The default dashboard wins when it is
        /// assigned to the discipline; otherwise the first assigned dashboard is used.
        /// </summary>
        public DashboardSettings ResolveDashboard(string discipline)
        {
            DashboardSettings fallback = GetDefaultDashboard();
            if (!General.AutoSwitchByDiscipline || string.IsNullOrEmpty(discipline)) return fallback;
            if (fallback.RaceTypes.Contains(discipline)) return fallback;
            return Dashboards.FirstOrDefault(d => d.RaceTypes != null && d.RaceTypes.Contains(discipline)) ?? fallback;
        }

        /// <summary>Adds a copy of a dashboard, or a new one from a template.</summary>
        public DashboardSettings AddDashboard(DashboardSettings copyFrom)
        {
            DashboardSettings created = SettingsStore.Clone(copyFrom);
            created.Id = NextId();
            created.Name = UniqueName(copyFrom.Name + " copy");
            created.RaceTypes = new List<string>();
            Dashboards.Add(created);
            return created;
        }

        /// <summary>Adds a designed dashboard started from a template.</summary>
        public DashboardSettings AddDesignedDashboard(DesignTemplate template, string name = null, System.Drawing.Size? screen = null)
        {
            System.Drawing.Size size = screen ?? new System.Drawing.Size(ScreenSizes.DefaultWidth, ScreenSizes.DefaultHeight);
            DashboardSettings created = DashboardSettings.CreateDesigned(template, size.Width, size.Height);
            created.Id = NextId();
            created.Name = UniqueName(name ?? template.Name);
            created.RaceTypes = new List<string>();
            Dashboards.Add(created);
            return created;
        }

        /// <summary>Puts a dashboard's lights and flag alerts back to their defaults.</summary>
        public void ResetDashboard(DashboardSettings dashboard)
        {
            DashboardSettings factory = dashboard.RaceTypes != null &&
                (dashboard.RaceTypes.Contains(Disciplines.Oval) || dashboard.RaceTypes.Contains(Disciplines.DirtOval))
                ? DashboardSettings.CreateOval() : DashboardSettings.CreateRoad();
            dashboard.Lights = factory.Lights;
            dashboard.Alerts = factory.Alerts;
        }

        /// <summary>Removes a dashboard; the last one can't be removed.</summary>
        public bool RemoveDashboard(DashboardSettings dashboard)
        {
            if (Dashboards.Count <= 1 || !Dashboards.Remove(dashboard)) return false;
            if (DefaultDashboardId == dashboard.Id) DefaultDashboardId = Dashboards[0].Id;
            return true;
        }

        private string NextId()
        {
            int next = 4;
            while (Dashboards.Any(d => d.Id == "dashboard-" + next)) next++;
            return "dashboard-" + next;
        }

        private string UniqueName(string name)
        {
            string candidate = name;
            for (int n = 2; Dashboards.Any(d => string.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = name + " " + n;
            return candidate;
        }
    }
}
