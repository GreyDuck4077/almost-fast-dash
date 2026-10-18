using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;

namespace AlmostFastDash.Designer
{
    public partial class DesignerWindow : Window
    {
        // ==================================================================
        // Rendering
        // ==================================================================

        /// <summary>Asks for a redraw after something changed: the design (so cached images go) or the overlay.</summary>
        private void RequestRender()
        {
            _renderPending = true;
            _designChanged = true;
            if (!_renderTimer.IsEnabled) _renderTimer.Start();
        }

        private bool _designChanged = true;

        private string _shownErrors = "";

        /// <summary>Grey squares, 16 px, behind a design whose background is see-through.</summary>
        private static readonly Brush SeeThrough = CreateSeeThrough();

        private static Brush CreateSeeThrough()
        {
            var squares = new GeometryGroup();
            squares.Children.Add(new RectangleGeometry(new Rect(0, 0, 16, 16)));
            squares.Children.Add(new RectangleGeometry(new Rect(16, 16, 16, 16)));
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(38, 42, 48)), null,
                new RectangleGeometry(new Rect(0, 0, 32, 32))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(54, 59, 66)), null, squares));
            var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 32, 32),
                ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            return brush;
        }

        private void RenderNow()
        {
            _renderPending = false;
            // The design's screen size can change (resized, or an undo); the canvas follows it.
            if (_frame == null || _frame.PixelWidth != (int)CanvasW || _frame.PixelHeight != (int)CanvasH)
            {
                FitCanvasToScreen();
                _rendererDirty = true;
            }
            if (_rendererDirty || _renderer == null)
            {
                _renderer?.Dispose();
                // The same redraw-what-changed renderer as the wheel; every edit invalidates it, so an
                // animated preview only redraws the moving parts between edits.
                _renderer = new LayoutRenderer((int)CanvasW, (int)CanvasH,
                    new DashboardSettings { Layout = Layouts.Custom, Design = _layout, AccentColor = _dashboard.AccentColor },
                    _units) { Taps = _designTaps, GhostHidden = true };
                _rendererDirty = false;
                _designChanged = true;
            }
            bool edited = _designChanged;
            if (edited)
            {
                _renderer.Invalidate();
                // A see-through background shows as a checkerboard, as in image editors: whatever is behind it shows there.
                Bezel.Background = _layout.Theme.BackgroundStyle == "None" ? SeeThrough : null;
            }
            _designChanged = false;

            TelemetrySnapshot snapshot = PreviewSnapshot(out bool flash, out int previousGear, out float progress);
            using (DrawingBitmap bitmap = _renderer.Render(snapshot, flash, previousGear, progress, _screen))
                CopyToFrame(bitmap);
            // After an edit, the layers' pictures catch up (animation frames leave them be).
            if (edited) UpdateLayerThumbs();
            // Animation frames leave the selection overlay and layer list alone unless an error, or a widget hidden by
            // its Show when, came or went.
            string errors = string.Join(",", _renderer.Errors.Keys.OrderBy(k => k));
            Dictionary<string, string> ghostsBefore = _ghosts;
            bool ghostsChanged = FindGhosts(snapshot, flash);
            if (edited || errors != _shownErrors || ghostsChanged)
            {
                _shownErrors = errors;
                UpdateOverlay();
                MarkLayerErrors();
            }
            // A selected widget that just became a ghost, or stopped being one, says so in Properties (unless it's being typed in).
            if (ghostsChanged && !Inspector.IsKeyboardFocusWithin &&
                _selection.Any(w => ghostsBefore.ContainsKey(w.Id) != _ghosts.ContainsKey(w.Id)))
                RefreshInspector();
            UpdateFormulaResults(snapshot, flash);
        }

        /// <summary>
        /// The widgets on this screen drawn as ghosts right now, with what their tag says: hidden by their Show when,
        /// or with nothing to show at this moment (no flag out, no car alongside).
        /// </summary>
        private Dictionary<string, string> _ghosts = new Dictionary<string, string>();

        private bool FindGhosts(TelemetrySnapshot snapshot, bool flash)
        {
            var scope = new EvalScope { T = snapshot, Units = _units, Theme = _layout.Theme, Accent = _dashboard.AccentColor,
                ShiftFlashOn = flash, Taps = _designTaps };
            var ghosts = new Dictionary<string, string>();
            foreach (DashWidget w in _screen.Widgets)
            {
                if (w.Hidden) continue;
                EvalScope own = LayoutRenderer.ForWidget(scope, w);
                string visible = w.Get("visible", "");
                if (!string.IsNullOrWhiteSpace(visible) && !LayoutRenderer.IsTapView(visible) &&
                    !Expr.Bool(Expr.Evaluate(LayoutRenderer.Strip(visible), own)))
                {
                    string condition = LayoutRenderer.Strip(visible).Trim();
                    if (condition.Length > 46) condition = condition.Substring(0, 44).TrimEnd() + "…";
                    ghosts[w.Id] = "Hidden now · shows when " + condition;
                }
                else if ((w.Type == "flag" || w.Type == "blindspot") && WidgetCatalog.Find(w.Type) is WidgetType type &&
                    LayoutRenderer.Idle(w.Type, new WidgetContext(_renderer, null, own, w, type)))
                    ghosts[w.Id] = w.Type == "flag" ? "A sample · shows the flag that's out"
                        : "A sample · lights while a car is alongside";
            }
            if (ghosts.Count == _ghosts.Count &&
                ghosts.All(g => _ghosts.TryGetValue(g.Key, out string tag) && tag == g.Value)) return false;
            _ghosts = ghosts;
            return true;
        }

        /// <summary>A dashed outline and a tag on each ghost, saying when it shows: one per group and condition.</summary>
        private void AddGhostTags()
        {
            var muted = new SolidColorBrush(Color.FromArgb(130, 160, 172, 190));
            foreach (var set in _screen.Widgets.Where(w => !w.Hidden && _ghosts.ContainsKey(w.Id))
                .GroupBy(w => (string.IsNullOrEmpty(w.Group) ? w.Id : w.Group) + "|" + _ghosts[w.Id]))
            {
                Rect around = Union(set);
                AddRect(around, muted, 1, true);
                // The tag shows on the one pointed at, so a screen of warnings stays clear; a selected one says it in Properties.
                if (set.Any(w => _selection.Contains(w)) ||
                    !set.Any(w => w == _hover || (_hover != null && !string.IsNullOrEmpty(w.Group) && w.Group == _hover.Group))) continue;
                string condition = _ghosts[set.First().Id];
                var tag = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(225, 12, 15, 20)), CornerRadius = new CornerRadius(3 * Px),
                        IsHitTestVisible = false,
                    Padding = new Thickness(5 * Px, 1 * Px, 5 * Px, 2 * Px), BorderBrush = muted, BorderThickness = new Thickness(1 * Px),
                    Child = new TextBlock { Text = condition, FontSize = 10.5 * Px,
                    Foreground = new SolidColorBrush(Color.FromRgb(176, 186, 200)) }
                };
                Canvas.SetLeft(tag, around.X);
                Canvas.SetTop(tag, around.Y - 17 * Px);
                Overlay.Children.Add(tag);
            }
        }

        /// <summary>The animated preview runs at full rate only while the designer is the window in use.</summary>
        private void PaceAnimation()
        {
            bool moving = PreviewState == "Animated lap" || PreviewState == "Live iRacing" || PreviewState == "Shift point" ||
                PreviewState.EndsWith(" flag");
            if (!moving || WindowState == WindowState.Minimized) { _animationTimer.Stop(); return; }
            _animationTimer.Interval = TimeSpan.FromMilliseconds(IsActive ? 33 : 250);
            if (!_animationTimer.IsEnabled) _animationTimer.Start();
        }

        private void CopyToFrame(DrawingBitmap bitmap)
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                _frame.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), data.Scan0, data.Stride * bitmap.Height, data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>Renders a design to an image for gallery cards and the template picker.</summary>
        internal static BitmapSource RenderThumbnail(DashboardSettings dashboard, UnitSystem units, TelemetrySnapshot snapshot = null)
        {
            snapshot = snapshot ?? IRacingTelemetrySource.CreatePreviewSnapshot();
            System.Drawing.Size size = dashboard.ScreenSize();
            using (IDashboardRenderer renderer = DashboardRendererFactory.Create(size.Width, size.Height, dashboard, units))
            using (DrawingBitmap bitmap = renderer.Render(snapshot, false, snapshot.Gear, 1f))
            {
                var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    BitmapSource source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, data.Scan0,
                        data.Stride * bitmap.Height, data.Stride);
                    source.Freeze();
                    return source;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        // ==================================================================
        // Preview data
        // ==================================================================

        private static readonly string[] PreviewStates =
        {
            "Animated lap", "Live iRacing", "Racing", "Shift point", "Pit lane", "Cars alongside", "Not connected",
            "Green flag", "Yellow flag", "White flag", "Red flag", "Blue flag", "Black flag", "Repair flag", "Checkered flag"
        };

        private void BuildPreviewCombo()
        {
            PreviewCombo.ItemsSource = PreviewStates;
            PreviewCombo.SelectedIndex = 0;
        }

        private string PreviewState => PreviewCombo.SelectedItem as string ?? "Racing";

        private void PreviewCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded && _frame == null) return;
            if (PreviewState == "Live iRacing") StartLive(); else StopLive();
            PaceAnimation();
            RequestRender();
        }

        private void StartLive()
        {
            if (_live != null) return;
            _liveLog = new DiagnosticLogger(false);
            _live = new IRacingTelemetrySource(_liveLog, _dashboard);
            _live.Start();
        }

        private void StopLive()
        {
            _live?.Dispose();
            _live = null;
            _liveLog?.Dispose();
            _liveLog = null;
        }

        private TelemetrySnapshot PreviewSnapshot(out bool flash, out int previousGear, out float progress)
        {
            flash = false;
            progress = 1;
            TelemetrySnapshot snapshot;
            switch (PreviewState)
            {
                case "Animated lap":
                    snapshot = _synthetic.Next(out flash, out previousGear, out progress);
                    return snapshot;
                case "Live iRacing":
                    snapshot = _live?.GetSnapshot() ?? TelemetrySnapshot.Disconnected;
                    flash = snapshot.ShiftAlert && ((Environment.TickCount / 140) & 1) == 0;
                    StatusText.Text = snapshot.Connected ? "Showing live iRacing data."
                        : "Waiting for iRacing… start a session to see live data.";
                    break;
                case "Shift point":
                    snapshot = ShiftSnapshot();
                    flash = ((Environment.TickCount / 140) & 1) == 0;
                    break;
                case "Pit lane": snapshot = IRacingTelemetrySource.CreatePitLanePreviewSnapshot(); break;
                case "Cars alongside": snapshot = IRacingTelemetrySource.CreateSpotterPreviewSnapshot(); break;
                case "Not connected": snapshot = TelemetrySnapshot.Disconnected; break;
                case "Green flag": snapshot = FlagPreview(RacingFlag.Green); break;
                case "Yellow flag": snapshot = FlagPreview(RacingFlag.Yellow); break;
                case "White flag": snapshot = FlagPreview(RacingFlag.White); break;
                case "Red flag": snapshot = FlagPreview(RacingFlag.Red); break;
                case "Blue flag": snapshot = FlagPreview(RacingFlag.BlueYellowStripe); break;
                case "Black flag": snapshot = FlagPreview(RacingFlag.Black); break;
                case "Repair flag": snapshot = FlagPreview(RacingFlag.Meatball); break;
                case "Checkered flag": snapshot = FlagPreview(RacingFlag.Checkered); break;
                default: snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(); break;
            }
            previousGear = snapshot.Gear;
            return snapshot;
        }

        private const int FlagPreviewLoop = 9000, FlagPreviewPulse = 2000;

        private string _flagPreviewState;

        private int _flagPreviewStart, _flagPreviewRound;

        /// <summary>
        /// A flag as a race shows it, over and over: green, yellow and white alert for a couple of seconds and
        /// then hand the screen back (yellow and white keep their frame); flags that stay out alert, then step
        /// aside to their frame; red and checkered stay.
        /// </summary>
        private TelemetrySnapshot FlagPreview(RacingFlag flag)
        {
            if (_flagPreviewState != PreviewState)
            {
                _flagPreviewState = PreviewState;
                _flagPreviewStart = Environment.TickCount;
                _flagPreviewRound = -1;
            }
            int elapsed = Environment.TickCount - _flagPreviewStart, round = elapsed / FlagPreviewLoop;
            if (round != _flagPreviewRound)
            {
                _flagPreviewRound = round;
                if (FlagTimeline.StaysOut(flag)) _renderer?.ResetFlags();
            }
            bool pulse = flag == RacingFlag.Green || flag == RacingFlag.Yellow || flag == RacingFlag.White;
            TelemetrySnapshot snapshot = IRacingTelemetrySource.CreatePreviewSnapshot(pulse && elapsed % FlagPreviewLoop > FlagPreviewPulse
                ? RacingFlag.None : flag);
            snapshot.CautionActive = flag == RacingFlag.Yellow;
            snapshot.WhiteFlagActive = flag == RacingFlag.White;
            return snapshot;
        }

        private static TelemetrySnapshot ShiftSnapshot()
        {
            var snapshot = new TelemetrySnapshot(
                true, 4, 148, RacingFlag.None, BlindSpotZone.None, BlindSpotZone.None, 1.0,
                SessionProgressMode.LapRace, 0, 0, 8, 18, 4, 2, 24.3, 58.104, 0.46,
                "J. MILLER", -0.46, "M. CARTER", 0.82, 22, false, 87.932, 87.521, 88.204, new bool[18], 0, 6, 7950);
            return IRacingTelemetrySource.WithPreviewExtras(snapshot);
        }
    }
}
