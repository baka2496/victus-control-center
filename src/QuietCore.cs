using System;

namespace VictusQuiet
{
    public sealed class FanSample
    {
        public readonly double ZoneC, GpuC, CpuLoad, GpuLoad;
        public readonly int Fan1, Fan2;
        public FanSample(double zone, double gpu, double cpuLoad, double gpuLoad, int fan1, int fan2)
        { ZoneC = zone; GpuC = gpu; CpuLoad = cpuLoad; GpuLoad = gpuLoad; Fan1 = fan1; Fan2 = fan2; }
        public bool Valid
        {
            get { return ZoneC >= 10 && ZoneC <= 115 && GpuC >= 10 && GpuC <= 110
                && Fan1 >= 0 && Fan1 <= 80 && Fan2 >= 0 && Fan2 <= 80; }
        }
    }
    public interface IQuietDevice : IDisposable
    {
        FanSample Read();
        // Level is in BIOS units of about 100 RPM; only FanLevels.Minimum..Maximum are accepted.
        void ApplyLevel(int level);
        void RestoreAutomatic();
    }
    public interface IRecoveryGuard : IDisposable
    {
        bool WasRecovered { get; }
        void Begin();
        // limitSeconds 0 = no time limit (boost); otherwise the guard restores after that many seconds.
        void Arm(int limitSeconds);
        void Pulse();
        void Complete();
    }
    public enum CoolingMode { Automatic, Quiet, RecoveryFailed }

    public static class FanLevels
    {
        public const int Minimum = 18, Maximum = 50, Quiet = 18;
        // At or above this level the fans run faster than the firmware's hand-off speed: no temperature,
        // load or time limits apply, because the change can only add cooling.
        public const int BoostFrom = 33;
        public const int LowSpeedSeconds = 300;
        public static bool IsBoost(int level) { return level >= BoostFrom; }
        public static int Rpm(int level) { return level * 100; }

        // Quiet curve: while a low speed runs, warmth raises the fans a little instead of handing the whole
        // machine back to the louder firmware control. Only these hard limits end low speed for temperature.
        public const int HandBackZone = 75, HandBackGpu = 70;
        // Starting (and renewing a five-minute round) needs a little margin below the hard limits.
        // Only temperature matters: load is shown in the panel but never blocks or ends a low speed.
        public const int StartZone = 72, StartGpu = 67;
        // The quiet curve never goes above this; faster than that is boost territory.
        public const int LowCeiling = 32;
        // Lowest level the temperature allows right now (0 = no floor). GPU counts 5°C hotter than ACPI.
        public static int CurveFloor(double zone, double gpu)
        {
            double t = Math.Max(zone, gpu + 5);
            if (t >= 72) return 32;
            if (t >= 68) return 29;
            if (t >= 64) return 26;
            if (t >= 60) return 22;
            return 0;
        }
        // Why a low-speed level cannot start now, or null when it can.
        public static string LowSpeedBlocker(FanSample sample)
        {
            if (sample == null || !sample.Valid) return "溫度資料尚未就緒。";
            if (sample.ZoneC >= StartZone) return "ACPI 熱區 " + sample.ZoneC.ToString("0") + "°C，需低於 " + StartZone + "°C。";
            if (sample.GpuC >= StartGpu) return "GPU " + sample.GpuC.ToString("0") + "°C，需低於 " + StartGpu + "°C。";
            return null;
        }
    }

    public sealed class QuietSession
    {
        readonly IQuietDevice device;
        readonly IRecoveryGuard recovery;
        readonly Func<double> seconds;
        double began, lastWrite, levelSince;
        int thermalFloor;
        // Level actually written to the fans: TargetLevel, raised by the quiet curve when warm.
        public int Level { get; private set; }
        public CoolingMode Mode { get; private set; }
        public bool NeedsRecovery { get; private set; }
        public FanSample Latest { get; private set; }
        public string Status { get; private set; }
        public int TargetLevel { get; private set; }
        // 1 for the first five-minute low-speed round, +1 each time it renews automatically.
        public int Round { get; private set; }
        public bool TimeLimited { get { return Mode == CoolingMode.Quiet && !FanLevels.IsBoost(TargetLevel); } }
        public int RemainingSeconds { get { return TimeLimited ? Math.Max(0, FanLevels.LowSpeedSeconds - (int)(seconds() - began)) : 0; } }
        public QuietSession(IQuietDevice device, IRecoveryGuard recovery, Func<double> monotonicSeconds)
        {
            this.device = device; this.recovery = recovery; seconds = monotonicSeconds;
            Mode = CoolingMode.Automatic; Status = "尚未手動設定；風扇由原有控制方式管理。";
        }

