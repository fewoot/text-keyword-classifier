using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// 图标预览图：把 ICO 里的各尺寸帧排成一张对照图（浅色 / 深色两种背景），
/// 小尺寸按 4 倍最近邻放大，便于肉眼判断在任务栏、文件列表里是否清晰。
/// 用法： IconPreview.exe &lt;app.ico&gt; &lt;输出png&gt;
/// </summary>
internal static class IconPreview
{
    private static int Main(string[] args)
    {
        string ico = args[0];
        string outPng = args[1];

        List<Frame> frames = ReadFrames(ico);
        if (frames.Count == 0)
        {
            Console.WriteLine("没有读到任何图标帧");
            return 1;
        }
        frames.Sort(delegate (Frame a, Frame b) { return a.Size.CompareTo(b.Size); });

        // 计算画布：每帧显示在固定高度的格子里（小尺寸放大、大尺寸缩小），便于公平比较
        const int cellH = 168;
        int gap = 22;
        List<int> widths = new List<int>();
        List<int> scales = new List<int>();
        foreach (Frame f in frames)
        {
            int scale = Math.Max(1, (int)Math.Round((double)cellH / f.Size));
            scales.Add(scale);
            widths.Add(f.Size * scale);
        }
        int totalW = gap;
        foreach (int w in widths) totalW += w + gap;
        int rowH = cellH;
        int totalH = gap + 26 + rowH + 20 + gap;

        using (Bitmap canvas = new Bitmap(totalW, totalH * 2, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // 上排：浅色背景（文件资源管理器浅色主题）
            g.Clear(Color.FromArgb(255, 246, 248, 251));
            DrawRow(g, frames, widths, scales, rowH, gap, 0, "浅色背景");

            // 下排：深色背景（深色主题 / 深色任务栏）
            g.FillRectangle(new SolidBrush(Color.FromArgb(255, 32, 34, 38)), 0, totalH, totalW, totalH);
            DrawRow(g, frames, widths, scales, rowH, gap, totalH, "深色背景");

            canvas.Save(outPng, ImageFormat.Png);
        }

        Console.WriteLine("预览图已生成：" + outPng);
        return 0;
    }

    private static void DrawRow(Graphics g, List<Frame> frames, List<int> widths, List<int> scales,
                                int rowH, int gap, int offsetY, string label)
    {
        bool dark = label.IndexOf("深") >= 0;
        Color fg = dark ? Color.FromArgb(230, 230, 235) : Color.FromArgb(70, 78, 90);
        using (Font font = new Font("Segoe UI", 9F))
        using (SolidBrush fb = new SolidBrush(fg))
        {
            g.DrawString(label, font, fb, gap, offsetY + 4);
            int x = gap;
            int y = offsetY + 26;
            for (int i = 0; i < frames.Count; i++)
            {
                Frame f = frames[i];
                int scale = scales[i];
                int w = f.Size * scale;
                int h = f.Size * scale;
                using (Bitmap bmp = LoadPng(f.Png))
                {
                    int dx = x + (widths[i] - w) / 2;
                    int dy = y + (rowH - h) / 2;
                    // 放大用最近邻（看清像素），缩小用高质量插值
                    g.InterpolationMode = w >= f.Size ? InterpolationMode.NearestNeighbor
                                                      : InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, new Rectangle(dx, dy, w, h));
                }
                string cap = f.Size + "px";
                SizeF cs = g.MeasureString(cap, font);
                g.DrawString(cap, font, fb, x + (widths[i] - cs.Width) / 2f, y + rowH + 2);
                x += widths[i] + gap;
            }
        }
    }

    private sealed class Frame
    {
        public int Size;
        public byte[] Png;
    }

    private static List<Frame> ReadFrames(string ico)
    {
        List<Frame> list = new List<Frame>();
        byte[] all = File.ReadAllBytes(ico);
        int count = BitConverter.ToUInt16(all, 4);
        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            int w = all[e] == 0 ? 256 : all[e];
            int len = (int)BitConverter.ToUInt32(all, e + 8);
            int off = (int)BitConverter.ToUInt32(all, e + 12);
            byte[] png = new byte[len];
            Array.Copy(all, off, png, 0, len);
            Frame f = new Frame();
            f.Size = w;
            f.Png = png;
            list.Add(f);
        }
        return list;
    }

    private static Bitmap LoadPng(byte[] png)
    {
        using (MemoryStream ms = new MemoryStream(png))
        {
            using (Image img = Image.FromStream(ms))
                return new Bitmap(img);
        }
    }
}
