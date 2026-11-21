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
                && CpuLoad >= 0 && CpuLoad <= 100 && GpuLoad >= 0 && GpuLoad <= 100
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
        // Why a low-speed level cannot start now, or null when it can.
        public static string LowSpeedBlocker(FanSample sample)
        {
            if (sample == null || !sample.Valid) return "溫度或負載資料尚未就緒。";
            if (sample.ZoneC >= 60) return "ACPI 熱區 " + sample.ZoneC.ToString("0") + "°C，需低於 60°C。";
            if (sample.GpuC >= 55) return "GPU " + sample.GpuC.ToString("0") + "°C，需低於 55°C。";
            if (sample.CpuLoad > 25) return "CPU 負載 " + sample.CpuLoad.ToString("0") + "%，需不超過 25%。";
            if (sample.GpuLoad > 30) return "GPU 負載 " + sample.GpuLoad.ToString("0") + "%，需不超過 30%。";
            return null;
        }
    }

    public sealed class QuietSession
    {
        readonly IQuietDevice device;
        readonly IRecoveryGuard recovery;
        readonly Func<double> seconds;
        double began, lastWrite, busySince = -1;
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
                throw new InvalidOperationException("溫度或負載資料尚未就緒，已停用手動轉速。");
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
                device.ApplyLevel(level);
                TargetLevel = level; Mode = CoolingMode.Quiet; Round = 1;
                began = lastWrite = seconds(); busySince = -1;
                Status = boost
                    ? "加強散熱已送出，風扇將升到約 " + FanLevels.Rpm(level) + " RPM；不限時間。"
                    : "低速已送出，風扇將逐步降至約 " + FanLevels.Rpm(level) + " RPM；每 5 分鐘檢查一次，條件符合就繼續。";
            }
            catch (Exception error)
            {
                if (NeedsRecovery) Restore("設定失敗，已恢復自動散熱。");
                throw new InvalidOperationException("無法設定轉速：" + error.Message, error);
            }
        }

        public void Restore(string reason)
        {
            if (!NeedsRecovery) { Mode = CoolingMode.Automatic; TargetLevel = 0; Status = reason; return; }
            try
            {
                device.RestoreAutomatic();
                NeedsRecovery = false;
                Mode = CoolingMode.Automatic; TargetLevel = 0;
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
                Mode = CoolingMode.Automatic; TargetLevel = 0;
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
                if (Latest.ZoneC >= 60 || Latest.GpuC >= 55)
                { Restore("溫度升高，已恢復自動散熱。"); return; }
                if (seconds() - began >= FanLevels.LowSpeedSeconds)
                {
                    // End of a five-minute round: keep going only if the start conditions still hold.
                    string blocker = FanLevels.LowSpeedBlocker(Latest);
                    if (blocker != null) { Restore("本輪五分鐘結束，" + blocker + "已恢復自動散熱。"); return; }
                    try
                    {
                        recovery.Arm(FanLevels.LowSpeedSeconds + 7);
                        device.ApplyLevel(TargetLevel);
                        began = lastWrite = seconds(); busySince = -1; Round++;
                        Status = "溫度與負載仍正常，已自動續轉第 " + Round + " 輪（約 " + FanLevels.Rpm(TargetLevel) + " RPM）。";
                    }
                    catch (Exception) { Restore("續轉失敗，已恢復自動散熱。"); }
                    return;
                }
                if (Latest.CpuLoad >= 70 || Latest.GpuLoad >= 50)
                { Restore("運算負載升高，已恢復自動散熱。"); return; }
                if (Latest.CpuLoad >= 35)
                {
                    if (busySince < 0) busySince = seconds();
                    if (seconds() - busySince >= 4)
                    { Restore("CPU 持續忙碌，已恢復自動散熱。"); return; }
                }
                else busySince = -1;
                if (seconds() - began >= 15 && (Latest.Fan1 < TargetLevel - 3 || Latest.Fan2 < TargetLevel - 3 || Latest.Fan1 > TargetLevel + 3 || Latest.Fan2 > TargetLevel + 3))
                { Restore("韌體未維持低速範圍，已恢復自動散熱。"); return; }
            }
            else if (seconds() - began >= 15 &&(Latest.Fan1 < TargetLevel - 5 || Latest.Fan2 < TargetLevel - 5))
            { Restore("風扇沒有升到目標轉速，已恢復自動散熱。"); return; }
            try
            {
                if (seconds() - lastWrite >= 30) { device.ApplyLevel(TargetLevel); lastWrite = seconds(); }
                recovery.Pulse();
            }
            catch (Exception) { Restore("手動轉速中斷，已恢復自動散熱。"); }
        }
    }
}
