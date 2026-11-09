using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// DPI 缩放验证：在 100% / 125% / 150% 三种缩放下构造主窗体，
/// 渲染成 PNG 并检查是否有控件被挤扁、文字被截断。
///
/// 背景：用户显示器缩放 150% 时，字体变大而写死的像素列宽没变，
/// 导致标签被截断、按钮文字被切掉。这个工具用来验证修复效果。
///
/// 用法： DpiTest.exe &lt;exe路径&gt; &lt;输出目录&gt;
/// </summary>
internal static class DpiTest
{
    private static int _pass;
    private static int _fail;
    private static Type _uiType;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        string exe = Path.GetFullPath(args[0]);
        string outDir = Path.GetFullPath(args.Length > 1 ? args[1] : "dpi-shots");
        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Assembly asm = Assembly.UnsafeLoadFrom(exe);
        Type mainForm = asm.GetType("WordClassify.MainForm");
        _uiType = asm.GetType("WordClassify.Ui");
        if (mainForm == null || _uiType == null)
        {
            Console.WriteLine("[失败] 找不到 MainForm 或 Ui 类型");
            return 1;
        }

        Console.WriteLine("=== DPI 缩放验证 ===");
        Console.WriteLine("目标：" + exe);
        Console.WriteLine();

        CheckScale(asm, mainForm, 1.0f, outDir);
        CheckScale(asm, mainForm, 1.25f, outDir);
        CheckScale(asm, mainForm, 1.5f, outDir);

        Console.WriteLine();
        Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
        return _fail == 0 ? 0 : 1;
    }

    private static void CheckScale(Assembly asm, Type mainForm, float scale, string outDir)
    {
        Console.WriteLine("--- 缩放 " + (int)(scale * 100) + "% ---");

        // 通过环境变量 + 重新初始化来指定缩放比例（不依赖反射改私有字段）
        try
        {
            Environment.SetEnvironmentVariable("WCT_DPI_SCALE",
                scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _uiType.GetMethod("Initialize", new Type[] { typeof(bool) })
                .Invoke(null, new object[] { true });
            object actual = _uiType.GetProperty("Scale", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            Console.WriteLine("  已设置缩放比例 = " + actual);
        }
        catch (Exception ex)
        {
            Exception inner = ex.InnerException != null ? ex.InnerException : ex;
            Console.WriteLine("  [失败] 无法设置缩放：" + inner.GetType().FullName + "：" + inner.Message);
            _fail++;
            return;
        }

        Form form = (Form)Activator.CreateInstance(mainForm);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-6000, -6000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        Application.DoEvents();

        Console.WriteLine("  窗体实际尺寸：" + form.Width + " x " + form.Height
            + "（客户区 " + form.ClientSize.Width + " x " + form.ClientSize.Height + "）");

        // 1) 文字是否放得下
        int clipped = 0;
        List<string> details = new List<string>();
        foreach (Control c in All(form))
        {
            if (!c.Visible) continue;
            string text = c.Text;
            if (string.IsNullOrEmpty(text)) continue;
            Size need = TextRenderer.MeasureText(text, c.Font);
            bool tooNarrow = false;
            if (c is Button) tooNarrow = need.Width > c.Width - 2;
            else if (c is Label && !((Label)c).AutoSize) tooNarrow = need.Width > c.Width + 2;
            else if (c is CheckBox) tooNarrow = need.Width > c.Width - 2;
            if (tooNarrow)
            {
                clipped++;
                if (details.Count < 5)
                    details.Add("    \"" + Clip(text) + "\" 需要 " + need.Width + "px，实际 " + c.Width + "px（"
                        + c.GetType().Name + "）");
            }
        }
        foreach (string d in details) Console.WriteLine(d);
        Check("缩放 " + (int)(scale * 100) + "% · 没有被截断的文字", clipped == 0, clipped + " 处被截断");

        // 2) 关键控件是否被挤扁
        Control editor = FindByName(form, "CategoryEditor");
        ListBox catList = FindListBox(editor, 0);
        ListBox kwList = FindListBox(editor, 1);
        int minCatH = UiInt("S", 96);
        int minKwH = UiInt("S", 72);

        Check("缩放 " + (int)(scale * 100) + "% · 类别列表高度 >= " + minCatH,
            catList != null && catList.Height >= minCatH, catList == null ? "未找到" : catList.Height + "px");
        Check("缩放 " + (int)(scale * 100) + "% · 关键词列表高度 >= " + minKwH,
            kwList != null && kwList.Height >= minKwH, kwList == null ? "未找到" : kwList.Height + "px");

        int stretched = 0;
        foreach (Control c in All(form))
        {
            Button b = c as Button;
            if (b == null || !b.Visible) continue;
            if (b.Height > UiInt("S", 40)) stretched++;
        }
        Check("缩放 " + (int)(scale * 100) + "% · 按钮没有被拉伸变形", stretched == 0, stretched + " 个变形");

        // 3) 重叠检查
        int overlaps = 0;
        foreach (Control parent in All(form))
        {
            if (parent.Controls.Count < 2) continue;
            if (parent is FlowLayoutPanel) continue;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                for (int j = i + 1; j < parent.Controls.Count; j++)
                {
                    Control a = parent.Controls[i];
                    Control b = parent.Controls[j];
                    if (!a.Visible || !b.Visible) continue;
                    if (!a.Bounds.IntersectsWith(b.Bounds)) continue;
                    if (a.Bounds.Contains(b.Bounds) || b.Bounds.Contains(a.Bounds)) continue;
                    overlaps++;
                }
            }
        }
        Check("缩放 " + (int)(scale * 100) + "% · 没有控件重叠", overlaps == 0, overlaps + " 处重叠");

        // 4) 存一张图，方便肉眼看
        try
        {
            form.Refresh();
            Application.DoEvents();
            string png = Path.Combine(outDir, "dpi-" + (int)(scale * 100) + ".png");
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(png, ImageFormat.Png);
            }
            Console.WriteLine("  截图：" + png);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  截图失败：" + ex.Message);
        }

        form.Close();
        form.Dispose();
        Application.DoEvents();
        Console.WriteLine();
    }

    private static int UiInt(string method, int design)
    {
        try
        {
            object v = _uiType.GetMethod(method, BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { design });
            return Convert.ToInt32(v);
        }
        catch (Exception) { return design; }
    }

    private static string Clip(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 20 ? s.Substring(0, 20) + "…" : s;
    }

    private static Control FindByName(Control root, string typeName)
    {
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == typeName) return c;
            Control f = FindByName(c, typeName);
            if (f != null) return f;
        }
        return null;
    }

    private static ListBox FindListBox(Control root, int index)
    {
        if (root == null) return null;
        int seen = 0;
        return FindListBox(root, index, ref seen);
    }

    private static ListBox FindListBox(Control root, int index, ref int seen)
    {
        foreach (Control c in root.Controls)
        {
            ListBox lb = c as ListBox;
            if (lb != null)
            {
                if (seen == index) return lb;
                seen++;
            }
            ListBox f = FindListBox(c, index, ref seen);
            if (f != null) return f;
        }
        return null;
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control d in All(c)) yield return d;
        }
    }

    private static void Check(string title, bool ok, string actual)
    {
        if (ok) { _pass++; Console.WriteLine("  [通过] " + title); }
        else { _fail++; Console.WriteLine("  [失败] " + title + "   → 实际：" + actual); }
    }
}
