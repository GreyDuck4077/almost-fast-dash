using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Settings
{
    // Upgrading settings saved by earlier versions: EnsureBuiltInDashboards runs each step a saved
    // schema is missing, in order, so any old settings.json comes up to date when it's loaded.
    internal sealed partial class AppSettings
    {
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
                if (SchemaVersion > 0 && SchemaVersion < 21) Tributes();
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
                        if (!restyle.Contains(widget.Name) || Fresh(widget.Name) is null ||
                            Fresh(widget.Name).Type != widget.Type) continue;
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
            Polish(DashboardSettings.RoadId, DesignTemplates.Road, new[] { "Rev sweep", "Gear", "Rule", "Pit speed ring" },
                new[] { "Gear light", "Rev sweep" }, new string[0]);
            Polish(DashboardSettings.OvalId, DesignTemplates.Oval, new[] { "Rev dial", "Pit speed ring" },
                new[] { "Dial face", "Rev dial" }, new[] { "Rpm unit" });
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
                            w.Set("radius", (w.H <= 32 ? Math.Round(w.H / 2) : w.H < 100 ? 16 : 18)
                                .ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                        if (w.Type == "arc" && w.Name.StartsWith("Rev lights"))
                        {
                            w.Set("rim", "#243041");
                            w.Set("rimWidth", "1");
                        }
                        if (w.Type == "arc" && w.Name == "Pit speed")
                            foreach (var p in new[] { "zones", "true", "rim", "border", "rimWidth", "1", "needle", "Marker",
                                "needleColor", "#FFFFFF", "needleLength", "130", "needleWidth", "3" }
                                .Select((v, i) => new { v, i }).GroupBy(x => x.i / 2, x => x.v).Select(g => g.ToArray()))
                                w.Set(p[0], p[1]);
                    }
                    // The hairlines across the top stepped down with a diagonal kink to meet the cut corners: now one straight line each
                    // side.
                    screen.Widgets.RemoveAll(w => w.Type == "shape" &&
                        (w.Name == "Hairline left" || w.Name == "Hairline right" || w.Name == "Hairline left angle" ||
                        w.Name == "Hairline right angle"));
                    DashWidget left = screen.Widgets.FirstOrDefault(w => w.Name == "Hairline left base"),
                        right = screen.Widgets.FirstOrDefault(w => w.Name == "Hairline right base");
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
                if (widgets == null ||
                    !new[] { "Rev ring", "Fuel ring", "Fuel pct", "Divider" }.All(n => widgets.Any(w => w.Name == n))) continue;
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
            var captions = new HashSet<string> { "Ahead lengths caption", "Behind lengths caption", "Fuel laps caption", "Falloff caption",
                "Traffic label" };
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
        private void WiderRadar() => Refresh("oval",
            widgets => widgets.Any(w => w.Type == "radar" && w.Name == "Radar") && widgets.Any(w => w.Name == "Ahead panel"));

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
            var names = new Dictionary<string,
                string> { ["Tyres panel"] = "Tires panel", ["Tyres label"] = "Tires label", ["Tyre box"] = "Tire box",
                ["Tyres"] = "Tires" };
            foreach (DashWidget widget in Dashboards.Where(d => d.Design?.Screens != null).SelectMany(d => d.Design.AllWidgets()))
            {
                if (widget.Type == "text" && widget.Get("text") == "TYRES · LAST STOP") widget.Set("text", "TIRES · LAST STOP");
                if (widget.Name != null && names.TryGetValue(widget.Name, out string name)) widget.Name = name;
            }
        }

        /// <summary>
        /// Schema 21: Roundy becomes Rowdy, for Kyle Busch, and Oval becomes Intimidator, for Dale Earnhardt; a name of the
        /// user's own stays. Rowdy's speed readout learns its easter egg: tap it eight times (TributeEight).
        /// </summary>
        private void Tributes()
        {
            foreach (DashboardSettings dashboard in Dashboards)
            {
                bool rowdy = dashboard.Name == "Roundy";
                if (rowdy) dashboard.Name = DesignTemplates.Find("roundy").Name;
                if (dashboard.Id == "dashboard-oval" && dashboard.Name == "Oval") dashboard.Name = DesignTemplates.Find("oval").Name;
                if (!(rowdy || dashboard.Id == DashboardSettings.OvalId) || dashboard.Design?.Screens == null) continue;
                foreach (DashWidget speed in dashboard.Design.AllWidgets().Where(w => w.Type == "text" && w.Name == "Speed" &&
                    (w.Get("text") ?? "").Contains("Speed")))
                    speed.Set("tribute", TributeEight.Mark);
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
            if (roundy?.Screens != null) TouchViews.Rowdy(roundy);
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
                Ensure(DesignTemplates.Oval.Name,
                    DashboardSettings.CreateOval).RaceTypes.AddRange(new[] { Disciplines.Oval, Disciplines.DirtOval });
            }

            foreach (string type in orphanTypes.Where(type => !Dashboards.Any(d => d.RaceTypes != null && d.RaceTypes.Contains(type))))
            {
                bool oval = type == Disciplines.Oval || type == Disciplines.DirtOval;
                DashboardSettings target = oval ? Ensure(DesignTemplates.Oval.Name, DashboardSettings.CreateOval)
                    : Ensure(DesignTemplates.Road.Name, DashboardSettings.CreateRoad);
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
            General.Devices.Add(KnownDevices.Create(KnownDevices.Lb10, General.BrowPort, Math.Max(1, General.BrowLedCount),
                General.BrowEnabled));
            General.LedPort = null;
            General.BrowPort = null;
        }

        /// <summary>Schema 8: a dashboard still on the pre-layer light settings gets them as layers, once.</summary>
        private void ClassicLightsToLayers()
        {
            foreach (DashboardSettings dashboard in Dashboards)
                if (dashboard.Lights == null) dashboard.Lights = LightPresets.FromClassic(dashboard);
        }

        /// <summary>Schema 7: light layers named after one device ("Brow rev lights") take the generic name ("Light bar rev
        /// lights").</summary>
        private void RenameBrowLayers()
        {
            foreach (DashboardSettings dashboard in Dashboards)
                foreach (LightLayer layer in dashboard.Lights?.Layers ?? new List<LightLayer>())
                    if (layer.Name != null && layer.Name.StartsWith("Brow ", StringComparison.Ordinal)) layer.Name = "Light bar " +
                        layer.Name.Substring(5);
        }
    }
}
