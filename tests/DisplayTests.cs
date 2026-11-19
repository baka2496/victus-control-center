using System;
using System.Collections.Generic;
using System.Threading;

namespace VictusQuiet
{
    // Pure tests always run. With --hardware, each screen is dimmed by 10 points, read back, then restored.
    static class DisplayTests
    {
        static int failures;
        static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS: " : "FAIL: ") + name); if (!ok) failures++; }

        sealed class Fake : DisplayTarget
        {
            public Fake(int min, int max, int current) { Minimum = min; Maximum = max; Current = current; Name = "fake"; }
            public int Raw(int percent) { return RawFromPercent(percent); }
            public override void SetPercent(int percent) { Current = RawFromPercent(percent); }
        }

        static int Main(string[] args)
        {
            Check(DisplayBrightness.NormaliseInstance(@"DISPLAY\AUS24A1\5&1a2b&0&UID4352_0") == DisplayBrightness.NormaliseInstance(@"\\?\DISPLAY#AUS24A1#5&1a2b&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"), "WMI and device names match the same monitor");
            Check(DisplayBrightness.NormaliseInstance(@"DISPLAY\BOE09F1\4&24b11f13&0&UID8388688_0") != DisplayBrightness.NormaliseInstance(@"DISPLAY\AUS24A1\5&1a2b&0&UID4352_0"), "different monitors stay different");
            Check(DisplayBrightness.NormaliseInstance(null) == "", "missing name is handled");
            Fake fake = new Fake(10, 60, 35);
            Check(fake.Percent == 50, "percent uses the monitor's own range");
            Check(fake.Raw(0) == 10 && fake.Raw(100) == 60 && fake.Raw(150) == 60 && fake.Raw(-5) == 10, "percent is clamped to the monitor range");
            Check(new Fake(0, 0, 0).Percent == 0, "empty range does not divide by zero");

            if (args.Length > 0 && args[0] == "--hardware")
            {
                List<DisplayTarget> displays = DisplayBrightness.Discover();
                Console.WriteLine("Found " + displays.Count + " screen(s)");
                foreach (DisplayTarget display in displays)
                {
                    int original = display.Percent, target = original >= 20 ? original - 10 : original + 10;
                    Console.WriteLine("  " + display.Name + " (" + display.Kind + ") now " + original + "%, raw " + display.Current + " in " + display.Minimum + "-" + display.Maximum);
                    try
                    {
                        display.SetPercent(target); Thread.Sleep(700);
                        int seen = ReadBack(display);
                        Check(Math.Abs(seen - target) <= 1, display.Name + " changed to " + target + "% (read back " + seen + "%)");
                    }
                    catch (Exception error) { Check(false, display.Name + " change failed: " + error.Message); }
                    finally
                    {
                        try { display.SetPercent(original); Thread.Sleep(700); int back = ReadBack(display); Check(Math.Abs(back - original) <= 1, display.Name + " restored to " + original + "% (read back " + back + "%)"); }
                        catch (Exception error) { Check(false, display.Name + " restore failed: " + error.Message); }
                    }
                }
                foreach (DisplayTarget display in displays) display.Dispose();
            }
            Console.WriteLine(failures == 0 ? "All display tests passed." : failures + " display test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        // Independent read through a fresh discovery, so the value comes from the screen, not our cache.
        static int ReadBack(DisplayTarget display)
        {
            List<DisplayTarget> fresh = DisplayBrightness.Discover();
            int result = -1;
            foreach (DisplayTarget item in fresh) { if (item.Name == display.Name && item.Internal == display.Internal) result = item.Percent; item.Dispose(); }
            return result;
        }
    }
}
