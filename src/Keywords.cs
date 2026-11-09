using System;
using System.Collections.Generic;

namespace WordClassify
{
    /// <summary>一个检索类别，例如「爱情」，包含若干关键词和一个插入正文的标记。</summary>
    public class Category
    {
        public string Name = "";
        /// <summary>插入正文的标记内容，例如 "123"。渲染成 〔123〕 由 Settings 控制。</summary>
        public string Marker = "";
        public List<string> Keywords = new List<string>();

        public Category() { }

        public Category(string name, string marker, params string[] keywords)
        {
            Name = name ?? "";
            Marker = marker ?? "";
            if (keywords != null)
            {
                foreach (string k in keywords)
                    if (!string.IsNullOrEmpty(k)) Keywords.Add(k);
            }
        }

        public Category Clone()
        {
            Category c = new Category();
            c.Name = Name;
            c.Marker = Marker;
            c.Keywords = new List<string>(Keywords);
            return c;
        }
    }

    /// <summary>单个关键词在某文件里的命中情况（用于日志明细）。</summary>
    public class KeywordHit
    {
        public string Keyword = "";
        /// <summary>该关键词在全文里的出现次数（按子串计，重复出现都算）。</summary>
        public int Count;
        /// <summary>该关键词作为「独立命中片段」被计入的次数（被同类更长词包含的部分不算）。</summary>
        public int SnippetCount;

        public KeywordHit() { }
        public KeywordHit(string keyword, int count) { Keyword = keyword; Count = count; }
        public KeywordHit(string keyword, int count, int snippetCount)
        {
            Keyword = keyword;
            Count = count;
            SnippetCount = snippetCount;
        }
    }

    /// <summary>某个类别在某文件里的命中情况。</summary>
    public class CategoryHit
    {
        public int CategoryIndex;
        public string CategoryName = "";
        /// <summary>命中的片段数（同名并列命中算一处）。</summary>
        public int Count;
        /// <summary>关键词出现次数（同一关键词出现几次算几次）。</summary>
        public int OccurrenceCount;
        /// <summary>命中的不同关键词个数。</summary>
        public int DistinctKeywordCount;
        /// <summary>首次命中位置，用于「按文中出现先后」排序文件名的类别顺序；-1 表示未命中。</summary>
        public int FirstPos = -1;
        public List<KeywordHit> Keywords = new List<KeywordHit>();

        /// <summary>按指定口径取「N次」里的 N。</summary>
        public int CountBy(CountMode mode)
        {
            switch (mode)
            {
                case CountMode.Occurrence: return OccurrenceCount;
                case CountMode.DistinctKeyword: return DistinctKeywordCount;
                default: return Count;
            }
        }

        /// <summary>明细文本：关键词×出现次数。例如「我爱你×1、爱你×2」。</summary>
        public string Detail()
        {
            List<string> parts = new List<string>();
            foreach (KeywordHit k in Keywords)
                parts.Add(k.Keyword + "×" + k.Count);
            return string.Join("、", parts.ToArray());
        }
    }

    /// <summary>替换方案：把某个位置的关键词替换成「标记 + 原词」。</summary>
    public class Replacement
    {
        public int Index;
        public int Length;
        public string Text = "";
        /// <summary>该位置命中的类别索引（可能多个类别在同一位置命中，标记会合并）。</summary>
        public List<int> CategoryIndexes = new List<int>();
    }

    /// <summary>一个文件匹配后的完整结果。</summary>
    public class MatchResult
    {
        public int TotalCount;
        public List<CategoryHit> Hits = new List<CategoryHit>();
        /// <summary>从后往前的替换方案，按 Index 降序，可直接顺序套用不会错位。</summary>
        public List<Replacement> Replacements = new List<Replacement>();

        public CategoryHit Find(int categoryIndex)
        {
            foreach (CategoryHit h in Hits)
                if (h.CategoryIndex == categoryIndex) return h;
            return null;
        }
    }

    public enum FileStatus
    {
        Success,
        Skipped,
        Failed
    }

    /// <summary>一个文件的处理结果，UI 的明细表直接绑定它。</summary>
    public class FileResult
    {
        public string SourcePath = "";
        public string SourceName = "";
        public string RelativeDir = "";
        public string OutputName = "";
        public string OutputPath = "";
        public FileStatus Status = FileStatus.Skipped;
        public string Message = "";
        public string EncodingName = "";
        public long CharCount;
        public long ElapsedMs;
        public List<CategoryHit> Hits = new List<CategoryHit>();

        /// <summary>命中类别的展示文本，例如「爱情 2、颜色 2」。</summary>
        public string HitText
        {
            get
            {
                if (Hits.Count == 0) return "—";
                List<string> parts = new List<string>();
                foreach (CategoryHit h in Hits)
                    parts.Add(h.CategoryName + " " + h.Count);
                return string.Join("、", parts.ToArray());
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case FileStatus.Success: return "成功";
                    case FileStatus.Failed: return "失败";
                    default: return "跳过";
                }
            }
        }
    }

    /// <summary>整体处理统计。</summary>
    public class RunSummary
    {
        public int Total;
        public int Scanned;
        public int Succeeded;
        public int Skipped;
        public int Failed;
        public long ElapsedMs;
        /// <summary>每个类别的命中文件数与命中总次数，按类别索引存放。</summary>
        public int[] CategoryFileCount = new int[0];
        public int[] CategoryHitCount = new int[0];
    }

    /// <summary>处理过程中的实时状态，供界面定时读取。</summary>
    public class ProcessProgress
    {
        public int Total;
        public int Scanned;
        public int Succeeded;
        public int Skipped;
        public int Failed;
        public string CurrentFile = "";
        public long CharCount;
        public DateTime StartedAt = DateTime.Now;
    }

    /// <summary>
    /// 文件名里「N次」的计数口径。
    /// </summary>
    public enum CountMode
    {
        /// <summary>命中片段数：同一类别连续重复的相同关键词只算 1 次（同名并列命中算 1 处）。</summary>
        Snippet = 0,
        /// <summary>关键词出现次数：同一关键词出现几次就算几次。</summary>
        Occurrence = 1,
        /// <summary>命中的不同关键词个数：类别里有几个关键词在文中出现过。</summary>
        DistinctKeyword = 2
    }

    /// <summary>处理选项。</summary>
    public class RunOptions
    {
        public bool Recurse;
        public bool IncludeTxt = true;
        public bool IncludeDocx = true;
        public bool InsertMarkers = true;
        /// <summary>每个类别只标记第一次出现，其余只计数。</summary>
        public bool MarkFirstOccurrenceOnly = true;
        /// <summary>无任何命中的文件不生成新文件。</summary>
        public bool SkipNoMatch = true;
        /// <summary>dry-run：只计算不写文件。</summary>
        public bool DryRun;
        /// <summary>文件名里类别顺序：0=按类别列表，1=按命中次数从多到少，2=按文中首次出现先后。</summary>
        public int NameOrder;
        /// <summary>「N次」的计数口径，见 CountMode。</summary>
        public CountMode CountMode = CountMode.Snippet;
    }
}
