using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WordClassify
{
    /// <summary>批量处理中的轻量进度回调。</summary>
    public delegate void ProgressReporter(ProcessProgress progress);

    /// <summary>单个文件处理完成的回调（用于界面明细表与日志）。</summary>
    public delegate void ResultReporter(FileResult result);

    /// <summary>一个文件的处理结果 + 需要写出的正文。</summary>
    public class PreparedFile
    {
        public FileResult Result;
        public string NewText;
    }

    /// <summary>
    /// 处理流水线：
    ///   读取文本 → 匹配计数 → 计算新文件名 → 生成带标记的副本 → 按「类名N次-」重命名写到输出文件夹。
    /// 原文件永不修改；重名自动加 (1)(2)…；「无命中」的文件默认跳过（选项控制，但界面要求至少一个类别命中才有意义）。
    /// </summary>
    public class Processor
    {
        private readonly List<Category> _categories;
        private readonly Settings _settings;
        private readonly KeywordMatcher _matcher;
        private readonly ProcessProgress _progress = new ProcessProgress();
        private readonly HashSet<string> _usedOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile bool _cancel;
        private volatile bool _pause;
        private readonly object _pauseLock = new object();

        public Processor(List<Category> categories, Settings settings)
        {
            _categories = categories;
            _settings = settings;
            _matcher = new KeywordMatcher(categories, settings.IgnoreCase);
        }

        public ProcessProgress LiveProgress { get { return _progress; } }

        public void Cancel() { _cancel = true; }

        public void Pause() { _pause = true; }

        public void Resume()
        {
            lock (_pauseLock)
            {
                _pause = false;
                System.Threading.Monitor.PulseAll(_pauseLock);
            }
        }

        /// <summary>找出类别里第一个配置了关键词的索引，用于给「无命中」加提示。</summary>
        private int FirstUsableCategory()
        {
            for (int i = 0; i < _categories.Count; i++)
                if (_matcher.HasKeywords(i)) return i;
            return -1;
        }

        /// <summary>枚举待处理文件（.txt / .docx，按名称排序）。</summary>
        public List<string> EnumerateFiles(string sourceDir, RunOptions options)
        {
            List<string> files = new List<string>();
            SearchOption scope = options.Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string[] all = Directory.GetFiles(sourceDir, "*", scope);
            foreach (string f in all)
            {
                if (TextReaders.IsTextFile(f) && options.IncludeTxt) { files.Add(f); continue; }
                if (TextReaders.IsDocxFile(f) && options.IncludeDocx) { files.Add(f); continue; }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>批量处理。</summary>
        public RunSummary RunBatch(string sourceDir, string outputDir, RunOptions options,
                                   ProgressReporter onProgress, ResultReporter onResult)
        {
            RunSummary summary = new RunSummary();
            Stopwatch watch = Stopwatch.StartNew();
            _progress.StartedAt = DateTime.Now;
            _progress.Total = 0;
            _progress.Scanned = 0;
            _progress.Succeeded = 0;
            _progress.Skipped = 0;
            _progress.Failed = 0;
            _progress.CharCount = 0;
            _progress.CurrentFile = "";
            _usedOutputPaths.Clear();

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException("源文件夹不存在：" + sourceDir);
            if (!options.DryRun && string.IsNullOrEmpty(outputDir))
                throw new DirectoryNotFoundException("请先选择输出文件夹");

            summary.CategoryFileCount = new int[_categories.Count];
            summary.CategoryHitCount = new int[_categories.Count];

            List<string> files = EnumerateFiles(sourceDir, options);
            summary.Total = files.Count;
            _progress.Total = files.Count;
            if (onProgress != null) onProgress(_progress);

            for (int i = 0; i < files.Count; i++)
            {
                WaitIfPaused();
                if (_cancel) break;

                string path = files[i];
                _progress.CurrentFile = Path.GetFileName(path);
                if (onProgress != null) onProgress(_progress);

                FileResult result;
                try
                {
                    result = ProcessFile(path, sourceDir, outputDir, options);
                }
                catch (Exception ex)
                {
                    result = new FileResult();
                    result.SourcePath = path;
                    result.SourceName = Path.GetFileName(path);
                    result.RelativeDir = RelativeDirOf(sourceDir, path);
                    result.Status = FileStatus.Failed;
                    result.Message = ex.GetType().Name + "：" + ex.Message;
                }

                summary.Scanned++;
                switch (result.Status)
                {
                    case FileStatus.Success: summary.Succeeded++; _progress.Succeeded++; break;
                    case FileStatus.Failed: summary.Failed++; _progress.Failed++; break;
                    default: summary.Skipped++; _progress.Skipped++; break;
                }
                _progress.CharCount += result.CharCount;

                if (result.Hits != null)
                {
                    foreach (CategoryHit hit in result.Hits)
                    {
                        if (hit.CategoryIndex < 0 || hit.CategoryIndex >= _categories.Count) continue;
                        summary.CategoryFileCount[hit.CategoryIndex]++;
                        summary.CategoryHitCount[hit.CategoryIndex] += hit.Count;
                    }
                }

                if (onProgress != null) onProgress(_progress);
                if (onResult != null) onResult(result);
            }

            watch.Stop();
            summary.ElapsedMs = watch.ElapsedMilliseconds;
            _progress.CurrentFile = "";
            if (onProgress != null) onProgress(_progress);
            return summary;
        }

        private void WaitIfPaused()
        {
            while (_pause && !_cancel)
            {
                lock (_pauseLock) { System.Threading.Monitor.Wait(_pauseLock, 150); }
            }
        }

        /// <summary>
        /// 处理单个文件：匹配、计算新名、按需写出（DryRun 时不落盘）。
        /// Windows 下接口保持同步，便于自动化测试直接调用。
        /// </summary>
        public FileResult ProcessFile(string path, string sourceDir, string outputDir, RunOptions options)
        {
            PreparedFile prepared = Prepare(path, sourceDir, options);
            FileResult result = prepared.Result;

            if (result.Status == FileStatus.Skipped || result.Status == FileStatus.Failed)
                return result;

            if (options.DryRun)
            {
                result.Message = "预演（未写文件）";
                return result;
            }

            string targetDir = ResolveTargetDir(outputDir, result.RelativeDir, options);
            result.OutputPath = MakeUniqueOutputPath(targetDir, result.OutputName);

            Stopwatch writeWatch = Stopwatch.StartNew();
            if (TextReaders.IsDocxFile(path))
            {
                // Word 文档：原样复制一份（保留可继续编辑的 Word 文件），正文带标记的版本另存为同名 .txt
                string txtName = Path.ChangeExtension(result.OutputName, ".txt");
                string txtPath = MakeUniqueOutputPath(targetDir, txtName);
                File.WriteAllText(txtPath, prepared.NewText, new UTF8Encoding(true));
                File.Copy(path, result.OutputPath, true);
                result.Message = "已生成（Word 原件 + 标记版 txt）";
            }
            else
            {
                File.WriteAllText(result.OutputPath, prepared.NewText, new UTF8Encoding(true));
                result.Message = "已生成";
            }
            writeWatch.Stop();

            result.ElapsedMs += writeWatch.ElapsedMilliseconds;
            return result;
        }

        /// <summary>只做读取与计算，不写任何文件。供预演与测试使用。</summary>
        public PreparedFile Prepare(string path, string sourceDir, RunOptions options)
        {
            PreparedFile prepared = new PreparedFile();
            FileResult result = new FileResult();
            prepared.Result = result;
            Stopwatch watch = Stopwatch.StartNew();

            result.SourcePath = path;
            result.SourceName = Path.GetFileName(path);
            result.RelativeDir = RelativeDirOf(sourceDir, path);

            try
            {
                TextContent content = TextReaders.Read(path);
                MatchResult match = _matcher.Match(content.Text);

                result.CharCount = content.Text.Length;
                result.EncodingName = content.EncodingName;
                result.Hits = match.Hits;

                if (match.Hits.Count == 0)
                {
                    result.Status = FileStatus.Skipped;
                    int first = FirstUsableCategory();
                    result.Message = first < 0 ? "没有配置有效关键词" : "无命中";
                    return prepared;
                }

                result.OutputName = BuildOutputName(result.SourceName, match, options);
                prepared.NewText = ApplyReplacements(content.Text, BuildInsertions(match, content.Text));
                result.Status = FileStatus.Success;
                result.Message = "待写出";
                return prepared;
            }
            catch (OldDocFormatException ex)
            {
                result.Status = FileStatus.Failed;
                result.Message = ex.Message;
                return prepared;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Status = FileStatus.Failed;
                result.Message = "拒绝访问：" + ex.Message;
                return prepared;
            }
            catch (FileNotFoundException)
            {
                result.Status = FileStatus.Failed;
                result.Message = "文件不存在（可能已被移动或删除）";
                return prepared;
            }
            catch (IOException ex)
            {
                result.Status = FileStatus.Failed;
                result.Message = "读写失败（文件可能正被 Word 占用）：" + ex.Message;
                return prepared;
            }
            catch (Exception ex)
            {
                result.Status = FileStatus.Failed;
                result.Message = ex.GetType().Name + "：" + ex.Message;
                return prepared;
            }
            finally
            {
                watch.Stop();
                result.ElapsedMs = watch.ElapsedMilliseconds;
            }
        }

        /// <summary>计算正文插入方案；「每类只标第一次」的过滤也在这里。</summary>
        private List<Replacement> BuildInsertions(MatchResult match, string text)
        {
            List<Replacement> ordered = new List<Replacement>(match.Replacements);
            ordered.Sort(delegate (Replacement a, Replacement b) { return a.Index.CompareTo(b.Index); });

            HashSet<int> markedCategories = new HashSet<int>();
            List<Replacement> output = new List<Replacement>();

            foreach (Replacement r in ordered)
            {
                List<string> markers = new List<string>();
                bool anyNewForFirstMode = false;
                foreach (int ci in r.CategoryIndexes)
                {
                    if (ci < 0 || ci >= _categories.Count) continue;
                    if (_settings.MarkFirstOccurrenceOnly)
                    {
                        if (markedCategories.Contains(ci)) continue;   // 该类已标记过，这次只计数不标记
                        anyNewForFirstMode = true;
                    }
                    string m = _categories[ci].Marker;
                    if (string.IsNullOrEmpty(m)) continue;
                    if (!markers.Contains(m)) markers.Add(m);
                }
                if (_settings.MarkFirstOccurrenceOnly && anyNewForFirstMode)
                {
                    foreach (int ci in r.CategoryIndexes)
                        if (ci >= 0 && ci < _categories.Count) markedCategories.Add(ci);
                }

                Replacement nr = new Replacement();
                nr.Index = r.Index;
                nr.Length = r.Length;
                nr.CategoryIndexes = r.CategoryIndexes;
                string originalPiece = text.Substring(r.Index, r.Length);
                nr.Text = markers.Count == 0
                    ? originalPiece
                    : _settings.MarkerPrefix + string.Join("、", markers.ToArray()) + _settings.MarkerSuffix + originalPiece;
                output.Add(nr);
            }

            // 从后往前应用，避免下标错位
            output.Sort(delegate (Replacement a, Replacement b) { return b.Index.CompareTo(a.Index); });
            return output;
        }

        private static string ApplyReplacements(string text, List<Replacement> replacements)
        {
            if (replacements == null || replacements.Count == 0) return text;
            StringBuilder sb = new StringBuilder(text.Length + replacements.Count * 8);
            int cursor = 0;
            List<Replacement> asc = new List<Replacement>(replacements);
            asc.Sort(delegate (Replacement a, Replacement b) { return a.Index.CompareTo(b.Index); });
            foreach (Replacement r in asc)
            {
                if (r.Index < cursor) continue;                 // 重叠保护
                if (r.Index > text.Length) break;
                sb.Append(text, cursor, r.Index - cursor);
                sb.Append(r.Text);
                cursor = r.Index + r.Length;
            }
            if (cursor < text.Length) sb.Append(text, cursor, text.Length - cursor);
            return sb.ToString();
        }

        /// <summary>新文件名：类别名N次-类别名N次-原名称。N 的口径由 options.CountMode 决定。</summary>
        public static string BuildOutputName(string sourceName, MatchResult match, RunOptions options)
        {
            List<CategoryHit> hits = new List<CategoryHit>(match.Hits);
            switch (options.NameOrder)
            {
                case 1:
                    hits.Sort(delegate (CategoryHit a, CategoryHit b)
                    {
                        int c = b.CountBy(options.CountMode).CompareTo(a.CountBy(options.CountMode));
                        return c != 0 ? c : a.CategoryIndex.CompareTo(b.CategoryIndex);
                    });
                    break;
                case 2:
                    hits.Sort(delegate (CategoryHit a, CategoryHit b)
                    {
                        int c = a.FirstPos.CompareTo(b.FirstPos);
                        return c != 0 ? c : a.CategoryIndex.CompareTo(b.CategoryIndex);
                    });
                    break;
                default:
                    hits.Sort(delegate (CategoryHit a, CategoryHit b) { return a.CategoryIndex.CompareTo(b.CategoryIndex); });
                    break;
            }

            List<string> parts = new List<string>();
            foreach (CategoryHit h in hits)
            {
                int count = h.CountBy(options.CountMode);
                if (count <= 0) continue;
                string name = Sanitize(h.CategoryName);
                if (name.Length == 0) name = "类别" + (h.CategoryIndex + 1);
                parts.Add(name + count + "次");
            }
            if (parts.Count == 0) return sourceName;
            return string.Join("-", parts.ToArray()) + "-" + sourceName;
        }

        /// <summary>去掉文件名里不允许的字符。</summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool ok = true;
                for (int i = 0; i < bad.Length; i++) { if (c == bad[i]) { ok = false; break; } }
                sb.Append(ok ? c : '_');
            }
            string s = sb.ToString().Trim();
            while (s.EndsWith(".", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1).TrimEnd();
            return s;
        }

        private string ResolveTargetDir(string outputDir, string relativeDir, RunOptions options)
        {
            string target = outputDir;
            if (options.Recurse && !string.IsNullOrEmpty(relativeDir))
                target = Path.Combine(outputDir, relativeDir);
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);
            return target;
        }

        private string MakeUniqueOutputPath(string targetDir, string fileName)
        {
            string candidate = Path.Combine(targetDir, fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int n = 0;
            while (File.Exists(candidate) || _usedOutputPaths.Contains(candidate))
            {
                n++;
                candidate = Path.Combine(targetDir, baseName + "(" + n + ")" + ext);
                if (n > 9999) break;
            }
            _usedOutputPaths.Add(candidate);
            return candidate;
        }

        private static string RelativeDirOf(string sourceDir, string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                string root = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrEmpty(dir)) return "";
                if (dir.Length <= root.Length) return "";
                return dir.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return "";
            }
        }

    }
}
