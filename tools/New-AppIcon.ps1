$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    $iconWindowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $iconWorker = Start-Process -FilePath $iconWindowsPowerShell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($iconWorker.ExitCode -ne 0) { throw '圖示產生失敗。' }
    return
}
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class VictusFanArtwork {
    static readonly Color Teal = Color.FromArgb(45,212,191), Amber = Color.FromArgb(251,191,36), White = Color.FromArgb(246,248,251);
    static Color Mix(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
    // Victus Control Center: rounded tile with a gauge ring fading from teal (fan) to amber (brightness).
    static void Render(Graphics g) {
        RectangleF r = new RectangleF(12, 12, 232, 232); float d = 116;
        using (GraphicsPath tile = new GraphicsPath()) {
            tile.AddArc(r.X, r.Y, d, d, 180, 90); tile.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            tile.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); tile.AddArc(r.X, r.Bottom - d, d, d, 90, 90); tile.CloseFigure();
            using (Brush fill = new LinearGradientBrush(new RectangleF(0, 0, 256, 256), Color.FromArgb(30,43,61), Color.FromArgb(10,15,23), 60f)) g.FillPath(fill, tile);
            using (Pen rim = new Pen(Color.FromArgb(60,255,255,255), 3)) g.DrawPath(rim, tile);
        }
        using (Pen track = new Pen(Color.FromArgb(48,62,82), 22) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(track, 58, 58, 140, 140, 135, 270);
        int steps = 90;
        for (int i = 0; i < steps; i++) {
            using (Pen pen = new Pen(Mix(Teal, Amber, i / (float)(steps - 1)), 22) { StartCap = i == 0 ? LineCap.Round : LineCap.Flat, EndCap = i == steps - 1 ? LineCap.Round : LineCap.Flat })
                g.DrawArc(pen, 58, 58, 140, 140, 135 + 205f * i / steps, 205f / steps + 0.8f);
        }
        double a = 340 * Math.PI / 180; float kx = (float)(128 + Math.Cos(a) * 70), ky = (float)(128 + Math.Sin(a) * 70);
        using (Brush knob = new SolidBrush(White)) { g.FillEllipse(knob, kx - 19, ky - 19, 38, 38); g.FillEllipse(knob, 110, 110, 36, 36); }
    }
    // Drawn at 4x and downscaled so 16-48 px frames stay crisp.
    public static Bitmap Draw(int size) {
        int big = Math.Max(size * 4, 256);
        using (Bitmap hi = new Bitmap(big, big, PixelFormat.Format32bppArgb)) {
            using (Graphics g = Graphics.FromImage(hi)) { g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(big / 256f, big / 256f); Render(g); }
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(hi, 0, 0, size, size); }
            return bitmap;
        }
    }
    public static void Save(string iconPath,string previewPath) {
        int[] sizes={16,24,32,48,64,128,256};byte[][] frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(Bitmap bitmap=Draw(sizes[i]))using(MemoryStream stream=new MemoryStream()) {
            if(sizes[i]==256) { bitmap.Save(stream,ImageFormat.Png);frames[i]=stream.ToArray(); }
            else {
                // Preserve true-color alpha and a DWORD-padded transparency mask.
                // Handle-based Icon.Save converted these frames to indexed color and
                // rendered opaque corners at 16, 24 and 48 pixels on Windows.
                int size=sizes[i], maskStride=((size+31)/32)*4;
                using(BinaryWriter frame=new BinaryWriter(stream)) {
                    frame.Write((uint)40);frame.Write(size);frame.Write(size*2);
                    frame.Write((ushort)1);frame.Write((ushort)32);
                    frame.Write((uint)0);frame.Write((uint)(size*size*4));
                    frame.Write(0);frame.Write(0);frame.Write((uint)0);frame.Write((uint)0);
                    for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++) {
                        Color pixel=bitmap.GetPixel(x,y);
                        frame.Write((byte)(pixel.B*pixel.A/255));
                        frame.Write((byte)(pixel.G*pixel.A/255));
                        frame.Write((byte)(pixel.R*pixel.A/255));
                        frame.Write(pixel.A);
                    }
                    for(int y=size-1;y>=0;y--) {
                        byte[] mask=new byte[maskStride];
                        for(int x=0;x<size;x++)if(bitmap.GetPixel(x,y).A==0)mask[x/8]|=(byte)(0x80>>(x%8));
                        frame.Write(mask);
                    }
                    frame.Flush();frames[i]=stream.ToArray();
                }
            }
        }
        using(BinaryWriter writer=new BinaryWriter(File.Create(iconPath))) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
            uint offset=(uint)(6+16*sizes.Length);
            for(int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length);writer.Write(offset);offset+=(uint)frames[i].Length;
            }
            foreach(byte[] frame in frames)writer.Write(frame);
        }
        using(Bitmap preview=Draw(256))preview.Save(previewPath,ImageFormat.Png);
    }
}
'@ -ReferencedAssemblies System.Drawing
$fanAssetDirectory = Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'assets'
[VictusFanArtwork]::Save((Join-Path $fanAssetDirectory 'app.ico'), (Join-Path $fanAssetDirectory 'icon.png'))
Write-Output '已建立多尺寸控制中心圖示。'
