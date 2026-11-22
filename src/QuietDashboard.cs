using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VictusQuiet
{
    // Persistent user preferences stored in %LOCALAPPDATA%\VictusControlCenter\settings.ini
    public sealed class AppSettings
    {
        public int SelectedLevel = FanLevels.Quiet;
        public CurvePreset Preset = CurvePreset.Balanced;
        public bool AutoQuiet = true;
        public bool CloseToTray = true;
        public bool SyncBrightness = false;
        public bool AutoStartConfigured = false;
        public int HistoryWindowSeconds = 120;

        static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VictusControlCenter", "settings.ini"); }
        }
        public static AppSettings Load() { return Load(DefaultPath); }
        public static AppSettings Load(string path)
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(path)) return s;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.StartsWith("#")) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n;
                    if (k == "SelectedLevel" && Int32.TryParse(v, out n) && n >= FanLevels.Minimum && n <= FanLevels.Maximum) s.SelectedLevel = n;
                    else if (k == "Preset" && Int32.TryParse(v, out n) && n >= 0 && n <= 2) s.Preset = (CurvePreset)n;
                    else if (k == "AutoQuiet") s.AutoQuiet = v == "1" || String.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    else if (k == "CloseToTray") s.CloseToTray = v == "1" || String.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    else if (k == "SyncBrightness") s.SyncBrightness = v == "1" || String.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    else if (k == "AutoStartConfigured") s.AutoStartConfigured = v == "1" || String.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    else if (k == "HistoryWindowSeconds" && Int32.TryParse(v, out n) && (n == 120 || n == 600 || n == 1800)) s.HistoryWindowSeconds = n;
                }
            }
            catch (Exception) { }
            return s;
        }
        public void Save() { Save(DefaultPath); }
        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string[] lines = new string[]
                {
                    "SelectedLevel=" + SelectedLevel,
                    "Preset=" + (int)Preset,
                    "AutoQuiet=" + (AutoQuiet ? "1" : "0"),
                    "CloseToTray=" + (CloseToTray ? "1" : "0"),
                    "SyncBrightness=" + (SyncBrightness ? "1" : "0"),
                    "AutoStartConfigured=" + (AutoStartConfigured ? "1" : "0"),
                    "HistoryWindowSeconds=" + HistoryWindowSeconds
                };
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    // Windows Task Scheduler & Registry Run helper for automatic startup at logon.
    static class AutoStartTask
    {
        const string TaskName = "VictusControlCenter_AutoStart";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "VictusControlCenter";
        public static bool IsTaskEnabled()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"" + TaskName + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(psi)) { p.WaitForExit(3000); return p.ExitCode == 0; }
            }
            catch (Exception) { return false; }
        }
        public static bool IsEnabled()
        {
            if (IsTaskEnabled()) return true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return key != null && key.GetValue(RunValue) != null;
            }
            catch (Exception) { return false; }
        }
        public static void SetEnabled(bool enable, string exePath)
        {
            if (enable)
            {
                if (Program.IsAdmin())
                {
                    string args = "/Create /F /RL HIGHEST /SC ONLOGON /TN \"" + TaskName + "\" /TR \"\\\"" + exePath + "\\\" --tray\"";
                    ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", args)
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (Process p = Process.Start(psi))
                    {
                        string err = p.StandardError.ReadToEnd();
                        p.WaitForExit(5000);
                        if (p.ExitCode != 0) throw new InvalidOperationException("無法設定開機自動啟動：" + err.Trim());
                    }
                    // Remove HKCU Run fallback so it doesn't launch twice
                    try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (key != null) key.DeleteValue(RunValue, false); } catch (Exception) { }
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                        if (key != null) key.SetValue(RunValue, "\"" + exePath + "\" --tray");
                }
            }
            else
            {
                try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (key != null) key.DeleteValue(RunValue, false); } catch (Exception) { }
                if (IsTaskEnabled())
                {
                    ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (Process p = Process.Start(psi)) p.WaitForExit(5000);
                }
            }
        }
    }

    // Shared look: colours, rounded shapes and icon font.
    static class Ui
    {
        public static readonly Color Canvas = Color.FromArgb(11,16,24), Sidebar = Color.FromArgb(15,21,31), Surface = Color.FromArgb(21,29,41),
            SurfaceHi = Color.FromArgb(29,39,54), Border = Color.FromArgb(37,49,66), Muted = Color.FromArgb(134,151,173), Faint = Color.FromArgb(88,104,126),
            Ink = Color.FromArgb(236,241,248), Accent = Color.FromArgb(45,212,191), AccentDeep = Color.FromArgb(20,70,72), Violet = Color.FromArgb(167,139,250),
            Warn = Color.FromArgb(251,191,36), Danger = Color.FromArgb(248,113,113), Sky = Color.FromArgb(56,189,248);
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

    enum ButtonKind { Primary, Secondary, Nav, Pill }

    sealed class DashboardButton : Button
    {
        bool hovered, pressed, selected;
        public ButtonKind Kind = ButtonKind.Secondary;
        public string Glyph;
        public bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Invalidate(); } }
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
                fill = Enabled ? (selected ? Color.FromArgb(21,54,60) : Ui.SurfaceHi) : Ui.Mix(Ui.Surface, outer, .4f);
                border = Enabled ? (selected ? Ui.Accent : Color.FromArgb(52,68,90)) : Ui.Border;
                text = Enabled ? (selected ? Ui.Accent : Ui.Ink) : Ui.Faint;
                if (Enabled && hovered && !selected) { fill = pressed ? Color.FromArgb(35,47,65) : Color.FromArgb(40,54,74); border = Color.FromArgb(70,90,116); }
            }
            else if (Kind == ButtonKind.Pill)
            {
                fill = selected ? Color.FromArgb(24,62,68) : hovered ? Ui.SurfaceHi : Color.FromArgb(18,25,36);
                border = selected ? Ui.Accent : Color.FromArgb(44,58,78);
                text = selected ? Ui.Accent : hovered ? Ui.Ink : Ui.Muted;
            }
            else
            {
                fill = selected ? Color.FromArgb(21,48,55) : hovered ? Ui.SurfaceHi : outer; border = fill;
                text = selected ? Ui.Accent : hovered ? Ui.Ink : Ui.Muted;
            }
            RectangleF box = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Ui.Round(box, Kind == ButtonKind.Pill ? 7 : 9))
            using (Brush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            { g.FillPath(brush, path); g.DrawPath(pen, path); }
            if (Kind == ButtonKind.Nav && selected)
                using (GraphicsPath bar = Ui.Round(new RectangleF(0, Height * .28f, 3.5f, Height * .44f), 1.5f))
                using (Brush accent = new SolidBrush(Ui.Accent)) g.FillPath(accent, bar);

            int left = Kind == ButtonKind.Nav ? 16 : Kind == ButtonKind.Pill ? 4 : Padding.Left + 10;
            Font icons = Glyph == null ? null : Ui.Icons(Font.Size * 1.05f);
            Size label = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            int glyphWidth = icons == null ? 0 : (int)(Font.Size * 2.1f);
            bool centered = TextAlign != ContentAlignment.MiddleLeft;
            int start = centered ? Math.Max(left, (Width - label.Width - glyphWidth) / 2) : left;
            if (icons != null)
            {
                TextRenderer.DrawText(g, Glyph, icons, new Rectangle(start, 0, glyphWidth, Height), text, fill, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                icons.Dispose();
            }
            Rectangle area = Rectangle.FromLTRB(start + glyphWidth, 0, Width - (Kind == ButtonKind.Pill ? 4 : 8), Height);
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

    // Dual-axis interactive history chart: RPM on left axis, Temperature (°C) on right axis, hover crosshair tooltip, and 2m / 10m / 30m selector.
    sealed class FanHistory : Control
    {
        sealed class PointSample { public DateTime At; public int First, Second; public double Zone, Gpu; }
        readonly List<PointSample> samples = new List<PointSample>();
        readonly DashboardButton[] rangeButtons = new DashboardButton[3];
        int windowSeconds = 120;
        int hoverX = -1;
        public event EventHandler WindowChanged;
        public int WindowSeconds
        {
            get { return windowSeconds; }
            set
            {
                if (value != 120 && value != 600 && value != 1800) value = 120;
                windowSeconds = value;
                for (int i = 0; i < 3; i++) if (rangeButtons[i] != null) rangeButtons[i].Selected = (int)rangeButtons[i].Tag == windowSeconds;
                Invalidate();
            }
        }
        public FanHistory()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            int[] secs = { 120, 600, 1800 };
            string[] labels = { "2 分鐘", "10 分鐘", "30 分鐘" };
            for (int i = 0; i < 3; i++)
            {
                int sec = secs[i];
                DashboardButton btn = new DashboardButton
                {
                    Kind = ButtonKind.Pill, Text = labels[i], Tag = sec, Selected = sec == 120,
                    Font = new Font(Ui.TextFont, 8.5f), Size = new Size(64, 24)
                };
                btn.Click += delegate { WindowSeconds = sec; if (WindowChanged != null) WindowChanged(this, EventArgs.Empty); };
                rangeButtons[i] = btn;
                Controls.Add(btn);
            }
            LayoutRangeButtons();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutRangeButtons(); }
        void LayoutRangeButtons()
        {
            int x = 185;
            for (int i = 0; i < 3; i++)
                if (rangeButtons[i] != null) { rangeButtons[i].Location = new Point(x, 26); x += 68; }
        }
        protected override void OnMouseMove(MouseEventArgs e) { hoverX = e.X; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverX = -1; Invalidate(); base.OnMouseLeave(e); }
        public void Add(DateTime at, int first, int second, double zone = Double.NaN, double gpu = Double.NaN)
        {
            if (samples.Count > 0 && (at - samples[samples.Count - 1].At).TotalSeconds < 0.8) return;
            samples.Add(new PointSample { At = at, First = first, Second = second, Zone = zone, Gpu = gpu });
            while (samples.Count > 0 && (at - samples[0].At).TotalSeconds > 1810) samples.RemoveAt(0);
            Invalidate();
        }
        static void Legend(Graphics g, Font font, string text, Color colour, float right, float y, out float left, bool dashed = false)
        {
            SizeF size = g.MeasureString(text, font);
            left = right - size.Width;
            using (Brush ink = new SolidBrush(Ui.Muted)) g.DrawString(text, font, ink, left, y);
            if (dashed)
            {
                using (Pen p = new Pen(colour, 2f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(p, left - 18, y + size.Height / 2, left - 4, y + size.Height / 2);
                left -= 20;
            }
            else
            {
                using (Brush dot = new SolidBrush(colour)) g.FillEllipse(dot, left - 13, y + size.Height / 2 - 4, 8, 8);
                left -= 13;
            }
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics; g.Clear(Ui.Outer(this)); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            RectangleF card = new RectangleF(0.5f, 10.5f, Width - 1.5f, Height - 20.5f);
            using (GraphicsPath path = Ui.Round(card, 12))
            using (Brush fill = new SolidBrush(Ui.Surface))
            using (Pen border = new Pen(Ui.Border)) { g.FillPath(fill, path); g.DrawPath(border, path); }

            Rectangle plot = Rectangle.FromLTRB(58, (int)card.Top + 52, Math.Max(90, Width - 52), Math.Max((int)card.Top + 84, (int)card.Bottom - 32));
            DateTime latest = samples.Count > 0 ? samples[samples.Count - 1].At : DateTime.UtcNow;
            List<PointSample> visible = new List<PointSample>();
            foreach (PointSample s in samples) if ((latest - s.At).TotalSeconds <= windowSeconds + 2) visible.Add(s);

            int ceiling = 4000;
            foreach (PointSample sample in visible) ceiling = Math.Max(ceiling, ((Math.Max(sample.First, sample.Second) + 999) / 1000) * 1000);
            const float tempMin = 30f, tempMax = 90f;

            using (Font heading = new Font(Ui.TextFont, 10.5f, FontStyle.Bold))
            using (Font small = new Font(Ui.TextFont, 8.5f))
            using (Brush ink = new SolidBrush(Ui.Ink))
            using (Brush muted = new SolidBrush(Ui.Muted))
            using (Brush faint = new SolidBrush(Ui.Faint))
            using (Pen grid = new Pen(Color.FromArgb(34,45,61)) { DashStyle = DashStyle.Dash, DashPattern = new float[] { 3, 4 } })
            {
                g.DrawString("轉速與溫度趨勢", heading, ink, 20, card.Top + 17);
                float left;
                Legend(g, small, "GPU °C", Ui.Sky, Width - 20, card.Top + 19, out left, true);
                Legend(g, small, "ACPI °C", Ui.Warn, left - 12, card.Top + 19, out left, true);
                Legend(g, small, "風扇 2", Ui.Violet, left - 14, card.Top + 19, out left);
                Legend(g, small, "風扇 1", Ui.Accent, left - 14, card.Top + 19, out left);

                for (int i = 0; i <= 4; i++)
                {
                    float y = plot.Top + plot.Height * i / 4f;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string mark = (ceiling * (4 - i) / 4 / 1000.0).ToString("0.#") + "k";
                    SizeF size = g.MeasureString(mark, small);
                    g.DrawString(mark, small, faint, plot.Left - 8 - size.Width, y - size.Height / 2);

                    float tMark = tempMax - (tempMax - tempMin) * i / 4f;
                    string tStr = tMark.ToString("0") + "°";
                    g.DrawString(tStr, small, faint, plot.Right + 6, y - size.Height / 2);
                }
                string agoLabel = (windowSeconds / 60) + " 分鐘前";
                g.DrawString(agoLabel, small, faint, plot.Left - 2, plot.Bottom + 6);
                SizeF now = g.MeasureString("現在 (滑鼠移入可查數值)", small);
                g.DrawString("現在 (滑鼠移入可查數值)", small, faint, plot.Right - now.Width + 4, plot.Bottom + 6);

                if (visible.Count < 2)
                {
                    string waiting = "等待足夠的即時讀值…";
                    SizeF size = g.MeasureString(waiting, small);
                    g.DrawString(waiting, small, muted, plot.Left + (plot.Width - size.Width) / 2, plot.Top + (plot.Height - size.Height) / 2);
                    return;
                }

                PointF[] first = new PointF[visible.Count], second = new PointF[visible.Count];
                List<PointF> zonePts = new List<PointF>(), gpuPts = new List<PointF>();
                for (int i = 0; i < visible.Count; i++)
                {
                    float x = Math.Max(plot.Left, plot.Right - (float)(latest - visible[i].At).TotalSeconds / windowSeconds * plot.Width);
                    first[i] = new PointF(x, plot.Bottom - visible[i].First / (float)ceiling * plot.Height);
                    second[i] = new PointF(x, plot.Bottom - visible[i].Second / (float)ceiling * plot.Height);
                    if (!Double.IsNaN(visible[i].Zone))
                    {
                        float ratio = Math.Max(0f, Math.Min(1f, ((float)visible[i].Zone - tempMin) / (tempMax - tempMin)));
                        zonePts.Add(new PointF(x, plot.Bottom - ratio * plot.Height));
                    }
                    if (!Double.IsNaN(visible[i].Gpu))
                    {
                        float ratio = Math.Max(0f, Math.Min(1f, ((float)visible[i].Gpu - tempMin) / (tempMax - tempMin)));
                        gpuPts.Add(new PointF(x, plot.Bottom - ratio * plot.Height));
                    }
                }
                Series(g, second, Ui.Violet, plot);
                Series(g, first, Ui.Accent, plot);
                if (gpuPts.Count >= 2)
                    using (Pen tp = new Pen(Color.FromArgb(190, Ui.Sky), 1.6f) { DashStyle = DashStyle.Dash }) g.DrawLines(tp, gpuPts.ToArray());
                if (zonePts.Count >= 2)
                    using (Pen tp = new Pen(Color.FromArgb(210, Ui.Warn), 1.8f) { DashStyle = DashStyle.Dash }) g.DrawLines(tp, zonePts.ToArray());

                // Hover crosshair and tooltip
                if (hoverX >= plot.Left && hoverX <= plot.Right)
                {
                    int best = 0; float bestDist = Single.MaxValue;
                    for (int i = 0; i < first.Length; i++)
                    {
                        float d = Math.Abs(first[i].X - hoverX);
                        if (d < bestDist) { bestDist = d; best = i; }
                    }
                    PointSample s = visible[best];
                    float cx = first[best].X;
                    using (Pen cross = new Pen(Color.FromArgb(120, Ui.Ink), 1f) { DashStyle = DashStyle.Dot })
                        g.DrawLine(cross, cx, plot.Top, cx, plot.Bottom);
                    using (Brush d1 = new SolidBrush(Ui.Accent)) g.FillEllipse(d1, cx - 4, first[best].Y - 4, 8, 8);
                    using (Brush d2 = new SolidBrush(Ui.Violet)) g.FillEllipse(d2, cx - 4, second[best].Y - 4, 8, 8);

                    int secAgo = Math.Max(0, (int)Math.Round((latest - s.At).TotalSeconds));
                    string tip = (secAgo == 0 ? "現在" : secAgo + " 秒前") + "  |  風扇 " + s.First + " / " + s.Second + " RPM"
                        + (Double.IsNaN(s.Zone) ? "" : "  ·  ACPI " + s.Zone.ToString("0") + "°C  ·  GPU " + s.Gpu.ToString("0") + "°C");
                    SizeF tipSize = g.MeasureString(tip, small);
                    float boxW = tipSize.Width + 18, boxH = tipSize.Height + 10;
                    float boxX = Math.Max(plot.Left + 4, Math.Min(plot.Right - boxW - 4, cx - boxW / 2));
                    float boxY = plot.Top + 6;
                    RectangleF tipRect = new RectangleF(boxX, boxY, boxW, boxH);
                    using (GraphicsPath tp = Ui.Round(tipRect, 6))
                    using (Brush tf = new SolidBrush(Color.FromArgb(235, 14, 20, 30)))
                    using (Pen tb = new Pen(Ui.Accent, 1f))
                    { g.FillPath(tf, tp); g.DrawPath(tb, tp); }
                    g.DrawString(tip, small, ink, boxX + 9, boxY + 5);
                }
            }
        }
        static void Series(Graphics g, PointF[] points, Color colour, Rectangle plot)
        {
            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(points);
                area.AddLine(points[points.Length-1].X, points[points.Length-1].Y, points[points.Length-1].X, plot.Bottom);
                area.AddLine(points[points.Length-1].X, plot.Bottom, points[0].X, plot.Bottom);
                area.CloseFigure();
                using (Brush shade = new LinearGradientBrush(new RectangleF(plot.Left, plot.Top - 1, plot.Width, plot.Height + 2), Color.FromArgb(46, colour), Color.FromArgb(0, colour), LinearGradientMode.Vertical))
                    g.FillPath(shade, area);
            }
            using (Pen line = new Pen(colour, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(line, points);
            PointF end = points[points.Length-1];
            using (Brush glow = new SolidBrush(Color.FromArgb(60, colour))) g.FillEllipse(glow, end.X - 7, end.Y - 7, 14, 14);
            using (Brush dot = new SolidBrush(colour)) g.FillEllipse(dot, end.X - 3.5f, end.Y - 3.5f, 7, 7);
        }
    }

    // Visual Quiet Curve control showing the temperature-to-RPM staircase and a glowing live operating dot.
    sealed class QuietCurveChart : Control
    {
        public CurvePreset Preset = CurvePreset.Balanced;
        public int BaseLevel = FanLevels.Quiet;
        public int ActiveLevel = FanLevels.Quiet;
        public double LiveZone = Double.NaN, LiveGpu = Double.NaN;
        public bool IsQuietActive;

        public QuietCurveChart()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            Graphics g = args.Graphics; g.Clear(Ui.Outer(this)); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle plot = Rectangle.FromLTRB(48, 8, Math.Max(80, Width - 16), Math.Max(34, Height - 22));
            const float minT = 50f, maxT = 86f, minL = 16f, maxL = 35f;
            Func<float, float> tx = t => plot.Left + Math.Max(0f, Math.Min(1f, (t - minT) / (maxT - minT))) * plot.Width;
            Func<float, float> ty = lv => plot.Bottom - Math.Max(0f, Math.Min(1f, (lv - minL) / (maxL - minL))) * plot.Height;

            using (Font small = new Font(Ui.TextFont, 8f))
            using (Brush faint = new SolidBrush(Ui.Faint))
            using (Brush muted = new SolidBrush(Ui.Muted))
            using (Brush ink = new SolidBrush(Ui.Ink))
            using (Pen grid = new Pen(Color.FromArgb(33,44,60)) { DashStyle = DashStyle.Dot })
            {
                int[] yLevels = { 18, 26, 32 };
                foreach (int lv in yLevels)
                {
                    float y = ty(lv);
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    string s = (lv * 100).ToString();
                    SizeF sz = g.MeasureString(s, small);
                    g.DrawString(s, small, faint, plot.Left - 5 - sz.Width, y - sz.Height / 2);
                }
                int[] steps = FanLevels.CurveThresholds(Preset);
                int[] levels = FanLevels.CurveSteps;
                int baseLv = Math.Min(FanLevels.LowCeiling, BaseLevel);

                // Staircase path
                List<PointF> pts = new List<PointF>();
                float curLv = baseLv;
                pts.Add(new PointF(tx(minT), ty(curLv)));
                for (int i = 0; i < 4; i++)
                {
                    float x = tx(steps[i]);
                    pts.Add(new PointF(x, ty(curLv)));
                    curLv = Math.Max(curLv, levels[i]);
                    pts.Add(new PointF(x, ty(curLv)));
                    g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                    string ts = steps[i] + "°";
                    SizeF tsz = g.MeasureString(ts, small);
                    g.DrawString(ts, small, faint, x - tsz.Width / 2, plot.Bottom + 3);
                }
                float xHand = tx(FanLevels.HandBackZone);
                pts.Add(new PointF(xHand, ty(curLv)));
                using (Pen limitPen = new Pen(Color.FromArgb(180, Ui.Danger), 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(limitPen, xHand, plot.Top, xHand, plot.Bottom);
                g.DrawString("85°原廠", small, new SolidBrush(Ui.Danger), xHand - 34, plot.Bottom + 3);

                if (pts.Count >= 2)
                {
                    using (Pen curvePen = new Pen(Ui.Accent, 2.2f) { LineJoin = LineJoin.Round })
                        g.DrawLines(curvePen, pts.ToArray());
                }

                // Live temperature marker
                if (!Double.IsNaN(LiveZone) && !Double.IsNaN(LiveGpu))
                {
                    double eff = FanLevels.EffectiveTemp(LiveZone, LiveGpu);
                    int dotLv = IsQuietActive ? Math.Min(FanLevels.LowCeiling, Math.Max(18, ActiveLevel))
                        : Math.Max(baseLv, FanLevels.CurveFloor(LiveZone, LiveGpu, Preset));
                    float dx = tx((float)eff), dy = ty(dotLv);
                    Color dotColor = eff >= FanLevels.HandBackZone ? Ui.Danger : dotLv > baseLv ? Ui.Warn : Ui.Accent;
                    using (Brush glow = new SolidBrush(Color.FromArgb(75, dotColor))) g.FillEllipse(glow, dx - 8, dy - 8, 16, 16);
                    using (Brush dot = new SolidBrush(dotColor)) g.FillEllipse(dot, dx - 4, dy - 4, 8, 8);
                    using (Pen ring = new Pen(Ui.Ink, 1.4f)) g.DrawEllipse(ring, dx - 4, dy - 4, 8, 8);
                    string badge = "目前等效 " + eff.ToString("0") + "°C → " + (dotLv * 100) + " RPM";
                    SizeF bsz = g.MeasureString(badge, small);
                    float bx = Math.Max(plot.Left + 2, Math.Min(plot.Right - bsz.Width - 6, dx - bsz.Width / 2));
                    float by = Math.Max(plot.Top, dy - bsz.Height - 7);
                    using (Brush bf = new SolidBrush(Color.FromArgb(210, 15, 22, 33))) g.FillRectangle(bf, bx - 2, by, bsz.Width + 4, bsz.Height);
                    g.DrawString(badge, small, ink, bx, by);
                }
            }
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
        public event Action<BrightnessCard, int> UserBrightnessChanged;
        public DisplayTarget Target { get { return target; } }
        public int CurrentPercent { get { return slider.Value; } }
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
                button.Click += delegate { SetFromUser(level, true); };
                presets.Controls.Add(button, i, 0);
            }
            presets.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            slider = new ValueSlider { Dock = DockStyle.Top, Value = target.Percent };
            slider.ValueChanged += delegate { SetFromUser(slider.Value, true); };
            Panel gap = new Panel { Dock = DockStyle.Top, Height = 8 };
            value = new Label { Dock = DockStyle.Top, Height = 50, Text = target.Percent + "%", Font = Ui.Numbers(24), ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft };
            Panel head = new Panel { Dock = DockStyle.Top, Height = 28 };
            Font icons = Ui.Icons(13);
            head.Controls.Add(new Label { Text = target.Name, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 6, 0), Font = new Font(Ui.TextFont, 12, FontStyle.Bold), ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true });
            head.Controls.Add(new Label { Text = target.Kind, Dock = DockStyle.Right, Width = 180, ForeColor = Ui.Muted, Font = new Font(Ui.TextFont, 9), TextAlign = ContentAlignment.MiddleRight });
            if (icons != null) head.Controls.Add(new Label { Text = target.Internal ? "" : "", Font = icons, ForeColor = Ui.Warn, Dock = DockStyle.Left, Width = 30, TextAlign = ContentAlignment.MiddleLeft });
            Controls.Add(note); Controls.Add(presets); Controls.Add(gap); Controls.Add(slider); Controls.Add(value); Controls.Add(head);
            apply.Tick += delegate { apply.Stop(); Push(); };
        }
        public void SetFromUser(int percent, bool notifySync)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            slider.Value = percent;
            Request(percent);
            if (notifySync && UserBrightnessChanged != null) UserBrightnessChanged(this, percent);
        }
        public void SyncExternalValue(int percent)
        {
            if (working || pending >= 0 || apply.Enabled) return;
            percent = Math.Max(0, Math.Min(100, percent));
            if (slider.Value != percent) { slider.Value = percent; value.Text = percent + "%"; }
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
        readonly bool preview, startInTray;
        readonly string previewScene;
        int previewRunning;
        static readonly string logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VictusControlCenter","紀錄.txt");
        readonly AppSettings settings;
        readonly Panel[] pages = new Panel[4];
        readonly DashboardButton[] navigation = new DashboardButton[4];
        readonly List<DisplayTarget> displays = new List<DisplayTarget>();
        readonly List<BrightnessCard> brightnessCards = new List<BrightnessCard>();
        Panel displayHost;
        Label displayNote;
        DashboardButton rescan, syncToggle, closeToTrayToggle, autoStartToggle;
        bool displaysLoaded, scanning;
        readonly List<Button> quietButtons = new List<Button>(), autoButtons = new List<Button>();
        readonly DashboardButton[] presetButtons = new DashboardButton[3];
        int selectedLevel = FanLevels.Quiet;
        ValueSlider speedSlider; Label speedValue, speedZone, speedNote; DashboardButton applySpeed;
        QuietCurveChart curveChart;
        readonly List<string> events = new List<string>();
        readonly Label title, subtitle, connection, fan1, fan2, zone, gpu, load, currentMode, countdown, controlMode, controlTimer, status, refreshed;
        readonly Button admin;
        readonly SessionProgress sessionProgress;
        readonly MeterBar zoneBar, gpuBar;
        readonly FanHistory history;
        readonly TextBox eventLog;
        readonly System.Windows.Forms.Timer timer;
        readonly NotifyIcon tray;
        Icon dynamicTrayIcon;
        HpQuietDevice device;
        ProcessRecoveryGuard guard;
        QuietSession session;
        bool busy, allowClose, closePending, forceExit, trayHintShown;
        string error = "", loggedStatus = "";
        int tickCount;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string list);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        const int HOTKEY_QUIET = 9001, HOTKEY_BRIGHT_UP = 9002, HOTKEY_BRIGHT_DOWN = 9003;
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        const int WM_DISPLAYCHANGE = 0x007E, WM_HOTKEY = 0x0312;

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            try
            {
                int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4);
                int caption = Ui.Sidebar.R | (Ui.Sidebar.G << 8) | (Ui.Sidebar.B << 16);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            }
            catch (Exception) { }
            if (!preview)
            {
                try
                {
                    RegisterHotKey(Handle, HOTKEY_QUIET, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.Q);
                    RegisterHotKey(Handle, HOTKEY_BRIGHT_UP, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.Up);
                    RegisterHotKey(Handle, HOTKEY_BRIGHT_DOWN, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.Down);
                }
                catch (Exception) { }
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (preview) return;
            if (m.Msg == WM_DISPLAYCHANGE && displaysLoaded && !scanning)
            {
                BeginInvoke(new Action(async delegate { await ScanDisplays(); }));
            }
            else if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_QUIET && session != null && !busy)
                {
                    BeginInvoke(new Action(async delegate
                    {
                        if (session.Mode == CoolingMode.Quiet) await Run(delegate { session.Restore("快捷鍵切換：已恢復原廠自動散熱。"); });
                        else await Run(delegate { session.Enable(selectedLevel); });
                    }));
                }
                else if (id == HOTKEY_BRIGHT_UP || id == HOTKEY_BRIGHT_DOWN)
                {
                    int delta = id == HOTKEY_BRIGHT_UP ? 10 : -10;
                    BeginInvoke(new Action(async delegate { await StepAllBrightness(delta); }));
                }
            }
        }

        public QuietWindow(bool preview, string scene = "quiet", bool startInTray = false)
        {
            this.preview = preview; previewScene = scene; this.startInTray = startInTray;
            settings = preview ? new AppSettings() : AppSettings.Load();
            selectedLevel = settings.SelectedLevel;

            Text = "Victus 控制中心"; Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); ClientSize = new Size(1080,700); MinimumSize = new Size(960,690);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font(Ui.TextFont,10); BackColor = Canvas; ForeColor = Ink;

            // System Tray Icon & Quick Context Menu
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("開啟 Victus 控制中心", null, delegate { RestoreFromTray(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("安靜模式 · 1800 RPM", null, async delegate { if (session != null) await Run(delegate { session.Enable(FanLevels.Quiet); }); });
            trayMenu.Items.Add("平衡低速 · 2500 RPM", null, async delegate { if (session != null) await Run(delegate { session.Enable(25); }); });
            trayMenu.Items.Add("最大散熱 · 5000 RPM", null, async delegate { if (session != null) await Run(delegate { session.Enable(FanLevels.Maximum); }); });
            trayMenu.Items.Add("恢復原廠自動散熱", null, async delegate { if (session != null) await Run(delegate { session.Restore("已從系統匣恢復原廠自動散熱。"); }); });
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem brightMenu = new ToolStripMenuItem("全螢幕快速亮度");
            foreach (int pct in new int[] { 25, 50, 75, 100 })
            {
                int targetPct = pct;
                brightMenu.DropDownItems.Add(targetPct + "%", null, async delegate { await ApplySceneBrightness(targetPct, targetPct); });
            }
            trayMenu.Items.Add(brightMenu);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("結束並恢復原廠散熱", null, delegate { forceExit = true; Close(); });

            tray = new NotifyIcon { Icon = Icon, Text = "Victus 控制中心", Visible = !preview, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { RestoreFromTray(); };

            // Sidebar: logo, navigation and device identity.
            Panel side = new Panel { Dock = DockStyle.Left, Width = 196, BackColor = Ui.Sidebar };
            side.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (Pen line = new Pen(Color.FromArgb(28,37,51))) args.Graphics.DrawLine(line, side.Width - 1, 0, side.Width - 1, side.Height);
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias; args.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
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
            RoundPanel identity = new RoundPanel { Dock = DockStyle.Bottom, Height = 104, BackColor = Color.FromArgb(19,27,39), BorderColor = Color.FromArgb(30,40,55), Padding = new Padding(14,10,10,8) };
            identity.Controls.Add(new Label { Text = "HP 88F8 · BIOS F.25\r\n系統匣常駐 · 睡眠自動接管\r\n熱鍵 Ctrl+Alt+Q / ↑↓", Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font(Ui.TextFont, 8.2f) });
            identity.Controls.Add(new Label { Text = "本機智慧守護", Dock = DockStyle.Top, Height = 22, ForeColor = Ink, Font = new Font(Ui.TextFont, 9, FontStyle.Bold) });
            Panel identityHost = new Panel { Dock = DockStyle.Bottom, Height = 120, Padding = new Padding(12,0,12,16) };
            identityHost.Controls.Add(identity); identity.Dock = DockStyle.Fill; side.Controls.Add(identityHost);
            Controls.Add(side);

            Panel main = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28,22,28,16) };
            Controls.Add(main); main.BringToFront();
            Panel header = new Panel { Dock = DockStyle.Top, Height = 84 };
            title = LabelAt("即時總覽",0,0,520,40,21,Ink); title.Font = new Font(Ui.TextFont, 21, FontStyle.Bold);
            subtitle = LabelAt("查看即時狀態，快速切換已驗證的安靜模式。",2,46,560,26,10,Muted);
            connection = LabelAt("尚未取得控制權限",0,0,260,24,9,Muted); connection.TextAlign = ContentAlignment.MiddleRight;
            admin = new DashboardButton { Kind = ButtonKind.Primary, Text = "取得管理員權限", Glyph = "", Font = new Font(Ui.TextFont, 10), Size = new Size(190,38) };
            DashboardButton hideTrayBtn = new DashboardButton { Kind = ButtonKind.Secondary, Text = "縮小常駐系統匣", Glyph = "", Font = new Font(Ui.TextFont, 9.5f), Size = new Size(154, 34) };
            hideTrayBtn.Click += delegate { MinimizeToTray(); };
            header.Controls.AddRange(new Control[] { title,subtitle,connection,admin,hideTrayBtn });
            header.Resize += delegate
            {
                connection.Location = new Point(header.Width-264,2);
                admin.Location = new Point(header.Width-190,32);
                hideTrayBtn.Location = new Point(header.Width - (admin.Visible ? 352 : 154), 34);
            };

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

            // Overview: live metric cards, dual-axis interactive history and quick controls.
            TableLayoutPanel cards = Grid(4); cards.Dock=DockStyle.Top; cards.Height=126;
            fan1=Metric(cards,0,"風扇 1","RPM · 估計",Accent,null,null);
            fan2=Metric(cards,1,"風扇 2","RPM · 估計",Ui.Violet,null,null);
            zoneBar=new MeterBar { Maximum=FanLevels.HandBackZone, ThresholdColours=true };
            gpuBar=new MeterBar { Maximum=FanLevels.HandBackGpu, ThresholdColours=true };
            zone=Metric(cards,2,"ACPI 熱區","°C · 非核心溫度",Ink,zoneBar,"85°C 恢復");
            gpu=Metric(cards,3,"GPU 溫度","°C · NVIDIA",Ink,gpuBar,"78°C 恢復");
            Panel summary = new Panel { Dock=DockStyle.Top, Height=38 };
            load=LabelAt("CPU 負載 —　 GPU 負載 —",2,12,520,24,10,Muted);
            refreshed=LabelAt("等待連線",0,12,240,24,9,Ui.Faint); refreshed.TextAlign=ContentAlignment.MiddleRight;
            summary.Controls.AddRange(new Control[]{load,refreshed}); summary.Resize+=delegate{refreshed.Location=new Point(summary.Width-240,12);};
            RoundPanel actions = new RoundPanel { Dock=DockStyle.Bottom, Height=114, Padding=new Padding(18,12,18,16) };
            Panel actionRow = new Panel { Dock=DockStyle.Top, Height=34 };
            currentMode=LabelAt("目前：原有管理方式",0,2,500,28,11,Ink); currentMode.Font=new Font(Ui.TextFont,11,FontStyle.Bold);
            countdown=LabelAt("低速每 5 分鐘檢查一次",0,4,280,26,9,Muted); countdown.TextAlign=ContentAlignment.MiddleRight;
            actionRow.Controls.AddRange(new Control[]{currentMode,countdown});
            actionRow.Resize+=delegate{countdown.Location=new Point(actionRow.Width-280,4);};
            TableLayoutPanel quick = Grid(3); quick.Dock=DockStyle.Fill;
            Button quickQuiet=LevelButton("安靜 · 1800 RPM",FanLevels.Quiet,ButtonKind.Primary,""); quickQuiet.Margin=new Padding(0,0,6,0); quick.Controls.Add(quickQuiet,0,0);
            Button quickBoost=LevelButton("最大散熱 · 5000 RPM",FanLevels.Maximum,ButtonKind.Secondary,""); quickBoost.Margin=new Padding(6,0,6,0); quick.Controls.Add(quickBoost,1,0);
            Button quickAuto=AutoButton(); quickAuto.Margin=new Padding(6,0,0,0); quick.Controls.Add(quickAuto,2,0);
            actions.Controls.Add(quick); actions.Controls.Add(actionRow);
            history=new FanHistory { Dock=DockStyle.Fill, WindowSeconds = settings.HistoryWindowSeconds };
            history.WindowChanged += delegate { settings.HistoryWindowSeconds = history.WindowSeconds; if (!preview) settings.Save(); };
            pages[0].Controls.Add(history); pages[0].Controls.Add(actions); pages[0].Controls.Add(summary); pages[0].Controls.Add(cards);

            // Controls: manual speed tile, visual quiet curve chart, and preset selector.
            RoundPanel modeHead=new RoundPanel { Dock=DockStyle.Top,Height=98,Padding=new Padding(22,0,22,14) };
            modeHead.Controls.Add(LabelAt("目前模式",22,12,300,20,9,Ui.Faint));
            controlMode=LabelAt("原有管理方式",20,30,540,38,16.5f,Ink); controlMode.Font=new Font(Ui.TextFont,16.5f,FontStyle.Bold);
            controlTimer=LabelAt("尚未手動設定",0,40,260,24,9.5f,Muted); controlTimer.TextAlign=ContentAlignment.MiddleRight;
            modeHead.Controls.AddRange(new Control[]{controlMode,controlTimer});
            modeHead.Resize+=delegate{controlTimer.Location=new Point(modeHead.Width-282,40);};
            sessionProgress=new SessionProgress { Dock=DockStyle.Bottom,Height=10,Value=0 }; modeHead.Controls.Add(sessionProgress);

            TableLayoutPanel modeTiles=Grid(2); modeTiles.Dock=DockStyle.Top; modeTiles.Height=264; modeTiles.Padding=new Padding(0,12,0,0);
            modeTiles.ColumnStyles[0].Width=60; modeTiles.ColumnStyles[1].Width=40;
            Panel speedTile=SpeedTile(); speedTile.Margin=new Padding(0,0,7,0); modeTiles.Controls.Add(speedTile,0,0);
            Panel autoTile=ModeTile("","原廠自動管理","依溫度調整","解除本工具的手動轉速，交回 BIOS 管理散熱。",Ink,AutoButton());
            autoTile.Margin=new Padding(7,0,0,0); modeTiles.Controls.Add(autoTile,1,0);

            Panel conditionHost=new Panel { Dock=DockStyle.Fill,Padding=new Padding(0,12,0,0) };
            RoundPanel curveCard=new RoundPanel { Dock=DockStyle.Fill,Padding=new Padding(20,12,20,12) };
            Panel curveHead = new Panel { Dock = DockStyle.Top, Height = 30 };
            curveHead.Controls.Add(new Label { Text = "智慧安靜曲線（低速期間升溫小幅加速，85°C / GPU 78°C 才交回原廠）", Dock = DockStyle.Fill, ForeColor = Ink, Font = new Font(Ui.TextFont, 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
            CurvePreset[] allPresets = { CurvePreset.Silent, CurvePreset.Balanced, CurvePreset.Cool };
            for (int i = 2; i >= 0; i--)
            {
                CurvePreset p = allPresets[i];
                DashboardButton pb = new DashboardButton
                {
                    Kind = ButtonKind.Pill, Text = FanLevels.PresetName(p), Selected = settings.Preset == p,
                    Font = new Font(Ui.TextFont, 8.5f), Dock = DockStyle.Right, Width = 78
                };
                int idx = i;
                pb.Click += delegate
                {
                    settings.Preset = p;
                    for (int k = 0; k < 3; k++) presetButtons[k].Selected = k == idx;
                    if (session != null) session.Preset = p;
                    if (!preview) settings.Save();
                    UpdateView(false);
                };
                presetButtons[i] = pb;
                curveHead.Controls.Add(pb);
                if (i > 0) curveHead.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 6 });
            }
            curveChart = new QuietCurveChart { Dock = DockStyle.Fill, Preset = settings.Preset, BaseLevel = selectedLevel };
            curveCard.Controls.Add(curveChart);
            curveCard.Controls.Add(curveHead);
            conditionHost.Controls.Add(curveCard);
            pages[1].Controls.Add(conditionHost); pages[1].Controls.Add(modeTiles); pages[1].Controls.Add(modeHead);

            // Protection details, startup/tray automation toggles, and activity log.
            TableLayoutPanel protection=Grid(2); protection.Dock=DockStyle.Fill;
            RoundPanel rulesCard=new RoundPanel { Dock=DockStyle.Fill,Margin=new Padding(0,0,7,0),Padding=new Padding(22,16,20,14) };
            Panel toggleBox = new Panel { Dock = DockStyle.Bottom, Height = 84, Padding = new Padding(0, 6, 0, 0) };
            closeToTrayToggle = new DashboardButton
            {
                Kind = ButtonKind.Secondary, Selected = settings.CloseToTray, Dock = DockStyle.Top, Height = 36,
                Glyph = "", Font = new Font(Ui.TextFont, 9.5f), TextAlign = ContentAlignment.MiddleLeft,
                Text = "關閉視窗時縮小到右下角系統匣繼續守護：" + (settings.CloseToTray ? "已開啟" : "已關閉")
            };
            closeToTrayToggle.Click += delegate
            {
                settings.CloseToTray = !settings.CloseToTray;
                closeToTrayToggle.Selected = settings.CloseToTray;
                closeToTrayToggle.Text = "關閉視窗時縮小到右下角系統匣繼續守護：" + (settings.CloseToTray ? "已開啟" : "已關閉");
                if (!preview) settings.Save();
            };
            bool taskOn = !preview && AutoStartTask.IsEnabled();
            autoStartToggle = new DashboardButton
            {
                Kind = ButtonKind.Secondary, Selected = taskOn, Dock = DockStyle.Bottom, Height = 36,
                Glyph = "GS"[0].ToString() == "G" ? "" : "", Font = new Font(Ui.TextFont, 9.5f), TextAlign = ContentAlignment.MiddleLeft,
                Text = "開機自動背景啟動（免 UAC 彈窗）：" + (taskOn ? "已開啟" : "未開啟")
            };
            autoStartToggle.Click += delegate
            {
                if (preview) return;
                try
                {
                    bool next = !AutoStartTask.IsEnabled();
                    AutoStartTask.SetEnabled(next, Application.ExecutablePath);
                    settings.AutoStartConfigured = true;
                    settings.Save();
                    bool nowOn = AutoStartTask.IsEnabled();
                    autoStartToggle.Selected = nowOn;
                    autoStartToggle.Text = "開機自動背景啟動（免 UAC 彈窗）：" + (nowOn ? "已開啟" : "未開啟");
                    Log(nowOn ? "已啟用開機自動背景啟動。" : "已停用開機自動背景啟動。");
                }
                catch (Exception ex) { error = ex.Message; UpdateView(false); }
            };
            toggleBox.Controls.Add(closeToTrayToggle);
            toggleBox.Controls.Add(autoStartToggle);
            rulesCard.Controls.Add(toggleBox);

            string[] restore={"ACPI 熱區達 85°C 或 GPU 達 78°C","溫度或風扇讀值失效／中斷","每 5 分鐘檢查時已高於 82°C／75°C","電腦進入睡眠前主動恢復，喚醒後自動重啟","選擇「結束並恢復原廠」時"};
            Label backup=new Label { Dock=DockStyle.Top,Height=62,ForeColor=Accent,Text="還沒到上限時不會切回原廠：依所選安靜曲線先小幅加速（2200～3200 RPM），降溫 3°C 以上自動降回。\r\n若因高溫暫時切回原廠，降溫至 ACPI ≤ 72°C 且 GPU ≤ 67°C 後自動重回安靜。另有獨立備援程序。",TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Ui.TextFont,9f) };
            rulesCard.Controls.Add(backup);
            for(int i=restore.Length-1;i>=0;i--) rulesCard.Controls.Add(RuleRow(restore[i]));
            rulesCard.Controls.Add(CardTitle("","自動保護與常駐設定"));

            RoundPanel logs=new RoundPanel { Dock=DockStyle.Fill,Margin=new Padding(7,0,0,0),Padding=new Padding(22,16,14,14) };
            eventLog=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Surface,ForeColor=Muted,Font=new Font(Ui.TextFont,9.5f) };
            eventLog.HandleCreated+=delegate{try{SetWindowTheme(eventLog.Handle,"DarkMode_Explorer",null);}catch(Exception){}};
            logs.Controls.Add(eventLog); logs.Controls.Add(CardTitle("","本次操作紀錄"));
            protection.Controls.Add(rulesCard,0,0); protection.Controls.Add(logs,1,0); pages[2].Controls.Add(protection);

            admin.Click+=delegate { if(preview)return; try { ProcessStartInfo start=new ProcessStartInfo(Application.ExecutablePath); start.UseShellExecute=true; start.Verb="runas"; Process.Start(start); allowClose=true; Close(); } catch(Win32Exception failure) { error=failure.NativeErrorCode==1223?"已取消提升權限，未改變風扇。":failure.Message; UpdateView(false); } };
            timer=new System.Windows.Forms.Timer { Interval=2000 };
            timer.Tick+=async delegate
            {
                if(!busy && session!=null) await Run(delegate{session.Poll();},false);
                tickCount++;
                if (!preview && displaysLoaded && !scanning && tickCount % 3 == 0) await RefreshExternalBrightness();
            };

            if (!preview) SystemEvents.PowerModeChanged += OnPowerModeChanged;

            Shown+=async delegate
            {
                Rectangle screen=Screen.FromControl(this).WorkingArea;
                if(Width>screen.Width || Height>screen.Height) { Size=new Size(Math.Min(Width,screen.Width),Math.Min(Height,screen.Height)); Location=screen.Location; }
                if(preview) { PaintPreview(); return; }
                TopMost = true; Activate(); TopMost = false;
                Log("控制面板已開啟。");
                if(!Program.IsAdmin())
                {
                    if (!settings.AutoStartConfigured) { try { AutoStartTask.SetEnabled(true, Application.ExecutablePath); } catch (Exception) { } }
                    UpdateView(false);
                    if (this.startInTray) BeginInvoke(new Action(MinimizeToTray));
                    return;
                }
                await Run(delegate
                {
                    // Enable auto-start at logon (UAC-free) by default when running as Administrator for the first time
                    if (!settings.AutoStartConfigured)
                    {
                        try
                        {
                            AutoStartTask.SetEnabled(true, Application.ExecutablePath);
                            settings.AutoStartConfigured = true;
                            settings.Save();
                            bool nowOn = AutoStartTask.IsEnabled();
                            if (autoStartToggle != null && !IsDisposed)
                            {
                                BeginInvoke(new Action(delegate
                                {
                                    autoStartToggle.Selected = nowOn;
                                    autoStartToggle.Text = "開機自動背景啟動（免 UAC 彈窗）：" + (nowOn ? "已開啟" : "未開啟");
                                }));
                            }
                        }
                        catch (Exception) { }
                    }
                    device=new HpQuietDevice();
                    guard=new ProcessRecoveryGuard();
                    session=new QuietSession(device,guard,()=>HpQuietDevice.Seconds) { AutoQuiet = settings.AutoQuiet, Preset = settings.Preset };
                    session.Poll();
                    if(session.Latest!=null && session.Latest.Valid && FanLevels.LowSpeedBlocker(session.Latest)==null)
                    {
                        try { session.Enable(selectedLevel); } catch(Exception) { }
                    }
                });
                timer.Start();
                if (this.startInTray) BeginInvoke(new Action(MinimizeToTray));
            };
            Resize += delegate
            {
                if (!preview && WindowState == FormWindowState.Minimized && settings.CloseToTray) MinimizeToTray();
            };
            FormClosing+=async delegate(object sender,FormClosingEventArgs args)
            {
                if(allowClose || preview)return;
                // Close-to-tray when user clicks the window X button, unless forceExit was chosen
                if (!forceExit && args.CloseReason == CloseReason.UserClosing && settings.CloseToTray && Program.IsAdmin())
                {
                    args.Cancel = true;
                    MinimizeToTray();
                    return;
                }
                if(busy){args.Cancel=true;closePending=true;return;}
                if(session==null || !session.NeedsRecovery)return;
                args.Cancel=true;timer.Stop();
                await Run(delegate{session.Restore("已恢復自動散熱，可以關閉。");});
                if(!session.NeedsRecovery){allowClose=true;Close();}else timer.Start();
            };
            FormClosed+=delegate
            {
                if (!preview)
                {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                    try { UnregisterHotKey(Handle, HOTKEY_QUIET); UnregisterHotKey(Handle, HOTKEY_BRIGHT_UP); UnregisterHotKey(Handle, HOTKEY_BRIGHT_DOWN); } catch (Exception) { }
                }
                timer.Stop();timer.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (dynamicTrayIcon != null) { DestroyIcon(dynamicTrayIcon.Handle); dynamicTrayIcon.Dispose(); dynamicTrayIcon = null; }
                if(guard!=null)guard.Dispose();if(device!=null)device.Dispose();
                foreach(DisplayTarget display in displays)display.Dispose();displays.Clear();
            };
            ShowPage(0); UpdateView(false);
        }

        async void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (preview || session == null) return;
            if (e.Mode == PowerModes.Suspend)
            {
                timer.Stop();
                if (session.NeedsRecovery)
                {
                    await Run(delegate { session.Restore("系統進入睡眠，已暫時交回原廠散熱。", true); });
                }
            }
            else if (e.Mode == PowerModes.Resume)
            {
                Log("系統自睡眠喚醒，等待感測器穩定後自動接管…");
                await Task.Delay(3000);
                if (IsDisposed) return;
                await Run(delegate
                {
                    session.Poll();
                    if (settings.AutoQuiet && session.Latest != null && session.Latest.Valid && FanLevels.LowSpeedBlocker(session.Latest) == null)
                    {
                        try { session.Enable(selectedLevel); } catch (Exception) { }
                    }
                }, false);
                timer.Start();
                if (displaysLoaded) await ScanDisplays();
            }
        }

        void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;
            if (!trayHintShown && tray != null)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(2500, "Victus 控制中心仍在背景守護", "雙擊右下角圖示可叫出視窗；右鍵可快速切換風扇與螢幕亮度。", ToolTipIcon.Info);
            }
        }

        void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void UpdateTrayBadge(FanSample data, CoolingMode state, int level, int running)
        {
            if (preview || tray == null) return;
            string tip = data == null ? "Victus 控制中心（未連線）"
                : "Victus · " + ModeName(state, level, running) + "\r\n風扇 " + (data.Fan1 * 100) + "/" + (data.Fan2 * 100) + " RPM · ACPI " + Value(data.ZoneC) + "°C · GPU " + Value(data.GpuC) + "°C";
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            tray.Text = tip;

            if (data == null || Double.IsNaN(data.ZoneC)) return;
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    Color bg = state == CoolingMode.Quiet ? (FanLevels.IsBoost(level) ? Color.FromArgb(140, 75, 12) : running > level ? Color.FromArgb(115, 78, 16) : Color.FromArgb(14, 74, 72)) : Color.FromArgb(28, 38, 54);
                    Color ring = state == CoolingMode.Quiet ? (FanLevels.IsBoost(level) ? Ui.Warn : running > level ? Ui.Warn : Ui.Accent) : Ui.Muted;
                    using (GraphicsPath box = Ui.Round(new RectangleF(1, 1, 30, 30), 7))
                    using (Brush fill = new SolidBrush(bg))
                    using (Pen pen = new Pen(ring, 2f))
                    { g.FillPath(fill, box); g.DrawPath(pen, box); }

                    string num = Math.Min(99, Math.Max(0, (int)Math.Round(data.ZoneC))).ToString("00");
                    using (Font f = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b = new SolidBrush(Ui.Ink))
                    {
                        SizeF sz = g.MeasureString(num, f);
                        g.DrawString(num, f, b, (32 - sz.Width) / 2f + 0.5f, 4);
                    }
                    string sub = state == CoolingMode.Quiet ? (running * 100 / 1000.0).ToString("0.0") + "k" : "AUTO";
                    using (Font sf = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush sb = new SolidBrush(ring))
                    {
                        SizeF sz = g.MeasureString(sub, sf);
                        g.DrawString(sub, sf, sb, (32 - sz.Width) / 2f, 19);
                    }
                    IntPtr hIcon = bmp.GetHicon();
                    Icon nextIcon = System.Drawing.Icon.FromHandle(hIcon);
                    tray.Icon = nextIcon;
                    if (dynamicTrayIcon != null) { DestroyIcon(dynamicTrayIcon.Handle); dynamicTrayIcon.Dispose(); }
                    dynamicTrayIcon = nextIcon;
                }
            }
            catch (Exception) { }
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
        Control CardTitle(string glyph,string text)
        {
            Panel row=new Panel{Dock=DockStyle.Top,Height=38};
            Font icons=Ui.Icons(13);
            if(icons!=null)row.Controls.Add(new Label{Text=glyph,Font=icons,ForeColor=Accent,Location=new Point(0,4),Size=new Size(28,28),TextAlign=ContentAlignment.MiddleLeft});
            row.Controls.Add(new Label{Text=text,Font=new Font(Ui.TextFont,12.5f,FontStyle.Bold),ForeColor=Ink,Location=new Point(icons==null?0:30,2),Size=new Size(320,30)});
            return row;
        }
        Control RuleRow(string text)
        {
            Panel row=new Panel{Dock=DockStyle.Top,Height=32};
            row.Paint+=delegate(object sender,PaintEventArgs args)
            {
                args.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
                using(Brush dot=new SolidBrush(Ui.AccentDeep))args.Graphics.FillEllipse(dot,0,7,18,18);
                using(Pen tick=new Pen(Accent,1.8f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
                    args.Graphics.DrawLines(tick,new[]{new PointF(5,16.2f),new PointF(8,19),new PointF(13.2f,13)});
                using(Pen line=new Pen(Color.FromArgb(30,40,55)))args.Graphics.DrawLine(line,30,row.Height-1,row.Width,row.Height-1);
            };
            row.Controls.Add(new Label{Text=text,Location=new Point(30,4),Size=new Size(380,24),ForeColor=Ink,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Ui.TextFont,9.5f)});
            return row;
        }
        Button LevelButton(string text,int level,ButtonKind kind,string glyph)
        {
            DashboardButton button=new DashboardButton{Kind=kind,Text=text,Glyph=glyph,Font=new Font(Ui.TextFont,10.5f,kind==ButtonKind.Primary?FontStyle.Bold:FontStyle.Regular),Dock=DockStyle.Fill,Margin=Padding.Empty,Tag=level};quietButtons.Add(button);
            button.Click+=async delegate{selectedLevel=level;if(speedSlider!=null)speedSlider.Value=level;settings.SelectedLevel=level;if(!preview)settings.Save();if(session!=null)await Run(delegate{session.Enable(level);});};return button;
        }
        Panel SpeedTile()
        {
            RoundPanel card=new RoundPanel{Dock=DockStyle.Fill,Padding=new Padding(22,16,22,16)};
            Panel head=new Panel{Dock=DockStyle.Top,Height=24};
            Font icons=Ui.Icons(12);
            if(icons!=null)head.Controls.Add(new Label{Text="",Font=icons,ForeColor=Muted,Location=new Point(0,3),Size=new Size(24,22)});
            head.Controls.Add(new Label{Text="手動轉速　1800～5000 RPM",ForeColor=Muted,Location=new Point(icons==null?0:26,2),Size=new Size(320,22)});
            Panel valueRow=new Panel{Dock=DockStyle.Top,Height=38};
            speedValue=new Label{Dock=DockStyle.Fill,Font=new Font(Ui.TextFont,18,FontStyle.Bold),ForeColor=Accent,TextAlign=ContentAlignment.MiddleLeft};
            speedZone=new Label{Dock=DockStyle.Right,Width=150,Font=new Font(Ui.TextFont,9.5f,FontStyle.Bold),ForeColor=Accent,TextAlign=ContentAlignment.MiddleRight};
            valueRow.Controls.Add(speedValue); valueRow.Controls.Add(speedZone);
            speedSlider=new ValueSlider{Dock=DockStyle.Top,Minimum=FanLevels.Minimum,Maximum=FanLevels.Maximum,Step=1,Marker=FanLevels.BoostFrom,FillColor=Accent};
            speedSlider.Value=selectedLevel;
            speedSlider.ValueChanged+=delegate{selectedLevel=speedSlider.Value;settings.SelectedLevel=selectedLevel;if(!preview)settings.Save();UpdateView(false);};
            Panel gap=new Panel{Dock=DockStyle.Top,Height=4};
            TableLayoutPanel presets=Grid(5); presets.Dock=DockStyle.Top; presets.Height=32;
            int[] levels={18,25,33,42,50};
            for(int i=0;i<5;i++)
            {
                int level=levels[i];
                DashboardButton preset=new DashboardButton{Kind=ButtonKind.Secondary,Text=FanLevels.Rpm(level).ToString(),Font=new Font(Ui.TextFont,9.5f),Dock=DockStyle.Fill,Margin=new Padding(i==0?0:3,0,i==4?0:3,0)};
                preset.Click+=delegate{speedSlider.Value=level;selectedLevel=level;settings.SelectedLevel=level;if(!preview)settings.Save();UpdateView(false);};
                presets.Controls.Add(preset,i,0);
            }
            speedNote=new Label{Dock=DockStyle.Fill,ForeColor=Muted,Font=new Font(Ui.TextFont,8.8f),Padding=new Padding(0,6,0,0)};
            applySpeed=new DashboardButton{Kind=ButtonKind.Primary,Text="套用",Glyph="",Font=new Font(Ui.TextFont,10.5f,FontStyle.Bold),Dock=DockStyle.Bottom,Height=38};
            applySpeed.Click+=async delegate{int level=selectedLevel;settings.SelectedLevel=level;if(!preview)settings.Save();if(session!=null)await Run(delegate{session.Enable(level);});};
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
            RoundPanel card=new RoundPanel{Dock=DockStyle.Fill,Padding=new Padding(22,16,22,16)};
            Panel head=new Panel{Dock=DockStyle.Top,Height=24};
            Font icons=Ui.Icons(12);
            if(icons!=null)head.Controls.Add(new Label{Text=glyph,Font=icons,ForeColor=Muted,Location=new Point(0,3),Size=new Size(24,22)});
            head.Controls.Add(new Label{Text=heading,ForeColor=Muted,Location=new Point(icons==null?0:26,2),Size=new Size(300,22)});
            Label large=new Label{Text=value,Dock=DockStyle.Top,Height=42,Font=new Font(Ui.TextFont,18,FontStyle.Bold),ForeColor=valueColor};
            Label text=new Label{Text=description,Dock=DockStyle.Fill,ForeColor=Muted,Padding=new Padding(0,4,0,0),Font=new Font(Ui.TextFont,9f)};
            button.Dock=DockStyle.Bottom;button.Height=38;
            card.Controls.Add(text);card.Controls.Add(button);card.Controls.Add(large);card.Controls.Add(head);return card;
        }
        void ShowPage(int index)
        {
            string[] titles={"即時總覽","風扇控制","保護與紀錄","螢幕亮度"};
            string[] descriptions={"查看即時轉速與溫度雙軸趨勢，快速切換安靜、最大散熱或原廠自動。","在 1800～5000 RPM 之間選擇轉速與智慧安靜曲線，或交回原廠自動管理。","設定系統匣常駐、開機免 UAC 自動啟動，並檢視保護條件與操作紀錄。","同步調整內建與外接螢幕亮度、套用日夜情境，支援熱插拔自動偵測。"};
            for(int i=0;i<4;i++){pages[i].Visible=i==index;navigation[i].Selected=i==index;}
            pages[index].BringToFront();title.Text=titles[index];subtitle.Text=descriptions[index];
            if(index==3 && !displaysLoaded && !preview) { var ignored=ScanDisplays(); }
        }
        void BuildDisplayPage(Panel page)
        {
            Panel bar=new Panel{Dock=DockStyle.Top,Height=88};
            displayNote=LabelAt("尚未偵測螢幕",2,8,520,24,10,Muted);
            rescan=new DashboardButton{Kind=ButtonKind.Secondary,Text="重新偵測",Glyph="",Font=new Font(Ui.TextFont,9.5f),Size=new Size(124,34)};
            rescan.Click+=async delegate{await ScanDisplays();};

            syncToggle = new DashboardButton
            {
                Kind = ButtonKind.Secondary, Selected = settings.SyncBrightness, Text = "同步所有螢幕：" + (settings.SyncBrightness ? "開" : "關"),
                Glyph = "", Font = new Font(Ui.TextFont, 9f), Location = new Point(0, 44), Size = new Size(164, 34)
            };
            syncToggle.Click += delegate
            {
                settings.SyncBrightness = !settings.SyncBrightness;
                syncToggle.Selected = settings.SyncBrightness;
                syncToggle.Text = "同步所有螢幕：" + (settings.SyncBrightness ? "開" : "關");
                if (!preview) settings.Save();
            };

            DashboardButton sceneDay = new DashboardButton { Kind = ButtonKind.Pill, Text = "☀ 日間明亮 (100%)", Font = new Font(Ui.TextFont, 9f), Location = new Point(174, 44), Size = new Size(138, 34) };
            sceneDay.Click += async delegate { await ApplySceneBrightness(100, 100); };
            DashboardButton sceneRead = new DashboardButton { Kind = ButtonKind.Pill, Text = "📖 舒適閱讀 (65%)", Font = new Font(Ui.TextFont, 9f), Location = new Point(320, 44), Size = new Size(138, 34) };
            sceneRead.Click += async delegate { await ApplySceneBrightness(65, 60); };
            DashboardButton sceneNight = new DashboardButton { Kind = ButtonKind.Pill, Text = "🌙 夜間護眼 (30%)", Font = new Font(Ui.TextFont, 9f), Location = new Point(466, 44), Size = new Size(138, 34) };
            sceneNight.Click += async delegate { await ApplySceneBrightness(35, 25); };

            bar.Controls.AddRange(new Control[]{displayNote,rescan,syncToggle,sceneDay,sceneRead,sceneNight});
            bar.Resize+=delegate{rescan.Location=new Point(bar.Width-124,4);};
            displayHost=new Panel{Dock=DockStyle.Fill,AutoScroll=true};
            displayHost.Resize+=delegate{LayoutDisplays();};
            Label hint=new Label{Dock=DockStyle.Bottom,Height=30,ForeColor=Ui.Faint,Font=new Font(Ui.TextFont,9),TextAlign=ContentAlignment.MiddleLeft,
                Text="支援外接螢幕插拔自動偵測與全域熱鍵（Ctrl+Alt+↑/↓ 調亮度、Ctrl+Alt+Q 切安靜）。外接螢幕需開啟 DDC/CI。"};
            page.Controls.Add(displayHost); page.Controls.Add(hint); page.Controls.Add(bar);
        }
        async Task ApplySceneBrightness(int internalPct, int externalPct)
        {
            if (!displaysLoaded && !preview) await ScanDisplays();
            foreach (BrightnessCard card in brightnessCards)
                card.SetFromUser(card.Target.Internal ? internalPct : externalPct, false);
        }
        async Task StepAllBrightness(int delta)
        {
            if (!displaysLoaded && !preview) await ScanDisplays();
            foreach (BrightnessCard card in brightnessCards)
                card.SetFromUser(card.CurrentPercent + delta, false);
        }
        async Task RefreshExternalBrightness()
        {
            if (brightnessCards.Count == 0) return;
            foreach (BrightnessCard card in brightnessCards.ToArray())
            {
                DisplayTarget t = card.Target;
                if (!t.Internal) continue;
                bool ok = await Task.Run(delegate { return t.Refresh(); });
                if (ok && !IsDisposed) card.SyncExternalValue(t.Percent);
            }
        }
        void ShowDisplays(List<DisplayTarget> found)
        {
            foreach(Control card in new List<Control>(displayHost.Controls.Cast<Control>())){displayHost.Controls.Remove(card);card.Dispose();}
            brightnessCards.Clear();
            foreach(DisplayTarget display in displays)display.Dispose();
            displays.Clear();
            foreach(DisplayTarget display in found)
            {
                displays.Add(display);
                BrightnessCard card = new BrightnessCard(display, preview);
                card.UserBrightnessChanged += delegate(BrightnessCard sender, int pct)
                {
                    if (!settings.SyncBrightness) return;
                    foreach (BrightnessCard other in brightnessCards)
                        if (other != sender) other.SetFromUser(pct, false);
                };
                brightnessCards.Add(card);
                displayHost.Controls.Add(card);
            }
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
            displayNote.Text=failure!=null?"偵測失敗："+failure:displays.Count==0?"找不到可以調整亮度的螢幕。":"找到 "+displays.Count+" 個螢幕，拖曳滑桿、按情境鈕或用 Ctrl+Alt+↑/↓ 就能調整。";
            scanning=false; rescan.Enabled=true; displaysLoaded=true;
        }
        void Log(string message)
        {
            events.Add(DateTime.Now.ToString("HH:mm:ss")+"   "+message);if(events.Count>120)events.RemoveAt(0);
            if(!preview)try{Directory.CreateDirectory(Path.GetDirectoryName(logFile));FanSample s=session==null?null:session.Latest;
                File.AppendAllText(logFile,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"  "+message+(s==null?"":"  [ACPI "+Value(s.ZoneC)+"°C GPU "+Value(s.GpuC)+"°C CPU "+Value(s.CpuLoad)+"% GPU負載 "+Value(s.GpuLoad)+"% 風扇 "+s.Fan1*100+"/"+s.Fan2*100+"]")+Environment.NewLine,Encoding.UTF8);}catch(Exception){}
            eventLog.Text=String.Join(Environment.NewLine+Environment.NewLine,events.ToArray());eventLog.SelectionStart=eventLog.TextLength;eventLog.ScrollToCaret();
        }
        async Task Run(Action action,bool clearError=true)
        {
            if(busy)return;busy=true;if(clearError)error="";UpdateView(false);
            try{await Task.Run(action);}catch(Exception failure){error=failure.Message;}
            finally{busy=false;if(!IsDisposed)UpdateView(true);if(closePending && !IsDisposed){closePending=false;BeginInvoke(new Action(Close));}}
        }
        static string Value(double value){return Double.IsNaN(value)?"—":value.ToString("0");}
        static string ModeName(CoolingMode state,int level,int running)
        {
            if(state==CoolingMode.RecoveryFailed)return "恢復待確認";
            if(state!=CoolingMode.Quiet)return "原有管理方式";
            string name=level==FanLevels.Quiet?"安靜模式 · 1800 RPM":(FanLevels.IsBoost(level)?"加強散熱 · ":"低速 · ")+FanLevels.Rpm(level)+" RPM";
            return running>level?name+"（升溫中，暫時 "+FanLevels.Rpm(running)+"）":name;
        }
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
            int running=previewRunning>0?previewRunning:session!=null && session.Mode==CoolingMode.Quiet?session.Level:level;
            string modeName=ModeName(state,level,running);
            Color modeColor=state==CoolingMode.RecoveryFailed?Ui.Danger:state!=CoolingMode.Quiet?Ink:FanLevels.IsBoost(level)?Ui.Warn:Accent;
            currentMode.Text="目前："+modeName;controlMode.Text=modeName;currentMode.ForeColor=modeColor;controlMode.ForeColor=modeColor;
            int round=session==null?1:Math.Max(1,session.Round);
            countdown.Text=state!=CoolingMode.Quiet?"低速每 5 分鐘檢查一次":timeLimited?"第 "+round+" 輪 · "+TimeSpan.FromSeconds(seconds).ToString(@"m\:ss")+" 後檢查，符合就續轉":"不限時間 · 關閉程式時恢復自動";
            controlTimer.Text=state==CoolingMode.Quiet?countdown.Text:"選好轉速後按「套用」。";
            sessionProgress.Value=timeLimited?Math.Max(0,Math.Min(300,seconds)):0;
            connection.Text=connected?"●  已取得管理員權限":"○  尚未取得控制權限";connection.ForeColor=connected?Accent:Muted;admin.Visible=!connected;admin.Enabled=!busy;
            foreach(Button button in quietButtons)button.Enabled=CanApply((int)button.Tag,data,state,level,connected);
            foreach(Button button in autoButtons)button.Enabled=!busy && connected && needsRecovery;

            bool boost=FanLevels.IsBoost(selectedLevel);
            Color tone=boost?Ui.Warn:Accent;
            speedValue.Text=FanLevels.Rpm(selectedLevel)+" RPM";speedValue.ForeColor=tone;
            speedZone.Text=boost?"加強散熱":"低速區 · "+FanLevels.PresetName(settings.Preset);speedZone.ForeColor=tone;speedSlider.FillColor=tone;
            string blocker=boost?null:FanLevels.LowSpeedBlocker(data);
            speedNote.ForeColor=Muted;
            int[] steps = FanLevels.CurveThresholds(settings.Preset);
            if(!connected)speedNote.Text="取得管理員權限後才能調整風扇。";
            else if(blocker!=null){speedNote.Text="現在不能低速："+blocker+"可改用 3300 RPM 以上。";speedNote.ForeColor=Ui.Warn;}
            else speedNote.Text=boost?"風扇只會更快，不受溫度與時間限制；結束程式時交回原廠自動。":"比原廠安靜；"+steps[0]+"°C 起小幅加速，到 85°C 才交回原廠；降溫後自動重回安靜模式。";
            bool active=state==CoolingMode.Quiet && level==selectedLevel;
            applySpeed.Text=active?"已套用 "+FanLevels.Rpm(selectedLevel)+" RPM":(state==CoolingMode.Quiet?"改為 ":"套用 ")+FanLevels.Rpm(selectedLevel)+" RPM";
            applySpeed.Enabled=CanApply(selectedLevel,data,state,level,connected);

            if (curveChart != null)
            {
                curveChart.Preset = settings.Preset;
                curveChart.BaseLevel = boost ? FanLevels.Quiet : selectedLevel;
                curveChart.ActiveLevel = state == CoolingMode.Quiet ? running : selectedLevel;
                curveChart.IsQuietActive = state == CoolingMode.Quiet && !boost;
                curveChart.LiveZone = data == null ? Double.NaN : data.ZoneC;
                curveChart.LiveGpu = data == null ? Double.NaN : data.GpuC;
                curveChart.Invalidate();
            }
            UpdateTrayBadge(data, state, level, running);
        }
        void UpdateView(bool record)
        {
            FanSample data=session==null?null:session.Latest;
            Fill(data,session==null?CoolingMode.Automatic:session.Mode,session==null?0:session.TargetLevel,session==null?0:session.RemainingSeconds,session!=null && session.TimeLimited,session!=null && session.NeedsRecovery,Program.IsAdmin());
            string message=error.Length>0?error:session==null?"取得管理員權限後開始監看；目前未變更風扇。":session.Status;
            status.Text=(busy?"正在處理，請稍候…":message)+"\r\n轉速為 BIOS 等級估計；ACPI 熱區並非 CPU 核心溫度。快捷鍵 Ctrl+Alt+Q 切換安靜、Ctrl+Alt+↑/↓ 調整螢幕亮度。";
            if(record && data!=null){history.Add(DateTime.UtcNow,data.Fan1*100,data.Fan2*100,data.ZoneC,data.GpuC);refreshed.Text="更新 "+DateTime.Now.ToString("HH:mm:ss");}
            if(record && message!=loggedStatus){loggedStatus=message;Log(message);}
        }
        void PaintPreview()
        {
            bool locked=previewScene=="locked",automatic=previewScene=="auto"||previewScene=="hot";
            FanSample data=locked?null:new FanSample(54,47,12,8,automatic?27:18,automatic?29:18);
            if(previewScene=="boost"){selectedLevel=42;speedSlider.Value=42;data=new FanSample(78,56,38,2,42,42);}
            if(previewScene=="curve"){data=new FanSample(71,52,14,6,26,26);previewRunning=26;}
            if(previewScene=="hot"){selectedLevel=25;speedSlider.Value=25;data=new FanSample(83,56,38,2,28,31);}
            Fill(data,locked||automatic?CoolingMode.Automatic:CoolingMode.Quiet,locked||automatic?0:previewScene=="boost"?42:18,locked||automatic||previewScene=="boost"?0:272,!locked&&!automatic&&previewScene!="boost",!locked&&!automatic,!locked);
            if(!locked)for(int i=0;i<=60;i++)history.Add(DateTime.UtcNow.AddSeconds(-120+2*i),(int)(i<38?2850+Math.Sin(i*.35)*130:1800+Math.Sin(i)*45),(int)(i<38?3000+Math.Cos(i*.32)*110:1800+Math.Cos(i)*45),i<38?63+Math.Sin(i*.2)*3:54+Math.Sin(i*.25)*1.5,i<38?52+Math.Cos(i*.2)*2:47+Math.Cos(i*.2)*1);
            refreshed.Text=locked?"等待連線":"更新 "+DateTime.Now.ToString("HH:mm:ss");
            status.Text="介面預覽 · 範例讀值，未控制風扇。\r\n轉速為 BIOS 等級估計；ACPI 熱區並非 CPU 核心溫度。快捷鍵 Ctrl+Alt+Q 切換安靜、Ctrl+Alt+↑/↓ 調整螢幕亮度。";
            Log("控制面板已開啟。");if(!locked){Log("管理員連線完成，開始監看。");Log("啟用安靜模式，目標約 1800 RPM。");}
            if(previewScene=="controls"||previewScene=="boost"||previewScene=="hot"||previewScene=="curve")ShowPage(1);if(previewScene=="protection")ShowPage(2);
            if(previewScene=="display")
            {
                ShowDisplays(new List<DisplayTarget>{new PreviewDisplay("內建螢幕","筆電面板 · Windows 亮度",true,100),new PreviewDisplay("VZ249HG","外接螢幕 · DDC/CI",false,65)});
                displayNote.Text="介面預覽 · 範例螢幕，未調整亮度。"; ShowPage(3);
            }
        }
    }
}
