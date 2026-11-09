using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// 配置存取测试：验证 config.json 能正确保存与恢复（路径、类别、关键词、标记、选项）。
/// 注意：程序把 config.json 放在 **exe 所在目录**（不是当前工作目录），这是设计如此。
/// 用法： ConfigTest.exe &lt;exe路径&gt; &lt;临时工作目录&gt;
/// 测试前会把 exe 复制到临时工作目录，测试后删掉整个目录，不影响原程序。
/// </summary>
internal static class ConfigTest
{
    private static int _pass;
    private static int _fail;

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        string sourceExe = Path.GetFullPath(args[0]);
        string work = Path.GetFullPath(args.Length > 1 ? args[1]
            : Path.Combine(Path.GetTempPath(), "wct_cfg_" + Guid.NewGuid().ToString("N")));

        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        string exe = Path.Combine(work, "KeywordClassifier.exe");
        File.Copy(sourceExe, exe, true);
        string cfg = Path.Combine(work, "config.json");

        // 关键：被测程序用 AppDomain.CurrentDomain.BaseDirectory 决定 config.json 的位置。
        // Assembly.LoadFrom 不会改变 BaseDirectory，所以这里显式把 APPBASE 指到临时目录，
        // 否则配置会被写到本测试程序自己的目录里。
        AppDomain.CurrentDomain.SetData("APPBASE", work);

        Console.WriteLine("=== 配置存取测试 ===");
        Console.WriteLine("测试副本：" + exe);
        Console.WriteLine("配置文件：" + cfg);
        Console.WriteLine();

        try
        {
            Assembly asm = Assembly.UnsafeLoadFrom(exe);
            Type mainForm = asm.GetType("WordClassify.MainForm");
            Type settingsType = asm.GetType("WordClassify.Settings");

            Console.WriteLine("--- 1. 没有配置文件时：自动用默认类别；退出时把配置写到 exe 旁边 ---");
            Safe("首次启动能正常打开（无配置也不报错）", delegate
            {
                Form form = (Form)Activator.CreateInstance(mainForm);
                bool ok = form != null;
                // 真实地"打开一次再关掉"：定时器到点自动关闭窗口，验证退出时会把配置写到 exe 旁边
                System.Windows.Forms.Timer closer = new System.Windows.Forms.Timer();
                closer.Interval = 800;
                closer.Tick += delegate { closer.Stop(); form.Close(); };
                closer.Start();
                Application.Run(form);
                return new object[] { ok, "null" };
            });
            Check("退出时 config.json 已生成在 exe 旁边", File.Exists(cfg), cfg);

            if (!File.Exists(cfg)) return Finish(work);

            string json = File.ReadAllText(cfg, Encoding.UTF8);
            Check("config.json 内容非空", json.Length > 50, json.Length + " 字符");
            Check("含默认类别「爱情」", json.IndexOf("爱情", StringComparison.Ordinal) >= 0, "未找到");
            Check("含默认类别「颜色」", json.IndexOf("颜色", StringComparison.Ordinal) >= 0, "未找到");
            Check("含默认标记 123 / 234",
                json.IndexOf("123", StringComparison.Ordinal) >= 0 && json.IndexOf("234", StringComparison.Ordinal) >= 0,
                "未找到");

            Console.WriteLine();
            Console.WriteLine("--- 2. 手写一份自定义配置，检查能否逐字段恢复 ---");
            string custom =
                "{\"SourceDir\":\"D:\\\\我的资料\",\"OutputDir\":\"D:\\\\分类结果\"," +
                "\"Categories\":[{\"Name\":\"动物\",\"Marker\":\"A1\",\"Keywords\":[\"猫\",\"狗\",\"老虎\"]}," +
                "{\"Name\":\"水果\",\"Marker\":\"B2\",\"Keywords\":[\"苹果\",\"香蕉\"]}]," +
                "\"Recurse\":false,\"IncludeTxt\":true,\"IncludeDocx\":false," +
                "\"InsertMarkers\":false,\"MarkFirstOccurrenceOnly\":false,\"SkipNoMatch\":true," +
                "\"IgnoreCase\":true,\"MarkerPrefix\":\"[\",\"MarkerSuffix\":\"]\"," +
                "\"NameOrder\":2,\"CountModeIndex\":1}";
            File.WriteAllText(cfg, custom, new UTF8Encoding(true));

            object settings = settingsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, null);
            Check("Load() 返回对象非空", settings != null, "null");
            if (settings == null) return Finish(work);

