using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VictusQuiet
{
    static class Program
    {
        public static bool IsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        static int Verify(string path)
        {
            using (StreamWriter log = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                log.AutoFlush = true;
                log.WriteLine("VictusQuiet 完成版短暫驗證 " + DateTimeOffset.Now.ToString("o"));
                if (!IsAdmin()) { log.WriteLine("ERROR: administrator privileges required"); return 2; }
                using (HpQuietDevice device = new HpQuietDevice())
                using (ProcessRecoveryGuard guard = new ProcessRecoveryGuard())
                {
                    QuietSession session = new QuietSession(device, guard, () => HpQuietDevice.Seconds);
                    try
                    {
                        bool ready = false;
                        for (int i = 0; i < 45; i++)
                        {
                            Thread.Sleep(2000); session.Poll(); FanSample sample = session.Latest;
                            if (sample != null) log.WriteLine("Preflight zone=" + sample.ZoneC.ToString("0.0") + " gpu=" + sample.GpuC + " cpuLoad=" + sample.CpuLoad.ToString("0.0"));
                            if (sample != null && sample.Valid && sample.ZoneC < 60 && sample.GpuC < 55 && sample.CpuLoad <= 25 && sample.GpuLoad <= 30) { ready = true; break; }
                        }
                        if (!ready) { log.WriteLine("SKIPPED: no cool idle window; no quiet write"); return 3; }
                        bool reachedTarget = false;
                        session.Enable(); log.WriteLine("QUIET ENABLED; independent recovery process ready");
                        for (int i = 0; i < 6; i++)
                        {
                            Thread.Sleep(2000); session.Poll(); FanSample sample = session.Latest;
                            log.WriteLine("Mode=" + session.Mode + " fans=" + (sample == null ? "unknown" : sample.Fan1 + "/" + sample.Fan2) + " zone=" + (sample == null ? "unknown" : sample.ZoneC.ToString("0.0")) + " gpu=" + (sample == null ? "unknown" : sample.GpuC.ToString()) + " status=" + session.Status);
                            if (session.Mode == CoolingMode.Quiet && sample != null && sample.Fan1 >= 16 && sample.Fan1 <= 20 && sample.Fan2 >= 16 && sample.Fan2 <= 20) reachedTarget = true;
                            if (session.Mode != CoolingMode.Quiet) break;
                        }
                        session.Restore("完成驗證，已恢復自動散熱。");
                        log.WriteLine("RESTORED: NeedsRecovery=" + session.NeedsRecovery);
                        if (!reachedTarget) { log.WriteLine("INCOMPLETE: target range not observed; restoration completed"); return 3; }
                        log.WriteLine("PASS: low-speed sequence completed; restoration confirmed"); return 0;
                    }
                    catch (Exception error) { log.WriteLine("ERROR: " + error); return 1; }
                    finally
                    {
                        if (session.NeedsRecovery)
                        {
                            try { session.Restore("驗證結束，已恢復自動散熱。"); log.WriteLine("Finally restored"); }
                            catch (Exception error) { log.WriteLine("RESTORATION ERROR; independent recovery remains armed: " + error.Message); }
                        }
                    }
                }
            }
        }
        // Short real-hardware check of adjustable speeds: 4200 and 5000 RPM always (they only add cooling),
        // 2500 RPM only when cool and idle. Restores automatic cooling at the end in every case.
        static int VerifyLevels(string path)
        {
            using (StreamWriter log = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                log.AutoFlush = true;
                log.WriteLine("Victus 控制中心 可調轉速驗證 " + DateTimeOffset.Now.ToString("o"));
                if (!IsAdmin()) { log.WriteLine("ERROR: administrator privileges required"); return 2; }
                int failures = 0;
                using (HpQuietDevice device = new HpQuietDevice())
                using (ProcessRecoveryGuard guard = new ProcessRecoveryGuard())
                {
                    QuietSession session = new QuietSession(device, guard, () => HpQuietDevice.Seconds);
                    try
                    {
                        Thread.Sleep(2000); session.Poll();
                        FanSample start = session.Latest;
                        log.WriteLine("Start fans=" + (start == null ? "unknown" : start.Fan1 + "/" + start.Fan2) + " zone=" + (start == null ? "?" : start.ZoneC.ToString("0.0")) + " gpu=" + (start == null ? "?" : start.GpuC.ToString("0")));
                        List<int> levels = new List<int> { 42, 50 };
                        if (FanLevels.LowSpeedBlocker(start) == null) levels.Add(25); else log.WriteLine("SKIP 2500 RPM: " + FanLevels.LowSpeedBlocker(start));
                        foreach (int level in levels)
                        {
                            session.Enable(level);
                            bool reached = false; FanSample sample = null;
                            for (int i = 0; i < 8 && session.Mode == CoolingMode.Quiet; i++)
                            {
                                Thread.Sleep(2000); session.Poll(); sample = session.Latest;
                                if (sample == null) continue;
                                bool ok = FanLevels.IsBoost(level) ? sample.Fan1 >= level - 5 && sample.Fan2 >= level - 5
                                    : Math.Abs(sample.Fan1 - level) <= 3 && Math.Abs(sample.Fan2 - level) <= 3;
                                if (ok) { reached = true; break; }
                            }
                            log.WriteLine((reached ? "PASS " : "FAIL ") + FanLevels.Rpm(level) + " RPM -> fans " + (sample == null ? "unknown" : FanLevels.Rpm(sample.Fan1) + "/" + FanLevels.Rpm(sample.Fan2)) + " mode=" + session.Mode + " status=" + session.Status);
                            if (!reached) failures++;
                            if (session.Mode != CoolingMode.Quiet) break;
                        }
                    }
                    catch (Exception error) { log.WriteLine("ERROR: " + error.Message); failures++; }
                    finally
                    {
                        try { session.Restore("驗證結束，已恢復自動散熱。"); log.WriteLine("RESTORED: NeedsRecovery=" + session.NeedsRecovery); }
                        catch (Exception error) { log.WriteLine("RESTORATION ERROR; independent recovery remains armed: " + error.Message); failures++; }
                    }
                }
                log.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)");
                return failures == 0 ? 0 : 1;
            }
        }
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--verify-levels")
            {
                bool owns = false;
                using (Mutex control = new Mutex(false, @"Local\VictusQuiet-88F8-Control"))
                {
                    try { try { owns = control.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                        if (!owns) { File.WriteAllText(args[1], "Another control instance is running; close the panel first"); return 2; }
                        return VerifyLevels(args[1]);
                    }
                    finally { if (owns) control.ReleaseMutex(); }
                }
            }
            if (args.Length == 3 && args[0] == "--rescue") { int parent; return Int32.TryParse(args[2], out parent) ? ProcessRecoveryGuard.Watch(args[1], parent) : 2; }
            if (args.Length == 2 && args[0] == "--guard-check")
            {
                try { using (ProcessRecoveryGuard guard = new ProcessRecoveryGuard()) { guard.Begin(); guard.Complete(); } File.WriteAllText(args[1], "PASS: independent process opened named shared memory and acknowledged readiness; no fan writes"); return 0; }
                catch (Exception error) { File.WriteAllText(args[1], error.ToString()); return 1; }
            }
            if (args.Length == 2 && args[0] == "--verify")
            {
                bool verifyOwns = false;
                using (Mutex control = new Mutex(false, @"Local\VictusQuiet-88F8-Control"))
                {
                    try { try { verifyOwns = control.WaitOne(0); } catch (AbandonedMutexException) { verifyOwns = true; }
                        if (!verifyOwns) { File.WriteAllText(args[1], "Another control instance is running; verification skipped"); return 2; }
                        return Verify(args[1]);
                    }
                    finally { if (verifyOwns) control.ReleaseMutex(); }
                }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool render = args.Length == 2 && (args[0] == "--render" || args[0] == "--render-auto" || args[0] == "--render-locked" || args[0] == "--render-controls" || args[0] == "--render-protection" || args[0] == "--render-display" || args[0] == "--render-boost" || args[0] == "--render-hot" || args[0] == "--render-curve");
            string scene = render && args[0] != "--render" ? args[0].Substring(9) : "quiet";
            using (QuietWindow window = new QuietWindow(render, scene))
            {
                if (render)
                {
                    window.StartPosition = FormStartPosition.Manual; window.Location = new Point(-16000,-16000);
                    window.Show(); Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]); }
                    return 0;
                }
                bool owns = false;
                using (Mutex control = new Mutex(false, @"Local\VictusQuiet-88F8-Control"))
                {
                    try
                    {
                        if (IsAdmin())
                        {
                            try { owns = control.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                            if (!owns) { MessageBox.Show("已有 Victus 控制中心正在執行，請使用原來的視窗。", "Victus 控制中心"); return 2; }
                        }
                        Application.Run(window);
                    }
                    finally { if (owns) control.ReleaseMutex(); }
                }
            }
            return 0;
        }
    }
}
