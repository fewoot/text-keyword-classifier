using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// 布局回归测试：真实构造主窗体，在多个窗口尺寸下检查
///   · 同级控件是否互相重叠（真正的"挤在一起"）
///   · 关键控件是否被压扁到不可用（宽度/高度小于最小可用值）
///
/// 之前出过的事故：SplitContainer 的 SplitterDistance 被钳制，左侧面板只剩 121 像素宽，
/// 所有控件叠在一起。这个测试就是为了防止那种问题再次出现。
///
/// 用法： LayoutTest.exe &lt;exe路径&gt;
/// </summary>
internal static class LayoutTest
{
    private static int _pass;
    private static int _fail;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        string exe = args != null && args.Length > 0 ? args[0] : "KeywordClassifier.exe";
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Console.WriteLine("=== 布局回归测试 ===");
        Console.WriteLine("目标：" + exe);
        Console.WriteLine();

        Assembly asm = Assembly.UnsafeLoadFrom(exe);
        Type mainForm = asm.GetType("WordClassify.MainForm");
        if (mainForm == null)
        {
            Console.WriteLine("[失败] 找不到 WordClassify.MainForm");
            return 1;
        }

        // 各种窗口尺寸：最小（你屏幕上的实际可用尺寸）、默认、大屏、以及一个很窄的尺寸
        CheckSize(mainForm, 1180, 860, "最小尺寸");
        CheckSize(mainForm, 1400, 900, "默认尺寸");
        CheckSize(mainForm, 1600, 1000, "常用尺寸");
        CheckSize(mainForm, 1920, 1080, "大屏尺寸");
        CheckSize(mainForm, 1103, 590, "比最小还小的尺寸（应被抬到最小）");

        Console.WriteLine();
        Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
        return _fail == 0 ? 0 : 1;
    }

    private static void CheckSize(Type mainForm, int width, int height, string label)
    {
        Console.WriteLine("--- " + label + " " + width + "x" + height + " ---");
        Form form = (Form)Activator.CreateInstance(mainForm);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);      // 放到屏幕外，避免打扰
        form.ClientSize = new Size(width, height);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        Application.DoEvents();

        int overlaps = 0;
        List<string> details = new List<string>();
        foreach (Control parent in AllControls(form))
        {
            if (parent.Controls.Count < 2) continue;
            if (parent is FlowLayoutPanel) continue;          // 流式布局自动排列
            if (parent is DataGridView || parent is TreeView) continue;
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
                    if (details.Count < 6)
                        details.Add("    " + Describe(parent) + " [" + parent.Bounds + "] 里 "
                            + Describe(a) + " [" + a.Bounds + "] 与 "
                            + Describe(b) + " [" + b.Bounds + "] 重叠");
                }
            }
        }

        foreach (string d in details) Console.WriteLine(d);
        Check(label + " · 没有控件重叠", overlaps == 0, overlaps + " 处重叠");

        // 关键控件可用性
        Control editor = FindByTypeName(form, "CategoryEditor");
        Check(label + " · 类别编辑器存在且够宽（>=480）",
            editor != null && editor.Width >= 480,
            editor == null ? "未找到" : editor.Width + "px");

        ListBox catList = FindListBox(editor, 0);
        Check(label + " · 类别列表够宽够高（>=180x96，约 4 行）",
            catList != null && catList.Width >= 180 && catList.Height >= 96,
            catList == null ? "未找到" : catList.Width + "x" + catList.Height);

        ListBox kwList = FindListBox(editor, 1);
        Check(label + " · 关键词列表够用（>=110x72，约 3 行）", kwList != null && kwList.Width >= 110 && kwList.Height >= 72,
            kwList == null ? "未找到" : kwList.Width + "x" + kwList.Height);

        TextBox source = FindPathBox(form, 0);
        Check(label + " · 源文件夹输入框够宽（>=200）", source != null && source.Width >= 200,
            source == null ? "未找到" : source.Width + "px");

        int smallButtons = 0;
        int stretched = 0;
        foreach (Control c in AllControls(form))
        {
            Button b = c as Button;
            if (b == null || !b.Visible) continue;
            if (b.Width < 24 || b.Height < 20) smallButtons++;
            if (b.Height > 40) stretched++;              // 被纵向拉伸变形
        }
        Check(label + " · 没有小到看不见的按钮", smallButtons == 0, smallButtons + " 个过小按钮");
        Check(label + " · 没有被拉伸变形的按钮", stretched == 0, stretched + " 个变形按钮");

        int invisibleText = 0;
        foreach (Control c in AllControls(form))
        {
            Label l = c as Label;
            if (l == null || !l.Visible || string.IsNullOrEmpty(l.Text)) continue;
            if (l.AutoSize) continue;
            // 文字比控件宽太多会被截断（允许 40% 余量，因为中文字宽估算不精确）
            Size need = TextRenderer.MeasureText(l.Text, l.Font);
            if (need.Width > l.Width * 1.6 && l.Width > 0) invisibleText++;
        }
        Check(label + " · 没有明显被截断的标签", invisibleText == 0, invisibleText + " 个标签文字超宽");

        form.Close();
        form.Dispose();
        Application.DoEvents();
        Console.WriteLine();
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
            ListBox found = FindListBox(c, index, ref seen);
            if (found != null) return found;
        }
        return null;
    }

    private static TextBox FindPathBox(Control root, int index)
    {
        int seen = 0;
        return FindPathBox(root, index, ref seen);
    }

    private static TextBox FindPathBox(Control root, int index, ref int seen)
    {
        foreach (Control c in root.Controls)
        {
            TextBox tb = c as TextBox;
            if (tb != null && tb.ReadOnly && !tb.Multiline)
            {
                if (seen == index) return tb;
                seen++;
            }
            TextBox found = FindPathBox(c, index, ref seen);
            if (found != null) return found;
        }
        return null;
    }

    private static Control FindByTypeName(Control root, string typeName)
    {
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == typeName) return c;
            Control found = FindByTypeName(c, typeName);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        yield return root;
        foreach (Control c in root.Controls)
            foreach (Control d in AllControls(c))
                yield return d;
    }

    private static string Describe(Control c)
    {
        string text = c.Text == null ? "" : c.Text.Replace("\r", " ").Replace("\n", " ");
        if (text.Length > 18) text = text.Substring(0, 18) + "…";
        return c.GetType().Name + "(\"" + text + "\")";
    }

    private static void Check(string title, bool ok, string actual)
    {
        if (ok) { _pass++; Console.WriteLine("  [通过] " + title); }
        else { _fail++; Console.WriteLine("  [失败] " + title + "   → 实际：" + actual); }
    }
}