            object cats = GetField(settings, "Categories");
            Check("类别数量 = 2", Count(cats) == 2, "实际 " + Count(cats));
            Check("类别1 名称 = 动物", GetCategoryName(cats, 0) == "动物", GetCategoryName(cats, 0));
            Check("类别2 名称 = 水果", GetCategoryName(cats, 1) == "水果", GetCategoryName(cats, 1));
            Check("类别1 标记 = A1", GetCategoryMarker(cats, 0) == "A1", GetCategoryMarker(cats, 0));
            Check("类别1 关键词 = 猫,狗,老虎", GetCategoryKeywords(cats, 0) == "猫,狗,老虎", GetCategoryKeywords(cats, 0));
            Check("类别2 关键词 = 苹果,香蕉", GetCategoryKeywords(cats, 1) == "苹果,香蕉", GetCategoryKeywords(cats, 1));
            Check("源文件夹恢复正确", Convert.ToString(GetField(settings, "SourceDir")) == @"D:\我的资料",
                Convert.ToString(GetField(settings, "SourceDir")));
            Check("输出文件夹恢复正确", Convert.ToString(GetField(settings, "OutputDir")) == @"D:\分类结果",
                Convert.ToString(GetField(settings, "OutputDir")));
            Check("递归子文件夹 = false", !Convert.ToBoolean(GetField(settings, "Recurse")),
                Convert.ToString(GetField(settings, "Recurse")));
            Check("处理 docx = false", !Convert.ToBoolean(GetField(settings, "IncludeDocx")),
                Convert.ToString(GetField(settings, "IncludeDocx")));
            Check("插入标记 = false", !Convert.ToBoolean(GetField(settings, "InsertMarkers")),
                Convert.ToString(GetField(settings, "InsertMarkers")));
            Check("每类只标第一次 = false", !Convert.ToBoolean(GetField(settings, "MarkFirstOccurrenceOnly")),
                Convert.ToString(GetField(settings, "MarkFirstOccurrenceOnly")));
            Check("忽略大小写 = true", Convert.ToBoolean(GetField(settings, "IgnoreCase")),
                Convert.ToString(GetField(settings, "IgnoreCase")));
            Check("标记括号恢复为 [ ]",
                Convert.ToString(GetField(settings, "MarkerPrefix")) == "["
                && Convert.ToString(GetField(settings, "MarkerSuffix")) == "]",
                Convert.ToString(GetField(settings, "MarkerPrefix")) + Convert.ToString(GetField(settings, "MarkerSuffix")));
            Check("类别顺序 = 2（按文中首次出现）", Convert.ToInt32(GetField(settings, "NameOrder")) == 2,
                Convert.ToString(GetField(settings, "NameOrder")));
            Check("计数口径 = 1（关键词出现次数）", Convert.ToInt32(GetField(settings, "CountModeIndex")) == 1,
                Convert.ToString(GetField(settings, "CountModeIndex")));

            Console.WriteLine();
            Console.WriteLine("--- 3. 界面能否按这份自定义配置恢复 ---");
            Form form2 = (Form)Activator.CreateInstance(mainForm);
            Check("按自定义配置打开界面不报错", form2 != null, "null");
            if (form2 != null)
            {
                ComboBox cmbCount = (ComboBox)FindControl(form2, typeof(ComboBox), 1);
                Check("界面「计数口径」下拉框选中第 2 项（索引 1）",
                    cmbCount != null && cmbCount.SelectedIndex == 1,
                    cmbCount == null ? "未找到下拉框" : cmbCount.SelectedIndex.ToString());
                ComboBox cmbOrder = (ComboBox)FindControl(form2, typeof(ComboBox), 0);
                Check("界面「类别顺序」下拉框选中第 3 项（索引 2）",
                    cmbOrder != null && cmbOrder.SelectedIndex == 2,
                    cmbOrder == null ? "未找到下拉框" : cmbOrder.SelectedIndex.ToString());
                Check("界面载入了自定义类别「动物」", ContainsText(form2, "动物"), "未找到");
                Check("界面载入了自定义类别「水果」", ContainsText(form2, "水果"), "未找到");
                Check("界面载入了源文件夹 D:\\我的资料", ContainsText(form2, @"D:\我的资料"), "未找到");
                form2.Close();
                form2.Dispose();
            }
            Check("关闭界面后自定义配置被回写保留", File.ReadAllText(cfg, Encoding.UTF8)
                .IndexOf("动物", StringComparison.Ordinal) >= 0, "未找到");

