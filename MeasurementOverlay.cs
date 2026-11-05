using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MeasuringStick;

public enum OverlayMode
{
    Measure,
    MeasureAndScreenshot,
    RegionScreenshot
}

public class MeasurementOverlay : Form
{
    // Measurement points
    private Point? _measureStart;
    private Point _measureEnd;
    private bool _measurementComplete;

    // Region selection points (for screenshot)
    private Point? _regionStart;
    private Point _regionEnd;
    private bool _isDragging;

    private readonly Font _distanceFont;
    private readonly Font _coordFont;
    private readonly int _elementAlpha;
    private readonly OverlayMode _mode;
    private readonly AppSettings _settings;
    private Bitmap? _backgroundImage;
    private Point _cursor;
    private string? _status;

    private const int WS_EX_TOOLWINDOW = 0x80;
    private const double SnapStepDegrees = 15.0;
    private const int MagnifierZoom = 8;
    private const int MagnifierRadius = 10; // source pixels each side of the cursor

    public MeasurementOverlay(AppSettings settings, OverlayMode mode = OverlayMode.Measure)
    {
        _settings = settings;
        _mode = mode;
        _elementAlpha = (int)(settings.Opacity * 255);
        _distanceFont = new Font("Segoe UI", 14f, FontStyle.Bold);
        _coordFont = new Font("Segoe UI", 9f, FontStyle.Regular);

        var bounds = GetVirtualScreenBounds();
        _backgroundImage = CaptureScreen(bounds);
        _cursor = new Point(Cursor.Position.X - bounds.X, Cursor.Position.Y - bounds.Y);

        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.Manual;
        this.Location = bounds.Location;
        this.Size = bounds.Size;
        this.TopMost = true;
        this.ShowInTaskbar = false;
        this.Cursor = Cursors.Cross;
        this.DoubleBuffered = true;
        this.KeyPreview = true;

        this.MouseDown += OnMouseDown;
        this.MouseMove += OnMouseMove;
        this.MouseUp += OnMouseUp;
        this.KeyDown += OnKeyDown;
    }

    private static Rectangle GetVirtualScreenBounds()
    {
        int left = SystemInformation.VirtualScreen.Left;
        int top = SystemInformation.VirtualScreen.Top;
        int width = SystemInformation.VirtualScreen.Width;
        int height = SystemInformation.VirtualScreen.Height;
        return new Rectangle(left, top, width, height);
    }

