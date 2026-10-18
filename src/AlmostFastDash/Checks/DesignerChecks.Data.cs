using System;
using System.Collections.Generic;
using System.Linq;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow
    {
        /// <summary>Checks: formulas.</summary>
        private void CheckFormulas()
        {
            EvalScope scope = _scope;
            (string Formula, string Expected)[] formulas =
            {
                ("1 + 2 * 3", "7"), ("(1 + 2) * 3", "9"), ("10 / 4", "2.5"), ("7 % 3", "1"), ("\"a\" + 5", "a5"),
                ("Gear", "4"), ("gear(-1)", "R"), ("GearText", "4"), ("laptime(87.932)", "1:27.932"), ("signed(0.456, 2)", "+0.46"),
                ("signed(-0.1, 1)", "-0.1"), ("Speed > 100 ? \"fast\" : \"slow\"", "fast"), ("clamp(5, 0, 3)", "3"),
                ("norm(5, 0, 10)", "0.5"),
                ("steps(3, \"a\", 2, \"b\", 5, \"c\")", "b"), ("Gear = 4", "true"), ("Gear == 4 && !OnPitRoad", "true"),
                ("not OnPitRoad and Gear >= 4", "true"), ("Position > 0 ? \"P\" + Position : \"P--\"", "P4"), ("round(2.345, 2)", "2.35"),
                ("format(1234.5, \"0.0\")", "1234.5"), ("pad(7, 2)", "07"), ("upper(\"abc\")", "ABC"), ("$accent", "#2DD4BF"),
                ("alpha($accent, 0.5)", "#2DD4BF"), ("mix(#000000, #FFFFFF, 0.5)", "#7F7F7F"), ("nz(0, 9)", "9"), ("min(3, 1, 2)", "1"),
                ("max(3, 1, 2)", "3"), ("abs(-2)", "2"), ("if(Gear > 3, \"hi\", \"lo\")", "hi"), ("FuelLapsLeft > 0", "true"),
                ("clock(75)", "01:15")
            };
            foreach (var f in formulas)
            {
                Expr.Compiled compiled = Expr.Compile(f.Formula);
                string actual = compiled.Error ?? Expr.Text(compiled.Run(scope));
                Check("formula " + f.Formula, actual == f.Expected, "got " + actual);
            }
            Check("formula error: incomplete", Expr.Compile("Speed +").Error != null);
            Check("formula error: unknown field", Expr.Compile("Sped * 2").Error?.Contains("Sped") == true);
            Check("formula error: unknown function", Expr.Compile("bogus(1)").Error != null);
            Check("formula error: unclosed text", Expr.Compile("\"abc").Error != null);
            Check("formula: fuel() follows units", Expr.Text(Expr.Evaluate("fuel(6.4)", scope)) == (_units.Gallons ? "6.40" : "6.4"));
        }

        /// <summary>Checks: quick values and race data.</summary>
        private void CheckLiveDataAndRaceData()
        {
            EvalScope scope = _scope;
            var quickErrors = new List<string>();
            foreach (DesignBlock quick in QuickValues.All())
            {
                var layout = DesignTemplates.Blank();
                foreach (DashWidget w in quick.Create()) { w.X += 100; w.Y += 60; layout.Screens[0].Widgets.Add(w); }
                using (var renderer = new LayoutRenderer(854, 480, new DashboardSettings { Layout = Layouts.Custom, Design = layout },
                    _units))
                    foreach (TelemetrySnapshot snapshot in new[] { IRacingTelemetrySource.CreatePreviewSnapshot(),
                        TelemetrySnapshot.Disconnected })
                        using (renderer.Render(snapshot, false, 4, 1, layout.Screens[0]))
                            quickErrors.AddRange(renderer.Errors.Values.Select(e => quick.Name + ": " + e));
                if (!QuickValues.Folders.Contains(quick.Folder)) quickErrors.Add(quick.Name + " is in an unknown folder");
            }
            Check("every quick value draws (" + QuickValues.All().Count() + ")", quickErrors.Count == 0,
                string.Join("; ", quickErrors.Take(3)));
            string Eval(string formula) => Expr.Text(Expr.Evaluate(formula, scope));
            Check("leaderboard formulas",
                Eval("standing(1, \"Name\")") == "L. HARTMANN" && Eval("standing(Position - 1, \"Number\")") == "05" &&
                Eval("standing(99, \"Exists\")") == "false" && Eval("standing(8, \"GapText\")") == "+16.35" &&
                Eval("standing(17, \"GapText\")") == "+1 LAP" &&
                Eval("standing(22, \"GapText\")") == "+2 LAPS" && Eval("GapToLeader") == "+5.94", Eval("standing(17, \"GapText\")"));
            // Leader just over the line and a car not there yet: same lap. A whole lap behind on track: lapped.
            Check("a car is a lap down only when a whole lap behind on track, not while it's yet to cross the line",
                IRacingTelemetrySource.LapsDown(18, 0.01f, 17, 0.95f) == 0 && IRacingTelemetrySource.LapsDown(18, 0.05f, 17, 0.10f) == 0 &&
                IRacingTelemetrySource.LapsDown(18, 0.50f, 17, 0.40f) == 1 && IRacingTelemetrySource.LapsDown(18, 0.60f, 16, 0.50f) == 2 &&
                IRacingTelemetrySource.LapsDown(18, -1, 17, 0.95f) == 1);
            Check("sector formulas",
                Eval("seconds(sectortime(1))") == "29.904" && Eval("seconds(sectortime(2))") == "--.---" &&
                Eval("laptime(IdealLap)") == "1:27.373" && Eval("Sector") == "2");
            Check("engine, tyre and system values",
                Eval("format(OilTemp, \"0\")") == (_units.Celsius ? "104" : "219") &&
                Eval("format(tyre(\"RF\", \"Wear\"), \"0\")") == "88" &&
                Eval("DrsText") == "OFF" && Expr.Num(Expr.Evaluate("CpuLoad", scope)) >= 0 &&
                Expr.Num(Expr.Evaluate("MemoryLoad", scope)) > 0,
                Eval("format(OilTemp, \"0\")"));
            Check("unknown engine values show as --",
                Expr.Text(Expr.Evaluate("format(OilTemp, \"0\")",
                new EvalScope { T = TelemetrySnapshot.Disconnected, Units = _units, Theme = _layout.Theme })) == "--");
        }

        /// <summary>Checks: trustworthy numbers: gaps, laps to go, fuel, settings.</summary>
        private void CheckTrustworthyNumbers()
        {
            {
                // A 90-second lap: the car ahead is 3% of a lap on, but 3.5 s away through the slow part of the track.
                float[] estimates = { 30f, 33.5f, 88f, 1f };
                double slow = IRacingTelemetrySource.TrackGap(estimates, 0, 1, 0.03, 90);
                double acrossLine = IRacingTelemetrySource.TrackGap(estimates, 2, 3, 0.03, 90);
                double missing = IRacingTelemetrySource.TrackGap(new[] { 30f, -1f }, 0, 1, 0.03, 90);
                double nonsense = IRacingTelemetrySource.TrackGap(new[] { 30f, 70f }, 0, 1, 0.03, 90);
                Check("gaps follow iRacing's per-car time estimates, across the line too, and fall back when they can't be trusted",
                    Math.Abs(slow - 3.5) < 0.001 && Math.Abs(acrossLine - 3) < 0.001 && Math.Abs(missing - 2.7) < 0.001 &&
                    Math.Abs(nonsense - 2.7) < 0.001,
                    slow + " " + acrossLine + " " + missing + " " + nonsense);

                TelemetrySnapshot race = IRacingTelemetrySource.CreatePreviewSnapshot();
                race.PlayerCarIdx = 0;
                race.CarLapDist = new[] { 0.4f };
                race.FuelPerLapAverage = 3;
                race.FuelLapsMeasured = 5;
                double expectLaps = Math.Max(0, race.TotalLaps - race.LapsCompleted - 0.4);
                double expectAdd = Math.Max(0, expectLaps * 3 - race.FuelLiters),
                    expectMargin = Math.Max(0, (expectLaps + 1) * 3 - race.FuelLiters);
                Check("fuel to add counts only the rest of this lap, and a safety margin adds whole laps",
                    race.ProgressMode == SessionProgressMode.LapRace && Math.Abs(race.LapsToGo - expectLaps) < 1e-5 && expectAdd > 0 &&
                    Math.Abs(race.FuelToFinish - expectAdd) < 1e-4 && Math.Abs(race.FuelToFinishWith(1) - expectMargin) < 1e-4,
                    race.LapsToGo + " laps, add " + race.FuelToFinish + ", with a lap's margin " + race.FuelToFinishWith(1));

                var timed = new TelemetrySnapshot(true, 4, 120, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 0.5,
                    SessionProgressMode.TimedRace, 250, 1800, 12, 0, 4, 2, 20, 10, 0, "A", -1, "B", 1, 10, false, 100, 100, 100,
                    new bool[0], 0, 6, 6000);
                timed.PlayerCarIdx = 0;
                timed.CarLapDist = new[] { 0.5f };
                // 250 s left on 100 s laps, half way round: the rest of this lap (50 s), then two more to pass zero; that one is the last.
                Check("a timed race runs to the end of the lap in progress when the clock runs out", Math.Abs(timed.LapsToGo - 2.5) < 1e-9,
                    timed.LapsToGo.ToString("0.00"));

                var oldFuel = new AppSettings { SchemaVersion = 12 };
                DashboardSettings withOld = DashboardSettings.CreateDesigned(DesignTemplates.Find("spotter"));
                DashWidget refuel = withOld.Design.AllWidgets().First(w => w.Get("value", "") == DesignTemplates.FuelToAdd);
                refuel.Set("value", AppSettings.OldFuelToAdd);
                refuel.Set("label", "FUEL TO END");
                refuel.Props.Remove("unit");
                oldFuel.Dashboards.Add(withOld);
                oldFuel.EnsureBuiltInDashboards();
                Check("saved designs' fuel to add gains its unit and a clear label", refuel.Get("value") == DesignTemplates.FuelToAdd &&
                    refuel.Get("unit") == DesignTemplates.FuelToAddUnit && refuel.Get("label") == DesignTemplates.FuelToAddLabel);

                // A new install sees the welcome card, not What's new; an upgrade (no version seen yet) sees What's new.
                AppSettings fresh = AppSettings.CreateDefault();
                Check("a new install starts with the welcome card, an update with What's new",
                    fresh.General.ShowWelcome && fresh.General.SeenVersion == WhatsNew.Version &&
                    new GeneralSettings().SeenVersion != WhatsNew.Version && WhatsNew.Highlights.Length > 0,
                    fresh.General.SeenVersion + " / " + WhatsNew.Version);

                // Schema 20: the shipped pit-lane label in US spelling; a label of the user's own stays.
                var british = new AppSettings { SchemaVersion = 19 };
                DashboardSettings pitPage = DashboardSettings.CreateDesigned(DesignTemplates.Find("apex"));
                DashWidget tiresLabel = pitPage.Design.AllWidgets().First(w => w.Get("text") == "TIRES · LAST STOP");
                tiresLabel.Set("text", "TYRES · LAST STOP");
                tiresLabel.Name = "Tyres label";
                DashWidget own = WidgetCatalog.Create("text", 0, 0);
                own.Set("text", "MY TYRES");
                pitPage.Design.Screens[0].Widgets.Add(own);
                british.Dashboards.Add(pitPage);
                british.EnsureBuiltInDashboards();
                Check("saved designs' pit-lane label reads TIRES, and the user's own words stay",
                    tiresLabel.Get("text") == "TIRES · LAST STOP" &&
                    tiresLabel.Name == "Tires label" && own.Get("text") == "MY TYRES", tiresLabel.Get("text") + " / " + tiresLabel.Name);

                // A damaged settings file comes back from the newest backup that reads.
                AppSettings current = SettingsStore.Load();
                SettingsStore.Save(current);
                SettingsStore.Save(current);
                string good = System.IO.File.ReadAllText(SettingsStore.FilePath);
                System.IO.File.WriteAllText(SettingsStore.FilePath, "{ this is not settings");
                AppSettings recovered = SettingsStore.Load();
                bool restoredFile = System.IO.File.ReadAllText(SettingsStore.FilePath).Length > 100;
                Check("a damaged settings file is recovered from its backup",
                    recovered != null && recovered.Dashboards.Count == current.Dashboards.Count && restoredFile &&
                    System.IO.File.Exists(SettingsStore.FilePath + ".bak") &&
                    System.IO.Directory.GetFiles(SettingsStore.BackupFolder, "settings-*.json").Length > 0);
                System.IO.File.WriteAllText(SettingsStore.FilePath, good);
                foreach (string unreadable in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(SettingsStore.FilePath),
                    "settings.json.unreadable-*"))
                    System.IO.File.Delete(unreadable);

                // A new install's units match its region, and only a new install shows the welcome card.
                GeneralSettings UnitsFor(string region)
                {
                    var general = new GeneralSettings();
                    general.UseRegionUnits(new System.Globalization.RegionInfo(region));
                    return general;
                }
                GeneralSettings us = UnitsFor("US"), uk = UnitsFor("GB"), de = UnitsFor("DE");
                Check("a new install's units match its region (US, UK, elsewhere)",
                    us.SpeedUnit == "MPH" && us.FuelUnit == "Gallons" && us.TemperatureUnit == "Fahrenheit" &&
                    uk.SpeedUnit == "MPH" && uk.FuelUnit == "Liters" && uk.TemperatureUnit == "Celsius" &&
                    de.SpeedUnit == "KPH" && de.FuelUnit == "Liters" && de.TemperatureUnit == "Celsius");
                // Settings saved before the welcome card don't have the field, so they read as a fresh GeneralSettings.
                Check("only a new install shows the welcome card",
                    AppSettings.CreateDefault().General.ShowWelcome && !new GeneralSettings().ShowWelcome);
            }
        }

        /// <summary>Checks: radar and traffic.</summary>
        private void CheckRadarAndTraffic()
        {
            {
                const double Track = 1000;
                float At(double meters) => (float)(0.5 + meters / Track);

                var radar = new RadarTracker();
                radar.Update(new[] { 0.5f, At(10), At(0.5), -1f }, new[] { false, false, true, false }, 0, Track, 1, 100);
                Check("radar: a car ten metres ahead is a car length clear, and a car in the pits isn't shown",
                    radar.Cars.Length == 1 && Math.Abs(radar.Cars[0].Meters - 10) < 0.05 &&
                    Math.Abs(RadarTracker.Lengths(radar.AheadMeters) - 1) < 0.02,
                    radar.Cars.Length + " cars, ahead " + radar.AheadMeters.ToString("0.00"));

                var wrap = new RadarTracker();
                wrap.Update(new[] { 0.995f, 0.004f }, null, 0, Track, 1, 0);
                Check("radar: a car just over the line is still just ahead",
                    wrap.Cars.Length == 1 && Math.Abs(wrap.Cars[0].Meters - 9) < 0.1);

                var sides = new RadarTracker();
                float[] three = { 0.5f, At(1.5), At(-2), At(30) };
                sides.Update(three, null, 0, Track, 2, 0);
                RadarCar first = sides.Cars.First(x => x.CarIdx == 1);
                sides.Update(three, null, 0, Track, 4, 0.05);
                RadarCar kept = sides.Cars.First(x => x.CarIdx == 1), second = sides.Cars.First(x => x.CarIdx == 2);
                Check("radar: a car left goes to the nearest car; a second alongside goes right and the first stays left",
                    first.Lane == -1 && first.Alongside && kept.Lane == -1 && second.Lane == 1 && second.Alongside &&
                    !sides.Cars.First(x => x.CarIdx == 3).Alongside);

                var ghost = new RadarTracker();
                ghost.Update(new[] { 0.5f, At(40) }, null, 0, Track, 3, 0);
                Check("radar: a car the spotter reports that positions can't place still shows alongside",
                    ghost.Cars.Any(x => x.CarIdx < 0 && x.Lane == 1 && x.Alongside));

                // A car that was alongside on the left and moves a car length and a half ahead: in your bubble it keeps
                // its lane for a good while (ten seconds on), then drifts into line once nothing says otherwise.
                var drift = new RadarTracker();
                drift.Update(new[] { 0.5f, At(1.5) }, null, 0, Track, 2, 0);
                for (int i = 1; i <= 200; i++) drift.Update(new[] { 0.5f, At(8) }, null, 0, Track, 1, i * 0.05);
                float held = drift.Cars[0].Lane;
                for (int i = 201; i <= 500; i++) drift.Update(new[] { 0.5f, At(8) }, null, 0, Track, 1, i * 0.05);
                Check("radar: a car in your bubble keeps its lane, then drifts into line ahead",
                    held == -1 && drift.Cars[0].Lane == 0 && Math.Abs(drift.AheadMeters - 8) < 0.05,
                    "after 10 s " + held + ", after 25 s " + drift.Cars[0].Lane + ", ahead " + drift.AheadMeters.ToString("0.00"));

                // Two by two: rows of two cars ahead and behind. Each row is spread across two lanes, the other line on the side
                // the spotter sees a car beside you (here, your row partner on the left).
                var formation = new RadarTracker();
                float[] rows = { 0.5f, At(-0.4), At(9.0), At(9.6), At(18.2), At(18.5), At(-9.0), At(-8.7) };
                for (int i = 0; i <= 30; i++) formation.Update(rows, null, 0, Track, 2, i * 0.05);
                int Lane(RadarTracker r, int car) => (int)Math.Round(r.Cars.First(x => x.CarIdx == car).Lane);
                bool pairsSplit = new[] { Tuple.Create(2, 3), Tuple.Create(4, 5),
                    Tuple.Create(6, 7) }.All(p => Lane(formation, p.Item1) != Lane(formation, p.Item2));
                bool onTheLeft = new[] { 2, 3, 4, 5, 6, 7 }.All(car => Lane(formation, car) == 0 || Lane(formation, car) == -1);
                Check("radar: a two-by-two formation shows two wide, with the other line on the side the spotter saw",
                    pairsSplit && onTheLeft && Lane(formation, 1) == -1 && Math.Abs(formation.AheadMeters - 9.0) < 0.7,
                    string.Join(" ",
                    formation.Cars.Select(x => "#" + x.CarIdx + "@" + x.Meters.ToString("0.0") + ":" + x.Lane.ToString("0.0"))));

                // With nothing beside you to say which side the other line is on, a row of two straddles your lane.
                var unknown = new RadarTracker();
                for (int i = 0; i <= 30; i++) unknown.Update(new[] { 0.5f, At(9.0), At(9.4) }, null, 0, Track, 1, i * 0.05);
                float l1 = unknown.Cars.First(x => x.CarIdx == 1).Lane, l2 = unknown.Cars.First(x => x.CarIdx == 2).Lane;
                Check("radar: a row of two with no side known straddles your lane",
                    Math.Abs(Math.Abs(l1) - 0.5f) < 0.01f && Math.Abs(l1 + l2) < 0.01f, l1 + " " + l2);

                // No template has text below the readable floor.
                var small = new List<string>();
                foreach (DesignTemplate template in DesignTemplates.All)
                    foreach (DashWidget w in template.Create().AllWidgets())
                        foreach (string key in new[] { "size", "labelSize", "valueSize", "unitSize", "numberSize" })
                        {
                            string raw = w.Get(key, null);
                            if (raw == null || raw.StartsWith("=") || w.Type == "shape" || w.Type == "gear" || w.Type == "radar" ||
                                w.Type == "laphistory") continue;
                            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v) &&
                                v > 0 && v < ReadableText.Floor)
                                small.Add(template.Name + "/" + w.Name + "." + key + "=" + raw);
                        }
                Check("every template's text is at least " + ReadableText.Floor + " px", small.Count == 0,
                    string.Join(", ", small.Take(6)));

                // Every piece of text on every template, as actually drawn, is big enough and stands out from what's behind it.
                foreach (DesignTemplate template in DesignTemplates.All)
                {
                    List<DashboardAudit.Finding> findings = DashboardAudit.Run(DashboardSettings.CreateDesigned(template), _units);
                    Check("readable at a glance: " + template.Name, findings.Count == 0, string.Join("; ", findings.Take(4)));
                }

                // Cars in single file stay in line.
                var single = new RadarTracker();
                for (int i = 0; i <= 30; i++) single.Update(new[] { 0.5f, At(6), At(12), At(-6) }, null, 0, Track, 1, i * 0.05);
                Check("radar: single file stays in line", single.Cars.All(x => x.Lane == 0));

                var closing = new RadarTracker();
                for (int i = 0; i <= 120; i++) closing.Update(new[] { 0.5f, At(30 - i * 2.0 / 60) }, null, 0, Track, 1, i / 60.0);
                Check("radar: a gap shrinking two metres a second closes at about 2 m/s", Math.Abs(closing.AheadClosing - 2) < 0.3,
                    closing.AheadClosing.ToString("0.00"));

                // The radar redraws as a car moves, and not otherwise.
                var radarLayout = DesignTemplates.Blank();
                radarLayout.Screens[0].Widgets.Add(WidgetCatalog.Create("radar", 280, 20));
                radarLayout.FullScreenFlags = false;
                using (var radarRenderer = new LayoutRenderer(854, 480,
                    new DashboardSettings { Layout = Layouts.Custom, Design = radarLayout }, _units) { FixedNow = TestNow, FlagClock = 0 })
                {
                    TelemetrySnapshot still = IRacingTelemetrySource.CreatePreviewSnapshot(),
                        moved = IRacingTelemetrySource.CreatePreviewSnapshot();
                    moved.Radar[0].Meters += 1;
                    int a = radarRenderer.StateHash(still, false, -1),
                        same = radarRenderer.StateHash(IRacingTelemetrySource.CreatePreviewSnapshot(), false, -1),
                        b = radarRenderer.StateHash(moved, false, -1);
                    Check("radar: redraws when a car moves, and only then", a == same && a != b);
                }

                // A prototype 2.5 seconds behind a GT car is a faster class closing.
                TelemetrySnapshot traffic = IRacingTelemetrySource.CreatePreviewSnapshot();
                traffic.Standings = traffic.Standings.Concat(new[] { new StandingsEntry { CarIdx = traffic.CarLapDist.Length,
                    Position = traffic.Standings.Length + 1, ClassName = "LMP2", ClassColor = System.Drawing.Color.RoyalBlue,
                    BestLap = 80.1, CarNumber = "30" } })
                    .ToArray();
                traffic.CarLapDist = traffic.CarLapDist.Concat(new[] { (float)(0.455 - 2.5 / traffic.ReferenceLapSeconds) }).ToArray();
                double faster = RadarPainters.FasterClassBehind(traffic, out string fasterClass);
                Check("traffic: a faster class 2.5 s behind is flagged", Math.Abs(faster - 2.5) < 0.1 && fasterClass == "LMP2",
                    faster.ToString("0.00") + " " + fasterClass);
                Check("traffic: one class alone has no faster class",
                    RadarPainters.FasterClassBehind(IRacingTelemetrySource.CreatePreviewSnapshot(), out _) < 0);
            }
        }

        /// <summary>Checks: the formula reference.</summary>
        private void CheckFormulaReference()
        {
            {
                // It's made from the live lists, so it covers everything; these keep its own parts complete and working.
                List<string> noExample = Functions.All.Where(f => !FormulaReferenceWindow.Examples.ContainsKey(f.Name)).Select(f => f.Name)
                    .ToList();
                List<string> broken = FormulaReferenceWindow.Examples.Where(e => Expr.Compile(e.Value).Error != null)
                    .Select(e => e.Key + ": " + Expr.Compile(e.Value).Error).ToList();
                Check("formula reference: every function has a worked example, and every example works",
                    noExample.Count == 0 && broken.Count == 0,
                    string.Join("; ", noExample.Select(n => "no example for " + n).Concat(broken)));
                List<string> bare = DataCatalog.All.Where(f => string.IsNullOrWhiteSpace(f.Description)).Select(f => f.Name)
                    .Concat(Functions.All.Where(f => string.IsNullOrWhiteSpace(f.Description)).Select(f => f.Name + "()")).ToList();
                List<string> recipes = DataPicker.Recipes.Where(r => Expr.Compile(r.Formula).Error != null).Select(r => r.Name).ToList();
                Check("formula reference: every data field and function is described, and every recipe works",
                    bare.Count == 0 && recipes.Count == 0,
                    string.Join(", ", bare.Concat(recipes)));
            }
        }

        /// <summary>Checks: lap falloff and the flagships.</summary>
        private void CheckLapFalloffAndFlagships()
        {
            {
                double[] stint = { 88.2, 87.5, 87.6, 87.9 };
                Check("lap falloff is the last lap against the stint's best",
                    Math.Abs(Falloff.Total(stint) - 0.4) < 1e-9 && Falloff.Total(new[] { 88.0 }) < 0);
                Check("the falloff trend runs from the best lap on",
                    Math.Abs(Falloff.PerLap(stint) - 0.2) < 1e-9 && Falloff.PerLap(new[] { 88.0, 87.0 }) == 0, Falloff.PerLap(stint)
                    .ToString("0.000"));

                var older = new AppSettings { SchemaVersion = 8 };
                DashboardSettings mine = DashboardSettings.CreateDesigned(DesignTemplates.Find("apex"));
                mine.Id = "dashboard-mine";
                mine.RaceTypes = new List<string> { Disciplines.Road, Disciplines.Oval };
                older.Dashboards.Add(mine);
                older.DefaultDashboardId = mine.Id;
                older.EnsureBuiltInDashboards();
                Check("an upgrade adds the flagships for road and oval and keeps the user's dashboards",
                    older.Dashboards.Count == 3 && older.Dashboards[0].Id == DashboardSettings.RoadId &&
                    older.Dashboards[1].Id == DashboardSettings.OvalId &&
                    older.Dashboards.Contains(mine) && mine.RaceTypes.Count == 0 &&
                    older.ResolveDashboard(Disciplines.Oval).Id == DashboardSettings.OvalId &&
                    older.GetDefaultDashboard().Id == DashboardSettings.RoadId);
                older.EnsureBuiltInDashboards();
                Check("the flagships are added once", older.Dashboards.Count == 3);

                var firstRoundy = new AppSettings { SchemaVersion = 9 };
                DashboardSettings boxed = DashboardSettings.CreateOval(), edited = DashboardSettings.CreateRoad();
                boxed.Design.Screens[0].Widgets.Add(new DashWidget { Type = "panel", Name = "Around panel" });
                boxed.Design.Screens[0].Widgets.Add(new DashWidget { Type = "panel", Name = "Falloff panel" });
                firstRoundy.Dashboards.Add(edited);
                firstRoundy.Dashboards.Add(boxed);
                firstRoundy.EnsureBuiltInDashboards();
                var softRoady = new AppSettings { SchemaVersion = 10 };
                DashboardSettings soft = DashboardSettings.CreateRoad();
                foreach (DashWidget w in soft.Design.Screens.SelectMany(sc => sc.Widgets)
                    .Where(w => w.Type == "panel" && w.Get("style") == "Liquid")) w.Set("style", "Soft");
                softRoady.Dashboards.Add(soft);
                softRoady.EnsureBuiltInDashboards();
                Check("Roady's panels become Liquid glass once", soft.Design.Screens.SelectMany(sc => sc.Widgets)
                    .Count(w => w.Type == "panel" && w.Get("style") == "Liquid") >= 7
                    && !soft.Design.Screens.SelectMany(sc => sc.Widgets).Any(w => w.Get("style") == "Soft"));
                Check("the first Roundy takes the redesign",
                    !boxed.Design.Screens[0].Widgets.Any(w => w.Name == "Around panel") &&
                    boxed.Design.Screens[0].Widgets.Any(w => w.Name == "Rev dial"));
            }
        }
    }
}
