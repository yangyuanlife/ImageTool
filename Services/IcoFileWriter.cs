using System.IO;
using SixLabors.ImageSharp.PixelFormats;
using RgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace ImageTool.Services;

/// <summary>
/// 手写 32-bit（含 alpha）ICO 文件编码器。
/// ImageSharp 3.1.7 主包未提供 IcoEncoder，这里按 ICO 文件格式直接封装：
/// ICONDIR + ICONDIRENTRY[] + 每尺寸(BITMAPINFOHEADER + XOR 32bpp BGRA + AND 1bpp)。
/// 支持多尺寸、透明（XP 风格图标），Windows 完全兼容。
/// </summary>
public static class IcoFileWriter
{
    public static void SaveAsIco(string path, IEnumerable<RgbaImage> frames)
    {
        var list = frames.ToList();
        if (list.Count == 0) return;

        var imageBytes = list.Select(EncodeIconImage).ToList();

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        // ---- ICONDIR ----
        bw.Write((ushort)0);                 // reserved
        bw.Write((ushort)1);                 // resource type = icon
        bw.Write((ushort)list.Count);

        // ---- ICONDIRENTRY（每条 16 字节）之前先累计偏移 ----
        int offset = 6 + 16 * list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            int w = list[i].Width, h = list[i].Height;
            bw.Write((byte)(w >= 256 ? 0 : w));   // 256 用 0 表示
            bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0);                     // 颜色数（>256 时为 0）
            bw.Write((byte)0);                     // reserved
            bw.Write((ushort)1);                   // color planes
            bw.Write((ushort)32);                  // bits per pixel
            bw.Write((uint)imageBytes[i].Length);  // 本条目字节数
            bw.Write((uint)offset);                // 本条目偏移
            offset += imageBytes[i].Length;
        }

        // ---- 各尺寸图像数据 ----
        foreach (var data in imageBytes)
            bw.Write(data);
    }

    /// <summary>将一个尺寸的图像编码为单条 ICO 图像：BITMAPINFOHEADER + XOR + AND。</summary>
    private static byte[] EncodeIconImage(RgbaImage img)
    {
        int w = img.Width, h = img.Height;
        int stride = w * 4;

        // XOR：32bpp BGRA，自下而上（DIB 行序）
        byte[] xor = new byte[h * stride];
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                int dstRow = h - 1 - y;
                for (int x = 0; x < w; x++)
                {
                    var px = row[x];
                    int idx = dstRow * stride + x * 4;
                    xor[idx]     = px.B;
                    xor[idx + 1] = px.G;
                    xor[idx + 2] = px.R;
                    xor[idx + 3] = px.A;
                }
            }
        });

        // AND 掩码：1 bit/像素，每行 4 字节对齐，全 0（透明由 alpha 通道表达）
        int andStride = ((w + 31) / 32) * 4;
        byte[] and = new byte[h * andStride];

        // BITMAPINFOHEADER
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((uint)40);                          // biSize
        bw.Write((int)w);
        bw.Write((int)(h * 2));                       // biHeight（含 XOR+AND）
        bw.Write((ushort)1);                          // biPlanes
        bw.Write((ushort)32);                         // biBitCount
        bw.Write((uint)0);                            // biCompression = BI_RGB
        bw.Write((uint)(xor.Length + and.Length));    // biSizeImage
        bw.Write((int)0);                             // biXPelsPerMeter
        bw.Write((int)0);                             // biYPelsPerMeter
        bw.Write((uint)0);                            // biClrUsed
        bw.Write((uint)0);                            // biClrImportant
        bw.Write(xor);
        bw.Write(and);
        return ms.ToArray();
    }
}
