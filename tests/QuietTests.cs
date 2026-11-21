using System;
using VictusQuiet;

sealed class FirmwareSimulator : IQuietDevice
{
    public FanSample Sample = new FanSample(54, 47, 10, 8, 18, 18);
    public bool QuietApplied;
    public int AppliedLevel;
    public bool FailRead;
    public bool FailRestore;
    public FanSample Read() { if (FailRead) throw new Exception("Sensor unavailable"); return Sample; }
    public void ApplyLevel(int level) { QuietApplied = true; AppliedLevel = level; }
    public void RestoreAutomatic() { if (FailRestore) throw new Exception("Firmware refused restoration"); QuietApplied = false; AppliedLevel = 0; }
    public void Dispose() { }
}
sealed class RecoverySimulator : IRecoveryGuard
{
    public bool WasRecovered { get; set; }
    public int Limit = -1;
    public void Begin() { }
    public void Arm(int limitSeconds) { Limit = limitSeconds; }
    public void Pulse() { }
    public void Complete() { }
    public void Dispose() { }
}
static class QuietTests
{
    static void MustReject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected a refusal");
    }
    static int Main()
    {
        try
        {
            FirmwareSimulator device = new FirmwareSimulator();
            device.Sample = new FanSample(Double.NaN, 47, 10, 8, 18, 18);
            QuietSession session = new QuietSession(device, new RecoverySimulator(), () => 0);
            MustReject(() => session.Enable());
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Missing temperature must not enable quiet mode");
            Console.WriteLine("PASS: missing thermal data prevents low-speed control");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.FailRead = true;
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery || session.Mode != CoolingMode.Automatic)
                throw new Exception("Lost telemetry must restore automatic cooling");
            Console.WriteLine("PASS: missing telemetry restores automatic cooling");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.Sample = new FanSample(60, 47, 10, 8, 18, 18);
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("A hot thermal zone must restore automatic cooling");
            Console.WriteLine("PASS: rising temperature restores automatic cooling");
            double clock = 0;
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable();
            device.Sample = new FanSample(54, 47, 30, 8, 18, 18);
            clock = 301;
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("A round ending while busier than the start rule must restore automatic cooling");
            Console.WriteLine("PASS: five-minute round ends low speed when start conditions no longer hold");
            clock = 0; device = new FirmwareSimulator(); RecoverySimulator renewGuard = new RecoverySimulator();
            session = new QuietSession(device, renewGuard, () => clock);
            session.Enable(); renewGuard.Limit = -1;
            clock = 301; session.Poll();
            if (!session.NeedsRecovery || session.Mode != CoolingMode.Quiet || session.Round != 2 || renewGuard.Limit != 307 || session.RemainingSeconds != 300)
                throw new Exception("A cool, idle machine must renew low speed and re-arm the guard");
            clock = 602; session.Poll();
            if (session.Round != 3 || !session.NeedsRecovery) throw new Exception("Low speed must keep renewing while conditions hold");
            device.Sample = new FanSample(61, 47, 10, 8, 18, 18); clock = 610; session.Poll();
            if (session.NeedsRecovery) throw new Exception("Heat must still end a renewed round immediately");
            Console.WriteLine("PASS: cool and idle renews low speed every five minutes; heat still ends it at once");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.Sample = new FanSample(54, 47, 75, 8, 18, 18);
            session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("High CPU load must restore automatic cooling");
            Console.WriteLine("PASS: high CPU load restores automatic cooling");
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            session.Enable();
            device.FailRestore = true;
            MustReject(() => session.Restore("Close"));
            if (!session.NeedsRecovery || session.Mode != CoolingMode.RecoveryFailed)
                throw new Exception("Failed restoration must remain visible and armed");
            device.FailRestore = false;
            session.Restore("Retry");
            if (session.NeedsRecovery || device.QuietApplied) throw new Exception("Restoration retry failed");
            Console.WriteLine("PASS: failed restoration stays armed and supports retry");
            device = new FirmwareSimulator();
            RecoverySimulator recovery = new RecoverySimulator();
            session = new QuietSession(device, recovery, () => 0);
            session.Enable();
            recovery.WasRecovered = true;
            device.QuietApplied = false;
            session.Poll();
            if (session.NeedsRecovery || session.Mode != CoolingMode.Automatic)
                throw new Exception("An independent recovery must end quiet mode");
            Console.WriteLine("PASS: independent recovery ends the low-speed session");
            clock = 0;
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable();
            device.Sample = new FanSample(54, 47, 40, 8, 18, 18);
            clock = 1; session.Poll();
            clock = 5; session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Sustained CPU work must end low speed");
            Console.WriteLine("PASS: sustained CPU load ends low speed");
            clock = 0;
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable();
            device.Sample = new FanSample(54, 47, 10, 8, 0, 0);
            clock = 16; session.Poll();
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Unresponsive fans must end low speed");
            Console.WriteLine("PASS: stalled or overridden fans end low speed");

            // Adjustable speed.
            device = new FirmwareSimulator();
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            MustReject(() => session.Enable(17)); MustReject(() => session.Enable(51)); MustReject(() => session.Enable(0));
            if (device.QuietApplied || session.NeedsRecovery) throw new Exception("Out-of-range levels must never be written");
            Console.WriteLine("PASS: levels outside 1800-5000 RPM are refused before any write");

            device = new FirmwareSimulator(); device.Sample = new FanSample(78, 56, 38, 2, 28, 31);
            session = new QuietSession(device, new RecoverySimulator(), () => 0);
            MustReject(() => session.Enable(25));
            if (device.QuietApplied) throw new Exception("A hot machine must not get a low speed");
            Console.WriteLine("PASS: low speeds are refused while hot or busy");

            clock = 0; device = new FirmwareSimulator(); device.Sample = new FanSample(78, 56, 38, 2, 42, 42);
            recovery = new RecoverySimulator();
            session = new QuietSession(device, recovery, () => clock);
            session.Enable(42);
            if (device.AppliedLevel != 42 || recovery.Limit != 0 || session.TimeLimited) throw new Exception("Boost must apply without a time limit");
            clock = 400; session.Poll();
            if (session.Mode != CoolingMode.Quiet || device.AppliedLevel != 42) throw new Exception("Boost must survive heat and the five-minute mark");
            Console.WriteLine("PASS: boost works while hot and has no time limit");

            clock = 0; device = new FirmwareSimulator(); device.Sample = new FanSample(54, 47, 10, 8, 25, 25);
            recovery = new RecoverySimulator();
            session = new QuietSession(device, recovery, () => clock);
            session.Enable(25);
            if (recovery.Limit != 307 || !session.TimeLimited) throw new Exception("Low speed must arm the timed guard");
            device.Sample = new FanSample(61, 47, 10, 8, 25, 25); clock = 5; session.Poll();
            if (session.NeedsRecovery) throw new Exception("Heat must end a 2500 RPM low speed");
            Console.WriteLine("PASS: 2500 RPM keeps the low-speed protections");

            clock = 0; device = new FirmwareSimulator(); device.Sample = new FanSample(54, 47, 10, 8, 18, 18);
            recovery = new RecoverySimulator();
            session = new QuietSession(device, recovery, () => clock);
            session.Enable(18); clock = 100; device.Sample = new FanSample(54, 47, 10, 8, 50, 50);
            session.Enable(50);
            if (device.AppliedLevel != 50 || recovery.Limit != 0 || !session.NeedsRecovery) throw new Exception("Switching level must keep one session and drop the limit");
            clock = 500; session.Poll();
            if (session.Mode != CoolingMode.Quiet) throw new Exception("Switched boost must not inherit the old timer");
            Console.WriteLine("PASS: switching from quiet to 5000 RPM keeps one session");

            clock = 0; device = new FirmwareSimulator(); device.Sample = new FanSample(54, 47, 10, 8, 30, 30);
            session = new QuietSession(device, new RecoverySimulator(), () => clock);
            session.Enable(45); clock = 16; session.Poll();
            if (session.NeedsRecovery) throw new Exception("Fans far below a boost target must hand back to automatic");
            Console.WriteLine("PASS: fans that never reach a boost target end manual control");

            if (FanLevels.LowSpeedBlocker(new FanSample(54, 47, 10, 8, 18, 18)) != null || FanLevels.LowSpeedBlocker(new FanSample(78, 47, 10, 8, 18, 18)) == null)
                throw new Exception("The panel's low-speed check must match the session's rule");
            Console.WriteLine("PASS: the panel uses the same low-speed rule as the controller");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
