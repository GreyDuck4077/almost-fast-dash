using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AlmostFastDash.Settings
{
    internal static class Disciplines
    {
        public const string Oval = "Oval";
        public const string Road = "Road";
        public const string DirtOval = "Dirt Oval";
        public const string DirtRoad = "Dirt Road";
        public static readonly string[] All = { Oval, Road, DirtOval, DirtRoad };

        // iRacing reports WeekendInfo.Category as "Oval", "Road", "DirtOval",
        // "DirtRoad" and, for newer license splits, "SportsCar"/"FormulaCar".
        // Everything that is not an oval or dirt surface is treated as road.
        public static string FromIRacing(string category, string trackType)
        {
            string text = ((category ?? "") + " " + (trackType ?? "")).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text)) return null;
            bool dirt = text.Contains("dirt");
            bool oval = text.Contains("oval");
            if (dirt) return oval ? DirtOval : DirtRoad;
            if (oval) return Oval;
            return Road;
        }
    }

    internal static class Layouts
    {
        /// <summary>Every dashboard is a design stored in <see cref="DashboardSettings.Design"/> and drawn by <see cref="LayoutRenderer"/>.</summary>
        public const string Custom = "Custom";

        /// <summary>Code-drawn layouts from earlier versions; schema 5 replaces them with the Liquid Glass designs.</summary>
        public static readonly string[] Retired = { "Summit", "Formula", "Oval", "Endurance", "Velocity", "Apex", "Telemetry" };
    }

    internal sealed class AppSettings
    {
        internal const int CurrentSchema = 20;

        public int SchemaVersion { get; set; }
        public string DefaultDashboardId { get; set; } = DashboardSettings.RoadId;
        public GeneralSettings General { get; set; } = new GeneralSettings();
        public List<DashboardSettings> Dashboards { get; set; } = new List<DashboardSettings>();

        /// <summary>A new install's settings: the built-in dashboards, units for the PC's region and the welcome card.</summary>
        public static AppSettings CreateDefault()
        {
            var settings = new AppSettings();
            settings.General.UseRegionUnits(System.Globalization.RegionInfo.CurrentRegion);
            settings.General.ShowWelcome = true;
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

        /// <summary>Makes sure there is at least one dashboard and upgrades settings from earlier versions.</summary>
        public void EnsureBuiltInDashboards()
        {
            if (Dashboards == null) Dashboards = new List<DashboardSettings>();
            if (General == null) General = new GeneralSettings();
            if (General.Devices == null) General.Devices = new List<DeviceSettings>();
            General.Devices.RemoveAll(d => d == null);
            Dashboards.RemoveAll(d => d == null);

            if (SchemaVersion < CurrentSchema)
            {
                // Two LB-10 bars daisy-chain on one controller, 10 LEDs each.
                if (SchemaVersion < 4 && General.BrowLedCount == 10) General.BrowLedCount = 20;
                if (SchemaVersion > 0 && SchemaVersion < 5) ReplaceCodeLayouts();
                if (SchemaVersion > 0 && SchemaVersion < 6) DevicesFromFixedSettings();
                if (SchemaVersion > 0 && SchemaVersion < 7) RenameBrowLayers();
                if (SchemaVersion > 0 && SchemaVersion < 8) ClassicLightsToLayers();
                if (SchemaVersion > 0 && SchemaVersion < 9) AddFlagships();
                if (SchemaVersion == 9) RedesignRoundy();
                if (SchemaVersion >= 9 && SchemaVersion < 11) GlassRoady();
                if (SchemaVersion >= 9 && SchemaVersion < 12) TouchFlagships();
                if (SchemaVersion > 0 && SchemaVersion < 13) ClearerFuelToAdd();
                if (SchemaVersion >= 9 && SchemaVersion < 14) PolishFlagships();
                if (SchemaVersion > 0 && SchemaVersion < 15) { RoundCutPanels(); RebuildNeon(); }
                if (SchemaVersion > 0 && SchemaVersion < 16) ReworkHaloAndOval();
                if (SchemaVersion > 0 && SchemaVersion < 17) BrighterLabels();
                if (SchemaVersion > 0 && SchemaVersion < 18) WiderRadar();
                if (SchemaVersion > 0 && SchemaVersion < 19) { RefreshHaloAndOval(); LargerText(); }
                if (SchemaVersion > 0 && SchemaVersion < 20) TiresSpelling();
                SchemaVersion = CurrentSchema;
            }
            foreach (DeviceSettings device in General.Devices) device.Normalize();

            if (Dashboards.Count == 0)
            {
                Dashboards.Add(DashboardSettings.CreateRoad());
                Dashboards.Add(DashboardSettings.CreateOval());
            }
            foreach (DashboardSettings dashboard in Dashboards) dashboard.Normalize();
            if (!Dashboards.Any(d => d.Id == DefaultDashboardId)) DefaultDashboardId = Dashboards[0].Id;
        }

        /// <summary>
        /// Schema 9: the flagship dashboards, Roady and Roundy, arrive and take over road and oval tracks.
        /// Every existing dashboard stays as it was, apart from handing those track types over.
        /// </summary>
        private void AddFlagships()
        {
            if (Dashboards.Count == 0) return;
            void Take(DashboardSettings flagship, params string[] types)
            {
                if (Dashboards.Any(d => d.Id == flagship.Id)) return;
                foreach (DashboardSettings d in Dashboards) d.RaceTypes?.RemoveAll(types.Contains);
                Dashboards.Insert(0, flagship);
            }
            Take(DashboardSettings.CreateOval(), Disciplines.Oval, Disciplines.DirtOval);
            Take(DashboardSettings.CreateRoad(), Disciplines.Road, Disciplines.DirtRoad);
            DefaultDashboardId = DashboardSettings.RoadId;
        }

        /// <summary>
        /// Schema 10: Roundy was redrawn round a numbered dial. A Roundy still in its first form (with its
        /// boxed panels) takes the new design. One that has been rebuilt by hand is left alone.
        /// </summary>
        private void RedesignRoundy()
        {
            DashboardSettings roundy = Dashboards.FirstOrDefault(d => d.Id == DashboardSettings.OvalId);
            var widgets = roundy?.Design?.Screens?.FirstOrDefault()?.Widgets;
            if (widgets == null || !widgets.Any(w => w.Name == "Around panel") || !widgets.Any(w => w.Name == "Falloff panel")) return;
            roundy.Design = DesignTemplates.Oval.Create();
        }

        /// <summary>
        /// Schema 14: the flagships' instrument finish. The rev scales gain hairline bezels and light up as
        /// they're passed, Roady a pool of light behind the gear, Roundy a dial face, and the pit-lane ring
        /// marks the limit. The saved copies take the look of the named widgets from freshly built templates,
        /// sized to match. Where everything sits, and the rest of each design, stays as it was.
        /// </summary>
        private void PolishFlagships()
        {
            void Polish(string id, DesignTemplate template, string[] restyle, string[] addBefore, string[] remove)
            {
                DashLayout saved = Dashboards.FirstOrDefault(d => d.Id == id)?.Design;
                if (saved?.Screens == null) return;
                DashLayout fresh = template.Create();
                fresh.ScaleTo(saved.Width, saved.Height);
                foreach (DashScreen screen in saved.Screens)
                {
                    DashScreen model = fresh.Screens.FirstOrDefault(s => s.Name == screen.Name);
                    if (model == null || screen.Widgets == null) continue;
                    DashWidget Fresh(string name) => model.Widgets.FirstOrDefault(w => w.Name == name);
                    foreach (DashWidget widget in screen.Widgets)
                    {
                        if (!restyle.Contains(widget.Name) || Fresh(widget.Name) is null || Fresh(widget.Name).Type != widget.Type) continue;
                        DashWidget look = Fresh(widget.Name);
                        foreach (var prop in look.Props) widget.Props[prop.Key] = prop.Value;
                    }
                    // New widgets go in just below the widget they light or sit behind.
                    for (int i = 0; i + 1 < addBefore.Length; i += 2)
                    {
                        DashWidget added = Fresh(addBefore[i]), anchor = screen.Widgets.FirstOrDefault(w => w.Name == addBefore[i + 1]);
                        if (added == null || anchor == null || screen.Widgets.Any(w => w.Name == added.Name)) continue;
                        added.Id = DashScreen.NewId("w");
                        screen.Widgets.Insert(screen.Widgets.IndexOf(anchor), added);
                    }
                    screen.Widgets.RemoveAll(w => remove.Contains(w.Name) && w.Type == "text");
                }
            }
            Polish(DashboardSettings.RoadId, DesignTemplates.Road, new[] { "Rev sweep", "Gear", "Rule", "Pit speed ring" }, new[] { "Gear light", "Rev sweep" }, new string[0]);
            Polish(DashboardSettings.OvalId, DesignTemplates.Oval, new[] { "Rev dial", "Pit speed ring" }, new[] { "Dial face", "Rev dial" }, new[] { "Rpm unit" });
        }

        /// <summary>
        /// Schema 15: Halo and Oval (designs from before the templates were rebuilt) take rounded corners to
        /// match their round centrepieces. Cut-cornered panels become rounded, short chips become pills, and
        /// the stepped hairlines across the top straighten out. Their rev lights gain bezel lines, and the
        /// pit-lane ring marks the limit. Only those two dashboards change, and only the widgets named here.
        /// </summary>
        private void RoundCutPanels()
        {
            foreach (DashboardSettings dashboard in Dashboards.Where(d => d.Id == "dashboard-halo" || d.Id == "dashboard-oval"))
            {
                if (dashboard.Design?.Screens == null) continue;
                foreach (DashScreen screen in dashboard.Design.Screens)
                {
                    if (screen.Widgets == null) continue;
                    foreach (DashWidget w in screen.Widgets)
                    {
                        string corners = w.Get("corners", "") ?? "";
                        if (w.Type == "panel" && (corners.StartsWith("Cut") || w.Name == "Delta badge"))
                        {
                            w.Set("corners", "Round");
                            // Short chips become pills; tiles and cards take one even radius.
                            w.Set("radius", (w.H <= 32 ? Math.Round(w.H / 2) : w.H < 100 ? 16 : 18).ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        if (w.Type == "arc" && w.Name.StartsWith("Rev lights"))
                        {
                            w.Set("rim", "#243041");
                            w.Set("rimWidth", "1");
                        }
                        if (w.Type == "arc" && w.Name == "Pit speed")
                            foreach (var p in new[] { "zones", "true", "rim", "border", "rimWidth", "1", "needle", "Marker", "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3" }
                                .Select((v, i) => new { v, i }).GroupBy(x => x.i / 2, x => x.v).Select(g => g.ToArray()))
                                w.Set(p[0], p[1]);
                    }
                    // The hairlines across the top stepped down with a diagonal kink to meet the cut corners: now one straight line each side.
                    screen.Widgets.RemoveAll(w => w.Type == "shape" && (w.Name == "Hairline left" || w.Name == "Hairline right" || w.Name == "Hairline left angle" || w.Name == "Hairline right angle"));
                    DashWidget left = screen.Widgets.FirstOrDefault(w => w.Name == "Hairline left base"), right = screen.Widgets.FirstOrDefault(w => w.Name == "Hairline right base");
                    if (left != null) { left.W = 252; left.Set("stroke", "#1E2836"); }
                    if (right != null) { right.X = 584; right.W = 252; right.Set("stroke", "#1E2836"); }
                }
            }
        }

        /// <summary>
        /// Schema 15: Neon was rebuilt from the ground up. A dashboard still made of the first Neon (its two
        /// tapered rings and the divider under them) takes the new design at its own size. Its name, lights
        /// and track types stay. One that has been taken apart and rebuilt by hand is left alone.
        /// </summary>
        private void RebuildNeon()
        {
            foreach (DashboardSettings dashboard in Dashboards)
            {
                var widgets = dashboard.Design?.Screens?.FirstOrDefault()?.Widgets;
                if (widgets == null || !new[] { "Rev ring", "Fuel ring", "Fuel pct", "Divider" }.All(n => widgets.Any(w => w.Name == n))) continue;
                DashLayout rebuilt = DesignTemplates.Find("neon").Create();
                rebuilt.ScaleTo(dashboard.Design.Width, dashboard.Design.Height);
                rebuilt.PageButton = dashboard.Design.PageButton;
                rebuilt.PageBackButton = dashboard.Design.PageBackButton;
                dashboard.Design = rebuilt;
            }
        }

        /// <summary>
        /// Schema 16: Halo and Oval were reworked from the ground up. Halo is now the endurance and multiclass
        /// dashboard (a rev ring over a live delta, traffic in class colors) and Oval the pack-racing radar. The
        /// saved Halo and Oval take the new designs at their own size. Their names, accent colors, lights,
        /// track types, page buttons and flag choices stay. No other dashboard changes.
        /// </summary>
        private void ReworkHaloAndOval()
        {
            foreach (DashboardSettings dashboard in Dashboards)
            {
                string template = dashboard.Id == "dashboard-halo" ? "halo" : dashboard.Id == "dashboard-oval" ? "oval" : null;
                DashLayout old = dashboard.Design;
                if (template == null || old == null) continue;
                DashLayout rebuilt = DesignTemplates.Find(template).Create();
                rebuilt.ScaleTo(old.Width, old.Height);
                rebuilt.PageButton = old.PageButton;
                rebuilt.PageBackButton = old.PageBackButton;
                rebuilt.FullScreenFlags = old.FullScreenFlags;
                rebuilt.WavingFlags = old.WavingFlags;
                rebuilt.FlagFrame = old.FlagFrame;
                dashboard.Design = rebuilt;
            }
        }

        /// <summary>
        /// Schema 17: labels are easier to read. A design whose muted color (labels and captions) is still the one
        /// a template gave it takes the brighter version; a color the user picked stays. The captions under
        /// Halo's and Oval's big numbers move from the dim color to the muted one, a size larger.
        /// </summary>
        private void BrighterLabels()
        {
            var brighter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#8190A4"] = "#A9B6C8", ["#968C84"] = "#BDB3AA", ["#8AA396"] = "#B2C7BB", ["#8DA0BF"] = "#B6C5DD", ["#AA9CDA"] = "#C2B6EC"
            };
            var captions = new HashSet<string> { "Ahead lengths caption", "Behind lengths caption", "Fuel laps caption", "Falloff caption", "Traffic label" };
            foreach (DashboardSettings dashboard in Dashboards)
            {
                LayoutTheme theme = dashboard.Design?.Theme;
                if (theme?.Muted != null && brighter.TryGetValue(theme.Muted.Trim(), out string lifted)) theme.Muted = lifted;
                if (dashboard.Design?.Screens == null) continue;
                foreach (DashWidget widget in dashboard.Design.AllWidgets())
                {
                    if (widget.Type != "text" || !captions.Contains(widget.Name) || widget.Get("color", "") != "dim") continue;
                    widget.Set("color", "muted");
                    if (widget.Get("size", "") == "10") widget.Set("size", "11");
                }
            }
        }

        /// <summary>
        /// Schema 18: the Oval's radar became a chase-camera view with bigger cars, and the panels either side
        /// narrowed to give it room. A dashboard still built on the first Oval layout (its radar and the ahead
        /// panel beside it) takes the new one at its own size, keeping its page buttons and flag choices.
        /// </summary>
        private void WiderRadar() => Refresh("oval", widgets => widgets.Any(w => w.Type == "radar" && w.Name == "Radar") && widgets.Any(w => w.Name == "Ahead panel"));

        /// <summary>
        /// Schema 19: Halo and Oval, both rebuilt on 13 October, take their templates again with text no smaller
        /// than 16 px and every label in a readable color. A dashboard is recognized by its first screen: Halo's
        /// rev ring and traffic strip, or the Oval's radar and ahead panel.
        /// </summary>
        private void RefreshHaloAndOval()
        {
            Refresh("halo", widgets => widgets.Any(w => w.Type == "traffic") && widgets.Any(w => w.Name == "Rev ring"));
            WiderRadar();
        }

        /// <summary>
        /// Rebuilds the dashboards still made from a template (as <paramref name="isTemplate"/> tells from their first
        /// screen) at their own size. Their page buttons, flag choices and label color stay.
        /// </summary>
        private void Refresh(string templateId, Func<List<DashWidget>, bool> isTemplate)
        {
            foreach (DashboardSettings dashboard in Dashboards)
            {
                DashLayout old = dashboard.Design;
                var widgets = old?.Screens?.FirstOrDefault()?.Widgets;
                if (widgets == null || !isTemplate(widgets)) continue;
                DashLayout rebuilt = DesignTemplates.Find(templateId).Create();
                rebuilt.ScaleTo(old.Width, old.Height);
                rebuilt.PageButton = old.PageButton;
                rebuilt.PageBackButton = old.PageBackButton;
                rebuilt.FullScreenFlags = old.FullScreenFlags;
                rebuilt.WavingFlags = old.WavingFlags;
                rebuilt.FlagFrame = old.FlagFrame;
                rebuilt.Theme.Muted = old.Theme?.Muted ?? rebuilt.Theme.Muted;
                dashboard.Design = rebuilt;
            }
        }

        /// <summary>
        /// Schemas 18 and 19: no text smaller than a wheel screen can show readably (16 px on an 854×480 screen since
        /// schema 19) and none in the too-faint dim color. Nothing else changes (see <see cref="ReadableText"/>).
        /// </summary>
        private void LargerText()
        {
            foreach (DashboardSettings dashboard in Dashboards)
            {
                ReadableText.Apply(dashboard.Design);
                // A Spotter's "LAST LAP SPEED" no longer fits its tile at the larger size.
                foreach (DashWidget w in dashboard.Design?.AllWidgets() ?? Enumerable.Empty<DashWidget>())
                    if (w.Type == "metric" && w.Name == "Lap speed" && w.Get("label", "") == "LAST LAP SPEED") w.Set("label", "LAP SPEED");
            }
        }

        /// <summary>
        /// Schema 20: the app's words are in US English, so the pit-lane page's "TYRES · LAST STOP" reads "TIRES". Only
        /// the shipped label and layer names change; anything typed differently is left as it is.
        /// </summary>
        private void TiresSpelling()
        {
            var names = new Dictionary<string, string> { ["Tyres panel"] = "Tires panel", ["Tyres label"] = "Tires label", ["Tyre box"] = "Tire box", ["Tyres"] = "Tires" };
            foreach (DashWidget widget in Dashboards.Where(d => d.Design?.Screens != null).SelectMany(d => d.Design.AllWidgets()))
            {
                if (widget.Type == "text" && widget.Get("text") == "TYRES · LAST STOP") widget.Set("text", "TIRES · LAST STOP");
                if (widget.Name != null && names.TryGetValue(widget.Name, out string name)) widget.Name = name;
            }
        }

        /// <summary>The fuel-to-add value as designs showed it before schema 13: a bare "+0.3".</summary>
        internal const string OldFuelToAdd = "=FuelToFinish < 0 ? \"--.-\" : FuelToFinish <= 0.05 ? \"NONE\" : \"+\" + fuel(FuelToFinish)";

        /// <summary>
        /// Schema 13: fuel to add reads "0.3 GAL" (or ENOUGH) under FUEL TO ADD, marked EST while it's an early
        /// estimate, instead of a bare "+0.3". Only widgets still showing the old value change, and a label the
        /// user wrote themselves stays.
        /// </summary>
        private void ClearerFuelToAdd()
        {
            var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FUEL TO END", "FUEL TO ADD", "REFUEL" };
            foreach (DashWidget widget in Dashboards.Where(d => d.Design?.Screens != null).SelectMany(d => d.Design.AllWidgets()))
            {
                if (widget.Type != "metric" || widget.Get("value") != OldFuelToAdd) continue;
                widget.Set("value", DesignTemplates.FuelToAdd);
                string unit = widget.Get("unit", "") ?? "";
                if (unit == "" || unit == "=FuelUnit") widget.Set("unit", DesignTemplates.FuelToAddUnit);
                if (ours.Contains(widget.Get("label", "") ?? "")) widget.Set("label", DesignTemplates.FuelToAddLabel);
            }
        }

        /// <summary>Schema 12: the flagships' glass panels gained tap views for touch screens; nothing else in them changes.</summary>
        private void TouchFlagships()
        {
            DashLayout roady = Dashboards.FirstOrDefault(d => d.Id == DashboardSettings.RoadId)?.Design;
            DashLayout roundy = Dashboards.FirstOrDefault(d => d.Id == DashboardSettings.OvalId)?.Design;
            if (roady?.Screens != null) TouchViews.Roady(roady);
            if (roundy?.Screens != null) TouchViews.Roundy(roundy);
        }

        /// <summary>Schema 11: Roady's panels became Liquid glass, as Roundy's are. Nothing else in it changes.</summary>
        private void GlassRoady()
        {
            DashboardSettings roady = Dashboards.FirstOrDefault(d => d.Id == DashboardSettings.RoadId);
            if (roady?.Design?.Screens == null) return;
            foreach (DashScreen screen in roady.Design.Screens)
                foreach (DashWidget widget in screen.Widgets ?? new List<DashWidget>())
                    if (widget.Type == "panel" && widget.Get("style") == "Soft") widget.Set("style", "Liquid");
        }

        /// <summary>
        /// Schema 5: the code-drawn dashboards are gone. Their track types move to the
        /// road and oval designs, which are added when the user has no design of that name.
        /// </summary>
        private void ReplaceCodeLayouts()
        {
            var retired = Dashboards.Where(d => d.Layout != Layouts.Custom).ToList();
            if (retired.Count == 0) return;
            var orphanTypes = retired.SelectMany(d => d.RaceTypes ?? new List<string>()).Distinct().ToList();
            bool defaultRetired = retired.Any(d => d.Id == DefaultDashboardId);
            foreach (DashboardSettings d in retired) Dashboards.Remove(d);

            DashboardSettings Ensure(string name, Func<DashboardSettings> create)
            {
                DashboardSettings found = Dashboards.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                found = create();
                found.RaceTypes = new List<string>();
                if (Dashboards.Any(d => d.Id == found.Id)) found.Id = NextId();
                Dashboards.Add(found);
                return found;
            }

            // A new Oval design takes over the oval tracks, as the old Oval layout did.
            if (!Dashboards.Any(d => string.Equals(d.Name, DesignTemplates.Oval.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (DashboardSettings d in Dashboards)
                    d.RaceTypes?.RemoveAll(type => type == Disciplines.Oval || type == Disciplines.DirtOval);
                Ensure(DesignTemplates.Oval.Name, DashboardSettings.CreateOval).RaceTypes.AddRange(new[] { Disciplines.Oval, Disciplines.DirtOval });
            }

            foreach (string type in orphanTypes.Where(type => !Dashboards.Any(d => d.RaceTypes != null && d.RaceTypes.Contains(type))))
            {
                bool oval = type == Disciplines.Oval || type == Disciplines.DirtOval;
                DashboardSettings target = oval ? Ensure(DesignTemplates.Oval.Name, DashboardSettings.CreateOval) : Ensure(DesignTemplates.Road.Name, DashboardSettings.CreateRoad);
                target.RaceTypes.Add(type);
            }
            if (Dashboards.Count == 0) return;
            if (defaultRetired) DefaultDashboardId = (Dashboards.FirstOrDefault(d => d.Name == DesignTemplates.Road.Name) ?? Dashboards[0]).Id;
        }

        /// <summary>
        /// Schema 6: the fixed DV-5 and LB-10 settings become set-up devices with their
        /// LED groups, so they work exactly as before and can be renamed or re-mapped.
        /// </summary>
        private void DevicesFromFixedSettings()
        {
            if (General.Devices.Count > 0) return;
            General.Devices.Add(KnownDevices.Create(KnownDevices.Dv5, General.LedPort, KnownDevices.Dv5.LedCount, true));
            General.Devices.Add(KnownDevices.Create(KnownDevices.Lb10, General.BrowPort, Math.Max(1, General.BrowLedCount), General.BrowEnabled));
            General.LedPort = null;
            General.BrowPort = null;
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
            DashboardSettings factory = dashboard.RaceTypes != null && (dashboard.RaceTypes.Contains(Disciplines.Oval) || dashboard.RaceTypes.Contains(Disciplines.DirtOval)) ? DashboardSettings.CreateOval() : DashboardSettings.CreateRoad();
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

        /// <summary>Schema 8: a dashboard still on the pre-layer light settings gets them as layers, once.</summary>
        private void ClassicLightsToLayers()
        {
            foreach (DashboardSettings dashboard in Dashboards)
                if (dashboard.Lights == null) dashboard.Lights = LightPresets.FromClassic(dashboard);
        }

        /// <summary>Schema 7: light layers named after one device ("Brow rev lights") take the generic name ("Light bar rev lights").</summary>
        private void RenameBrowLayers()
        {
            foreach (DashboardSettings dashboard in Dashboards)
                foreach (LightLayer layer in dashboard.Lights?.Layers ?? new List<LightLayer>())
                    if (layer.Name != null && layer.Name.StartsWith("Brow ", StringComparison.Ordinal)) layer.Name = "Light bar " + layer.Name.Substring(5);
        }
    }

    internal sealed class GeneralSettings
    {
        /// <summary>Every LED device that has been set up: the LEDs on a wheel screen, light bars, strips.</summary>
        public List<DeviceSettings> Devices { get; set; } = new List<DeviceSettings>();

        public int ScreenBrightness { get; set; } = 100;

        /// <summary>The wheel screen is sent only the parts of each frame that changed (much less to send). Off sends every frame whole.</summary>
        public bool SendChangesOnly { get; set; } = true;

        /// <summary>Frames a second while racing, at most (drawn only when something changed): 60, or 30 to use less of the PC.</summary>
        public int FrameRate { get; set; } = 60;

        /// <summary>The display (Windows' name, such as \\.\DISPLAY2) the running dashboard is shown on full screen, when PcOutput is FullScreen.</summary>
        public string MonitorDevice { get; set; }

        /// <summary>
        /// Where on the PC the running dashboard is also shown: Off, Window, Overlay (over the game) or FullScreen (on
        /// MonitorDevice). Empty in settings from before it existed, where a chosen display meant full screen.
        /// </summary>
        public string PcOutput { get; set; }

        /// <summary>PcOutput as it applies: what was chosen, or for older settings full screen when a display was.</summary>
        public string EffectivePcOutput() =>
            PcOutputs.Contains(PcOutput) ? PcOutput : string.IsNullOrEmpty(MonitorDevice) ? "Off" : "FullScreen";

        public static readonly string[] PcOutputs = { "Off", "Window", "Overlay", "FullScreen" };

        /// <summary>How opaque the dashboard is over the game, 30 to 100 percent.</summary>
        public int OverlayOpacity { get; set; } = 100;

        /// <summary>Over the game, the dashboard shows only while iRacing is running (and while it's being placed or previewed).</summary>
        public bool OverlayOnlyWhileRacing { get; set; } = true;

        /// <summary>Where in iRacing the dashboard shows (Show when, at the top). Hidden elsewhere: the wheel screen black, the PC window away.</summary>
        public bool ShowInCar { get; set; } = true;
        public bool ShowOutOfCar { get; set; } = true;
        public bool ShowInGarage { get; set; } = true;
        public bool ShowSpectating { get; set; } = true;

        /// <summary>Whether the dashboard shows at this point in iRacing.</summary>
        public bool ShowsAt(CarState state)
        {
            switch (state)
            {
                case CarState.OutOfCar: return ShowOutOfCar;
                case CarState.Garage: return ShowInGarage;
                case CarState.Spectating: return ShowSpectating;
                default: return ShowInCar;
            }
        }
        public string SpeedUnit { get; set; } = "MPH";
        public string FuelUnit { get; set; } = "Liters";
        /// <summary>"Celsius" or "Fahrenheit"; empty follows the speed unit (km/h gives Celsius), as before it could be chosen.</summary>
        public string TemperatureUnit { get; set; }
        /// <summary>The sidebar page the window opens on: Dashboards, Devices or Settings.</summary>
        public string StartPage { get; set; } = "Dashboards";
        /// <summary>Minimizing the window hides it to the notification area instead of the taskbar.</summary>
        public bool MinimizeToTray { get; set; }
        /// <summary>Asks GitHub for the latest release when the window opens. Off unless the user turns it on.</summary>
        public bool CheckForUpdates { get; set; }

        /// <summary>Extra laps of fuel counted into fuel to add (0, 0.5, 1 or 2): a margin for a slower or thirstier finish.</summary>
        public double FuelReserveLaps { get; set; }

        /// <summary>The screen size last chosen for a new dashboard when no wheel screen was found (0 until then).</summary>
        public int LastScreenWidth { get; set; }
        public int LastScreenHeight { get; set; }
        public bool AutoSwitchByDiscipline { get; set; } = true;
        public bool DiagnosticsEnabled { get; set; } = true;

        /// <summary>
        /// The Dashboards page shows the getting-started steps. Only a new install turns this on, so an upgrade never
        /// shows them; it goes off when the dashboard is first started or the card is dismissed.
        /// </summary>
        public bool ShowWelcome { get; set; }

        /// <summary>
        /// The version whose "What's new" card was last seen (major.minor). After an update the Dashboards page shows
        /// the card once; a new install starts at its own version, so it sees the welcome card instead.
        /// </summary>
        public string SeenVersion { get; set; } = "";

        /// <summary>
        /// Units for the region Windows is set to: mph, gallons and °F where those are the norm (the US), mph with
        /// liters and °C in the UK, and km/h, liters and °C everywhere else.
        /// </summary>
        public void UseRegionUnits(System.Globalization.RegionInfo region)
        {
            bool british = region.TwoLetterISORegionName == "GB";
            bool imperial = !region.IsMetric && !british;
            SpeedUnit = imperial || british ? "MPH" : "KPH";
            FuelUnit = imperial ? "Gallons" : "Liters";
            TemperatureUnit = imperial ? "Fahrenheit" : "Celsius";
        }

        // Before schema 6 the DV-5 and LB-10 were fixed settings. They are read once to
        // build Devices and then cleared; nothing else uses them.
        public string LedPort { get; set; }
        public bool BrowEnabled { get; set; } = true;
        public string BrowPort { get; set; }
        public int BrowLedCount { get; set; } = 20;
    }

    internal sealed class DashboardSettings
    {
        public string Id { get; set; } = "dashboard-new";
        public string Name { get; set; } = "Dashboard";
        public string Layout { get; set; } = Layouts.Custom;
        public List<string> RaceTypes { get; set; } = new List<string>();
        public string AccentColor { get; set; } = "#22D3EE";
        // Before schema 8: the light settings from before light layers. Read once to build Lights, then cleared.
        public LedBankSettings CenterLeds { get; set; }
        public SideLedSettings LeftLeds { get; set; }
        public SideLedSettings RightLeds { get; set; }
        public BrowLedSettings BrowLeds { get; set; }
        public AlertSettings Alerts { get; set; } = new AlertSettings();
        /// <summary>The dashboard's design: screens of widgets, drawn by <see cref="LayoutRenderer"/>.</summary>
        public DashLayout Design { get; set; }

        /// <summary>The screen size the dashboard is built for.</summary>
        public System.Drawing.Size ScreenSize() => Design != null
            ? new System.Drawing.Size(ScreenSizes.Clamp(Design.Width), ScreenSizes.Clamp(Design.Height))
            : new System.Drawing.Size(ScreenSizes.DefaultWidth, ScreenSizes.DefaultHeight);
        /// <summary>The dashboard's lights: layers of effects on the LED devices.</summary>
        public LightProfile Lights { get; set; }

        /// <summary>A new dashboard started from a template.</summary>
        public static DashboardSettings CreateDesigned(DesignTemplate template, int width = ScreenSizes.DefaultWidth, int height = ScreenSizes.DefaultHeight)
        {
            DashboardSettings created = Create("dashboard-new", template.Name, template.Accent);
            created.Design = template.Create();
            // Templates are drawn for 854×480 and scaled to the screen the dashboard is for.
            created.Design.ScaleTo(width, height);
            created.Lights = template.OvalLights ? LightPresets.SpotterFocus() : LightPresets.Refined();
            return created;
        }

        public const string RoadId = "dashboard-roady", OvalId = "dashboard-roundy";

        /// <summary>The default road dashboard.</summary>
        public static DashboardSettings CreateRoad()
        {
            DashboardSettings halo = Create(RoadId, DesignTemplates.Road.Name, DesignTemplates.Road.Accent, Disciplines.Road, Disciplines.DirtRoad);
            halo.Design = DesignTemplates.Road.Create();
            halo.Lights = LightPresets.Refined();
            return halo;
        }

        /// <summary>The default oval dashboard.</summary>
        public static DashboardSettings CreateOval()
        {
            DashboardSettings oval = Create(OvalId, DesignTemplates.Oval.Name, DesignTemplates.Oval.Accent, Disciplines.Oval, Disciplines.DirtOval);
            oval.Design = DesignTemplates.Oval.Create();
            oval.Lights = LightPresets.SpotterFocus();
            return oval;
        }

        private static DashboardSettings Create(string id, string name, string accent, params string[] raceTypes) => new DashboardSettings
        {
            Id = id,
            Name = name,
            Layout = Layouts.Custom,
            AccentColor = accent,
            RaceTypes = raceTypes.ToList()
        };

        public void Normalize()
        {
            Layout = Layouts.Custom;
            if (string.IsNullOrWhiteSpace(Name)) Name = "Dashboard";
            if (RaceTypes == null) RaceTypes = new List<string>();
            if (Alerts == null) Alerts = new AlertSettings();
            if (Design == null) Design = DesignTemplates.Blank();
            Design.Normalize();
            if (Lights == null) Lights = LightPresets.Refined();
            Lights.Normalize();
            // The pre-layer light settings have been converted by now.
            CenterLeds = null;
            LeftLeds = RightLeds = null;
            BrowLeds = null;
        }

        [ScriptIgnore]
        public Color Accent => SettingsStore.ParseColor(AccentColor, Color.FromArgb(0, 155, 210));
    }

    // ------------------------------------------------------------------ before schema 8
    // The light settings from before light layers, kept only so old settings can be read and converted.

    internal static class LedFunctions
    {
        public const string BlindSpot = "Blind spot", Rpm = "RPM", Brake = "Brake pressure", Throttle = "Throttle", Disabled = "Disabled";
    }

    internal static class BrowFunctions
    {
        public const string Shift = "Shift lights", BlindSpot = "Blind spot", Flags = "Flags only", Pedals = "Brake and throttle",
            Brake = "Brake pressure", Throttle = "Throttle", Delta = "Lap delta", Fuel = "Fuel level", Off = "Off";
    }

    internal static class LedProfiles
    {
        public const string GreenYellowRed = "Green / Yellow / Red", BluePurpleRed = "Blue / Purple / Red", Single = "Single color";
    }

    internal class LedBankSettings
    {
        public string Function { get; set; } = "RPM";
        public string Pattern { get; set; } = "Edges to center";
        public int Brightness { get; set; } = 55;
        public string ColorProfile { get; set; } = LedProfiles.GreenYellowRed;
        public string LowColor { get; set; } = "#00B85A";
        public string MidColor { get; set; } = "#FFB800";
        public string HighColor { get; set; } = "#FF202F";
    }

    internal sealed class SideLedSettings : LedBankSettings { }

    internal sealed class BrowLedSettings : LedBankSettings
    {
        /// <summary>When on, any racing flag took over the light bar.</summary>
        public bool FlagOverride { get; set; } = true;

        public static BrowLedSettings CreateDefault(string function) => new BrowLedSettings
        {
            Function = function, Pattern = "Edges to center", Brightness = 60, ColorProfile = LedProfiles.GreenYellowRed, FlagOverride = true
        };
    }

    internal sealed class AlertSettings
    {
        public int BlinkCount { get; set; } = 6;
        public int BlinkMilliseconds { get; set; } = 150;
        public int HoldMilliseconds { get; set; } = 0;
    }

    internal static class SettingsStore
    {
        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        /// <summary>Earlier copies of settings.json, newest first: the save before last, then the timed backups.</summary>
        public static readonly string BackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SettingsBackups");
        private const int BackupsKept = 12;
        private static readonly TimeSpan BackupEvery = TimeSpan.FromMinutes(10);

        public static AppSettings Load()
        {
            if (TryLoad(out AppSettings loaded)) return loaded;
            // Unreadable even after retrying: keep the file (it may be fixable by hand) and go back to the newest
            // backup that reads, so a damaged file costs the last few minutes of changes rather than everything.
            try
            {
                if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch { }
            foreach (string backup in Backups())
            {
                try
                {
                    var restored = Json.Deserialize<AppSettings>(File.ReadAllText(backup));
                    if (restored == null) continue;
                    restored.EnsureBuiltInDashboards();
                    try { Save(restored); } catch { }
                    return restored;
                }
                catch { }
            }
            var defaults = AppSettings.CreateDefault();
            try { if (!File.Exists(FilePath)) Save(defaults); } catch { }
            return defaults;
        }

        /// <summary>
        /// Reads settings.json, retrying for a moment: another process may be swapping in a new copy at that
        /// instant. True with defaults when there's no file yet; false when it exists but couldn't be read.
        /// </summary>
        public static bool TryLoad(out AppSettings settings)
        {
            settings = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        settings = AppSettings.CreateDefault();
                        try { Save(settings); } catch { }
                        return true;
                    }
                    var loaded = Json.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                    if (loaded == null) return false;
                    int schema = loaded.SchemaVersion;
                    loaded.EnsureBuiltInDashboards();
                    // Persist one-time upgrades so every process sees the same lineup.
                    if (loaded.SchemaVersion != schema) try { Save(loaded); } catch { }
                    settings = loaded;
                    return true;
                }
                catch (IOException) { System.Threading.Thread.Sleep(40); }
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(40); }
                catch (Exception) { return false; }
            }
            return false;
        }

        /// <summary>
        /// Writes settings.json whole or not at all: a new copy is written beside it and swapped in, the old
        /// one becoming settings.json.bak. Every ten minutes of saving, a dated copy also goes into
        /// SettingsBackups (the last twelve are kept). Each process writes its own temporary file, so the
        /// settings window and the dashboard can never write into the same one.
        /// </summary>
        public static void Save(AppSettings settings)
        {
            string temporary = FilePath + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllText(temporary, Indent(Json.Serialize(settings)));
            if (File.Exists(FilePath))
            {
                TimedBackup();
                File.Replace(temporary, FilePath, FilePath + ".bak");
            }
            else File.Move(temporary, FilePath);
        }

        private static void TimedBackup()
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                string[] dated = Directory.GetFiles(BackupFolder, "settings-*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToArray();
                if (dated.Length > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(dated[0]) < BackupEvery) return;
                File.Copy(FilePath, Path.Combine(BackupFolder, "settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                foreach (string old in dated.Skip(BackupsKept - 1)) File.Delete(old);
            }
            catch { }
        }

        /// <summary>The copies to fall back on, newest first.</summary>
        public static IEnumerable<string> Backups()
        {
            if (File.Exists(FilePath + ".bak")) yield return FilePath + ".bak";
            string[] dated;
            try { dated = Directory.Exists(BackupFolder) ? Directory.GetFiles(BackupFolder, "settings-*.json") : new string[0]; }
            catch { dated = new string[0]; }
            foreach (string file in dated.OrderByDescending(f => f, StringComparer.Ordinal)) yield return file;
        }

        public static DateTime LastWriteUtc()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        public static T Clone<T>(T value) => Json.Deserialize<T>(Json.Serialize(value));

        public static string ToJson(object value) => Json.Serialize(value);

        // Designs can embed images, so allow large settings files.
        private static JavaScriptSerializer Json => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        /// <summary>Lays out compact JSON with two-space indents so settings.json stays readable.</summary>
        private static string Indent(string json)
        {
            var output = new System.Text.StringBuilder(json.Length * 2);
            int depth = 0;
            bool quoted = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    output.Append(c);
                    if (c == '\\' && i + 1 < json.Length) output.Append(json[++i]);
                    else if (c == '"') quoted = false;
                    continue;
                }
                switch (c)
                {
                    case '"': quoted = true; output.Append(c); break;
                    case '{':
                    case '[':
                        output.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { output.Append(json[++i]); break; }
                        output.AppendLine().Append(' ', ++depth * 2);
                        break;
                    case '}':
                    case ']': output.AppendLine().Append(' ', --depth * 2).Append(c); break;
                    case ',': output.Append(c).AppendLine().Append(' ', depth * 2); break;
                    case ':': output.Append(": "); break;
                    default: output.Append(c); break;
                }
            }
            return output.ToString();
        }

        public static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
