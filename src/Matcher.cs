using System;
using System.Collections.Generic;
using System.Text;

namespace WordClassify
{
    /// <summary>
    /// 关键词匹配核心。
    ///
    /// 规则（与需求一一对应）：
    ///  1. 同一类别里的多个关键词都归属该类；类别命中次数 = 命中的片段数。
    ///  2. 同一类别内，若某关键词被另一个关键词完全包含，被包含者不重复计数：
    ///     采用「从左到右、最长优先」扫描，命中一个片段后跳过它，不在片段内部重复匹配。
    ///     例：颜色类含「红色」「深红色」，文中出现「深红色」只算 1 次（计在「深红色」上）。
    ///  3. 跨类别的重叠片段互相独立：每个类别都按自己的最长命中计数。
    ///     例：「深红色」若同时被 A 类（含深红色）和 B 类（含红色）命中，A、B 各 +1。
    ///  4. 同一位置同一片段被多个类别命中时，计数各 +1，但正文标记只插一次。
    /// </summary>
    public class KeywordMatcher
    {
        private sealed class Node
        {
            public readonly Dictionary<char, Node> Next = new Dictionary<char, Node>();
            /// <summary>完整关键词走到底时存放关键词原文。</summary>
            public string Keyword;
        }

        private sealed class CatTrie
        {
            public int CategoryIndex;
            public readonly Node Root = new Node();
            public bool Empty = true;
        }

        private readonly List<CatTrie> _tries = new List<CatTrie>();
        private readonly List<string> _categoryNames = new List<string>();
        private readonly List<string> _markers = new List<string>();
        /// <summary>每个类别去重后的关键词原文（用于计算"出现次数"口径）。</summary>
        private readonly List<List<string>> _originalKeywords = new List<List<string>>();
        private readonly bool _ignoreCase;

        public KeywordMatcher(IList<Category> categories, bool ignoreCase)
        {
            _ignoreCase = ignoreCase;
            for (int ci = 0; ci < categories.Count; ci++)
            {
                Category cat = categories[ci];
                CatTrie trie = new CatTrie();
                trie.CategoryIndex = ci;
                _categoryNames.Add(cat.Name == null ? "" : cat.Name);
                _markers.Add(cat.Marker == null ? "" : cat.Marker);

                HashSet<string> seenOriginal = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> seenNormalized = new HashSet<string>(StringComparer.Ordinal);
                List<string> originals = new List<string>();

                foreach (string raw in cat.Keywords)
                {
                    if (raw == null) continue;
                    string norm = NormalizeForMatch(raw);
                    if (norm.Length == 0) continue;             // 空关键词直接忽略
                    if (!seenOriginal.Add(raw)) continue;       // 同类别内同样写法的关键词只留一个
                    if (!seenNormalized.Add(norm)) continue;    // 归一化后重复的（如「红色」与「红色 」）只留一个
                    trie.Empty = false;
                    originals.Add(raw);
                    Insert(trie, norm, raw);
                }
                _tries.Add(trie);
                _originalKeywords.Add(originals);
            }
        }

        public int CategoryCount { get { return _tries.Count; } }

        /// <summary>该类别是否配置了可用关键词。</summary>
        public bool HasKeywords(int categoryIndex)
        {
            return categoryIndex >= 0 && categoryIndex < _tries.Count && !_tries[categoryIndex].Empty;
        }

        private void Insert(CatTrie trie, string norm, string original)
        {
            Node node = trie.Root;
            for (int i = 0; i < norm.Length; i++)
            {
                char c = norm[i];
                Node next;
                if (!node.Next.TryGetValue(c, out next))
                {
                    next = new Node();
                    node.Next[c] = next;
                }
                node = next;
            }
            if (node.Keyword == null) node.Keyword = original;
        }