    private static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }
        return bitmap;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _status = null;

            if (_mode == OverlayMode.MeasureAndScreenshot && _measurementComplete)
            {
                // Phase 2: Start region selection
                _regionStart = e.Location;
                _regionEnd = e.Location;
            }
            else
            {
                // Phase 1: Start measurement (or normal measure/region modes)
                if (_mode == OverlayMode.RegionScreenshot)
                {
                    _regionStart = e.Location;
                    _regionEnd = e.Location;
                }
                else
                {
                    _measureStart = e.Location;
                    _measureEnd = e.Location;
                }
            }
            Invalidate();
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        _cursor = e.Location;
        if (_isDragging)
        {
            if (_mode == OverlayMode.MeasureAndScreenshot && _measurementComplete)
            {
                _regionEnd = e.Location;
            }
            else if (_mode == OverlayMode.RegionScreenshot)
            {
                _regionEnd = e.Location;
            }
            else
            {
                // Hold Shift to snap the line to 15 degree steps (0, 15, 30, 45 ... 90).
                _measureEnd = (ModifierKeys & Keys.Shift) != 0 && _measureStart != null
                    ? MeasureMath.SnapAngle(_measureStart.Value, e.Location, SnapStepDegrees)
                    : e.Location;
            }
        }
        Invalidate(); // the magnifier follows the cursor
    }

    private bool CanNudgeMeasurement =>
        _measureStart != null && !_isDragging && _mode != OverlayMode.RegionScreenshot && _regionStart == null;

    // Arrow keys move the end point 1 px (Shift: 10 px) for exact placement.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (CanNudgeMeasurement && key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            int step = (keyData & Keys.Shift) != 0 ? 10 : 1;
            int dx = key == Keys.Left ? -step : key == Keys.Right ? step : 0;
            int dy = key == Keys.Up ? -step : key == Keys.Down ? step : 0;
            _measureEnd = new Point(_measureEnd.X + dx, _measureEnd.Y + dy);
            _cursor = _measureEnd;
            _status = null;
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _isDragging)
        {
            _isDragging = false;

            if (_mode == OverlayMode.RegionScreenshot)
            {
                // Region screenshot: save and close
                SaveRegionScreenshot();
                Close();
            }
            else if (_mode == OverlayMode.MeasureAndScreenshot)
            {
                if (_measurementComplete)
                {
                    // Phase 2 complete: save region with measurement and close
                    SaveRegionWithMeasurement();
                    Close();
                }
                else
                {
                    // Phase 1 complete: measurement done, now wait for region selection
                    _measurementComplete = true;
                    Invalidate();
                }
            }
            else
            {
                // Normal measure mode: just show measurement
                Invalidate();
            }
        }
        else if (e.Button == MouseButtons.Right)
        {
            Close();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
        else if (e.KeyCode == Keys.S && e.Control)
        {
            if (_mode == OverlayMode.RegionScreenshot && _regionStart != null)
            {
                SaveRegionScreenshot();
            }
            else if (_mode == OverlayMode.MeasureAndScreenshot && _measurementComplete && _regionStart != null)
            {
                SaveRegionWithMeasurement();
            }
            else if (_measureStart != null)
            {
                SaveFullScreenWithMeasurement();
            }
        }
        else if (e.KeyCode == Keys.C && e.Control)
        {
            if (_regionStart != null)
            {
                if (_mode == OverlayMode.MeasureAndScreenshot && _measurementComplete)
                {
                    SaveRegionWithMeasurement(clipboardOnly: true);
                }
                else
                {
                    SaveRegionScreenshot(clipboardOnly: true);
                }
            }
            else if (_measureStart != null)
            {
                Clipboard.SetText(MeasureMath.Summary(_measureStart.Value, _measureEnd, this.Location));
                _status = "Values copied to clipboard";
                Invalidate();
            }
        }
    }

    private Rectangle GetRegionRect()
    {
        if (_regionStart == null) return Rectangle.Empty;

        int x = Math.Min(_regionStart.Value.X, _regionEnd.X);
        int y = Math.Min(_regionStart.Value.Y, _regionEnd.Y);
        int width = Math.Abs(_regionEnd.X - _regionStart.Value.X);
        int height = Math.Abs(_regionEnd.Y - _regionStart.Value.Y);

        return new Rectangle(x, y, width, height);
    }

    private void SaveRegionScreenshot(bool clipboardOnly = false)
    {
        if (_regionStart == null || _backgroundImage == null) return;

        var rect = GetRegionRect();
        if (rect.Width < 1 || rect.Height < 1) return;

        using var regionBitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(regionBitmap))
        {
            g.DrawImage(_backgroundImage, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        }

        if (_settings.CopyToClipboard || clipboardOnly)
        {
            Clipboard.SetImage(regionBitmap);
        }

        if (!clipboardOnly && _settings.AutoSaveScreenshot)
        {
            var fileName = $"MeasuringStick_Region_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(_settings.ScreenshotFolder, fileName);
            regionBitmap.Save(filePath, ImageFormat.Png);

            if (_settings.ShowNotification)
            {
                MessageBox.Show($"Screenshot saved to:\n{filePath}" +
                    (_settings.CopyToClipboard ? "\n\n(Also copied to clipboard)" : ""),
                    "Screenshot Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        else if (clipboardOnly && _settings.ShowNotification)
        {
            MessageBox.Show("Region copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SaveRegionWithMeasurement(bool clipboardOnly = false)
    {
        if (_regionStart == null || _backgroundImage == null || _measureStart == null) return;

        var rect = GetRegionRect();
        if (rect.Width < 1 || rect.Height < 1) return;

        using var regionBitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(regionBitmap))
        {
            // Draw the background region
            g.DrawImage(_backgroundImage, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);

            // Translate graphics to draw measurement relative to region
            g.TranslateTransform(-rect.X, -rect.Y);
            DrawMeasurement(g);
        }

        if (_settings.CopyToClipboard || clipboardOnly)
        {
            Clipboard.SetImage(regionBitmap);
        }

        if (!clipboardOnly && _settings.AutoSaveScreenshot)
        {
            var fileName = $"MeasuringStick_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(_settings.ScreenshotFolder, fileName);
            regionBitmap.Save(filePath, ImageFormat.Png);

            if (_settings.ShowNotification)
            {
                MessageBox.Show($"Screenshot saved to:\n{filePath}" +
                    (_settings.CopyToClipboard ? "\n\n(Also copied to clipboard)" : ""),
                    "Screenshot Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        else if (clipboardOnly && _settings.ShowNotification)
        {
            MessageBox.Show("Region copied to clipboard!", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SaveFullScreenWithMeasurement()
    {
        if (_measureStart == null || _backgroundImage == null) return;

        using var bitmap = new Bitmap(_backgroundImage.Width, _backgroundImage.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.DrawImage(_backgroundImage, 0, 0);
            DrawMeasurement(g);
        }

        if (_settings.CopyToClipboard)
        {
            Clipboard.SetImage(bitmap);
        }

        if (_settings.AutoSaveScreenshot)
        {
            var fileName = $"MeasuringStick_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var filePath = Path.Combine(_settings.ScreenshotFolder, fileName);
            bitmap.Save(filePath, ImageFormat.Png);

            if (_settings.ShowNotification)
            {
                MessageBox.Show($"Screenshot saved to:\n{filePath}" +
                    (_settings.CopyToClipboard ? "\n\n(Also copied to clipboard)" : ""),
                    "Screenshot Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    private void DrawMeasurement(Graphics g)
    {
        if (_measureStart == null) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var start = _measureStart.Value;
        var end = _measureEnd;

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        var lineColor = Color.FromArgb(_elementAlpha, Color.Red);
        var pointColor = Color.FromArgb(_elementAlpha, Color.Yellow);
        var dashColor = Color.FromArgb(_elementAlpha, Color.Cyan);
        var bgColor = Color.FromArgb(Math.Min(_elementAlpha, 220), 0, 0, 0);
        var textColor = Color.FromArgb(_elementAlpha, Color.White);

        using var linePen = new Pen(lineColor, 2f);
        linePen.CustomEndCap = new AdjustableArrowCap(5, 5);
        linePen.CustomStartCap = new AdjustableArrowCap(5, 5);
        g.DrawLine(linePen, start, end);

        using var pointBrush = new SolidBrush(pointColor);
        g.FillEllipse(pointBrush, start.X - 4, start.Y - 4, 8, 8);
        g.FillEllipse(pointBrush, end.X - 4, end.Y - 4, 8, 8);

        using var dashPen = new Pen(dashColor, 1f);
        dashPen.DashStyle = DashStyle.Dash;
        g.DrawLine(dashPen, start.X, start.Y, end.X, start.Y);
        g.DrawLine(dashPen, end.X, start.Y, end.X, end.Y);

        int midX = (start.X + end.X) / 2;
        int midY = (start.Y + end.Y) / 2;

        double fromH = MeasureMath.FromHorizontal(start, end);
        double fromV = MeasureMath.FromVertical(start, end);
        var angleColor = Color.FromArgb(_elementAlpha, 255, 170, 60);

        // Angle arc at the start point, from the horizontal guide to the line.
        if (distance > 24)
        {
            float radius = (float)Math.Min(55, distance / 2);
            float refAngle = end.X >= start.X ? 0f : 180f;
            float lineAngle = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            float sweep = lineAngle - refAngle;
            while (sweep > 180f) sweep -= 360f;
            while (sweep < -180f) sweep += 360f;
            using var arcPen = new Pen(angleColor, 2f);
            g.DrawArc(arcPen, start.X - radius, start.Y - radius, radius * 2, radius * 2, refAngle, sweep);
        }

        string distanceText = $"{distance:F1} px";
        string angleText = $"{fromH:F1}° from horizontal · {fromV:F1}° from vertical";
        var distanceSize = g.MeasureString(distanceText, _distanceFont);
        var angleSize = g.MeasureString(angleText, _coordFont);
        float boxW = Math.Max(distanceSize.Width, angleSize.Width);
        float boxH = distanceSize.Height + angleSize.Height;

        var distanceBgRect = new RectangleF(midX - boxW / 2 - 5, midY - boxH / 2 - 5, boxW + 10, boxH + 10);

        using var elementBgBrush = new SolidBrush(bgColor);
        using var elementTextBrush = new SolidBrush(textColor);
        using var angleBrush = new SolidBrush(angleColor);
        using var borderPen = new Pen(lineColor, 1f);

        g.FillRectangle(elementBgBrush, distanceBgRect);
        g.DrawRectangle(borderPen, distanceBgRect.X, distanceBgRect.Y, distanceBgRect.Width, distanceBgRect.Height);
        g.DrawString(distanceText, _distanceFont, elementTextBrush, midX - distanceSize.Width / 2, midY - boxH / 2);
        g.DrawString(angleText, _coordFont, angleBrush, midX - angleSize.Width / 2, midY - boxH / 2 + distanceSize.Height);

        string hText = $"W: {Math.Abs(dx):F0} px";
        var hSize = g.MeasureString(hText, _coordFont);
        float hX = (start.X + end.X) / 2f - hSize.Width / 2;
        float hY = start.Y - hSize.Height - 5;

        using var cyanBrush = new SolidBrush(dashColor);
        g.FillRectangle(elementBgBrush, hX - 3, hY - 2, hSize.Width + 6, hSize.Height + 4);
        g.DrawString(hText, _coordFont, cyanBrush, hX, hY);

        string vText = $"H: {Math.Abs(dy):F0} px";
        var vSize = g.MeasureString(vText, _coordFont);
        float vX = end.X + 5;
        float vY = (start.Y + end.Y) / 2f - vSize.Height / 2;

        g.FillRectangle(elementBgBrush, vX - 3, vY - 2, vSize.Width + 6, vSize.Height + 4);
        g.DrawString(vText, _coordFont, cyanBrush, vX, vY);

        using var yellowBrush = new SolidBrush(pointColor);

        string startCoord = $"({start.X + this.Left}, {start.Y + this.Top})";
        var startCoordSize = g.MeasureString(startCoord, _coordFont);
        g.FillRectangle(elementBgBrush, start.X - startCoordSize.Width / 2 - 2, start.Y + 10, startCoordSize.Width + 4, startCoordSize.Height + 2);
        g.DrawString(startCoord, _coordFont, yellowBrush, start.X - startCoordSize.Width / 2, start.Y + 10);

        string endCoord = $"({end.X + this.Left}, {end.Y + this.Top})";
        var endCoordSize = g.MeasureString(endCoord, _coordFont);
        g.FillRectangle(elementBgBrush, end.X - endCoordSize.Width / 2 - 2, end.Y + 10, endCoordSize.Width + 4, endCoordSize.Height + 2);
        g.DrawString(endCoord, _coordFont, yellowBrush, end.X - endCoordSize.Width / 2, end.Y + 10);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PaintScene(e.Graphics);
        DrawMagnifier(e.Graphics);
    }

    // Zoomed view of the pixels under the cursor so points can be placed exactly.
    private void DrawMagnifier(Graphics g)
    {
        if (_backgroundImage == null) return;

        int srcSize = MagnifierRadius * 2 + 1;
        int boxSize = srcSize * MagnifierZoom;
        var src = new Rectangle(_cursor.X - MagnifierRadius, _cursor.Y - MagnifierRadius, srcSize, srcSize);

        // Sit beyond the end of the line (default below-right) so it never covers the labels;
        // flip to the other side near screen edges.
        bool right = true, down = true;
        if (_measureStart != null && _measureEnd != _measureStart.Value && _regionStart == null)
        {
            right = _measureEnd.X >= _measureStart.Value.X;
            down = _measureEnd.Y >= _measureStart.Value.Y;
        }
        const int gap = 40;
        int x = right ? _cursor.X + gap : _cursor.X - gap - boxSize;
        int y = down ? _cursor.Y + gap : _cursor.Y - gap - boxSize - 22;
        if (x + boxSize > ClientSize.Width) x = _cursor.X - gap - boxSize;
        if (x < 0) x = _cursor.X + gap;
        if (y + boxSize + 22 > ClientSize.Height) y = _cursor.Y - gap - boxSize - 22;
        if (y < 0) y = _cursor.Y + gap;
        var box = new Rectangle(x, y, boxSize, boxSize);

        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;
        g.FillRectangle(Brushes.Black, box);
        g.DrawImage(_backgroundImage, box, src, GraphicsUnit.Pixel);

        using var crossPen = new Pen(Color.FromArgb(160, Color.Red), 1f);
        int c = MagnifierRadius * MagnifierZoom;
        g.DrawLine(crossPen, box.X + c + MagnifierZoom / 2, box.Y, box.X + c + MagnifierZoom / 2, box.Bottom);
        g.DrawLine(crossPen, box.X, box.Y + c + MagnifierZoom / 2, box.Right, box.Y + c + MagnifierZoom / 2);
        g.DrawRectangle(Pens.White, box.X + c, box.Y + c, MagnifierZoom, MagnifierZoom);
        g.DrawRectangle(Pens.White, box);

        var px = _backgroundImage.GetPixel(
            Math.Clamp(_cursor.X, 0, _backgroundImage.Width - 1),
            Math.Clamp(_cursor.Y, 0, _backgroundImage.Height - 1));
        string info = $"({_cursor.X + Left}, {_cursor.Y + Top})  #{px.R:X2}{px.G:X2}{px.B:X2}";
        using var infoBg = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        g.FillRectangle(infoBg, box.X, box.Bottom, boxSize, 20);
        g.DrawString(info, _coordFont, Brushes.White, box.X + 4, box.Bottom + 3);
        g.Restore(state);
    }

    private void PaintScene(Graphics g)
    {
        if (_backgroundImage != null)
        {
            g.DrawImage(_backgroundImage, 0, 0);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        using var textBrush = new SolidBrush(Color.White);

        // Region screenshot mode
        if (_mode == OverlayMode.RegionScreenshot)
        {
            // Dim the background slightly
            using var dimBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
            g.FillRectangle(dimBrush, 0, 0, this.Width, this.Height);

            if (_regionStart != null)
            {
                var rect = GetRegionRect();

                // Draw the selected region from original image (undimmed)
                if (_backgroundImage != null && rect.Width > 0 && rect.Height > 0)
                {
                    g.DrawImage(_backgroundImage, rect, rect, GraphicsUnit.Pixel);
                }

                // Draw selection border
                using var selectionPen = new Pen(Color.FromArgb(_elementAlpha, Color.Lime), 2f);
                selectionPen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(selectionPen, rect);

                // Draw size label
                string sizeText = $"{rect.Width} x {rect.Height}";
                var sizeSize = g.MeasureString(sizeText, _coordFont);
                float labelX = rect.X + rect.Width / 2f - sizeSize.Width / 2;
                float labelY = rect.Y + rect.Height + 5;

                using var labelBgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
                g.FillRectangle(labelBgBrush, labelX - 5, labelY, sizeSize.Width + 10, sizeSize.Height + 4);
                g.DrawString(sizeText, _coordFont, textBrush, labelX, labelY + 2);
            }

            string instruction = _regionStart == null
                ? "Click and drag to select region | Right-click or ESC to cancel"
                : (_isDragging ? "Release to capture | Right-click or ESC to cancel" : "Ctrl+C to copy | Ctrl+S to save | Right-click or ESC to close");
            var instrSize = g.MeasureString(instruction, _coordFont);
            g.FillRectangle(bgBrush, 10, 10, instrSize.Width + 10, instrSize.Height + 6);
            g.DrawString(instruction, _coordFont, textBrush, 15, 13);
            return;
        }

        // Measure + Screenshot mode
        if (_mode == OverlayMode.MeasureAndScreenshot)
        {
            // Draw measurement if we have one
            if (_measureStart != null)
            {
                DrawMeasurement(g);
            }

            // If measurement complete, show region selection
            if (_measurementComplete)
            {
                // Dim the background slightly
                using var dimBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
                g.FillRectangle(dimBrush, 0, 0, this.Width, this.Height);

                // Redraw measurement on top of dim
                if (_measureStart != null)
                {
                    DrawMeasurement(g);
                }

                if (_regionStart != null)
                {
                    var rect = GetRegionRect();

                    // Draw the selected region from original image (undimmed) with measurement
                    if (_backgroundImage != null && rect.Width > 0 && rect.Height > 0)
                    {
                        g.DrawImage(_backgroundImage, rect, rect, GraphicsUnit.Pixel);

                        // Draw measurement within the region
                        var clipState = g.Save();
                        g.SetClip(rect);
                        DrawMeasurement(g);
                        g.Restore(clipState);
                    }

                    // Draw selection border
                    using var selectionPen = new Pen(Color.FromArgb(_elementAlpha, Color.Lime), 2f);
                    selectionPen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(selectionPen, rect);

                    // Draw size label
                    string sizeText = $"{rect.Width} x {rect.Height}";
                    var sizeSize = g.MeasureString(sizeText, _coordFont);
                    float labelX = rect.X + rect.Width / 2f - sizeSize.Width / 2;
                    float labelY = rect.Y + rect.Height + 5;

                    using var labelBgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
                    g.FillRectangle(labelBgBrush, labelX - 5, labelY, sizeSize.Width + 10, sizeSize.Height + 4);
                    g.DrawString(sizeText, _coordFont, textBrush, labelX, labelY + 2);
                }

                string instruction = _regionStart == null
                    ? "Now drag to select screenshot region | Right-click or ESC to cancel"
                    : (_isDragging ? "Release to capture | Right-click or ESC to cancel" : "Ctrl+C to copy | Ctrl+S to save | Right-click or ESC to close");
                var instrSize = g.MeasureString(instruction, _coordFont);
                g.FillRectangle(bgBrush, 10, 10, instrSize.Width + 10, instrSize.Height + 6);
                g.DrawString(instruction, _coordFont, textBrush, 15, 13);
            }
            else
            {
                // Phase 1: measuring
                string instruction = _measureStart == null
                    ? "Click and drag to measure | Right-click or ESC to cancel"
                    : (_isDragging ? "Release to set measurement | Right-click or ESC to cancel" : "");
                if (!string.IsNullOrEmpty(instruction))
                {
                    var instrSize = g.MeasureString(instruction, _coordFont);
                    g.FillRectangle(bgBrush, 10, 10, instrSize.Width + 10, instrSize.Height + 6);
                    g.DrawString(instruction, _coordFont, textBrush, 15, 13);
                }
            }
            return;
        }

        // Normal measurement mode
        if (_measureStart == null)
        {
            string startInstruction = "Click and drag to measure (hold Shift to snap the angle) | Right-click or ESC to cancel";
            var startSize = g.MeasureString(startInstruction, _coordFont);
            g.FillRectangle(bgBrush, 10, 10, startSize.Width + 10, startSize.Height + 6);
            g.DrawString(startInstruction, _coordFont, textBrush, 15, 13);
            return;
        }

        DrawMeasurement(g);

        string measureInstruction = _status ?? (_isDragging
            ? "Release to finish | Hold Shift to snap to 15° | Right-click or ESC to cancel"
            : "Arrows nudge end (Shift: 10 px) | Ctrl+C copy values | Ctrl+S save screenshot | Right-click or ESC to close");
        var instrSize2 = g.MeasureString(measureInstruction, _coordFont);
        using var elementBgBrush = new SolidBrush(Color.FromArgb(Math.Min(_elementAlpha, 220), 0, 0, 0));
        using var elementTextBrush = new SolidBrush(Color.FromArgb(_elementAlpha, Color.White));
        g.FillRectangle(elementBgBrush, 10, 10, instrSize2.Width + 10, instrSize2.Height + 6);
        g.DrawString(measureInstruction, _coordFont, elementTextBrush, 15, 13);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _distanceFont.Dispose();
            _coordFont.Dispose();
            _backgroundImage?.Dispose();
        }
        base.Dispose(disposing);
    }
}