            Console.WriteLine();
            Console.WriteLine("--- 4. 配置文件损坏时的容错 ---");
            File.WriteAllText(cfg, "这不是合法的 json {{{", new UTF8Encoding(true));
            object fallback = null;
            try
            {
                fallback = settingsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static)
                    .Invoke(null, null);
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException != null ? ex.InnerException : ex;
                Check("配置损坏时 Load() 不抛异常", false, inner.GetType().Name + "：" + inner.Message);
            }
            if (fallback != null)
            {
                Check("配置损坏时回退到默认类别（2 个示例类别）",
                    Count(GetField(fallback, "Categories")) == 2, "实际 " + Count(GetField(fallback, "Categories")));
            }
            Form form3 = (Form)Activator.CreateInstance(mainForm);
            Check("配置损坏时界面仍能启动", form3 != null, "null");
            if (form3 != null) { form3.Close(); form3.Dispose(); }

            return Finish(work);
        }
        catch (Exception ex)
        {
            Exception inner = ex.InnerException != null ? ex.InnerException : ex;
            Console.WriteLine();
            Console.WriteLine("[异常] " + inner.GetType().Name + "：" + inner.Message);
            Console.WriteLine(inner.StackTrace);
            _fail++;
            return Finish(work);
        }
    }

    private static void Safe(string title, Func<object[]> action)
    {
        try
        {
            object[] r = action();
            Check(title, Convert.ToBoolean(r[0]), Convert.ToString(r[1]));
        }
        catch (Exception ex)
        {
            Exception inner = ex.InnerException != null ? ex.InnerException : ex;
            Check(title, false, inner.GetType().Name + "：" + inner.Message);
        }
    }

    private static int Finish(string work)
    {
        try { Directory.Delete(work, true); } catch (Exception) { }
        Console.WriteLine();
        Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
        return _fail == 0 ? 0 : 1;
    }

    private static object GetField(object obj, string name)
    {
        if (obj == null) return null;
        FieldInfo f = obj.GetType().GetField(name);
        return f == null ? null : f.GetValue(obj);
    }

    private static int Count(object list)
    {
        System.Collections.ICollection c = list as System.Collections.ICollection;
        return c == null ? -1 : c.Count;
    }

    private static object CategoryAt(object list, int index)
    {
        System.Collections.IList l = list as System.Collections.IList;
        return l == null || index >= l.Count ? null : l[index];
    }

    private static string GetCategoryName(object list, int index)
    {
        object c = CategoryAt(list, index);
        return c == null ? "(无)" : Convert.ToString(GetField(c, "Name"));
    }

    private static string GetCategoryMarker(object list, int index)
    {
        object c = CategoryAt(list, index);
        return c == null ? "(无)" : Convert.ToString(GetField(c, "Marker"));
    }

    private static string GetCategoryKeywords(object list, int index)
    {
        object c = CategoryAt(list, index);
        if (c == null) return "(无)";
        System.Collections.IList l = GetField(c, "Keywords") as System.Collections.IList;
        if (l == null) return "(无)";
        List<string> parts = new List<string>();
        foreach (object k in l) parts.Add(Convert.ToString(k));
        return string.Join(",", parts.ToArray());
    }

    private static Control FindControl(Control root, Type type, int index)
    {
        int seen = 0;
        return FindControl(root, type, index, ref seen);
    }

    private static Control FindControl(Control root, Type type, int index, ref int seen)
    {
        foreach (Control c in root.Controls)
        {
            if (type.IsInstanceOfType(c))
            {
                if (seen == index) return c;
                seen++;
            }
            Control found = FindControl(c, type, index, ref seen);
            if (found != null) return found;
        }
        return null;
    }

    private static bool ContainsText(Control root, string text)
    {
        if (root is ListBox)
        {
            ListBox lb = (ListBox)root;
            foreach (object item in lb.Items)
                if (item != null && item.ToString().IndexOf(text, StringComparison.Ordinal) >= 0) return true;
        }
        TextBox tb = root as TextBox;
        if (tb != null && tb.Text != null && tb.Text.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
        if (!(root is TextBox) && root.Text != null && root.Text.IndexOf(text, StringComparison.Ordinal) >= 0) return true;
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