        /// <summary>关键词归一化：去掉首尾空白，连续空白（半角/全角）合并成单个半角空格；可选忽略大小写。</summary>
        private string NormalizeForMatch(string s)
        {
            int start = 0, end = s.Length - 1;
            while (start <= end && (IsSpace(s[start]) || s[start] == '\r' || s[start] == '\n')) start++;
            while (end >= start && (IsSpace(s[end]) || s[end] == '\r' || s[end] == '\n')) end--;

            StringBuilder sb = new StringBuilder(end - start + 1);
            bool lastSpace = false;
            for (int i = start; i <= end; i++)
            {
                char c = s[i];
                if (c == '\r' || c == '\n') continue;           // 关键词不跨段落
                if (IsSpace(c))
                {
                    if (!lastSpace) sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(_ignoreCase ? char.ToLowerInvariant(c) : c);
                    lastSpace = false;
                }
            }
            return sb.ToString();
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\u3000' || c == '\u00A0' || c == '\u200B';
        }

        /// <summary>匹配整段文本（可含换行；换行只用于分行，不参与跨行匹配）。</summary>
        public MatchResult Match(string text)
        {
            MatchResult result = new MatchResult();
            if (string.IsNullOrEmpty(text)) return result;

            int categoryCount = _tries.Count;
            if (categoryCount == 0) return result;

            int n = text.Length;

            // 第一步：把每个类别在每一处的「最长命中」收集成区间。
            // 跨类别的命中必须全部留下来，最后统一处理重叠，否则短命中会被别的类别的长命中吞掉。
            List<HitSpan>[] raw = new List<HitSpan>[categoryCount];
            for (int ci = 0; ci < categoryCount; ci++) raw[ci] = new List<HitSpan>();

            int pos = 0;
            while (pos < n)
            {
                char c = text[pos];
                if (c == '\r' || c == '\n') { pos++; continue; }

                for (int ci = 0; ci < categoryCount; ci++)
                {
                    CatTrie trie = _tries[ci];
                    if (trie.Empty) continue;
                    int len = LongestMatchAt(trie.Root, text, pos);
                    if (len > 0) raw[ci].Add(new HitSpan(pos, pos + len));
                }
                pos++;
            }

            // 第二步：同类别内「最长优先 + 不重叠」——被更长命中包含的短命中丢弃。
            List<HitSpan>[] accepted = new List<HitSpan>[categoryCount];
            for (int ci = 0; ci < categoryCount; ci++)
            {
                List<HitSpan> spans = raw[ci];
                spans.Sort(HitSpan.CompareForAccept);
                List<HitSpan> kept = new List<HitSpan>();
                int lastEnd = -1;
                for (int k = 0; k < spans.Count; k++)
                {
                    HitSpan s = spans[k];
                    if (s.Start < lastEnd) continue;      // 落在同类别已接受区间内 → 被包含，不重复计数
                    kept.Add(s);
                    lastEnd = s.End;
                }
                accepted[ci] = kept;
            }

            // 第三步：合并跨类别的重叠区间（同一片段只插一次标记）。
            // 只有不同类别的命中在同一处重叠时才会合并，此时标记并列，例如 〔123、234〕。
            List<int[]> regions = new List<int[]>();
            for (int ci = 0; ci < categoryCount; ci++)
                foreach (HitSpan s in accepted[ci])
                    regions.Add(new int[] { s.Start, s.End });
            regions.Sort(delegate (int[] a, int[] b)
            {
                int c = a[0].CompareTo(b[0]);
                return c != 0 ? c : b[1].CompareTo(a[1]);
            });

            List<Replacement> replacements = new List<Replacement>();
            for (int k = 0; k < regions.Count; k++)
            {
                int start = regions[k][0];
                int end = regions[k][1];
                if (replacements.Count > 0)
                {
                    Replacement last = replacements[replacements.Count - 1];
                    if (start < last.Index + last.Length)         // 有重叠：合并，保留最长片段
                    {
                        if (end > last.Index + last.Length) last.Length = end - last.Index;
                        continue;
                    }
                }
                Replacement rep = new Replacement();
                rep.Index = start;
                rep.Length = end - start;
                replacements.Add(rep);
            }

            // 第四步：把每个类别接受的命中分配到标记区间上；若区间跨越了某类别的命中，就把区间扩大。
            for (int ci = 0; ci < categoryCount; ci++)
            {
                List<HitSpan> spans = accepted[ci];
                if (spans.Count == 0) continue;

                CategoryHit ch = new CategoryHit();
                ch.CategoryIndex = ci;
                ch.CategoryName = _categoryNames[ci];
                ch.FirstPos = spans[0].Start;
                ch.Count = spans.Count;
                result.Hits.Add(ch);

                for (int k = 0; k < spans.Count; k++)
                {
                    HitSpan s = spans[k];
                    int region = FindRegion(replacements, s.Start);
                    if (region < 0) continue;
                    Replacement rep = replacements[region];
                    if (!rep.CategoryIndexes.Contains(ci)) rep.CategoryIndexes.Add(ci);
                    if (s.End > rep.Index + rep.Length)
                    {
                        rep.Length = s.End - rep.Index;             // 区间不足以覆盖该命中，扩大
                        MergeRegionsAfter(replacements, region);
                    }
                }
            }

            // 第五步：按类别补全统计。
            //   Count              = 命中片段数（同类内被更长关键词包含的短词不重复计数）
            //   OccurrenceCount    = 关键词在全文里的出现总次数（子串计数）
            //   DistinctKeywordCount = 在文中出现过的不同关键词个数
            for (int ci = 0; ci < categoryCount; ci++)
            {
                CategoryHit ch = result.Find(ci);
                if (ch == null) continue;

                // 先按"独立命中片段"统计每个关键词被计了几次，以及每个片段匹配到的原文
                Dictionary<string, int> snippetTimes = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> snippetLen = new Dictionary<string, int>(StringComparer.Ordinal);
                List<HitSpan> spans = accepted[ci];
                for (int k = 0; k < spans.Count; k++)
                {
                    string piece = text.Substring(spans[k].Start, spans[k].End - spans[k].Start);
                    int times;
                    snippetTimes.TryGetValue(piece, out times);
                    snippetTimes[piece] = times + 1;
                    if (!snippetLen.ContainsKey(piece)) snippetLen[piece] = piece.Length;
                }

                // 再补上被包含的短关键词：它们不计入片段数，但出现次数要显示出来
                List<KeywordHit> keywordList = new List<KeywordHit>();
                int occurrences = 0;
                List<string> originals = _originalKeywords[ci];
                for (int k = 0; k < originals.Count; k++)
                {
                    string kw = originals[k];
                    int times = CountOccurrences(text, kw);
                    if (times <= 0) continue;

                    int snippets = 0;
                    foreach (KeyValuePair<string, int> kv in snippetTimes)
                    {
                        if (kv.Key.Length >= kw.Length && kv.Key.IndexOf(kw, StringComparison.Ordinal) >= 0)
                            snippets += kv.Value;
                    }
                    keywordList.Add(new KeywordHit(kw, times, snippets));
                    occurrences += times;
                }
                ch.Keywords = keywordList;
                ch.OccurrenceCount = occurrences;
                ch.DistinctKeywordCount = keywordList.Count;
            }

            int total = 0;
            for (int k = 0; k < replacements.Count; k++)
            {
                Replacement rep = replacements[k];
                rep.CategoryIndexes.Sort();
                rep.Text = BuildReplacement(rep.CategoryIndexes, text.Substring(rep.Index, rep.Length));
                total += rep.CategoryIndexes.Count;
            }
            result.TotalCount = total;
            result.Replacements = replacements;

            // 从后往前，方便直接顺序套用（不会因为长度变化而错位）
            replacements.Sort(delegate (Replacement a, Replacement b) { return b.Index.CompareTo(a.Index); });
            return result;
        }

