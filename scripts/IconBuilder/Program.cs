using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconBuilder;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string targetIco = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "assets", "app.ico");
        Console.WriteLine($"Icon target path: {targetIco}");

        // Standard Windows icon dimensions
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var frames = new List<(int width, int height, byte[] data)>();

        foreach (var size in sizes)
        {
            var rtb = RenderVector(size, size);

            // For sizes <= 128 use DIB (BMP format without BITMAPFILEHEADER), for 256 use PNG
            byte[] frameBytes;
            if (size >= 256)
            {
                frameBytes = EncodePng(rtb);
            }
            else
            {
                frameBytes = EncodeDib(rtb, size, size);
            }

            frames.Add((size, size, frameBytes));
            Console.WriteLine($"Rendered frame {size}x{size}: {frameBytes.Length} bytes");
        }

        byte[] icoBytes = BuildIco(frames);
        Directory.CreateDirectory(Path.GetDirectoryName(targetIco)!);
        File.WriteAllBytes(targetIco, icoBytes);
        Console.WriteLine($"Successfully saved {targetIco} ({icoBytes.Length} bytes, {frames.Count} frames)");

        // Also save a PNG preview for inspection
        string previewPng = Path.Combine(Path.GetDirectoryName(targetIco)!, "icon_preview_256.png");
        var previewRtb = RenderVector(256, 256);
        File.WriteAllBytes(previewPng, EncodePng(previewRtb));
        Console.WriteLine($"Preview saved: {previewPng}");
    }

    private static RenderTargetBitmap RenderVector(int width, int height)
    {
        var geom = Geometry.Parse("M3 5.75A.75.75 0 0 1 3.75 5c2.663 0 5.258-.943 7.8-2.85a.75.75 0 0 1 .9 0C14.992 4.057 17.587 5 20.25 5a.75.75 0 0 1 .75.75V11q0 .507-.04.996a6.5 6.5 0 0 0-1.465-.684q.005-.155.005-.312V6.478c-2.577-.152-5.08-1.09-7.5-2.8c-2.42 1.71-4.923 2.648-7.5 2.8V11c0 4.149 2.332 7.221 7.125 9.285a6.5 6.5 0 0 0 1.005 1.52l-.355.143a.75.75 0 0 1-.55 0C5.958 19.676 3 16 3 11zM23 17.5a5.5 5.5 0 1 1-11 0a5.5 5.5 0 0 1 11 0m-2.146-2.354a.5.5 0 0 0-.708 0L16.5 18.793l-1.646-1.647a.5.5 0 0 0-.708.708l2 2a.5.5 0 0 0 .708 0l4-4a.5.5 0 0 0 0-.708");

        var brush = new LinearGradientBrush(
            Color.FromRgb(0x00, 0xD2, 0xD3),
            Color.FromRgb(0x00, 0x78, 0xD4),
            new Point(0, 0),
            new Point(1, 1));

        var bounds = geom.Bounds;
        double pad = width * 0.05;
        double availW = width - pad * 2;
        double availH = height - pad * 2;
        double scale = Math.Min(availW / bounds.Width, availH / bounds.Height);

        double offsetX = pad + (availW - bounds.Width * scale) / 2 - bounds.Left * scale;
        double offsetY = pad + (availH - bounds.Height * scale) / 2 - bounds.Top * scale;

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(new TranslateTransform(offsetX, offsetY));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(group);
            dc.DrawGeometry(brush, null, geom);
            dc.Pop();
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }

    private static byte[] EncodePng(RenderTargetBitmap rtb)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static byte[] EncodeDib(RenderTargetBitmap rtb, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        rtb.CopyPixels(pixels, stride, 0);

        int andRowStride = ((width + 31) / 32) * 4;
        int andMaskSize = andRowStride * height;
        byte[] dib = new byte[40 + pixels.Length + andMaskSize];

        using var ms = new MemoryStream(dib);
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER
        bw.Write(40);            // biSize
        bw.Write(width);         // biWidth
        bw.Write(height * 2);    // biHeight (doubled for XOR + AND)
        bw.Write((short)1);      // biPlanes
        bw.Write((short)32);     // biBitCount
        bw.Write(0);             // biCompression (BI_RGB)
        bw.Write(pixels.Length); // biSizeImage
        bw.Write(0);             // biXPelsPerMeter
        bw.Write(0);             // biYPelsPerMeter
        bw.Write(0);             // biClrUsed
        bw.Write(0);             // biClrImportant

        // Pixels (bottom-up BGRA)
        for (int y = height - 1; y >= 0; y--)
        {
            bw.Write(pixels, y * stride, stride);
        }

        // AND mask (all 0s)
        byte[] andMask = new byte[andMaskSize];
        bw.Write(andMask);

        return dib;
    }

    private static byte[] BuildIco(List<(int width, int height, byte[] data)> frames)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICONDIR
        bw.Write((short)0); // idReserved
        bw.Write((short)1); // idType (1 = icon)
        bw.Write((short)frames.Count); // idCount

        int offset = 6 + frames.Count * 16;

        foreach (var frame in frames)
        {
            bw.Write((byte)(frame.width >= 256 ? 0 : frame.width));   // bWidth
            bw.Write((byte)(frame.height >= 256 ? 0 : frame.height)); // bHeight
            bw.Write((byte)0);                                         // bColorCount
            bw.Write((byte)0);                                         // bReserved
            bw.Write((short)1);                                        // wPlanes
            bw.Write((short)32);                                       // wBitCount
            bw.Write(frame.data.Length);                               // dwBytesInRes
            bw.Write(offset);                                          // dwImageOffset

            offset += frame.data.Length;
        }

        foreach (var frame in frames)
        {
            bw.Write(frame.data);
        }

        return ms.ToArray();
    }
}
