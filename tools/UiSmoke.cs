using System;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
/// 界面冒烟测试：用反射加载 关键词分类工具.exe，构造主窗体但不显示，
/// 检查构造函数不会抛异常、控件树完整。
/// 用法： UiSmoke.exe &lt;exe路径&gt;
/// </summary>
internal static class UiSmoke
{
    private static int _pass;
    private static int _fail;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); }
        catch (Exception) { }

        string exe = args != null && args.Length > 0 ? args[0] : "关键词分类工具.exe";
        Console.WriteLine("=== 界面冒烟测试 ===");
        Console.WriteLine("目标：" + exe);
        Console.WriteLine();

        Application.EnableVisualStyles();

        Assembly asm = Assembly.UnsafeLoadFrom(exe);
        Check("程序集加载成功", asm != null, "null");

        Type mainForm = asm.GetType("WordClassify.MainForm");
        Check("找到 WordClassify.MainForm 类型", mainForm != null, "未找到");
        if (mainForm == null) return Finish();

        Type keywordMatcher = asm.GetType("WordClassify.KeywordMatcher");
        Check("找到 WordClassify.KeywordMatcher 类型", keywordMatcher != null, "未找到");

        Form form = null;
        try
        {
            form = (Form)Activator.CreateInstance(mainForm);
            Check("主窗体构造成功（不抛异常）", form != null, "null");
        }
        catch (Exception ex)
        {
            Exception inner = ex.InnerException != null ? ex.InnerException : ex;
            Check("主窗体构造成功（不抛异常）", false, inner.GetType().Name + "：" + inner.Message);
            return Finish();
        }

        Check("窗体标题正确", form.Text == "文本关键词分类工具", form.Text);
        Check("窗体尺寸合理（宽>900 高>600）", form.Width > 900 && form.Height > 600,
            form.Width + "x" + form.Height);

        int buttons = CountControls(form, typeof(Button));
        int textBoxes = CountControls(form, typeof(TextBox));
        int combos = CountControls(form, typeof(ComboBox));
        int checks = CountControls(form, typeof(CheckBox));
        int grids = CountControls(form, typeof(DataGridView));
        int lists = CountControls(form, typeof(ListBox));
        int progress = CountControlsByName(form, "ProgressBarEx");

        Console.WriteLine("  控件统计：按钮 " + buttons + "，文本框 " + textBoxes + "，下拉 " + combos
            + "，勾选框 " + checks + "，表格 " + grids + "，列表 " + lists + "，进度条 " + progress);

        Check("有开始处理等按钮（>=12 个）", buttons >= 12, "实际 " + buttons);
        Check("有日志/路径文本框（>=4 个）", textBoxes >= 4, "实际 " + textBoxes);
        Check("有类别顺序与计数口径两个下拉框", combos >= 2, "实际 " + combos);
        Check("有处理选项勾选框（>=7 个）", checks >= 7, "实际 " + checks);
        Check("有文件明细表格", grids == 1, "实际 " + grids);
        Check("有类别列表和关键词列表", lists >= 2, "实际 " + lists);
        Check("有自绘进度条 ProgressBarEx", progress >= 1, "实际 " + progress);

        // 默认类别是否正确载入
        Check("默认带 2 个示例类别（爱情/颜色）", ContainsText(form, "爱情") && ContainsText(form, "颜色"), "未找到");

        form.Dispose();
        return Finish();
    }

    private static int Finish()
    {
        Console.WriteLine();
        Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
        return _fail == 0 ? 0 : 1;
    }

    private static int CountControls(Control root, Type type)
    {
        int n = 0;
        foreach (Control c in root.Controls)
        {
            if (type.IsInstanceOfType(c)) n++;
            n += CountControls(c, type);
        }
        return n;
    }

    private static int CountControlsByName(Control root, string typeName)
    {
        int n = 0;
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == typeName) n++;
            n += CountControlsByName(c, typeName);
        }
        return n;
    }

    private static bool ContainsText(Control root, string text)
    {
        if (root is ListBox)
        {
            ListBox lb = (ListBox)root;
            foreach (object item in lb.Items)
                if (item != null && item.ToString().IndexOf(text, StringComparison.Ordinal) >= 0) return true;
        }
        if (root.Text != null && root.Text.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
        foreach (Control c in root.Controls)
            if (ContainsText(c, text)) return true;
        return false;
    }

    private static void Check(string title, bool ok, string actual)
    {
        if (ok) { _pass++; Console.WriteLine("  [通过] " + title); }
        else { _fail++; Console.WriteLine("  [失败] " + title + "   → 实际：" + actual); }
    }
}