        public void Enable() { Enable(FanLevels.Quiet); }

        // Starts manual control, or changes the level of an active manual session.
        public void Enable(int level)
        {
            if (level < FanLevels.Minimum || level > FanLevels.Maximum)
                throw new InvalidOperationException("只開放 " + FanLevels.Rpm(FanLevels.Minimum) + "～" + FanLevels.Rpm(FanLevels.Maximum) + " RPM。");
            if (Mode == CoolingMode.RecoveryFailed) throw new InvalidOperationException("請先恢復自動散熱。");
            Latest = device.Read();
            if (Latest == null || !Latest.Valid)
                throw new InvalidOperationException("溫度資料尚未就緒，已停用手動轉速。");
            bool boost = FanLevels.IsBoost(level);
            if (!boost)
            {
                string blocker = FanLevels.LowSpeedBlocker(Latest);
                if (blocker != null) throw new InvalidOperationException("目前不能低速：" + blocker + "可改用 " + FanLevels.Rpm(FanLevels.BoostFrom) + " RPM 以上。");
            }
            bool changing = Mode == CoolingMode.Quiet && NeedsRecovery;
            try
            {
                if (!changing) { recovery.Begin(); NeedsRecovery = true; }
                // Re-arming restarts the guard's clock, so switching levels starts a fresh low-speed round.
                recovery.Arm(boost ? 0 : FanLevels.LowSpeedSeconds + 7);
                int floor = boost ? 0 : FanLevels.CurveFloor(Latest.ZoneC, Latest.GpuC);
                int write = boost ? level : Math.Min(FanLevels.LowCeiling, Math.Max(level, floor));
                device.ApplyLevel(write);
                TargetLevel = level; Level = write; thermalFloor = floor; Mode = CoolingMode.Quiet; Round = 1;
                began = lastWrite = levelSince = seconds();
                Status = boost
                    ? "加強散熱已送出，風扇將升到約 " + FanLevels.Rpm(level) + " RPM；不限時間。"
                    : write > level
                        ? "低速已送出；目前溫度偏高，先用約 " + FanLevels.Rpm(write) + " RPM，降溫後會自動降到 " + FanLevels.Rpm(level) + " RPM。"
                        : "低速已送出，風扇將逐步降至約 " + FanLevels.Rpm(level) + " RPM；升溫時會小幅加速，不會直接切回原廠。";
            }
            catch (Exception error)
            {
                if (NeedsRecovery) Restore("設定失敗，已恢復自動散熱。");
                throw new InvalidOperationException("無法設定轉速：" + error.Message, error);
            }
        }

        public void Restore(string reason)
        {
            if (!NeedsRecovery) { Mode = CoolingMode.Automatic; TargetLevel = 0; Level = 0; Status = reason; return; }
            try
            {
                device.RestoreAutomatic();
                NeedsRecovery = false;
                Mode = CoolingMode.Automatic; TargetLevel = 0; Level = 0;
                recovery.Complete();
                Status = reason;
            }
            catch (Exception error)
            {
                Mode = CoolingMode.RecoveryFailed;
                Status = "尚未確認恢復，備援程序仍啟用中。請按「恢復自動」重試。";
                throw new InvalidOperationException(Status + " " + error.Message, error);
            }
        }

