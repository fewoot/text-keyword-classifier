using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>
/// 生成程序图标 app.ico（多尺寸：16/24/32/48/64/128/256）。
///
/// 设计含义：
///   · 蓝色圆角底块   —— 应用本体（与界面主色调一致 #2B579A）
///   · 白色文档卡片   —— 被处理的文本
///   · 三行文字线     —— 正文；第一行用深蓝色，表示"命中的关键词"
///   · 右侧三个色块   —— 类别（红/绿/琥珀，与界面里类别列表的色块同色）
///   合起来就是：一段文字被归类到不同类别。
///
/// 小尺寸（16/24）会简化：去掉色块、加粗文字线，保证任务栏上也看得清。
///
/// 用法： IconMaker.exe &lt;输出路径&gt;
/// 为了让"零依赖"成立，这里手写 ICO 文件结构，不引用任何第三方库。
/// </summary>
internal static class IconMaker
{
    private static int Main(string[] args)
    {
        string outPath = args != null && args.Length > 0
            ? args[0]
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");

        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            // 小尺寸先按 8 倍画再缩小，避免细线断裂；大尺寸直接按目标尺寸画
            pngs[i] = RenderPng(sizes[i]);
        }

        using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);              // reserved
            w.Write((ushort)1);              // type = icon
            w.Write((ushort)sizes.Length);   // count

            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));   // 宽（0 表示 256）
                w.Write((byte)(s >= 256 ? 0 : s));   // 高
                w.Write((byte)0);                    // 调色板颜色数
                w.Write((byte)0);                    // 保留
                w.Write((ushort)1);                  // 颜色平面
                w.Write((ushort)32);                 // 位深
                w.Write((uint)pngs[i].Length);       // 数据长度
                w.Write((uint)offset);               // 数据偏移
                offset += pngs[i].Length;
            }
            for (int i = 0; i < pngs.Length; i++) w.Write(pngs[i]);
        }

        Console.WriteLine("图标已生成：" + outPath + "（" + sizes.Length + " 个尺寸）");
        return 0;
    }

    private static readonly Color TileTop = Color.FromArgb(255, 64, 108, 168);
    private static readonly Color TileBottom = Color.FromArgb(255, 23, 52, 98);
    private static readonly Color SheetColor = Color.FromArgb(255, 252, 253, 255);
    private static readonly Color LineDark = Color.FromArgb(255, 43, 87, 154);
    private static readonly Color LineLight = Color.FromArgb(255, 197, 210, 226);
    private static readonly Color[] ChipColors = new Color[]
    {
        Color.FromArgb(255, 224, 92, 122),   // 红（爱情类）
        Color.FromArgb(255, 58, 167, 109),   // 绿（颜色类）
        Color.FromArgb(255, 224, 163, 58)    // 琥珀（第三类）
    };

    private static byte[] RenderPng(int size)
    {
        const int Ss = 8;                       // 超采样倍数
        int hi = size * Ss;

        using (Bitmap big = new Bitmap(hi, hi, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(big))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                Draw(g, hi, size <= 24);
            }

            using (Bitmap small = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(big, new Rectangle(0, 0, size, size));
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    small.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }
    }

    /// <summary>在 size×size 的画布上绘制图标（size 为超采样后的像素）。</summary>
    private static void Draw(Graphics g, float size, bool simplified)
    {
        // ---------- 底块：圆角方形 + 渐变 ----------
        float pad = size * 0.045f;
        RectangleF tile = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
        using (GraphicsPath path = RoundedRect(tile, size * 0.225f))
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(tile, TileTop, TileBottom, 58f))
                g.FillPath(brush, path);

            // 顶部一层很淡的高光，避免整体发闷（简化尺寸时不画）
            if (!simplified)
            {
                RectangleF glow = new RectangleF(tile.X, tile.Y, tile.Width, tile.Height * 0.42f);
                using (LinearGradientBrush gb = new LinearGradientBrush(glow,
                    Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                using (GraphicsPath gp = RoundedRect(glow, size * 0.20f))
                    g.FillPath(gb, gp);
            }
        }

        // ---------- 白色文档卡片 ----------
        // 大尺寸：卡片偏左，右边留给类别色块；小尺寸：卡片居中、留出更明显的蓝边
        float sheetX = simplified ? size * 0.265f : size * 0.185f;
        float sheetY = simplified ? size * 0.225f : size * 0.185f;
        float sheetW = simplified ? size * 0.47f : size * 0.435f;
        float sheetH = simplified ? size * 0.55f : size * 0.63f;
        RectangleF sheet = new RectangleF(sheetX, sheetY, sheetW, sheetH);
        float sheetR = size * 0.055f;

        // 卡片阴影（往下偏一点点）
        using (GraphicsPath sp = RoundedRect(new RectangleF(sheet.X, sheet.Y + size * 0.012f,
                                                          sheet.Width, sheet.Height), sheetR))
        using (SolidBrush sb = new SolidBrush(Color.FromArgb(46, 8, 24, 48)))
            g.FillPath(sb, sp);

        using (GraphicsPath sp = RoundedRect(sheet, sheetR))
        using (SolidBrush sb = new SolidBrush(SheetColor))
            g.FillPath(sb, sp);

        // ---------- 三行文字线 ----------
        // 第一行深蓝（表示命中关键词），其余浅灰蓝
        float lineH = size * (simplified ? 0.070f : 0.056f);
        float lx = sheet.X + sheet.Width * (simplified ? 0.14f : 0.16f);
        float maxW = sheet.Width * (simplified ? 0.72f : 0.70f);
        float[] relY = simplified
            ? new float[] { 0.245f, 0.455f, 0.665f }
            : new float[] { 0.235f, 0.455f, 0.675f };
        float[] relW = new float[] { 0.86f, 1.00f, 0.64f };
        float[] lineCenters = new float[3];

        for (int i = 0; i < 3; i++)
        {
            float y = sheet.Y + sheet.Height * relY[i];
            RectangleF line = new RectangleF(lx, y, maxW * relW[i], lineH);
            lineCenters[i] = y + lineH / 2f;
            using (GraphicsPath lp = RoundedRect(line, lineH / 2f))
            using (SolidBrush lb = new SolidBrush(i == 0 ? LineDark : LineLight))
                g.FillPath(lb, lp);
        }

        // ---------- 右侧类别色块 ----------
        if (!simplified)
        {
            float chipSide = size * 0.088f;
            float chipX = size * 0.705f;
            for (int i = 0; i < 3; i++)
            {
                RectangleF chip = new RectangleF(chipX, lineCenters[i] - chipSide / 2f, chipSide, chipSide);
                using (GraphicsPath cp = RoundedRect(chip, chipSide * 0.30f))
                using (SolidBrush cb = new SolidBrush(ChipColors[i]))
                    g.FillPath(cb, cp);
            }
        }
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        if (d <= 0.01f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