        /// <summary>命中区间。</summary>
        private struct HitSpan
        {
            public int Start;
            public int End;

            public HitSpan(int start, int end) { Start = start; End = end; }

            /// <summary>排序：起点升序；起点相同则长的在前（保证"最长优先"）。</summary>
            public static int CompareForAccept(HitSpan a, HitSpan b)
            {
                int c = a.Start.CompareTo(b.Start);
                if (c != 0) return c;
                return b.End.CompareTo(a.End);
            }
        }

        /// <summary>找到包含指定位置（或紧随其后）的标记区间。</summary>
        private static int FindRegion(List<Replacement> regions, int pos)
        {
            for (int k = 0; k < regions.Count; k++)
            {
                Replacement r = regions[k];
                if (pos >= r.Index && pos < r.Index + r.Length) return k;
            }
            // 落在区间之间的空隙（正常情况下不会发生）：挂到后面最近的一个
            for (int k = 0; k < regions.Count; k++)
                if (regions[k].Index >= pos) return k;
            return regions.Count > 0 ? regions.Count - 1 : -1;
        }

        /// <summary>区间扩大后，若与后面的区间产生重叠则合并。</summary>
        private static void MergeRegionsAfter(List<Replacement> regions, int index)
        {
            int k = index + 1;
            while (k < regions.Count)
            {
                Replacement prev = regions[index];
                Replacement next = regions[k];
                if (next.Index >= prev.Index + prev.Length) break;
                int end = next.Index + next.Length;
                if (end > prev.Index + prev.Length) prev.Length = end - prev.Index;
                foreach (int ci in next.CategoryIndexes)
                    if (!prev.CategoryIndexes.Contains(ci)) prev.CategoryIndexes.Add(ci);
                regions.RemoveAt(k);
            }
        }