        public void Poll()
        {
            bool externalRestore = false;
            try { if (NeedsRecovery) externalRestore = recovery.WasRecovered; }
            catch (Exception) { Restore("備援程序中斷，已恢復自動散熱。"); return; }
            if (externalRestore)
            {
                NeedsRecovery = false;
                Mode = CoolingMode.Automatic; TargetLevel = 0; Level = 0;
                recovery.Complete();
                Status = "備援程序已恢復自動散熱；手動轉速已結束。";
            }
            try { Latest = device.Read(); }
            catch (Exception error)
            {
                Latest = null;
                if (NeedsRecovery) Restore("監測中斷，已恢復自動散熱。");
                else Status = "讀取資料失敗：" + error.Message;
                return;
            }
            if (NeedsRecovery && (Latest == null || !Latest.Valid))
            { Restore("監測資料無效，已恢復自動散熱。"); return; }
            if (Mode != CoolingMode.Quiet) return;
            bool boost = FanLevels.IsBoost(TargetLevel);
            if (!boost)
            {
                double now = seconds();
                if (Latest.ZoneC >= FanLevels.HandBackZone || Latest.GpuC >= FanLevels.HandBackGpu)
                { Restore("溫度達 ACPI " + Latest.ZoneC.ToString("0") + "°C／GPU " + Latest.GpuC.ToString("0") + "°C（上限 " + FanLevels.HandBackZone + "／" + FanLevels.HandBackGpu + "°C），已恢復自動散熱。"); return; }
                if (now - began >= FanLevels.LowSpeedSeconds)
                {
                    // End of a five-minute round: keep going only if the start conditions still hold.
                    string blocker = FanLevels.LowSpeedBlocker(Latest);
                    if (blocker != null) { Restore("本輪五分鐘結束，" + blocker + "已恢復自動散熱。"); return; }
                    try
                    {
                        recovery.Arm(FanLevels.LowSpeedSeconds + 7);
                        device.ApplyLevel(Level);
                        began = lastWrite = now; Round++;
                        Status = "溫度仍正常，已自動續轉第 " + Round + " 輪（約 " + FanLevels.Rpm(Level) + " RPM）。";
                    }
                    catch (Exception) { Restore("續轉失敗，已恢復自動散熱。"); }
                    return;
                }
                // Quiet curve with 3°C hysteresis: rise at once, come down only when clearly cooler.
                int rising = FanLevels.CurveFloor(Latest.ZoneC, Latest.GpuC);
                if (rising >= thermalFloor) thermalFloor = rising;
                else thermalFloor = Math.Min(thermalFloor, FanLevels.CurveFloor(Latest.ZoneC + 3, Latest.GpuC + 3));
                int want = Math.Min(FanLevels.LowCeiling, Math.Max(TargetLevel, thermalFloor));
                if (want != Level)
                {
                    try
                    {
                        device.ApplyLevel(want);
                        string why = want <= Level ? "已降溫，降回約 "
                            : "溫度升到 ACPI " + Latest.ZoneC.ToString("0") + "°C／GPU " + Latest.GpuC.ToString("0") + "°C，小幅加速到約 ";
                        Level = want; levelSince = lastWrite = now;
                        Status = why + FanLevels.Rpm(want) + " RPM（仍比原廠安靜）。";
                    }
                    catch (Exception) { Restore("調整轉速失敗，已恢復自動散熱。"); return; }
                }
                else if (now - levelSince >= 20 && (Latest.Fan1 < Level - 3 || Latest.Fan2 < Level - 3 || Latest.Fan1 > Level + 3 || Latest.Fan2 > Level + 3))
                { Restore("韌體未維持設定轉速（風扇 " + FanLevels.Rpm(Latest.Fan1) + "／" + FanLevels.Rpm(Latest.Fan2) + "，設定 " + FanLevels.Rpm(Level) + "），已恢復自動散熱。"); return; }
            }
            else if (seconds() - began >= 15 &&(Latest.Fan1 < TargetLevel - 5 || Latest.Fan2 < TargetLevel - 5))
            { Restore("風扇沒有升到目標轉速，已恢復自動散熱。"); return; }
            try
            {
                if (seconds() - lastWrite >= 30) { device.ApplyLevel(Level); lastWrite = seconds(); }
                recovery.Pulse();
            }
            catch (Exception) { Restore("手動轉速中斷，已恢復自動散熱。"); }
        }
    }
}
