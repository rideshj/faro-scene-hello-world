using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;
using FAROSceneSDCardWorkflow.Registration;

namespace FAROSceneSDCardWorkflow.UI
{
    /// <summary>
    /// High-performance 2D GDI+ orthographic canvas for top-view (Bird's Eye) scan registration verification.
    /// Displays scan footprints, station tripod glyphs, detected targets, and inter-station correspondence links.
    /// </summary>
    public class TopViewCanvas : UserControl
    {
        private List<ScanStation> _stations = new List<ScanStation>();
        private List<TargetCorrespondence> _correspondences = new List<TargetCorrespondence>();

        // View navigation state
        private float _zoom = 20.0f; // pixels per meter
        private PointF _panOffset = new PointF(0, 0);
        private bool _isPanning = false;
        private Point _lastMousePos;

        // Visual display toggles
        public bool ShowPoints { get; set; } = true;
        public bool ShowTargetLinks { get; set; } = true;
        public bool ShowTargetLabels { get; set; } = true;
        public bool ShowGrid { get; set; } = true;

        public TopViewCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            BackColor = Color.FromArgb(28, 30, 36); // Modern dark CAD background
            Cursor = Cursors.Cross;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseWheel += OnMouseWheel;
            MouseDoubleClick += (s, e) => ResetView();
        }

        public void SetData(List<ScanStation> stations, List<TargetCorrespondence> correspondences)
        {
            _stations = stations ?? new List<ScanStation>();
            _correspondences = correspondences ?? new List<TargetCorrespondence>();
            ResetView();
            Invalidate();
        }

        public void ResetView()
        {
            if (_stations == null || _stations.Count == 0)
            {
                _panOffset = new PointF(Width / 2f, Height / 2f);
                _zoom = 25f;
                Invalidate();
                return;
            }

            // Compute bounding box in world space (X, Y)
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var st in _stations)
            {
                var pos = st.ScannerPosition;
                minX = Math.Min(minX, pos.X);
                maxX = Math.Max(maxX, pos.X);
                minY = Math.Min(minY, pos.Y);
                maxY = Math.Max(maxY, pos.Y);

                foreach (var t in st.Targets)
                {
                    var tc = t.TransformedCenter;
                    minX = Math.Min(minX, tc.X);
                    maxX = Math.Max(maxX, tc.X);
                    minY = Math.Min(minY, tc.Y);
                    maxY = Math.Max(maxY, tc.Y);
                }
            }

            // Add margin
            float spanX = Math.Max(maxX - minX + 6.0f, 6.0f);
            float spanY = Math.Max(maxY - minY + 6.0f, 6.0f);
            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;

            float scaleX = (Width > 0 ? Width : 800) / spanX * 0.85f;
            float scaleY = (Height > 0 ? Height : 600) / spanY * 0.85f;
            _zoom = Math.Max(5.0f, Math.Min(scaleX, scaleY));

            // Pan center to screen center
            _panOffset = new PointF(
                (Width / 2f) - (centerX * _zoom),
                (Height / 2f) + (centerY * _zoom) // Screen Y is inverted
            );