        /// <summary>关键词在全文里的出现次数（按子串计，包含被更长关键词覆盖的那些）。</summary>
        private static int CountOccurrences(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return 0;
            int count = 0;
            int from = 0;
            while (true)
            {
                int at = text.IndexOf(keyword, from, StringComparison.Ordinal);
                if (at < 0) break;
                count++;
                from = at + keyword.Length;
            }
            return count;
        }

        /// <summary>
        /// 从 pos 开始沿匹配树前进，返回最长命中在原文里的长度（0 = 不命中）。
        /// 空白处理：树里的单个空格可对应原文里任意长度的一段空白；关键词本身不含首尾空白，
        /// 因此「空格 + 关键词」不会误命中，只有关键词内部的空白才会吃掉原文空白。
        /// </summary>
        private int LongestMatchAt(Node root, string text, int pos)
        {
            Node node = root;
            int best = 0;
            int i = pos;
            int n = text.Length;
            int limit = pos;                     // 当前已匹配部分在原文里的结束位置

            while (i < n)
            {
                char c = text[i];
                if (c == '\r' || c == '\n') break;              // 不跨段落匹配

                if (IsSpace(c))
                {
                    int j = i;
                    while (j < n && IsSpace(text[j])) j++;      // 跳过整段空白
                    Node nextSpace;
                    if (!node.Next.TryGetValue(' ', out nextSpace)) break;
                    node = nextSpace;
                    limit = j;
                    if (node.Keyword != null) best = limit - pos;
                    i = j;
                    continue;
                }

                Node next;
                char key = _ignoreCase ? char.ToLowerInvariant(c) : c;
                if (!node.Next.TryGetValue(key, out next)) break;
                node = next;
                i++;
                limit = i;
                if (node.Keyword != null) best = i - pos;
            }
            return best;
        }

        /// <summary>构造「〔标记〕原词」；该片段没有任何标记时返回原词。</summary>
        private string BuildReplacement(List<int> categoryIndexes, string original)
        {
            List<string> markers = new List<string>();
            foreach (int ci in categoryIndexes)
            {
                string m = _markers[ci];
                if (string.IsNullOrEmpty(m)) continue;
                if (!markers.Contains(m)) markers.Add(m);
            }
            if (markers.Count == 0) return original;
            return "〔" + string.Join("、", markers.ToArray()) + "〕" + original;
        }
    }
}
