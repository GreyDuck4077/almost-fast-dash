using System;
using System.Drawing;

namespace AlmostFastDash.Rendering
{
    internal sealed partial class LayoutRenderer : IDashboardRenderer, IStateRenderer
    {
        public int StateHash(TelemetrySnapshot t, bool shiftFlashOn, int gearAnimationFrame)
        {
            unchecked
            {
                EvalScope scope = Scope(t, shiftFlashOn);
                int now = FlagClock ?? Environment.TickCount;
                FlagView flags = Flags(scope.T, now);
                int flagHash = FlagHash(flags, scope.T, now);
                if (flags.Full) return flagHash;
                DashScreen screen = ActiveScreen(scope);
                int hash = screen.Id.GetHashCode();
                hash = hash * 31 + gearAnimationFrame;
                hash = hash * 31 + (scope.T.Connected ? 1 : 0);
                if (ReuseFingerprints)
                {
                    _fingerprints.Clear();
                    _fingerprintsFor = scope.T;
                    _fingerprintsFlash = shiftFlashOn;
                }
                foreach (DashWidget widget in screen.Widgets)
                {
                    if (widget.Hidden) continue;
                    int fingerprint = WidgetHash(widget, scope, 0, 1);
                    if (ReuseFingerprints) _fingerprints[widget.Id] = fingerprint;
                    hash = hash * 31 + fingerprint;
                }
                return hash * 31 + flagHash;
            }
        }

        /// <summary>A fingerprint of what the flags add to a frame: the flag and how far it has faded, the cloth's movement, the frame's breath.</summary>
        private int FlagHash(FlagView flags, TelemetrySnapshot t, int now)
        {
            unchecked
            {
                int hash = 0;
                if (flags.Alpha > 0)
                {
                    hash = (int)flags.Takeover * 7919 + (int)(flags.Alpha * 48) * 131 + FlagPainter.Key(flags.Takeover, t).GetHashCode();
                    if (_layout.WavingFlags) hash = hash * 31 + WavingFlag.Frame(now);
                }
                if (flags.Frame != RacingFlag.None)
                    hash = hash * 31 + (int)flags.Frame * 104729 + (int)(flags.FrameAlpha * 48) * 17 + FlagFramePainter.Breath(now);
                return hash;
            }
        }

        /// <summary>
        /// A fingerprint of everything a widget would draw: its formula results and, for widgets
        /// that read telemetry directly, that data. Equal fingerprints mean identical pixels.
        /// </summary>
        private int WidgetHash(DashWidget widget, EvalScope scope, int previousGear, float progress)
        {
            unchecked
            {
                WidgetType type = WidgetCatalog.Find(widget.Type);
                if (type == null) return 0;
                scope = ForWidget(scope, widget);
                // A hidden widget draws nothing, whatever its other values do (in the designer, a faint ghost of itself).
                string visible = widget.Get("visible", "");
                bool hidden = !string.IsNullOrWhiteSpace(visible) && !Expr.Bool(Expr.Evaluate(Strip(visible), scope));
                if (hidden && !Ghosts(visible)) return 1;
                int hash = hidden ? 23 : 17;
                foreach (PropDef prop in type.Props)
                {
                    string raw = widget.Get(prop.Key, prop.Default);
                    if (!IsFormula(raw, prop)) continue;
                    object value = Expr.Evaluate(Strip(raw), scope);
                    // Exact values: even a tiny change moves an anti-aliased edge.
                    hash = hash * 31 + (value is double d ? d.GetHashCode() : value is Color c ? c.ToArgb() : Expr.Text(value).GetHashCode());
                }
                TelemetrySnapshot t = scope.T;
                // Several widgets look different while iRacing isn't connected.
                hash = hash * 31 + (t.Connected ? 1 : 0);
                // A tap zone's dots show which view it's on.
                string tapName = widget.Get("tapName", "");
                if (!string.IsNullOrWhiteSpace(tapName) && scope.Taps != null)
                {
                    scope.Taps.TryGetValue(tapName.Trim(), out int view);
                    hash = hash * 31 + view * 7 + TapViews(widget) * 101;
                }
                // Data-heavy widgets read more than their properties show.
                switch (widget.Type)
                {
                    case "gear": hash = hash * 31 + t.Gear * 7 + previousGear * 13 + (int)(progress * 1000) + t.MaxForwardGear * 3; break;
                    case "relative": hash = hash * 31 + RelativeHash(t); break;
                    case "laphistory": hash = hash * 31 + (t.RecentLapSeconds?.Length ?? 0) * 17 + (int)(t.BestLapSeconds * 1000) + ArrayHash(t.RecentLapSeconds) * 7 + ArrayHash(t.StintLapSeconds); break;
                    case "progress":
                        hash = hash * 31 + t.LapsCompleted * 13 + t.LapColorVersion + (int)t.SessionSecondsRemaining + t.TotalLaps * 7 + (int)t.ProgressMode;
                        // Which laps ran under yellow colors their blocks.
                        if (t.YellowCompletedLaps != null) for (int i = 0; i < t.YellowCompletedLaps.Length; i++) if (t.YellowCompletedLaps[i]) hash = hash * 31 + i + 1;
                        break;
                    case "blindspot": hash = hash * 31 + (int)t.LeftBlindSpot * 5 + (int)t.RightBlindSpot; break;
                    case "flag": hash = hash * 31 + (int)t.Flag; break;
                    case "standings": hash = hash * 31 + StandingsHash(t) + (int)(t.SessionBestLapSeconds * 1000); break;
                    case "tyres": hash = hash * 31 + TyresHash(t); break;
                    case "trace": hash = hash * 31 + t.TraceVersion; break;
                    case "deltatrace": hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t.DeltaTrace ?? new float[0]); break;
                    case "graph": hash = hash * 31 + SampleGraph(widget, type, scope).Version; break;
                    case "trackmap": hash = hash * 31 + MapHash(t); break;
                    case "radar": hash = hash * 31 + RadarHash(t); break;
                    case "traffic": hash = hash * 31 + MapHash(t) + (int)(t.ReferenceLapSeconds * 10) + (int)t.SpeedMph; break;
                }
                DashWidget clipBase = ClipBase(widget);
                if (clipBase != null) hash = hash * 31 + (clipBase.Hidden ? 3 : WidgetHash(clipBase, scope, previousGear, progress));
                return hash;
            }
        }