            Invalidate();
        }

        #region Mouse Interaction

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _lastMousePos = e.Location;
                Cursor = Cursors.SizeAll;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;
                _panOffset.X += dx;
                _panOffset.Y += dy;
                _lastMousePos = e.Location;
                Invalidate();
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                Cursor = Cursors.Cross;
            }
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            float oldZoom = _zoom;
            float factor = e.Delta > 0 ? 1.15f : 0.87f;
            _zoom = Math.Max(2.0f, Math.Min(300.0f, _zoom * factor));

            // Zoom centered on cursor
            float mouseWorldX = (e.X - _panOffset.X) / oldZoom;
            float mouseWorldY = (_panOffset.Y - e.Y) / oldZoom;

            _panOffset.X = e.X - (mouseWorldX * _zoom);
            _panOffset.Y = e.Y + (mouseWorldY * _zoom);

            Invalidate();
        }

        #endregion

        #region Coordinate Transformations

        private PointF WorldToScreen(float x, float y)
        {
            return new PointF(
                _panOffset.X + (x * _zoom),
                _panOffset.Y - (y * _zoom)
            );
        }

        #endregion

        #region Rendering Pipeline

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 1. Grid lines and axes
            if (ShowGrid)
            {
                DrawGrid(g);
            }

            if (_stations == null || _stations.Count == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(140, 150, 165)))
                using (var font = new Font("Segoe UI", 12f, FontStyle.Italic))
                {
                    string text = "No scan data loaded. Ingest scans to view registration top-view.";
                    var sz = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, (Width - sz.Width) / 2f, (Height - sz.Height) / 2f);
                }
                return;
            }

            // 2. Scan point clouds (footprint)
            if (ShowPoints)
            {
                DrawScanPoints(g);
            }

            // 3. Target correspondence links (inter-station tension lines)
            if (ShowTargetLinks)
            {
                DrawCorrespondenceLinks(g);
            }

            // 4. Detected target glyphs
            DrawTargets(g);

            // 5. Scanner station positions and tripods
            DrawScannerStations(g);

            // 6. UI overlays: Scale bar and Legend
            DrawScaleBar(g);
            DrawLegend(g);
        }

        private void DrawGrid(Graphics g)
        {
            // Determine grid step in meters based on zoom level
            float meterStep = 1.0f;
            if (_zoom < 5f) meterStep = 10.0f;
            else if (_zoom < 15f) meterStep = 5.0f;
            else if (_zoom < 40f) meterStep = 2.0f;
            else meterStep = 1.0f;

            float stepPixels = meterStep * _zoom;
            if (stepPixels < 15f) return;

            using (var gridPen = new Pen(Color.FromArgb(42, 45, 54), 1f))
            using (var axisPen = new Pen(Color.FromArgb(65, 72, 85), 1.5f))
            using (var textBrush = new SolidBrush(Color.FromArgb(100, 110, 125)))
            using (var font = new Font("Segoe UI", 8f))
            {
                // Vertical lines
                float startX = _panOffset.X % stepPixels;
                for (float x = startX; x < Width; x += stepPixels)
                {
                    g.DrawLine(gridPen, x, 0, x, Height);
                }

                // Horizontal lines
                float startY = _panOffset.Y % stepPixels;
                for (float y = startY; y < Height; y += stepPixels)
                {
                    g.DrawLine(gridPen, 0, y, Width, y);
                }

                // World coordinate origin axes (X = 0, Y = 0)
                PointF origin = WorldToScreen(0, 0);
                if (origin.X >= 0 && origin.X <= Width)
                {
                    g.DrawLine(axisPen, origin.X, 0, origin.X, Height);
                    g.DrawString("+Y", font, textBrush, origin.X + 4, 10);
                }
                if (origin.Y >= 0 && origin.Y <= Height)
                {
                    g.DrawLine(axisPen, 0, origin.Y, Width, origin.Y);
                    g.DrawString("+X", font, textBrush, Width - 25, origin.Y - 16);
                }
            }
        }

        private void DrawScanPoints(Graphics g)
        {
            foreach (var st in _stations)
            {
                if (st.Points == null || st.Points.Count == 0) continue;

                // Color with slight transparency for overlapping scan footprint visibility
                Color baseCol = st.Color;
                Color ptCol = Color.FromArgb(130, baseCol.R, baseCol.G, baseCol.B);

                using (var brush = new SolidBrush(ptCol))
                {
                    // Stride downsampling to keep rendering smooth at 60 FPS
                    int stride = Math.Max(1, st.Points.Count / 8000);
                    var mat = st.Transformation;

                    for (int i = 0; i < st.Points.Count; i += stride)
                    {
                        var localPt = st.Points[i];
                        // Transform to registered world space
                        var worldPt = Vector3.Transform(localPt, mat);
                        PointF pt = WorldToScreen(worldPt.X, worldPt.Y);

                        // Frustum culling
                        if (pt.X >= -5 && pt.X <= Width + 5 && pt.Y >= -5 && pt.Y <= Height + 5)
                        {
                            g.FillRectangle(brush, pt.X - 1, pt.Y - 1, 2, 2);
                        }
                    }
                }
            }
        }

        private void DrawCorrespondenceLinks(Graphics g)
        {
            if (_correspondences == null) return;

            foreach (var link in _correspondences)
            {
                var pA = WorldToScreen(link.TargetInScanA.TransformedCenter.X, link.TargetInScanA.TransformedCenter.Y);
                var pB = WorldToScreen(link.TargetInScanB.TransformedCenter.X, link.TargetInScanB.TransformedCenter.Y);

                // Residual quality color: Green < 2mm, Yellow < 4mm, Red > 4mm
                Color linkCol;
                if (link.DistanceErrorMm <= 2.0f) linkCol = Color.FromArgb(200, 76, 217, 100);
                else if (link.DistanceErrorMm <= 4.0f) linkCol = Color.FromArgb(200, 255, 204, 0);
                else linkCol = Color.FromArgb(200, 255, 59, 48);

                using (var pen = new Pen(linkCol, 1.5f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawLine(pen, pA, pB);
                }
            }
        }

        private void DrawTargets(Graphics g)
        {
            var drawnTargets = new HashSet<string>();

            using (var targetBrush = new SolidBrush(Color.FromArgb(255, 215, 0))) // Golden target color
            using (var ringPen = new Pen(Color.FromArgb(255, 255, 255), 1.5f))
            using (var centerPen = new Pen(Color.FromArgb(28, 30, 36), 1.5f))
            using (var textBrush = new SolidBrush(Color.FromArgb(240, 240, 240)))
            using (var font = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                foreach (var st in _stations)
                {
                    if (st.Targets == null) continue;

                    foreach (var target in st.Targets)
                    {
                        var tc = target.TransformedCenter;
                        PointF pt = WorldToScreen(tc.X, tc.Y);

                        // Frustum culling
                        if (pt.X < -50 || pt.X > Width + 50 || pt.Y < -50 || pt.Y > Height + 50)
                            continue;

                        // Draw target concentric circle glyph
                        float r = 7f;
                        g.FillEllipse(targetBrush, pt.X - r, pt.Y - r, r * 2, r * 2);
                        g.DrawEllipse(ringPen, pt.X - r, pt.Y - r, r * 2, r * 2);

                        // Inner crosshair
                        g.DrawLine(centerPen, pt.X - 3, pt.Y, pt.X + 3, pt.Y);
                        g.DrawLine(centerPen, pt.X, pt.Y - 3, pt.X, pt.Y + 3);

                        // Target label
                        if (ShowTargetLabels)
                        {
                            string label = target.TargetId;
                            if (target.TensionResidualMm > 0)
                            {
                                label += $" ({target.TensionResidualMm:F1}mm)";
                            }

                            g.DrawString(label, font, textBrush, pt.X + 9, pt.Y - 8);
                        }
                    }
                }
            }
        }

        private void DrawScannerStations(Graphics g)
        {
            using (var textFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 7.5f))
            using (var labelBrush = new SolidBrush(Color.White))
            using (var subBrush = new SolidBrush(Color.FromArgb(180, 190, 205)))
            using (var bgBrush = new SolidBrush(Color.FromArgb(210, 20, 22, 28)))
            {
                foreach (var st in _stations)
                {
                    var pos = st.ScannerPosition;
                    PointF pt = WorldToScreen(pos.X, pos.Y);

                    // Frustum culling
                    if (pt.X < -100 || pt.X > Width + 100 || pt.Y < -100 || pt.Y > Height + 100)
                        continue;

                    // 1. Station circular base & tripod glyph
                    float r = 11f;
                    using (var fillBrush = new SolidBrush(st.Color))
                    using (var strokePen = new Pen(Color.White, 2f))
                    {
                        g.FillEllipse(fillBrush, pt.X - r, pt.Y - r, r * 2, r * 2);
                        g.DrawEllipse(strokePen, pt.X - r, pt.Y - r, r * 2, r * 2);

                        // Tripod 3-legged glyph
                        using (var tripodPen = new Pen(Color.FromArgb(240, 240, 240), 2f))
                        {
                            g.DrawLine(tripodPen, pt.X, pt.Y - r - 3, pt.X, pt.Y);
                            g.DrawLine(tripodPen, pt.X - (r * 0.866f), pt.Y + (r * 0.5f), pt.X, pt.Y);
                            g.DrawLine(tripodPen, pt.X + (r * 0.866f), pt.Y + (r * 0.5f), pt.X, pt.Y);
                        }
                    }

                    // 2. Station Callout Label
                    string title = st.Name;
                    string coords = $"X:{pos.X:+0.00;-0.00;0.00} Y:{pos.Y:+0.00;-0.00;0.00}";

                    var titleSz = g.MeasureString(title, textFont);
                    var coordsSz = g.MeasureString(coords, subFont);
                    float boxW = Math.Max(titleSz.Width, coordsSz.Width) + 12;
                    float boxH = titleSz.Height + coordsSz.Height + 6;
                    float boxX = pt.X + 16;
                    float boxY = pt.Y - (boxH / 2);

                    // Callout background
                    g.FillRectangle(bgBrush, boxX, boxY, boxW, boxH);
                    using (var borderPen = new Pen(st.Color, 1.5f))
                    {
                        g.DrawRectangle(borderPen, boxX, boxY, boxW, boxH);
                    }

                    g.DrawString(title, textFont, labelBrush, boxX + 6, boxY + 2);
                    g.DrawString(coords, subFont, subBrush, boxX + 6, boxY + titleSz.Height + 1);
                }
            }
        }

        private void DrawScaleBar(Graphics g)
        {
            // Dynamic scale bar in bottom-left
            float targetBarPixels = 120f;
            float worldMeters = targetBarPixels / _zoom;

            // Round to sensible standard units (0.5m, 1m, 2m, 5m, 10m, 20m)
            float[] standardSteps = { 0.5f, 1f, 2f, 5f, 10f, 20f, 50f, 100f };
            float chosenMeters = standardSteps[0];
            foreach (var step in standardSteps)
            {
                if (step <= worldMeters * 1.3f)
                {
                    chosenMeters = step;
                }
            }

            float actualBarPixels = chosenMeters * _zoom;
            float margin = 20f;
            float x1 = margin;
            float y1 = Height - margin;
            float x2 = x1 + actualBarPixels;

            using (var pen = new Pen(Color.White, 3f))
            using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            using (var bgBrush = new SolidBrush(Color.FromArgb(190, 20, 20, 25)))
            {
                string text = $"{chosenMeters} m";
                var sz = g.MeasureString(text, font);

                g.FillRectangle(bgBrush, x1 - 5, y1 - 25, actualBarPixels + 10, 32);

                // Horizontal bar
                g.DrawLine(pen, x1, y1, x2, y1);
                // End ticks
                g.DrawLine(pen, x1, y1 - 5, x1, y1 + 5);
                g.DrawLine(pen, x2, y1 - 5, x2, y1 + 5);

                g.DrawString(text, font, textBrush, x1 + (actualBarPixels - sz.Width) / 2f, y1 - 22);
            }
        }

        private void DrawLegend(Graphics g)
        {
            float x = Width - 190;
            float y = 15;
            float w = 175;
            float h = 95 + (_stations.Count * 18);

            using (var bgBrush = new SolidBrush(Color.FromArgb(220, 22, 24, 30)))
            using (var borderPen = new Pen(Color.FromArgb(60, 66, 78), 1f))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var itemFont = new Font("Segoe UI", 8f))
            using (var textBrush = new SolidBrush(Color.FromArgb(220, 225, 235)))
            {
                g.FillRectangle(bgBrush, x, y, w, h);
                g.DrawRectangle(borderPen, x, y, w, h);

                g.DrawString("TOP VIEW LEGEND", titleFont, textBrush, x + 8, y + 6);

                // Scanner tripod icon
                float curY = y + 28;
                using (var tripBrush = new SolidBrush(Color.FromArgb(0, 120, 215)))
                {
                    g.FillEllipse(tripBrush, x + 10, curY, 8, 8);
                }
                g.DrawString("Scanner Station", itemFont, textBrush, x + 24, curY - 2);

                // Target icon
                curY += 18;
                using (var tgtBrush = new SolidBrush(Color.FromArgb(255, 215, 0)))
                {
                    g.FillEllipse(tgtBrush, x + 10, curY, 8, 8);
                }
                g.DrawString("Target Sphere / Marker", itemFont, textBrush, x + 24, curY - 2);

                // Correspondence line
                curY += 18;
                using (var linkPen = new Pen(Color.FromArgb(76, 217, 100), 1.5f))
                {
                    linkPen.DashStyle = DashStyle.Dash;
                    g.DrawLine(linkPen, x + 8, curY + 4, x + 20, curY + 4);
                }
                g.DrawString("Tension Link (< 2mm)", itemFont, textBrush, x + 24, curY - 2);

                curY += 22;
                g.DrawLine(borderPen, x + 8, curY, x + w - 8, curY);
                curY += 6;

                // Stations list
                for (int i = 0; i < _stations.Count; i++)
                {
                    using (var stBrush = new SolidBrush(_stations[i].Color))
                    {
                        g.FillRectangle(stBrush, x + 10, curY + 2, 8, 8);
                    }
                    g.DrawString(_stations[i].Name, itemFont, textBrush, x + 24, curY);
                    curY += 18;
                }
            }
        }

        #endregion
    }
}
