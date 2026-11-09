using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WordClassify
{
    /// <summary>
    /// 命令行自检：不启动界面、不写任何文件，直接验证核心算法。
    /// 用法： 关键词分类工具.exe --selftest
    /// </summary>
    public static class SelfTest
    {
        private static int _pass;
        private static int _fail;

        public static int Run()
        {
            UseUtf8Console();
            Console.WriteLine("=== 关键词分类工具 · 核心算法自检 ===");
            Console.WriteLine();

            CaseUserExample();
            CaseNestedKeywords();
            CaseCrossCategoryOverlap();
            CaseSamePositionTwoCategories();
            CaseCountModes();
            CaseBoundaryNoDuplicate();
            CaseMultipleOccurrences();
            CaseCaseInsensitive();
            CaseWhitespaceAndFullWidth();
            CaseNewlineNoCross();
            CaseFileNameOrder();
            CaseMarkerInsertion();
            CaseRegexLikeChars();
            CaseEmptyAndInvalidInput();

            Console.WriteLine();
            Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
            return _fail == 0 ? 0 : 1;
        }

        // ---------- 各个用例 ----------

        private static void CaseUserExample()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("爱情", "123", "我爱你", "爱你"));
            cats.Add(new Category("颜色", "234", "红色", "绿色", "蓝色"));
            string text = "我爱你，爱你，红色，绿色，蓝色，哈哈哈";

            KeywordMatcher m = new KeywordMatcher(cats, false);
            MatchResult r = m.Match(text);

            // 文中实际出现：我爱你(0) 爱你(5) 红色(8) 绿色(11) 蓝色(14)
            Check("用户例子 · 爱情命中片段 = 2（我爱你、爱你）", Count(r, 0) == 2, "实际 " + Count(r, 0));
            Check("用户例子 · 颜色命中片段 = 3（红色、绿色、蓝色）", Count(r, 1) == 3, "实际 " + Count(r, 1));
            Check("用户例子 · 爱情明细含 我爱你×1、爱你×2（爱你出现 2 次：1 次在我爱你里，1 次独立）",
                Detail(r, 0) == "我爱你×1、爱你×2", Detail(r, 0));
            RunOptions o = new RunOptions();
            string name = Processor.BuildOutputName("原名称.txt", r, o);
            Check("用户例子 · 默认口径(命中片段数) 新文件名 = 爱情2次-颜色3次-原名称.txt",
                name == "爱情2次-颜色3次-原名称.txt", name);

            RunOptions distinct = new RunOptions();
            distinct.CountMode = CountMode.DistinctKeyword;
            Check("用户例子 · 「命中的不同关键词个数」口径 = 爱情2次-颜色3次",
                Processor.BuildOutputName("原名称.txt", r, distinct) == "爱情2次-颜色3次-原名称.txt",
                Processor.BuildOutputName("原名称.txt", r, distinct));
        }

        private static void CaseNestedKeywords()
        {
            // 同一类别内一个关键词被另一个完全包含 → 只算一次
            List<Category> cats = new List<Category>();
            cats.Add(new Category("颜色", "234", "红色", "深红色"));
            string text = "深红色很显眼，红色也醒目";

            KeywordMatcher m = new KeywordMatcher(cats, false);
            MatchResult r = m.Match(text);

            Check("包含关系 · 「深红色」只算 1 次（不再重复算「红色」）", Count(r, 0) == 2,
                "实际 " + Count(r, 0) + "，明细 " + Detail(r, 0));
            Check("包含关系 · 明细为 红色×2、深红色×1（红色出现 2 次：1 次在深红色里，1 次独立）",
                Detail(r, 0) == "红色×2、深红色×1", Detail(r, 0));
            CategoryHit hit = r.Find(0);
            Check("包含关系 · 片段数=2 出现次数=3 不同关键词=2",
                hit != null && hit.Count == 2 && hit.OccurrenceCount == 3 && hit.DistinctKeywordCount == 2,
                hit == null ? "无命中" : (hit.Count + "/" + hit.OccurrenceCount + "/" + hit.DistinctKeywordCount));
        }

        private static void CaseCrossCategoryOverlap()
        {
            // 跨类别重叠互不影响（这是曾经的真实 bug：短命中被另一类别的长命中吞掉）
            List<Category> cats = new List<Category>();
            cats.Add(new Category("深色系", "A", "深红色"));
            cats.Add(new Category("颜色", "B", "红色"));
            string text = "深红色";

            KeywordMatcher m = new KeywordMatcher(cats, false);
            MatchResult r = m.Match(text);

            Check("跨类别 · 深色系 = 1 次", Count(r, 0) == 1, "实际 " + Count(r, 0));
            Check("跨类别 · 颜色 = 1 次（跨类别不互相吞掉）", Count(r, 1) == 1, "实际 " + Count(r, 1));
            Check("跨类别 · 「深红色」的标记合并成一处",
                r.Replacements.Count == 1 && r.Replacements[0].Length == 3,
                "替换点数 " + r.Replacements.Count + "，长度 " + (r.Replacements.Count > 0 ? r.Replacements[0].Length : 0));

            RunOptions o = new RunOptions();
            string name = Processor.BuildOutputName("a.txt", r, o);
            Check("跨类别 · 文件名 = 深色系1次-颜色1次-a.txt", name == "深色系1次-颜色1次-a.txt", name);

            // 长片段占比更大的情形
            List<Category> cats2 = new List<Category>();
            cats2.Add(new Category("长", "L", "ABCDE"));
            cats2.Add(new Category("短", "S", "CD"));
            MatchResult r2 = new KeywordMatcher(cats2, false).Match("ABCDE");
            Check("跨类别 · 长词与短词同时命中各计 1 次",
                Count(r2, 0) == 1 && Count(r2, 1) == 1,
                Count(r2, 0) + "/" + Count(r2, 1));
        }

        private static void CaseSamePositionTwoCategories()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("甲", "1", "苹果"));
            cats.Add(new Category("乙", "2", "苹果"));
            KeywordMatcher m = new KeywordMatcher(cats, false);
            MatchResult r = m.Match("苹果");

            Check("同片段两类命中 · 两类各 1 次", Count(r, 0) == 1 && Count(r, 1) == 1,
                Count(r, 0) + "/" + Count(r, 1));
            Check("同片段两类命中 · 只插一次标记", r.Replacements.Count == 1, "实际 " + r.Replacements.Count + " 处");
        }

        private static void CaseMultipleOccurrences()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("爱情", "1", "爱你"));
            KeywordMatcher m = new KeywordMatcher(cats, false);
            MatchResult r = m.Match("爱你爱你爱你");

            Check("连续重复 · 3 次", Count(r, 0) == 3, "实际 " + Count(r, 0));
        }

        /// <summary>三种计数口径的差异（用同类内互相包含的两个词来体现）。</summary>
        private static void CaseCountModes()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("颜色", "C", "红色", "深红色"));
            // 文中：深红色(1 处) + 红色(1 处)
            //   命中片段数 = 2（「深红色」内部的「红色」被包含，不重复算）
            //   关键词出现次数 = 3（深红色 1 次 + 里面/后面的 红色 2 次）
            //   不同关键词个数 = 2
            MatchResult r = new KeywordMatcher(cats, false).Match("深红色，还有红色");

            RunOptions snippet = new RunOptions();
            snippet.CountMode = CountMode.Snippet;
            RunOptions occurrence = new RunOptions();
            occurrence.CountMode = CountMode.Occurrence;
            RunOptions distinct = new RunOptions();
            distinct.CountMode = CountMode.DistinctKeyword;

            Check("计数口径 · 命中片段数 = 2",
                Processor.BuildOutputName("a.txt", r, snippet) == "颜色2次-a.txt",
                Processor.BuildOutputName("a.txt", r, snippet) + "  明细=" + Detail(r, 0));
            Check("计数口径 · 关键词出现次数 = 3",
                Processor.BuildOutputName("a.txt", r, occurrence) == "颜色3次-a.txt",
                Processor.BuildOutputName("a.txt", r, occurrence));
            Check("计数口径 · 不同关键词个数 = 2",
                Processor.BuildOutputName("a.txt", r, distinct) == "颜色2次-a.txt",
                Processor.BuildOutputName("a.txt", r, distinct));

            // 同一个词重复三次：三种口径下片段数与出现次数都是 3
            List<Category> cats2 = new List<Category>();
            cats2.Add(new Category("爱情", "1", "爱你"));
            MatchResult r2 = new KeywordMatcher(cats2, false).Match("爱你爱你爱你");
            Check("计数口径 · 同一词出现 3 次 → 3",
                Processor.BuildOutputName("a.txt", r2, snippet) == "爱情3次-a.txt",
                Processor.BuildOutputName("a.txt", r2, snippet));
        }

        /// <summary>同一类别内两个关键词首尾相接（边界相邻）不能重复计数。</summary>
        private static void CaseBoundaryNoDuplicate()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("颜色", "C", "红色", "深红色"));
            KeywordMatcher m = new KeywordMatcher(cats, false);

            // 只出现一次「深红色」，不应同时再算一次「红色」
            Check("边界 · 只有一个「深红色」= 1 次", Count(m.Match("深红色"), 0) == 1,
                "实际 " + Count(m.Match("深红色"), 0));
            // 两个「深红色」相邻
            Check("边界 · 「深红色深红色」= 2 次", Count(m.Match("深红色深红色"), 0) == 2,
                "实际 " + Count(m.Match("深红色深红色"), 0));
            // 「红色深红色」= 红色 1 次 + 深红色 1 次
            Check("边界 · 「红色深红色」= 2 次", Count(m.Match("红色深红色"), 0) == 2,
                "实际 " + Count(m.Match("红色深红色"), 0));
        }

        private static void CaseCaseInsensitive()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("英文", "E", "Hello"));
            MatchResult sensitive = new KeywordMatcher(cats, false).Match("hello HELLO Hello");
            MatchResult insensitve = new KeywordMatcher(cats, true).Match("hello HELLO Hello");

            Check("区分大小写 · 只命中 1 次", Count(sensitive, 0) == 1, "实际 " + Count(sensitive, 0));
            Check("忽略大小写 · 命中 3 次", Count(insensitve, 0) == 3, "实际 " + Count(insensitve, 0));
        }

        private static void CaseWhitespaceAndFullWidth()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("短语", "P", "红色 汽车"));   // 关键词里是半角空格
            KeywordMatcher m = new KeywordMatcher(cats, false);

            Check("空白归一 · 全角空格也算命中", Count(m.Match("红色　汽车"), 0) == 1,
                "实际 " + Count(m.Match("红色　汽车"), 0));
            Check("空白归一 · 多个空格算命中", Count(m.Match("红色   汽车"), 0) == 1,
                "实际 " + Count(m.Match("红色   汽车"), 0));
            Check("空白归一 · 正常空格算命中", Count(m.Match("红色 汽车"), 0) == 1,
                "实际 " + Count(m.Match("红色 汽车"), 0));
        }

        private static void CaseNewlineNoCross()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("跨行", "N", "红色汽车"));
            KeywordMatcher m = new KeywordMatcher(cats, false);

            Check("不跨段落匹配 · 换行隔开不算命中", Count(m.Match("红色\r\n汽车"), 0) == 0,
                "实际 " + Count(m.Match("红色\r\n汽车"), 0));
            Check("不跨段落匹配 · 同一行算命中", Count(m.Match("红色汽车"), 0) == 1,
                "实际 " + Count(m.Match("红色汽车"), 0));
        }

        private static void CaseFileNameOrder()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("甲", "1", "AA"));
            cats.Add(new Category("乙", "2", "BB"));
            KeywordMatcher m = new KeywordMatcher(cats, false);
            MatchResult r = m.Match("BB BB BB AA");   // 乙 3 次、甲 1 次；甲先出现位置更靠后

            RunOptions byList = new RunOptions(); byList.NameOrder = 0;
            RunOptions byCount = new RunOptions(); byCount.NameOrder = 1;
            RunOptions byFirst = new RunOptions(); byFirst.NameOrder = 2;

            Check("文件名顺序 · 按类别列表 = 甲1次-乙3次-x.txt",
                Processor.BuildOutputName("x.txt", r, byList) == "甲1次-乙3次-x.txt",
                Processor.BuildOutputName("x.txt", r, byList));
            Check("文件名顺序 · 按次数 = 乙3次-甲1次-x.txt",
                Processor.BuildOutputName("x.txt", r, byCount) == "乙3次-甲1次-x.txt",
                Processor.BuildOutputName("x.txt", r, byCount));
            Check("文件名顺序 · 按首次出现 = 乙3次-甲1次-x.txt",
                Processor.BuildOutputName("x.txt", r, byFirst) == "乙3次-甲1次-x.txt",
                Processor.BuildOutputName("x.txt", r, byFirst));
        }

        private static void CaseMarkerInsertion()
        {
            // 同类两个关键词各出现一次：「每类只标第一次」= 该类只标一次，所以只标「我爱你」
            List<Category> cats = new List<Category>();
            cats.Add(new Category("爱情", "123", "我爱你", "爱你"));
            MatchResult r = new KeywordMatcher(cats, false).Match("我爱你，爱你");
            string markedOne = ApplyText("我爱你，爱你", r.Replacements);
            Settings keepOne = new Settings();
            keepOne.MarkFirstOccurrenceOnly = true;
            Settings keepAll = new Settings();
            keepAll.MarkFirstOccurrenceOnly = false;

            string firstOnly = ApplyViaProcessor(cats, keepOne, "我爱你，爱你");
            string allOfThem = ApplyViaProcessor(cats, keepAll, "我爱你，爱你");
            Check("标记插入 · 每类只标第一次 = 〔123〕我爱你，爱你",
                firstOnly == "〔123〕我爱你，爱你", firstOnly);
            Check("标记插入 · 全标 = 〔123〕我爱你，〔123〕爱你",
                allOfThem == "〔123〕我爱你，〔123〕爱你", allOfThem);

            // 同一个关键词重复出现
            List<Category> cats2 = new List<Category>();
            cats2.Add(new Category("爱情", "123", "爱你"));
            string one = ApplyViaProcessor(cats2, keepOne, "爱你，爱你，爱你");
            string all = ApplyViaProcessor(cats2, keepAll, "爱你，爱你，爱你");
            Check("标记插入 · 只标第一次 = 〔123〕爱你，爱你，爱你",
                one == "〔123〕爱你，爱你，爱你", one);
            Check("标记插入 · 每次都标 = 〔123〕爱你，〔123〕爱你，〔123〕爱你",
                all == "〔123〕爱你，〔123〕爱你，〔123〕爱你", all);

            // 多类别：每类各标第一次
            List<Category> cats3 = new List<Category>();
            cats3.Add(new Category("爱情", "123", "爱你"));
            cats3.Add(new Category("颜色", "234", "红色"));
            string multi = ApplyViaProcessor(cats3, keepOne, "爱你 红色 爱你 红色");
            Check("标记插入 · 多类别各标第一次 = 〔123〕爱你 〔234〕红色 爱你 红色",
                multi == "〔123〕爱你 〔234〕红色 爱你 红色", multi);
        }

        private static string ApplyViaProcessor(List<Category> cats, Settings s, string text)
        {
            s.Categories = cats;
            Processor p = new Processor(cats, s);
            string tmp = Path.Combine(Path.GetTempPath(), "wct_selftest_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                PreparedFile pf = p.Prepare(tmp, Path.GetDirectoryName(tmp), s.ToRunOptions(true));
                return pf.Result.Status == FileStatus.Failed ? "错误：" + pf.Result.Message : pf.NewText;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }

        /// <summary>按 Replacement.Text 直接把标记打进原文（等价于 Processor 的核心动作）。</summary>
        private static string ApplyText(string text, List<Replacement> reps)
        {
            List<Replacement> asc = new List<Replacement>(reps);
            asc.Sort(delegate (Replacement a, Replacement b) { return a.Index.CompareTo(b.Index); });
            StringBuilder sb = new StringBuilder();
            int cursor = 0;
            foreach (Replacement r in asc)
            {
                sb.Append(text, cursor, r.Index - cursor);
                sb.Append(r.Text);
                cursor = r.Index + r.Length;
            }
            sb.Append(text, cursor, text.Length - cursor);
            return sb.ToString();
        }

        private static void CaseRegexLikeChars()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("特殊", "S", "a.b", "x[1]", "(test)", "C++", "100%", "a|b"));
            KeywordMatcher m = new KeywordMatcher(cats, false);
            string text = "a.b 和 x[1] 还有 (test) 以及 C++ 100% a|b";

            Check("特殊字符 · 按纯文本匹配（不当正则）", Count(m.Match(text), 0) == 6,
                "实际 " + Count(m.Match(text), 0));
        }

        private static void CaseEmptyAndInvalidInput()
        {
            List<Category> cats = new List<Category>();
            cats.Add(new Category("空类", "0"));
            cats.Add(new Category("正常", "1", "", "  ", "苹果"));
            KeywordMatcher m = new KeywordMatcher(cats, false);

            Check("空输入 · 空文本不报错", m.Match("").Hits.Count == 0, "OK");
            Check("空输入 · null 不报错", m.Match(null).Hits.Count == 0, "OK");
            Check("空关键词 · 自动忽略空串与纯空白", m.HasKeywords(1) && !m.HasKeywords(0), "OK");

            MatchResult r = m.Match("苹果");
            Check("空类别 · 不产生命中", Count(r, 0) == 0, "实际 " + Count(r, 0));
        }

        // ---------- 辅助 ----------

        private static int Count(MatchResult r, int categoryIndex)
        {
            CategoryHit h = r.Find(categoryIndex);
            return h == null ? 0 : h.Count;
        }

        private static string Detail(MatchResult r, int categoryIndex)
        {
            CategoryHit h = r.Find(categoryIndex);
            return h == null ? "（无命中）" : h.Detail();
        }

        private static void Check(string title, bool ok, string actual)
        {
            if (ok)
            {
                _pass++;
                Console.WriteLine("  [通过] " + title);
            }
            else
            {
                _fail++;
                Console.WriteLine("  [失败] " + title + "   → 实际：" + actual);
            }
        }

        /// <summary>把标准输出切到 UTF-8；GUI 子系统下没有控制台会抛异常，忽略即可。</summary>
        public static void UseUtf8Console()
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch (Exception) { }
        }
    }
}