        /// <summary>
        /// The radar's cars, the gaps its sensors show and how far you've driven (the lanes move), all exact:
        /// even a fraction of a pixel moves an anti-aliased edge.
        /// </summary>
        private static int RadarHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = t.AheadMeters.GetHashCode() * 31 + t.BehindMeters.GetHashCode();
                hash = hash * 31 + (t.LapFraction * t.TrackLengthMiles).GetHashCode();
                foreach (RadarCar car in t.Radar ?? new RadarCar[0])
                    hash = hash * 31 + car.CarIdx * 7 + car.Meters.GetHashCode() * 13 + car.Lane.GetHashCode() * 17 + (car.Alongside ? 1 : 0) + (car.PaceCar ? 2 : 0)
                        + (car.Number?.GetHashCode() ?? 0) + car.ClassColor.ToArgb();
                return hash;
            }
        }

        private static int MapHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = (t.TrackMap?.Key?.GetHashCode() ?? 0) * 31 + (int)(t.MapProgress * 100) + t.PlayerCarIdx * 7;
                // Positions to a two-thousandth of a lap: smooth on screen without redrawing for nothing.
                foreach (float d in t.CarLapDist ?? new float[0]) hash = hash * 31 + (int)Math.Round(d * 2000);
                foreach (StandingsEntry r in t.Standings ?? new StandingsEntry[0]) hash = hash * 31 + (r.InPits ? 1 : 0) + r.CarIdx;
                return hash;
            }
        }

        private static int ArrayHash(double[] values)
        {
            unchecked
            {
                int hash = 0;
                if (values != null) foreach (double v in values) hash = hash * 31 + v.GetHashCode();
                return hash;
            }
        }

        private static int StandingsHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = 0;
                foreach (StandingsEntry r in t.Standings ?? new StandingsEntry[0])
                    hash = hash * 31 + r.CarIdx * 7 + r.Position + (int)Math.Round(r.GapToLeader * 10) * 13 + (int)(r.LastLap * 1000) + (int)(r.BestLap * 1000) + (r.InPits ? 1 : 0);
                return hash;
            }
        }

        private static int TyresHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = 0;
                foreach (TyreCorner c in t.Tyres ?? new TyreCorner[0])
                    hash = hash * 31 + (c.Temp + c.TempInner * 3 + c.TempOuter * 7 + c.Wear * 1000 + c.PressureKpa * 11).GetHashCode();
                return hash;
            }
        }

        private static int RelativeHash(TelemetrySnapshot t)
        {
            unchecked
            {
                int hash = 0;
                foreach (RelativeEntry r in t.Relatives ?? new RelativeEntry[0])
                    hash = hash * 31 + (r.Name?.GetHashCode() ?? 0) + (int)Math.Round(r.Gap * 10) * 7 + r.Position + (r.InPits ? 1 : 0);
                return hash;
            }
        }
    }
}
