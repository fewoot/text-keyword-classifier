using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WordClassify
{
    /// <summary>界面设置。存到程序目录下的 config.json，下次启动自动恢复。</summary>
    public class Settings
    {
        public string SourceDir = "";
        public string OutputDir = "";
        public List<Category> Categories = new List<Category>();
        public bool Recurse = true;
        public bool IncludeTxt = true;
        public bool IncludeDocx = true;
        public bool InsertMarkers = true;
        /// <summary>每个类别只标记第一次出现，其余只计数。</summary>
        public bool MarkFirstOccurrenceOnly = true;
        /// <summary>无任何命中的文件不生成新文件。</summary>
        public bool SkipNoMatch = true;
        public bool IgnoreCase;
        public string MarkerPrefix = "〔";
        public string MarkerSuffix = "〕";
        /// <summary>0=按类别列表顺序，1=按命中次数从多到少，2=按文中首次出现先后。</summary>
        public int NameOrder;
        /// <summary>文件名里「N次」的计数口径，对应 CountMode 枚举：0=命中片段数，1=关键词出现次数，2=命中的不同关键词个数。</summary>
        public int CountModeIndex;

        public static string ConfigPath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(dir, "config.json");
            }
        }

        public static Settings CreateDefault()
        {
            Settings s = new Settings();
            s.Categories.Add(new Category("爱情", "123", "我爱你", "爱你"));
            s.Categories.Add(new Category("颜色", "234", "红色", "绿色", "蓝色"));
            return s;
        }

        public RunOptions ToRunOptions(bool dryRun)
        {
            RunOptions o = new RunOptions();
            o.Recurse = Recurse;
            o.IncludeTxt = IncludeTxt;
            o.IncludeDocx = IncludeDocx;
            o.InsertMarkers = InsertMarkers;
            o.MarkFirstOccurrenceOnly = MarkFirstOccurrenceOnly;
            o.SkipNoMatch = SkipNoMatch;
            o.DryRun = dryRun;
            o.NameOrder = NameOrder;
            if (CountModeIndex < 0 || CountModeIndex > 2) CountModeIndex = 0;
            o.CountMode = (CountMode)CountModeIndex;
            return o;
        }

        public Settings Clone()
        {
            Settings s = new Settings();
            s.SourceDir = SourceDir;
            s.OutputDir = OutputDir;
            s.Recurse = Recurse;
            s.IncludeTxt = IncludeTxt;
            s.IncludeDocx = IncludeDocx;
            s.InsertMarkers = InsertMarkers;
            s.MarkFirstOccurrenceOnly = MarkFirstOccurrenceOnly;
            s.SkipNoMatch = SkipNoMatch;
            s.IgnoreCase = IgnoreCase;
            s.MarkerPrefix = MarkerPrefix;
            s.MarkerSuffix = MarkerSuffix;
            s.NameOrder = NameOrder;
            s.CountModeIndex = CountModeIndex;
            s.Categories = new List<Category>();
            foreach (Category c in Categories) s.Categories.Add(c.Clone());
            return s;
        }

        public string ToJson()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 32 * 1024 * 1024;
            return serializer.Serialize(Clone());
        }

        public static Settings FromJson(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 32 * 1024 * 1024;
            Settings s = serializer.Deserialize<Settings>(json);
            if (s == null) s = CreateDefault();
            s.Repair();
            return s;
        }

        /// <summary>反序列化后的兜底修正。</summary>
        private void Repair()
        {
            if (Categories == null) Categories = new List<Category>();
            foreach (Category c in Categories)
            {
                if (c.Name == null) c.Name = "";
                if (c.Marker == null) c.Marker = "";
                if (c.Keywords == null) c.Keywords = new List<string>();
                // 去掉空关键词
                List<string> clean = new List<string>();
                foreach (string k in c.Keywords)
                    if (!string.IsNullOrEmpty(k)) clean.Add(k);
                c.Keywords = clean;
            }
            if (SourceDir == null) SourceDir = "";
            if (OutputDir == null) OutputDir = "";
            if (MarkerPrefix == null) MarkerPrefix = "〔";
            if (MarkerSuffix == null) MarkerSuffix = "〕";
            if (NameOrder < 0 || NameOrder > 2) NameOrder = 0;
            if (CountModeIndex < 0 || CountModeIndex > 2) CountModeIndex = 0;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath, ToJson(), new UTF8Encoding(true));
            }
            catch (Exception)
            {
                // 配置保存失败不影响主流程
            }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return FromJson(File.ReadAllText(ConfigPath, Encoding.UTF8));
            }
            catch (Exception)
            {
                // 配置损坏时回退到默认，避免启动失败
            }
            return CreateDefault();
        }
    }
}
