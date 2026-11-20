using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VictusQuiet
{
    // Shared look: colours, rounded shapes and icon font.
    static class Ui
    {
        public static readonly Color Canvas = Color.FromArgb(11,16,24), Sidebar = Color.FromArgb(15,21,31), Surface = Color.FromArgb(21,29,41),
            SurfaceHi = Color.FromArgb(29,39,54), Border = Color.FromArgb(37,49,66), Muted = Color.FromArgb(134,151,173), Faint = Color.FromArgb(88,104,126),
            Ink = Color.FromArgb(236,241,248), Accent = Color.FromArgb(45,212,191), AccentDeep = Color.FromArgb(20,70,72), Violet = Color.FromArgb(167,139,250),
            Warn = Color.FromArgb(251,191,36), Danger = Color.FromArgb(248,113,113);
        public const string TextFont = "Microsoft JhengHei UI";
        static string iconFamily, numberFamily;
        static bool Installed(string name)
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
                foreach (FontFamily family in fonts.Families) if (family.Name == name) return true;
            return false;
        }
        public static Font Icons(float size)
        {
            if (iconFamily == null) iconFamily = Installed("Segoe Fluent Icons") ? "Segoe Fluent Icons" : Installed("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : "";
            return iconFamily.Length == 0 ? null : new Font(iconFamily, size);
        }
        public static Font Numbers(float size)
        {
            if (numberFamily == null) numberFamily = Installed("Segoe UI Variable Display Semib") ? "Segoe UI Variable Display Semib" : "Segoe UI Semibold";
            return new Font(numberFamily, size);
        }
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        public static Color Outer(Control control) { return control.Parent != null ? control.Parent.BackColor : Canvas; }
        public static Color Mix(Color a, Color b, float t)
        { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
    }

    // Panel painted as a rounded card; BackColor is the card fill so child labels blend in.
    class RoundPanel : Panel
    {
        public int Radius = 12;
        public Color BorderColor = Ui.Border;
        public RoundPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
        }
        protected override void OnPaintBackground(PaintEventArgs args)
        {
            Graphics g = args.Graphics; g.Clear(Ui.Outer(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
            using (Brush fill = new SolidBrush(BackColor))
            using (Pen border = new Pen(BorderColor))
            { g.FillPath(fill, path); if (BorderColor.A > 0) g.DrawPath(border, path); }
        }
    }

    enum ButtonKind { Primary, Secondary, Nav }

    sealed class DashboardButton : Button
    {
        bool hovered, pressed, selected;
        public ButtonKind Kind = ButtonKind.Secondary;
        public string Glyph;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public DashboardButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs args) { hovered = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnMouseDown(MouseEventArgs args) { pressed = true; Invalidate(); base.OnMouseDown(args); }
        protected override void OnMouseUp(MouseEventArgs args) { pressed = false; Invalidate(); base.OnMouseUp(args); }
        protected override void OnEnabledChanged(EventArgs args) { Invalidate(); base.OnEnabledChanged(args); }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics; Color outer = Ui.Outer(this); g.Clear(outer); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, border, text;
            if (Kind == ButtonKind.Primary)
            {
                fill = Enabled ? Ui.Accent : Color.FromArgb(25,46,52); border = fill;
                text = Enabled ? Color.FromArgb(6,28,30) : Color.FromArgb(82,122,124);
                if (Enabled && hovered) fill = pressed ? Color.FromArgb(32,184,166) : Color.FromArgb(94,234,212);
            }
            else if (Kind == ButtonKind.Secondary)
            {
                fill = Enabled ? Ui.SurfaceHi : Ui.Mix(Ui.Surface, outer, .4f); border = Enabled ? Color.FromArgb(52,68,90) : Ui.Border;
                text = Enabled ? Ui.Ink : Ui.Faint;
                if (Enabled && hovered) { fill = pressed ? Color.FromArgb(35,47,65) : Color.FromArgb(40,54,74); border = Color.FromArgb(70,90,116); }
            }
            else
            {
                fill = selected ? Color.FromArgb(21,48,55) : hovered ? Ui.SurfaceHi : outer; border = fill;
                text = selected ? Ui.Accent : hovered ? Ui.Ink : Ui.Muted;
            }
            RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Ui.Round(box, 9))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            { g.FillPath(brush, path); g.DrawPath(pen, path); }
            if (Kind == ButtonKind.Nav && selected)
                using (GraphicsPath bar = Ui.Round(new RectangleF(0, Height * .28f, 3.5f, Height * .44f), 1.5f))
                using (Brush accent = new SolidBrush(Ui.Accent)) g.FillPath(accent, bar);

            int left = Kind == ButtonKind.Nav ? 16 : Padding.Left + 10;
            Font icons = Glyph == null ? null : Ui.Icons(Font.Size * 1.05f);
            Size label = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            int glyphWidth = icons == null ? 0 : (int)(Font.Size * 2.3f);
            bool centered = TextAlign != ContentAlignment.MiddleLeft;
            int start = centered ? Math.Max(left, (Width - label.Width - glyphWidth) / 2) : left;
            if (icons != null)
            {
                TextRenderer.DrawText(g, Glyph, icons, new Rectangle(start, 0, glyphWidth, Height), text, fill, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                icons.Dispose();
            }
            Rectangle area = Rectangle.FromLTRB(start + glyphWidth, 0, Width - 8, Height);
            TextRenderer.DrawText(g, Text, Font, area, text, fill, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
            if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(Color.FromArgb(150, Ui.Ink)) { DashStyle = DashStyle.Dot })
                using (GraphicsPath path = Ui.Round(new RectangleF(3, 3, Width - 7, Height - 7), 7)) g.DrawPath(focus, path);
        }
    }

    // Thin rounded bar; used for the quiet-session countdown and temperature headroom.
    sealed class MeterBar : Control
    {
        double value = Double.NaN;
        public double Maximum = 100;
        public bool ThresholdColours;
        public Color FillColor = Ui.Accent;
        public int BarHeight = 6;
        public double Value { get { return value; } set { this.value = value; Invalidate(); } }
        public MeterBar() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics; g.Clear(Ui.Outer(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(0, (Height - BarHeight) / 2f, Width - 1, BarHeight);
            using (GraphicsPath path = Ui.Round(track, BarHeight / 2f))
            using (Brush brush = new SolidBrush(Color.FromArgb(36,48,65))) g.FillPath(brush, path);
            if (Double.IsNaN(value) || value <= 0 || Maximum <= 0) return;
            double ratio = Math.Min(1, value / Maximum);
            Color colour = FillColor;
            // Amber only within 3 units of the automatic-restore threshold, red at or above it.
            if (ThresholdColours) colour = value >= Maximum ? Ui.Danger : value >= Maximum - 3 ? Ui.Warn : Ui.Accent;
            RectangleF bar = new RectangleF(track.X, track.Y, Math.Max(BarHeight, track.Width * (float)ratio), track.Height);
            using (GraphicsPath path = Ui.Round(bar, BarHeight / 2f))
            using (Brush brush = new LinearGradientBrush(new RectangleF(bar.X - 1, bar.Y, bar.Width + 2, bar.Height), Ui.Mix(colour, Ui.Canvas, .35f), colour, LinearGradientMode.Horizontal))
                g.FillPath(brush, path);
        }
    }

    sealed class SessionProgress : Control
    {
        readonly MeterBar bar = new MeterBar { Dock = DockStyle.Fill, Maximum = 300, BarHeight = 6 };
        public int Value { get { return (int)(Double.IsNaN(bar.Value) ? 0 : bar.Value); } set { bar.Value = Math.Max(0, Math.Min(300, value)); } }
        public SessionProgress() { Controls.Add(bar); }
        protected override void OnParentBackColorChanged(EventArgs args) { bar.Invalidate(); base.OnParentBackColorChanged(args); }
    }

    sealed class FanHistory : Control
    {
        sealed class PointSample { public DateTime At; public int First, Second; }
        readonly List<PointSample> samples = new List<PointSample>();
        public FanHistory() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        public void Add(DateTime at, int first, int second)
        {
            if (samples.Count > 0 && (at - samples[samples.Count-1].At).TotalSeconds < 0.8) return;
            samples.Add(new PointSample { At = at, First = first, Second = second });
            while (samples.Count > 0 && (at - samples[0].At).TotalSeconds > 122) samples.RemoveAt(0);
            Invalidate();
        }
        static void Legend(Graphics g, Font font, string text, Color colour, float right, float y, out float left)
        {
            SizeF size = g.MeasureString(text, font);
            left = right - size.Width;
            using (Brush ink = new SolidBrush(Ui.Muted)) g.DrawString(text, font, ink, left, y);
            using (Brush dot = new SolidBrush(colour)) g.FillEllipse(dot, left - 13, y + size.Height / 2 - 4, 8, 8);
            left -= 13;
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics; g.Clear(Ui.Outer(this)); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            RectangleF card = new RectangleF(0.5f, 12.5f, Width - 1.5f, Height - 26.5f);
            using (GraphicsPath path = Ui.Round(card, 12))
            using (Brush fill = new SolidBrush(Ui.Surface))
            using (Pen border = new Pen(Ui.Border)) { g.FillPath(fill, path); g.DrawPath(border, path); }

            Rectangle plot = Rectangle.FromLTRB(64, (int)card.Top + 58, Math.Max(80, Width - 24), Math.Max((int)card.Top + 90, (int)card.Bottom - 36));
            int ceiling = 4000;
            foreach (PointSample sample in samples) ceiling = Math.Max(ceiling, ((Math.Max(sample.First, sample.Second) + 999) / 1000) * 1000);
            using (Font heading = new Font(Ui.TextFont, 10.5f, FontStyle.Bold))
            using (Font small = new Font(Ui.TextFont, 9))
            using (Brush ink = new SolidBrush(Ui.Ink))
            using (Brush muted = new SolidBrush(Ui.Muted))
            using (Brush faint = new SolidBrush(Ui.Faint))
            using (Pen grid = new Pen(Color.FromArgb(34,45,61)) { DashStyle = DashStyle.Dash, DashPattern = new float[] { 3, 4 } })
            {
                g.DrawString("轉速變化", heading, ink, 20, card.Top + 18);
                g.DrawString("最近 2 分鐘", small, faint, 20 + g.MeasureString("轉速變化", heading).Width + 4, card.Top + 20);
                float left;
                Legend(g, small, "風扇 2", Ui.Violet, Width - 22, card.Top + 20, out left);
                Legend(g, small, "風扇 1", Ui.Accent, left - 18, card.Top + 20, out left);
                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Top + plot.Height * i / 4f;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string mark = (ceiling * (4 - i) / 4 / 1000.0).ToString("0.#") + "k";
                    SizeF size = g.MeasureString(mark, small);
                    g.DrawString(mark, small, faint, plot.Left - 12 - size.Width, y - size.Height / 2);
                }
                g.DrawString("2 分鐘前", small, faint, plot.Left - 4, plot.Bottom + 8);
                SizeF now = g.MeasureString("現在", small);
                g.DrawString("現在", small, faint, plot.Right - now.Width + 4, plot.Bottom + 8);
                if (samples.Count < 2)
                {
                    string waiting = "等待足夠的即時讀值…";
                    SizeF size = g.MeasureString(waiting, small);
                    g.DrawString(waiting, small, muted, plot.Left + (plot.Width - size.Width) / 2, plot.Top + (plot.Height - size.Height) / 2);
                }
            }
            if (samples.Count < 2) return;
            DateTime latest = samples[samples.Count-1].At;
            PointF[] first = new PointF[samples.Count], second = new PointF[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                float x = Math.Max(plot.Left, plot.Right - (float)(latest - samples[i].At).TotalSeconds / 120f * plot.Width);
                first[i] = new PointF(x, plot.Bottom - samples[i].First / (float)ceiling * plot.Height);
                second[i] = new PointF(x, plot.Bottom - samples[i].Second / (float)ceiling * plot.Height);
            }
            Series(g, second, Ui.Violet, plot);
            Series(g, first, Ui.Accent, plot);
        }
        static void Series(Graphics g, PointF[] points, Color colour, Rectangle plot)
        {
            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(points);
                area.AddLine(points[points.Length-1].X, points[points.Length-1].Y, points[points.Length-1].X, plot.Bottom);
                area.AddLine(points[points.Length-1].X, plot.Bottom, points[0].X, plot.Bottom);
                area.CloseFigure();
                using (Brush shade = new LinearGradientBrush(new RectangleF(plot.Left, plot.Top - 1, plot.Width, plot.Height + 2), Color.FromArgb(52, colour), Color.FromArgb(0, colour), LinearGradientMode.Vertical))
                    g.FillPath(shade, area);
            }
            using (Pen line = new Pen(colour, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(line, points);
            PointF end = points[points.Length-1];
            using (Brush glow = new SolidBrush(Color.FromArgb(60, colour))) g.FillEllipse(glow, end.X - 7, end.Y - 7, 14, 14);
            using (Brush dot = new SolidBrush(colour)) g.FillEllipse(dot, end.X - 3.5f, end.Y - 3.5f, 7, 7);
        }
    }

    // Rounded slider for any integer range; used for brightness (0-100 %) and fan levels (18-50).
    sealed class ValueSlider : Control
    {
        int value, minimum, maximum = 100;
        bool dragging, hovered;
        public int Step = 5;
        public int Marker = -1;
        Color fill = Ui.Warn;
        public event EventHandler ValueChanged;
        public Color FillColor { get { return fill; } set { if (fill == value) return; fill = value; Invalidate(); } }
        public int Minimum { get { return minimum; } set { minimum = value; this.value = Math.Max(minimum, this.value); Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = Math.Max(minimum + 1, value); this.value = Math.Min(maximum, this.value); Invalidate(); } }
        public int Value { get { return value; } set { int next = Math.Max(minimum, Math.Min(maximum, value)); if (next == this.value) return; this.value = next; Invalidate(); } }
        public ValueSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; Cursor = Cursors.Hand; Height = 34;
        }
        RectangleF Track { get { return new RectangleF(10, Height / 2f - 3, Math.Max(1, Width - 20), 6); } }
        float Ratio(int v) { return (v - minimum) / (float)(maximum - minimum); }
        void FromUser(int next) { int old = value; Value = next; if (value != old && ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        int FromX(int x) { RectangleF track = Track; return minimum + (int)Math.Round((x - track.X) / track.Width * (maximum - minimum)); }
        protected override void OnMouseDown(MouseEventArgs args) { Focus(); dragging = true; FromUser(FromX(args.X)); base.OnMouseDown(args); }
        protected override void OnMouseMove(MouseEventArgs args) { if (dragging) FromUser(FromX(args.X)); base.OnMouseMove(args); }
        protected override void OnMouseUp(MouseEventArgs args) { dragging = false; base.OnMouseUp(args); }
        protected override void OnMouseEnter(EventArgs args) { hovered = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { hovered = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnMouseWheel(MouseEventArgs args) { FromUser(value + (args.Delta > 0 ? Step : -Step)); base.OnMouseWheel(args); }
        protected override void OnGotFocus(EventArgs args) { Invalidate(); base.OnGotFocus(args); }
        protected override void OnLostFocus(EventArgs args) { Invalidate(); base.OnLostFocus(args); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Left || args.KeyCode == Keys.Down) FromUser(value - Step);
            else if (args.KeyCode == Keys.Right || args.KeyCode == Keys.Up) FromUser(value + Step);
            else if (args.KeyCode == Keys.PageDown) FromUser(value - Step * 2);
            else if (args.KeyCode == Keys.PageUp) FromUser(value + Step * 2);
            else if (args.KeyCode == Keys.Home) FromUser(minimum);
            else if (args.KeyCode == Keys.End) FromUser(maximum);
            base.OnKeyDown(args);
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics; g.Clear(Ui.Outer(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = Track;
            using (GraphicsPath path = Ui.Round(track, 3)) using (Brush brush = new SolidBrush(Color.FromArgb(38,50,68))) g.FillPath(brush, path);
            float x = track.X + track.Width * Ratio(value);
            if (x > track.X + 1)
                using (GraphicsPath path = Ui.Round(new RectangleF(track.X, track.Y, x - track.X, track.Height), 3))
                using (Brush brush = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height), Ui.Mix(fill, Ui.Canvas, .25f), fill, LinearGradientMode.Horizontal))
                    g.FillPath(brush, path);
            if (Marker > minimum && Marker < maximum)
            {
                float mx = track.X + track.Width * Ratio(Marker);
                using (Pen tick = new Pen(Color.FromArgb(150, Ui.Ink), 2)) g.DrawLine(tick, mx, track.Y - 6, mx, track.Bottom + 6);
            }
            float cy = Height / 2f, radius = hovered || dragging ? 9.5f : 8.5f;
            if (Focused) using (Brush glow = new SolidBrush(Color.FromArgb(60, fill))) g.FillEllipse(glow, x - radius - 5, cy - radius - 5, (radius + 5) * 2, (radius + 5) * 2);
            using (Brush thumb = new SolidBrush(Ui.Ink)) g.FillEllipse(thumb, x - radius, cy - radius, radius * 2, radius * 2);
            using (Pen ring = new Pen(fill, 2.4f)) g.DrawEllipse(ring, x - radius + 1.2f, cy - radius + 1.2f, radius * 2 - 2.4f, radius * 2 - 2.4f);
        }
    }

    // One monitor: name, live percentage, slider and presets. Writes are debounced and run off the UI thread.
    sealed class BrightnessCard : RoundPanel
    {
        readonly DisplayTarget target;
        readonly ValueSlider slider;
        readonly Label value, note;
        readonly System.Windows.Forms.Timer apply = new System.Windows.Forms.Timer { Interval = 150 };
        readonly bool preview;
        int pending = -1;
        bool working;
        public BrightnessCard(DisplayTarget target, bool preview)
        {
            this.target = target; this.preview = preview; Padding = new Padding(22,16,22,14);
            note = new Label { Dock = DockStyle.Fill, ForeColor = Ui.Faint, Font = new Font(Ui.TextFont, 9), Text = "", Padding = new Padding(0,6,0,0) };
            TableLayoutPanel presets = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            int[] levels = { 25, 50, 75, 100 };
            for (int i = 0; i < 4; i++)
            {
                int level = levels[i];
                presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                DashboardButton button = new DashboardButton { Kind = ButtonKind.Secondary, Text = level + "%", Font = new Font(Ui.TextFont, 9.5f), Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0) };
                button.Click += delegate { slider.Value = level; Request(level); };
                presets.Controls.Add(button, i, 0);
            }
            presets.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            slider = new ValueSlider { Dock = DockStyle.Top, Value = target.Percent };
            slider.ValueChanged += delegate { Request(slider.Value); };
            Panel gap = new Panel { Dock = DockStyle.Top, Height = 8 };
            value = new Label { Dock = DockStyle.Top, Height = 50, Text = target.Percent + "%", Font = Ui.Numbers(24), ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft };
            Panel head = new Panel { Dock = DockStyle.Top, Height = 28 };
            Font icons = Ui.Icons(13);
            // Docked side by side (icon | name | kind) so nothing overlaps.
            head.Controls.Add(new Label { Text = target.Name, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 6, 0), Font = new Font(Ui.TextFont, 12, FontStyle.Bold), ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
            head.Controls.Add(new Label { Text = target.Kind, Dock = DockStyle.Right, Width = 180, ForeColor = Ui.Muted, Font = new Font(Ui.TextFont, 9), TextAlign = ContentAlignment.MiddleRight });
            if (icons != null) head.Controls.Add(new Label { Text = target.Internal ? "" : "", Font = icons, ForeColor = Ui.Warn, Dock = DockStyle.Left, Width = 30, TextAlign = ContentAlignment.MiddleLeft });
            Controls.Add(note); Controls.Add(presets); Controls.Add(gap); Controls.Add(slider); Controls.Add(value); Controls.Add(head);
            apply.Tick += delegate { apply.Stop(); Push(); };
        }
        void Request(int percent) { pending = percent; value.Text = percent + "%"; apply.Stop(); apply.Start(); }
        async void Push()
        {
            if (working || pending < 0) return;
            int percent = pending; pending = -1; working = true; string failure = null;
            try { if (!preview) await Task.Run(delegate { target.SetPercent(percent); }); }
            catch (Exception error) { failure = error.Message; }
            working = false;
            if (IsDisposed) return;
            note.Text = failure ?? ""; note.ForeColor = failure == null ? Ui.Faint : Ui.Danger;
            if (pending >= 0) Push();
        }
        protected override void Dispose(bool disposing) { if (disposing) { apply.Stop(); apply.Dispose(); } base.Dispose(disposing); }
    }

    // Fixed values for --render-display; never touches real screens.
    sealed class PreviewDisplay : DisplayTarget
    {
        public PreviewDisplay(string name, string kind, bool isInternal, int current) { Name = name; Kind = kind; Internal = isInternal; Minimum = 0; Maximum = 100; Current = current; }
        public override void SetPercent(int percent) { Current = percent; }
    }

    sealed class QuietWindow : Form
    {
        static readonly Color Canvas = Ui.Canvas, Surface = Ui.Surface, Muted = Ui.Muted, Ink = Ui.Ink, Accent = Ui.Accent;
        readonly bool preview;
        readonly string previewScene;
        readonly Panel[] pages = new Panel[4];
        readonly DashboardButton[] navigation = new DashboardButton[4];
        readonly List<DisplayTarget> displays = new List<DisplayTarget>();
        Panel displayHost;
        Label displayNote;
        DashboardButton rescan;
        bool displaysLoaded, scanning;
        readonly List<Button> quietButtons = new List<Button>(), autoButtons = new List<Button>();
        int selectedLevel=FanLevels.Quiet;
        ValueSlider speedSlider; Label speedValue, speedZone, speedNote; DashboardButton applySpeed;
        readonly List<string> events = new List<string>();
        readonly Label title, subtitle, connection, fan1, fan2, zone, gpu, load, currentMode, countdown, controlMode, controlTimer, status, refreshed;
        readonly Button admin;
        readonly SessionProgress sessionProgress;
        readonly MeterBar zoneBar, gpuBar;
        readonly FanHistory history;
        readonly TextBox eventLog;
        readonly System.Windows.Forms.Timer timer;
        HpQuietDevice device;
        ProcessRecoveryGuard guard;
        QuietSession session;
        bool busy, allowClose, closePending;
        string error = "", loggedStatus = "";

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string list);
        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            try
            {
                int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4);           // dark title bar
                int caption = Ui.Sidebar.R | (Ui.Sidebar.G << 8) | (Ui.Sidebar.B << 16);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);                    // Windows 11 caption colour
            }
            catch (Exception) { }
        }

        public QuietWindow(bool preview, string scene = "quiet")
        {
            this.preview = preview; previewScene = scene;
            Text = "Victus 控制中心"; Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); ClientSize = new Size(1080,700); MinimumSize = new Size(960,690);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font(Ui.TextFont,10); BackColor = Canvas; ForeColor = Ink;

            // Sidebar: logo, navigation and device identity.
            Panel side = new Panel { Dock = DockStyle.Left, Width = 196, BackColor = Ui.Sidebar };
            side.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (Pen line = new Pen(Color.FromArgb(28,37,51))) args.Graphics.DrawLine(line, side.Width - 1, 0, side.Width - 1, side.Height);
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias; args.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // The app icon is itself a rounded tile; use the 48 px frame scaled to 42 px.
                if (Icon != null) using (Icon large = new Icon(Icon, 48, 48)) using (Bitmap picture = large.ToBitmap()) args.Graphics.DrawImage(picture, new Rectangle(19, 25, 42, 42));
            };
            Label brand = LabelAt("VICTUS",70,24,120,28,15,Ink); brand.Font = new Font("Bahnschrift SemiBold", 16); side.Controls.Add(brand);
            side.Controls.Add(LabelAt("控制中心",71,50,120,20,9,Muted));
            side.Controls.Add(LabelAt("功能",22,104,120,20,8,Ui.Faint));
            string[] names = { "即時總覽", "風扇控制", "保護與紀錄", "螢幕亮度" };
            string[] glyphs = { "", "", "", "" };
            for (int i=0; i<4; i++)
            {
                int page = i;
                DashboardButton button = new DashboardButton { Kind = ButtonKind.Nav, Text = names[i], Glyph = glyphs[i], Font = new Font(Ui.TextFont, 10.5f), TextAlign = ContentAlignment.MiddleLeft, Location = new Point(12, 128 + i*52), Size = new Size(172, 44) };
                button.Click += delegate { ShowPage(page); }; navigation[i]=button; side.Controls.Add(button);
            }
            RoundPanel identity = new RoundPanel { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.FromArgb(19,27,39), BorderColor = Color.FromArgb(30,40,55), Padding = new Padding(14,10,10,8) };
            identity.Controls.Add(new Label { Text = "HP 88F8 · BIOS F.25\r\n每 2 秒更新讀值", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font(Ui.TextFont, 8.5f) });
            identity.Controls.Add(new Label { Text = "本機控制", Dock = DockStyle.Top, Height = 24, ForeColor = Ink, Font = new Font(Ui.TextFont, 9, FontStyle.Bold) });
            Panel identityHost = new Panel { Dock = DockStyle.Bottom, Height = 108, Padding = new Padding(12,0,12,16) };
            identityHost.Controls.Add(identity); identity.Dock = DockStyle.Fill; side.Controls.Add(identityHost);
            Controls.Add(side);

            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28,22,28,16) };
            Controls.Add(main); main.BringToFront();
            Panel header = new Panel { Dock = DockStyle.Top, Height = 84 };
            title = LabelAt("即時總覽",0,0,520,40,21,Ink); title.Font = new Font(Ui.TextFont, 21, FontStyle.Bold);
            subtitle = LabelAt("查看即時狀態，快速切換已驗證的安靜模式。",2,46,560,26,10,Muted);
            connection = LabelAt("尚未取得控制權限",0,0,240,24,9,Muted); connection.TextAlign = ContentAlignment.MiddleRight;
            admin = new DashboardButton { Kind = ButtonKind.Primary, Text = "取得管理員權限", Glyph = "", Font = new Font(Ui.TextFont, 10), Size = new Size(190,38) };
            header.Controls.AddRange(new Control[] { title,subtitle,connection,admin });
            header.Resize += delegate { connection.Location = new Point(header.Width-244,4); admin.Location = new Point(header.Width-190,34); };

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(0,10,0,0) };
            RoundPanel statusCard = new RoundPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17,24,35), BorderColor = Color.FromArgb(30,40,55), Padding = new Padding(44,6,14,4) };
            statusCard.Paint += delegate(object sender, PaintEventArgs args)
            {
                Font icons = Ui.Icons(12); if (icons == null) return;
                TextRenderer.DrawText(args.Graphics, "", icons, new Rectangle(14,0,24,statusCard.Height), Muted, TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
                icons.Dispose();
            };
            status = new Label { Text = "開啟面板不會直接改變風扇。", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font(Ui.TextFont, 9), TextAlign = ContentAlignment.MiddleLeft };
            statusCard.Controls.Add(status); footer.Controls.Add(statusCard);
            Panel host = new Panel { Dock = DockStyle.Fill };
            main.Controls.Add(host); main.Controls.Add(footer); main.Controls.Add(header);
            for(int i=0;i<4;i++) { pages[i]=new Panel { Dock=DockStyle.Fill }; host.Controls.Add(pages[i]); }
            BuildDisplayPage(pages[3]);

            // Overview: live metric cards, history and quick controls.
            TableLayoutPanel cards = Grid(4); cards.Dock=DockStyle.Top; cards.Height=126;
            fan1=Metric(cards,0,"風扇 1","RPM · 估計",Accent,null,null);
            fan2=Metric(cards,1,"風扇 2","RPM · 估計",Ui.Violet,null,null);
            zoneBar=new MeterBar { Maximum=60, ThresholdColours=true };
            gpuBar=new MeterBar { Maximum=55, ThresholdColours=true };
            zone=Metric(cards,2,"ACPI 熱區","°C · 非核心溫度",Ink,zoneBar,"60°C 恢復");
            gpu=Metric(cards,3,"GPU 溫度","°C · NVIDIA",Ink,gpuBar,"55°C 恢復");
            Panel summary = new Panel { Dock=DockStyle.Top, Height=40 };
            load=LabelAt("CPU 負載 —　 GPU 負載 —",2,14,470,24,10,Muted);
            refreshed=LabelAt("等待連線",0,14,200,24,9,Ui.Faint); refreshed.TextAlign=ContentAlignment.MiddleRight;
            summary.Controls.AddRange(new Control[]{load,refreshed}); summary.Resize+=delegate{refreshed.Location=new Point(summary.Width-200,14);};
            RoundPanel actions = new RoundPanel { Dock=DockStyle.Bottom, Height=114, Padding=new Padding(18,12,18,16) };
            Panel actionRow = new Panel { Dock=DockStyle.Top, Height=34 };
            currentMode=LabelAt("目前：原有管理方式",0,2,480,28,11,Ink); currentMode.Font=new Font(Ui.TextFont,11,FontStyle.Bold);
            countdown=LabelAt("每次低速最多 5 分鐘",0,4,260,26,9,Muted); countdown.TextAlign=ContentAlignment.MiddleRight;
            actionRow.Controls.AddRange(new Control[]{currentMode,countdown});
            actionRow.Resize+=delegate{countdown.Location=new Point(actionRow.Width-260,4);};
            TableLayoutPanel quick = Grid(3); quick.Dock=DockStyle.Fill;
            Button quickQuiet=LevelButton("安靜 · 1800 RPM",FanLevels.Quiet,ButtonKind.Primary,""); quickQuiet.Margin=new Padding(0,0,6,0); quick.Controls.Add(quickQuiet,0,0);
            Button quickBoost=LevelButton("最大散熱 · 5000 RPM",FanLevels.Maximum,ButtonKind.Secondary,""); quickBoost.Margin=new Padding(6,0,6,0); quick.Controls.Add(quickBoost,1,0);
            Button quickAuto=AutoButton(); quickAuto.Margin=new Padding(6,0,0,0); quick.Controls.Add(quickAuto,2,0);
            actions.Controls.Add(quick); actions.Controls.Add(actionRow);
            history=new FanHistory { Dock=DockStyle.Fill };
            pages[0].Controls.Add(history); pages[0].Controls.Add(actions); pages[0].Controls.Add(summary); pages[0].Controls.Add(cards);

            // Controls: two explicit modes, each backed by the existing controller.
            RoundPanel modeHead=new RoundPanel { Dock=DockStyle.Top,Height=104,Padding=new Padding(22,0,22,16) };
            modeHead.Controls.Add(LabelAt("目前模式",22,14,300,20,9,Ui.Faint));
            controlMode=LabelAt("原有管理方式",20,34,560,40,19,Ink); controlMode.Font=new Font(Ui.TextFont,19,FontStyle.Bold);
            controlTimer=LabelAt("尚未手動設定",0,44,380,24,10,Muted); controlTimer.TextAlign=ContentAlignment.MiddleRight;
            modeHead.Controls.AddRange(new Control[]{controlMode,controlTimer});
            modeHead.Resize+=delegate{controlTimer.Location=new Point(modeHead.Width-402,44);};
            sessionProgress=new SessionProgress { Dock=DockStyle.Bottom,Height=12,Value=0 }; modeHead.Controls.Add(sessionProgress);
            TableLayoutPanel modeTiles=Grid(2); modeTiles.Dock=DockStyle.Top; modeTiles.Height=286; modeTiles.Padding=new Padding(0,14,0,0);
            modeTiles.ColumnStyles[0].Width=62; modeTiles.ColumnStyles[1].Width=38;
            Panel speedTile=SpeedTile(); speedTile.Margin=new Padding(0,0,7,0); modeTiles.Controls.Add(speedTile,0,0);
            Panel autoTile=ModeTile("","原廠自動管理","依溫度調整","解除本工具的手動轉速，交回 BIOS 管理散熱。",Ink,AutoButton());
            autoTile.Margin=new Padding(7,0,0,0); modeTiles.Controls.Add(autoTile,1,0);
            Panel conditionHost=new Panel { Dock=DockStyle.Fill,Padding=new Padding(0,14,0,0) };
            RoundPanel conditions=new RoundPanel { Dock=DockStyle.Fill,Padding=new Padding(20,12,20,12) };
            TableLayoutPanel chips=Grid(4); chips.Dock=DockStyle.Top; chips.Height=58;
            string[,] rules={{"ACPI 熱區","低於 60°C"},{"GPU 溫度","低於 55°C"},{"CPU 負載","不超過 25%"},{"GPU 負載","不超過 30%"}};
            for(int i=0;i<4;i++) chips.Controls.Add(Chip(rules[i,0],rules[i,1],i),i,0);
            conditions.Controls.Add(chips);
            conditions.Controls.Add(new Label { Text="低速（3300 RPM 以下）的啟用條件　·　3300 RPM 以上不受限制",Dock=DockStyle.Top,Height=28,ForeColor=Ink,Font=new Font(Ui.TextFont,10.5f,FontStyle.Bold) });
            conditionHost.Controls.Add(conditions);
            pages[1].Controls.Add(conditionHost); pages[1].Controls.Add(modeTiles); pages[1].Controls.Add(modeHead);

            // Protection details and a session-only activity log.
            TableLayoutPanel protection=Grid(2); protection.Dock=DockStyle.Fill;
            RoundPanel rulesCard=new RoundPanel { Dock=DockStyle.Fill,Margin=new Padding(0,0,7,0),Padding=new Padding(22,18,20,18) };
            string[] restore={"ACPI 熱區達 60°C","GPU 溫度達 55°C","運算負載升高","溫度或風扇讀值失效","本輪低速滿五分鐘","正常關閉程式"};
            Label backup=new Label { Dock=DockStyle.Bottom,Height=76,ForeColor=Accent,Text="以上條件只適用 3300 RPM 以下的低速；3300 RPM 以上的加強散熱只會在讀值失效或風扇沒升上去時恢復。\r\n另有獨立備援程序，在主程式中斷時嘗試恢復散熱。",TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Ui.TextFont,9.5f) };
            backup.Dock=DockStyle.Top; rulesCard.Controls.Add(backup);
            for(int i=restore.Length-1;i>=0;i--) rulesCard.Controls.Add(RuleRow(restore[i]));
            rulesCard.Controls.Add(CardTitle("","自動恢復條件"));
            RoundPanel logs=new RoundPanel { Dock=DockStyle.Fill,Margin=new Padding(7,0,0,0),Padding=new Padding(22,18,14,14) };
            eventLog=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Surface,ForeColor=Muted,Font=new Font(Ui.TextFont,9.5f) };
            eventLog.HandleCreated+=delegate{try{SetWindowTheme(eventLog.Handle,"DarkMode_Explorer",null);}catch(Exception){}};
            logs.Controls.Add(eventLog); logs.Controls.Add(CardTitle("","本次操作紀錄"));
            protection.Controls.Add(rulesCard,0,0); protection.Controls.Add(logs,1,0); pages[2].Controls.Add(protection);

            admin.Click+=delegate { if(preview)return; try { ProcessStartInfo start=new ProcessStartInfo(Application.ExecutablePath); start.UseShellExecute=true; start.Verb="runas"; Process.Start(start); allowClose=true; Close(); } catch(Win32Exception failure) { error=failure.NativeErrorCode==1223?"已取消提升權限，未改變風扇。":failure.Message; UpdateView(false); } };
            timer=new System.Windows.Forms.Timer { Interval=2000 };
            timer.Tick+=async delegate { if(!busy && session!=null) await Run(delegate{session.Poll();},false); };
            Shown+=async delegate
            {
                Rectangle screen=Screen.FromControl(this).WorkingArea;
                if(Width>screen.Width || Height>screen.Height) { Size=new Size(Math.Min(Width,screen.Width),Math.Min(Height,screen.Height)); Location=screen.Location; }
                if(preview) { PaintPreview(); return; }
                Log("控制面板已開啟；尚未變更風扇。");
                if(!Program.IsAdmin()) { UpdateView(false); return; }
                await Run(delegate{device=new HpQuietDevice();guard=new ProcessRecoveryGuard();session=new QuietSession(device,guard,()=>HpQuietDevice.Seconds);session.Poll();});
                timer.Start();
            };
            FormClosing+=async delegate(object sender,FormClosingEventArgs args)
            {
                if(allowClose || preview)return;
                if(busy){args.Cancel=true;closePending=true;return;}
                if(session==null || !session.NeedsRecovery)return;
                args.Cancel=true;timer.Stop();
                await Run(delegate{session.Restore("已恢復自動散熱，可以關閉。");});
                if(!session.NeedsRecovery){allowClose=true;Close();}else timer.Start();
            };
            FormClosed+=delegate{timer.Stop();timer.Dispose();if(guard!=null)guard.Dispose();if(device!=null)device.Dispose();foreach(DisplayTarget display in displays)display.Dispose();displays.Clear();};
            ShowPage(0); UpdateView(false);
        }

        Label LabelAt(string text,int x,int y,int width,int height,float size,Color color)
        { return new Label { Text=text,Location=new Point(x,y),Size=new Size(width,height),Font=new Font(Ui.TextFont,size),ForeColor=color }; }
        TableLayoutPanel Grid(int columns)
        {
            TableLayoutPanel table=new TableLayoutPanel{ColumnCount=columns,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
            for(int i=0;i<columns;i++)table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
            table.RowStyles.Add(new RowStyle(SizeType.Percent,100));return table;
        }
        Label Metric(TableLayoutPanel parent,int column,string name,string unit,Color color,MeterBar bar,string limit)
        {
            RoundPanel card=new RoundPanel{Dock=DockStyle.Fill,Margin=new Padding(column==0?0:6,0,column==3?0:6,0),Padding=new Padding(16,13,16,11)};
            Panel head=new Panel{Dock=DockStyle.Top,Height=22};
            head.Controls.Add(new Label{Text=name,Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font(Ui.TextFont,9.5f)});
            if(limit!=null)head.Controls.Add(new Label{Text=limit,Dock=DockStyle.Right,Width=80,ForeColor=Ui.Faint,Font=new Font(Ui.TextFont,8.5f),TextAlign=ContentAlignment.TopRight});
            card.Controls.Add(head);
            Label value=new Label{Text="—",Dock=DockStyle.Fill,Font=Ui.Numbers(23),ForeColor=color,TextAlign=ContentAlignment.MiddleLeft};
            card.Controls.Add(value);value.BringToFront();
            if(bar!=null){bar.Dock=DockStyle.Bottom;bar.Height=12;card.Controls.Add(bar);}
            else card.Controls.Add(new Panel{Dock=DockStyle.Bottom,Height=12});
            card.Controls.Add(new Label{Text=unit,Dock=DockStyle.Bottom,Height=19,Font=new Font(Ui.TextFont,8.5f),ForeColor=Ui.Faint});
            parent.Controls.Add(card,column,0);return value;
        }
        Control Chip(string name,string value,int column)
        {
            RoundPanel chip=new RoundPanel{Dock=DockStyle.Fill,BackColor=Ui.SurfaceHi,BorderColor=Color.FromArgb(40,53,72),Radius=9,Margin=new Padding(column==0?0:5,0,column==3?0:5,0),Padding=new Padding(12,7,8,6)};
            chip.Controls.Add(new Label{Text=value,Dock=DockStyle.Fill,ForeColor=Ink,Font=new Font(Ui.TextFont,10.5f,FontStyle.Bold)});
            chip.Controls.Add(new Label{Text=name,Dock=DockStyle.Top,Height=20,ForeColor=Muted,Font=new Font(Ui.TextFont,8.5f)});
            return chip;
        }
        Control CardTitle(string glyph,string text)
        {
            Panel row=new Panel{Dock=DockStyle.Top,Height=44};
            Font icons=Ui.Icons(13);
            if(icons!=null)row.Controls.Add(new Label{Text=glyph,Font=icons,ForeColor=Accent,Location=new Point(0,4),Size=new Size(28,28),TextAlign=ContentAlignment.MiddleLeft});
            row.Controls.Add(new Label{Text=text,Font=new Font(Ui.TextFont,13,FontStyle.Bold),ForeColor=Ink,Location=new Point(icons==null?0:30,2),Size=new Size(300,32)});
            return row;
        }
        Control RuleRow(string text)
        {
            Panel row=new Panel{Dock=DockStyle.Top,Height=36};
            row.Paint+=delegate(object sender,PaintEventArgs args)
            {
                args.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(Brush dot=new SolidBrush(Ui.AccentDeep))args.Graphics.FillEllipse(dot,0,9,18,18);
                using(Pen tick=new Pen(Accent,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                    args.Graphics.DrawLines(tick,new[]{new PointF(5,18.2f),new PointF(8,21),new PointF(13.2f,15)});
                using(Pen line=new Pen(Color.FromArgb(30,40,55)))args.Graphics.DrawLine(line,30,row.Height-1,row.Width,row.Height-1);
            };
            row.Controls.Add(new Label{Text=text,Location=new Point(30,6),Size=new Size(360,26),ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft});
            return row;
        }
        Button LevelButton(string text,int level,ButtonKind kind,string glyph)
        {
            DashboardButton button=new DashboardButton{Kind=kind,Text=text,Glyph=glyph,Font=new Font(Ui.TextFont,10.5f,kind==ButtonKind.Primary?FontStyle.Bold:FontStyle.Regular),Dock=DockStyle.Fill,Margin=Padding.Empty,Tag=level};quietButtons.Add(button);
            button.Click+=async delegate{if(session!=null)await Run(delegate{session.Enable(level);});};return button;
        }
        Panel SpeedTile()
        {
            RoundPanel card=new RoundPanel{Dock=DockStyle.Fill,Padding=new Padding(22,18,22,18)};
            Panel head=new Panel{Dock=DockStyle.Top,Height=26};
            Font icons=Ui.Icons(12);
            if(icons!=null)head.Controls.Add(new Label{Text="",Font=icons,ForeColor=Muted,Location=new Point(0,3),Size=new Size(24,22)});
            head.Controls.Add(new Label{Text="手動轉速　1800～5000 RPM",ForeColor=Muted,Location=new Point(icons==null?0:26,2),Size=new Size(320,24)});
            Panel valueRow=new Panel{Dock=DockStyle.Top,Height=42};
            speedValue=new Label{Dock=DockStyle.Fill,Font=new Font(Ui.TextFont,19,FontStyle.Bold),ForeColor=Accent,TextAlign=ContentAlignment.MiddleLeft};
            speedZone=new Label{Dock=DockStyle.Right,Width=150,Font=new Font(Ui.TextFont,9.5f,FontStyle.Bold),ForeColor=Accent,TextAlign=ContentAlignment.MiddleRight};
            valueRow.Controls.Add(speedValue); valueRow.Controls.Add(speedZone);
            speedSlider=new ValueSlider{Dock=DockStyle.Top,Minimum=FanLevels.Minimum,Maximum=FanLevels.Maximum,Step=1,Marker=FanLevels.BoostFrom,FillColor=Accent};
            speedSlider.Value=selectedLevel;
            speedSlider.ValueChanged+=delegate{selectedLevel=speedSlider.Value;UpdateView(false);};
            Panel gap=new Panel{Dock=DockStyle.Top,Height=6};
            TableLayoutPanel presets=Grid(5); presets.Dock=DockStyle.Top; presets.Height=34;
            int[] levels={18,25,33,42,50};
            for(int i=0;i<5;i++)
            {
                int level=levels[i];
                DashboardButton preset=new DashboardButton{Kind=ButtonKind.Secondary,Text=FanLevels.Rpm(level).ToString(),Font=new Font(Ui.TextFont,9.5f),Dock=DockStyle.Fill,Margin=new Padding(i==0?0:3,0,i==4?0:3,0)};
                preset.Click+=delegate{speedSlider.Value=level;selectedLevel=level;UpdateView(false);};
                presets.Controls.Add(preset,i,0);
            }
            speedNote=new Label{Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font(Ui.TextFont,9),Padding=new Padding(0,8,0,0)};
            applySpeed=new DashboardButton{Kind=ButtonKind.Primary,Text="套用",Glyph="",Font=new Font(Ui.TextFont,10.5f,FontStyle.Bold),Dock=DockStyle.Bottom,Height=42};
            applySpeed.Click+=async delegate{int level=selectedLevel;if(session!=null)await Run(delegate{session.Enable(level);});};
            card.Controls.Add(speedNote);card.Controls.Add(applySpeed);card.Controls.Add(presets);card.Controls.Add(gap);card.Controls.Add(speedSlider);card.Controls.Add(valueRow);card.Controls.Add(head);
            return card;
        }
        Button AutoButton()
        {
            DashboardButton button=new DashboardButton{Kind=ButtonKind.Secondary,Text="恢復自動散熱",Glyph="",Font=new Font(Ui.TextFont,10.5f),Dock=DockStyle.Fill,Margin=Padding.Empty};autoButtons.Add(button);
            button.Click+=async delegate{if(session!=null)await Run(delegate{session.Restore("已恢復原廠自動散熱。");});};return button;
        }
        Panel ModeTile(string glyph,string heading,string value,string description,Color valueColor,Button button)
        {
            RoundPanel card=new RoundPanel{Dock=DockStyle.Fill,Padding=new Padding(22,18,22,20)};
            Panel head=new Panel{Dock=DockStyle.Top,Height=28};
            Font icons=Ui.Icons(12);
            if(icons!=null)head.Controls.Add(new Label{Text=glyph,Font=icons,ForeColor=Muted,Location=new Point(0,3),Size=new Size(24,22)});
            head.Controls.Add(new Label{Text=heading,ForeColor=Muted,Location=new Point(icons==null?0:26,2),Size=new Size(300,24)});
            Label large=new Label{Text=value,Dock=DockStyle.Top,Height=46,Font=new Font(Ui.TextFont,19,FontStyle.Bold),ForeColor=valueColor};
            Label text=new Label{Text=description,Dock=DockStyle.Fill,ForeColor=Muted,Padding=new Padding(0,6,0,0)};
            button.Dock=DockStyle.Bottom;button.Height=44;
            card.Controls.Add(text);card.Controls.Add(button);card.Controls.Add(large);card.Controls.Add(head);return card;
        }
        void ShowPage(int index)
        {
            string[] titles={"即時總覽","風扇控制","保護與紀錄","螢幕亮度"};
            string[] descriptions={"查看即時狀態，快速切換安靜、最大散熱或原廠自動。","在 1800～5000 RPM 之間選擇轉速，或交回原廠自動管理。","查看恢復條件，以及本次開啟期間的操作紀錄。","調整內建與外接螢幕的亮度，不需要管理員權限。"};
            for(int i=0;i<4;i++){pages[i].Visible=i==index;navigation[i].Selected=i==index;}
            pages[index].BringToFront();title.Text=titles[index];subtitle.Text=descriptions[index];
            if(index==3 && !displaysLoaded && !preview) { var ignored=ScanDisplays(); }
        }
        void BuildDisplayPage(Panel page)
        {
            Panel bar=new Panel{Dock=DockStyle.Top,Height=54};
            displayNote=LabelAt("尚未偵測螢幕",2,12,620,26,10,Muted);
            rescan=new DashboardButton{Kind=ButtonKind.Secondary,Text="重新偵測",Glyph="",Font=new Font(Ui.TextFont,10),Size=new Size(140,38)};
            rescan.Click+=async delegate{await ScanDisplays();};
            bar.Controls.AddRange(new Control[]{displayNote,rescan});
            bar.Resize+=delegate{rescan.Location=new Point(bar.Width-140,2);};
            displayHost=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
            displayHost.Resize+=delegate{LayoutDisplays();};
            Label hint=new Label{Dock=DockStyle.Bottom,Height=30,ForeColor=Ui.Faint,Font=new Font(Ui.TextFont,9),TextAlign=ContentAlignment.MiddleLeft,
                Text="外接螢幕需要在螢幕本身的選單開啟 DDC/CI。亮度直接寫進螢幕，關掉這個程式後也會保留。"};
            page.Controls.Add(displayHost); page.Controls.Add(hint); page.Controls.Add(bar);
        }
        void ShowDisplays(List<DisplayTarget> found)
        {
            foreach(Control card in new List<Control>(displayHost.Controls.Cast<Control>())){displayHost.Controls.Remove(card);card.Dispose();}
            foreach(DisplayTarget display in displays)display.Dispose();
            displays.Clear();
            foreach(DisplayTarget display in found){displays.Add(display);displayHost.Controls.Add(new BrightnessCard(display,preview));}
            LayoutDisplays();
        }
        void LayoutDisplays()
        {
            if(displayHost==null)return;
            int gap=14,count=displayHost.Controls.Count,columns=displayHost.ClientSize.Width>=640?2:1;
            int width=(displayHost.ClientSize.Width-gap*(columns-1))/columns;
            for(int i=0;i<count;i++)displayHost.Controls[i].Bounds=new Rectangle((i%columns)*(width+gap),(i/columns)*(206+gap),width,206);
        }
        async Task ScanDisplays()
        {
            if(scanning)return; scanning=true; rescan.Enabled=false; displayNote.Text="正在偵測螢幕…";
            List<DisplayTarget> found=null; string failure=null;
            try{found=await Task.Run(delegate{return DisplayBrightness.Discover();});}catch(Exception error){failure=error.Message;}
            if(IsDisposed){if(found!=null)foreach(DisplayTarget display in found)display.Dispose();return;}
            ShowDisplays(found??new List<DisplayTarget>());
            displayNote.Text=failure!=null?"偵測失敗："+failure:displays.Count==0?"找不到可以調整亮度的螢幕。":"找到 "+displays.Count+" 個螢幕，拖曳滑桿或按百分比按鈕就能調整。";
            scanning=false; rescan.Enabled=true; displaysLoaded=true;
        }
        void Log(string message)
        {
            events.Add(DateTime.Now.ToString("HH:mm:ss")+"   "+message);if(events.Count>120)events.RemoveAt(0);
            eventLog.Text=String.Join(Environment.NewLine+Environment.NewLine,events.ToArray());eventLog.SelectionStart=eventLog.TextLength;eventLog.ScrollToCaret();
        }
        async Task Run(Action action,bool clearError=true)
        {
            if(busy)return;busy=true;if(clearError)error="";UpdateView(false);
            try{await Task.Run(action);}catch(Exception failure){error=failure.Message;}
            finally{busy=false;if(!IsDisposed)UpdateView(true);if(closePending && !IsDisposed){closePending=false;BeginInvoke(new Action(Close));}}
        }
        static string Value(double value){return Double.IsNaN(value)?"—":value.ToString("0");}
        static string ModeName(CoolingMode state,int level)
        {
            if(state==CoolingMode.RecoveryFailed)return "恢復待確認";
            if(state!=CoolingMode.Quiet)return "原有管理方式";
            if(level==FanLevels.Quiet)return "安靜模式 · 1800 RPM";
            return (FanLevels.IsBoost(level)?"加強散熱 · ":"低速 · ")+FanLevels.Rpm(level)+" RPM";
        }
        // One rule for every speed button: valid readings, no pending recovery, not already running that level,
        // and low speeds only when the controller would accept them.
        bool CanApply(int level,FanSample data,CoolingMode state,int current,bool connected)
        {
            return !busy && connected && data!=null && data.Valid && state!=CoolingMode.RecoveryFailed
                && !(state==CoolingMode.Quiet && current==level)
                && (FanLevels.IsBoost(level) || FanLevels.LowSpeedBlocker(data)==null);
        }
        void Fill(FanSample data,CoolingMode state,int level,int seconds,bool timeLimited,bool needsRecovery,bool connected)
        {
            fan1.Text=data==null?"—":(data.Fan1*100).ToString();fan2.Text=data==null?"—":(data.Fan2*100).ToString();
            zone.Text=data==null?"—":Value(data.ZoneC);gpu.Text=data==null?"—":Value(data.GpuC);
            zoneBar.Value=data==null?Double.NaN:data.ZoneC;gpuBar.Value=data==null?Double.NaN:data.GpuC;
            load.Text=data==null?"CPU 負載 —　 GPU 負載 —":"CPU 負載 "+Value(data.CpuLoad)+"%　 GPU 負載 "+Value(data.GpuLoad)+"%";
            string modeName=ModeName(state,level);
            Color modeColor=state==CoolingMode.RecoveryFailed?Ui.Danger:state!=CoolingMode.Quiet?Ink:FanLevels.IsBoost(level)?Ui.Warn:Accent;
            currentMode.Text="目前："+modeName;controlMode.Text=modeName;currentMode.ForeColor=modeColor;controlMode.ForeColor=modeColor;
            countdown.Text=state!=CoolingMode.Quiet?"低速每次最多 5 分鐘":timeLimited?"本輪剩餘 "+TimeSpan.FromSeconds(seconds).ToString(@"m\:ss"):"不限時間 · 關閉程式時恢復自動";
            controlTimer.Text=state==CoolingMode.Quiet?countdown.Text:"選好轉速後按「套用」。";
            sessionProgress.Value=timeLimited?Math.Max(0,Math.Min(300,seconds)):0;
            connection.Text=connected?"●  已取得管理員權限":"○  尚未取得控制權限";connection.ForeColor=connected?Accent:Muted;admin.Visible=!connected;admin.Enabled=!busy;
            foreach(Button button in quietButtons)button.Enabled=CanApply((int)button.Tag,data,state,level,connected);
            foreach(Button button in autoButtons)button.Enabled=!busy && connected && needsRecovery;

            bool boost=FanLevels.IsBoost(selectedLevel);
            Color tone=boost?Ui.Warn:Accent;
            speedValue.Text=FanLevels.Rpm(selectedLevel)+" RPM";speedValue.ForeColor=tone;
            speedZone.Text=boost?"加強散熱":"低速區";speedZone.ForeColor=tone;speedSlider.FillColor=tone;
            string blocker=boost?null:FanLevels.LowSpeedBlocker(data);
            speedNote.ForeColor=Muted;
            if(!connected)speedNote.Text="取得管理員權限後才能調整風扇。";
            else if(blocker!=null){speedNote.Text="現在不能低速："+blocker+"可改用 3300 RPM 以上。";speedNote.ForeColor=Ui.Warn;}
            else speedNote.Text=boost?"風扇只會更快，不受溫度與時間限制；關閉程式時交回原廠自動。":"比原廠安靜；每次最多 5 分鐘，溫度或負載升高會自動恢復。";
            bool active=state==CoolingMode.Quiet && level==selectedLevel;
            applySpeed.Text=active?"已套用 "+FanLevels.Rpm(selectedLevel)+" RPM":(state==CoolingMode.Quiet?"改為 ":"套用 ")+FanLevels.Rpm(selectedLevel)+" RPM";
            applySpeed.Enabled=CanApply(selectedLevel,data,state,level,connected);
        }
        void UpdateView(bool record)
        {
            FanSample data=session==null?null:session.Latest;
            Fill(data,session==null?CoolingMode.Automatic:session.Mode,session==null?0:session.TargetLevel,session==null?0:session.RemainingSeconds,session!=null && session.TimeLimited,session!=null && session.NeedsRecovery,Program.IsAdmin());
            string message=error.Length>0?error:session==null?"取得管理員權限後開始監看；目前未變更風扇。":session.Status;
            status.Text=(busy?"正在處理，請稍候…":message)+"\r\n轉速為 BIOS 等級估計；ACPI 熱區並非 CPU 核心溫度。";
            if(record && data!=null){history.Add(DateTime.UtcNow,data.Fan1*100,data.Fan2*100);refreshed.Text="更新 "+DateTime.Now.ToString("HH:mm:ss");}
            if(record && message!=loggedStatus){loggedStatus=message;Log(message);}
        }
        void PaintPreview()
        {
            bool locked=previewScene=="locked",automatic=previewScene=="auto"||previewScene=="hot";
            FanSample data=locked?null:new FanSample(54,47,12,8,automatic?27:18,automatic?29:18);
            if(previewScene=="boost"){selectedLevel=42;speedSlider.Value=42;data=new FanSample(78,56,38,2,42,42);}
            if(previewScene=="hot"){selectedLevel=25;speedSlider.Value=25;data=new FanSample(78,56,38,2,28,31);}
            Fill(data,locked||automatic?CoolingMode.Automatic:CoolingMode.Quiet,locked||automatic?0:previewScene=="boost"?42:18,locked||automatic||previewScene=="boost"?0:272,!locked&&!automatic&&previewScene!="boost",!locked&&!automatic,!locked);
            if(!locked)for(int i=0;i<=60;i++)history.Add(DateTime.UtcNow.AddSeconds(-120+2*i),(int)(i<38?2850+Math.Sin(i*.35)*130:1800+Math.Sin(i)*45),(int)(i<38?3000+Math.Cos(i*.32)*110:1800+Math.Cos(i)*45));
            refreshed.Text=locked?"等待連線":"更新 "+DateTime.Now.ToString("HH:mm:ss");
            status.Text="介面預覽 · 範例讀值，未控制風扇。\r\n轉速為 BIOS 等級估計；ACPI 熱區並非 CPU 核心溫度。";
            Log("控制面板已開啟。");if(!locked){Log("管理員連線完成，開始監看。");Log("啟用安靜模式，目標約 1800 RPM。");}
            if(previewScene=="controls"||previewScene=="boost"||previewScene=="hot")ShowPage(1);if(previewScene=="protection")ShowPage(2);
            if(previewScene=="display")
            {
                ShowDisplays(new List<DisplayTarget>{new PreviewDisplay("內建螢幕","筆電面板 · Windows 亮度",true,100),new PreviewDisplay("VZ249HG","外接螢幕 · DDC/CI",false,65)});
                displayNote.Text="介面預覽 · 範例螢幕，未調整亮度。"; ShowPage(3);
            }
        }
    }
}
