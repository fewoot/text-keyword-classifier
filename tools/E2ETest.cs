using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using WordClassify;

/// <summary>
/// 端到端测试：真的建文件、真的跑处理、真的核对生成结果。
/// 用法： E2ETest.exe &lt;临时目录&gt;
/// 会自己建临时目录并在结束时删掉，不动任何已有文件。
/// </summary>
internal static class E2ETest
{
    private static int _pass;
    private static int _fail;

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (Exception) { }

        string root = args != null && args.Length > 0
            ? args[0]
            : Path.Combine(Path.GetTempPath(), "wct_e2e_" + Guid.NewGuid().ToString("N"));

        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        try
        {
            Console.WriteLine("=== 端到端测试 ===");
            Console.WriteLine("工作目录：" + root);
            Console.WriteLine();

            string src = Path.Combine(root, "源文件夹");
            string outDir = Path.Combine(root, "输出文件夹");
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(Path.Combine(src, "子目录A"));

            // 1) UTF-8 无 BOM，含用户例子 + 嵌套关键词
            File.WriteAllText(Path.Combine(src, "示例文档A.txt"),
                "我爱你，爱你，红色，绿色，蓝色，哈哈哈", new UTF8Encoding(false));

            // 2) GBK 编码（中文 Windows 记事本默认）
            Encoding gbk = Encoding.GetEncoding(936);
            File.WriteAllBytes(Path.Combine(src, "GBK文档.txt"),
                gbk.GetBytes("今天天气不错，天空是蓝色的，草地是绿色的。"));

            // 3) UTF-8 带 BOM
            File.WriteAllText(Path.Combine(src, "带BOM文档.txt"),
                "红色的苹果，深红色的玫瑰。", new UTF8Encoding(true));

            // 4) 无命中的文件
            File.WriteAllText(Path.Combine(src, "无命中.txt"),
                "这里没有任何需要分类的词。", new UTF8Encoding(false));

            // 5) 老版 .doc（应当失败并给出提示）
            File.WriteAllBytes(Path.Combine(src, "老格式.doc"), new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3 });

            // 6) 子目录里的文件
            File.WriteAllText(Path.Combine(src, "子目录A", "嵌套.txt"),
                "我爱你，我爱你", new UTF8Encoding(false));

            // 7) 真正的 .docx
            CreateDocx(Path.Combine(src, "Word文档.docx"),
                new string[] { "我爱你，爱你，红色，绿色，蓝色，哈哈哈", "第二段：深红色也很醒目" });

            // 8) 输出目录里预先放一个同名文件，测试重名处理
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "爱情2次-颜色3次-示例文档A.txt"), "占位", new UTF8Encoding(false));

            Settings settings = new Settings();
            settings.Categories.Add(new Category("爱情", "123", "我爱你", "爱你"));
            settings.Categories.Add(new Category("颜色", "234", "红色", "绿色", "蓝色", "深红色"));
            settings.MarkFirstOccurrenceOnly = true;
            settings.Recurse = true;
            settings.SkipNoMatch = true;
            settings.IncludeTxt = true;
            settings.IncludeDocx = true;

            RunOptions options = settings.ToRunOptions(false);
            Processor processor = new Processor(settings.Categories, settings);
            List<FileResult> results = new List<FileResult>();
            RunSummary summary = processor.RunBatch(src, outDir, options, null,
                delegate (FileResult r) { results.Add(r); Print(r); });

            Console.WriteLine();
            Console.WriteLine("--- 核对 ---");
            Check("扫描到 6 个文件（.doc 不属于 .txt/.docx，被忽略）", summary.Total == 6,
                "实际 " + summary.Total);
            Check("成功 " + summary.Succeeded + " 个（5 个有命中）", summary.Succeeded == 5, "实际 " + summary.Succeeded);
            Check("跳过 " + summary.Skipped + " 个（1 个无命中）", summary.Skipped == 1, "实际 " + summary.Skipped);
            Check("失败 " + summary.Failed + " 个（0 个）", summary.Failed == 0, "实际 " + summary.Failed);

            // 用户例子的文件
            FileResult a = Find(results, "示例文档A.txt");
            Check("示例文档A 新名 = 爱情2次-颜色3次-示例文档A.txt",
                a != null && a.OutputName == "爱情2次-颜色3次-示例文档A.txt",
                a == null ? "未处理" : a.OutputName);
            Check("示例文档A 重名时自动加 (1)",
                a != null && a.OutputPath.EndsWith("爱情2次-颜色3次-示例文档A(1).txt", StringComparison.Ordinal),
                a == null ? "未处理" : Path.GetFileName(a.OutputPath));

            string aText = ReadAll(a);
            Check("示例文档A 正文 = 〔123〕我爱你，爱你，〔234〕红色，绿色，蓝色，哈哈哈",
                aText == "〔123〕我爱你，爱你，〔234〕红色，绿色，蓝色，哈哈哈", aText);

            // GBK 文件
            FileResult gbkResult = Find(results, "GBK文档.txt");
            Check("GBK 文件编码识别为 GBK", gbkResult != null && gbkResult.EncodingName == "GBK",
                gbkResult == null ? "未处理" : gbkResult.EncodingName);
            Check("GBK 文件命中 颜色 = 2 次（蓝色、绿色）",
                gbkResult != null && gbkResult.HitText == "颜色 2",
                gbkResult == null ? "未处理" : gbkResult.HitText);
            string gbkText = ReadAll(gbkResult);
            Check("GBK 文件正文只标第一次：天空是〔234〕蓝色的，草地是绿色的。",
                gbkText == "今天天气不错，天空是〔234〕蓝色的，草地是绿色的。", gbkText);

            // 带 BOM 文件：深红色 与 红色 同类别 → 只算 1 次（最长优先）
            FileResult bom = Find(results, "带BOM文档.txt");
            Check("带BOM 文件「红色/深红色」同类别最长优先 → 颜色 2 次",
                bom != null && bom.HitText == "颜色 2", bom == null ? "未处理" : bom.HitText);
            string bomText = ReadAll(bom);
            Check("带BOM 文件正文（只标第一次）= 〔234〕红色的苹果，深红色的玫瑰。",
                bomText == "〔234〕红色的苹果，深红色的玫瑰。", bomText);

            // 子目录
            FileResult nested = Find(results, "嵌套.txt");
            Check("子目录文件输出到输出的同名子目录",
                nested != null && nested.OutputPath.IndexOf(Path.Combine("输出文件夹", "子目录A"), StringComparison.OrdinalIgnoreCase) >= 0,
                nested == null ? "未处理" : nested.OutputPath);
            Check("子目录文件新名 = 爱情2次-嵌套.txt",
                nested != null && nested.OutputName == "爱情2次-嵌套.txt",
                nested == null ? "未处理" : nested.OutputName);
            Check("子目录文件「我爱你，我爱你」标记只标第一次 = 〔123〕我爱你，我爱你",
                ReadAll(nested) == "〔123〕我爱你，我爱你", ReadAll(nested));

            // docx：颜色类命中 红色/绿色/蓝色/深红色 = 4 个独立片段
            FileResult docx = Find(results, "Word文档.docx");
            Check("docx 命中 爱情 2 次、颜色 4 次",
                docx != null && docx.HitText == "爱情 2、颜色 4",
                docx == null ? "未处理" : docx.HitText);
            Check("docx 新名 = 爱情2次-颜色4次-Word文档.docx",
                docx != null && docx.OutputName == "爱情2次-颜色4次-Word文档.docx",
                docx == null ? "未处理" : docx.OutputName);
            string docxTxt = Path.Combine(outDir, "爱情2次-颜色4次-Word文档.txt");
            Check("docx 同时生成带标记的 txt 副本，Word 原件也保留",
                File.Exists(docxTxt) && docx != null && File.Exists(docx.OutputPath), docxTxt);
            if (File.Exists(docxTxt))
            {
                string t = File.ReadAllText(docxTxt, Encoding.UTF8).Replace("\r\n", "\n").TrimEnd('\n');
                Check("docx 的 txt 副本内容正确（每类只标第一次）",
                    t == "〔123〕我爱你，爱你，〔234〕红色，绿色，蓝色，哈哈哈\n第二段：深红色也很醒目", t);
            }

            // 无命中
            FileResult none = Find(results, "无命中.txt");
            Check("无命中文件被跳过且不生成文件",
                none != null && none.Status == FileStatus.Skipped
                && !File.Exists(Path.Combine(outDir, "无命中.txt")),
                none == null ? "未处理" : none.Status + "/" + none.Message);

            // 原文件未被修改
            string originalA = File.ReadAllText(Path.Combine(src, "示例文档A.txt"), Encoding.UTF8);
            Check("原文件保持原样（没有被插入标记）",
                originalA == "我爱你，爱你，红色，绿色，蓝色，哈哈哈", originalA);

            // 老版 .doc：显式指定处理时应当失败并给出可读提示
            Processor p3 = new Processor(settings.Categories, settings);
            FileResult docResult = p3.ProcessFile(Path.Combine(src, "老格式.doc"), src, outDir, settings.ToRunOptions(false));
            Check("老版 .doc 报错并提示另存为 .docx",
                docResult.Status == FileStatus.Failed && docResult.Message.IndexOf(".docx", StringComparison.Ordinal) >= 0,
                docResult.Status + " / " + docResult.Message);

            // 预演模式不写文件
            string dryDir = Path.Combine(root, "预演输出");
            Directory.CreateDirectory(dryDir);
            RunOptions dry = settings.ToRunOptions(true);
            Processor p2 = new Processor(settings.Categories, settings);
            RunSummary drySummary = p2.RunBatch(src, dryDir, dry, null, null);
            Check("预演模式不写任何文件",
                Directory.GetFiles(dryDir, "*", SearchOption.AllDirectories).Length == 0
                && drySummary.Succeeded == 5,
                "输出目录文件数 " + Directory.GetFiles(dryDir, "*", SearchOption.AllDirectories).Length);

            Console.WriteLine();
            Console.WriteLine("通过 " + _pass + " 项，失败 " + _fail + " 项");
            return _fail == 0 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    private static string ReadAll(FileResult r)
    {
        if (r == null || !File.Exists(r.OutputPath)) return "(没有输出文件)";
        string ext = Path.GetExtension(r.OutputPath);
        if (!string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)) return "(非 txt)";
        string text = File.ReadAllText(r.OutputPath, Encoding.UTF8);
        return text.TrimStart('\uFEFF').TrimEnd('\r', '\n');
    }

    private static FileResult Find(List<FileResult> list, string name)
    {
        foreach (FileResult r in list)
            if (string.Equals(r.SourceName, name, StringComparison.Ordinal)) return r;
        return null;
    }

    private static void Print(FileResult r)
    {
        string status = r.Status == FileStatus.Success ? "成功"
            : r.Status == FileStatus.Skipped ? "跳过" : "失败";
        Console.WriteLine("  [" + status + "] " + r.SourceName
            + "  命中=" + r.HitText
            + "  新名=" + (r.OutputName.Length > 0 ? r.OutputName : "—")
            + (r.Message.Length > 0 ? "  (" + r.Message + ")" : ""));
    }

    private static void Check(string title, bool ok, string actual)
    {
        if (ok) { _pass++; Console.WriteLine("  [通过] " + title); }
        else { _fail++; Console.WriteLine("  [失败] " + title + "   → 实际：" + actual); }
    }

    /// <summary>生成一个最小但合法的 .docx（zip 条目用正斜杠）。</summary>
    private static void CreateDocx(string path, string[] paragraphs)
    {
        StringBuilder body = new StringBuilder();
        foreach (string p in paragraphs)
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">").Append(Escape(p)).Append("</w:t></w:r></w:p>");

        string document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + body.ToString()
            + "</w:body></w:document>";

        string contentTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
            + "</Types>";

        string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
            + "</Relationships>";

        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(zip, "[Content_Types].xml", contentTypes);
            AddEntry(zip, "_rels/.rels", rels);
            AddEntry(zip, "word/document.xml", document);
        }
    }

    private static void AddEntry(ZipArchive zip, string name, string text)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using (Stream s = entry.Open())
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            s.Write(bytes, 0, bytes.Length);
        }
    }

    private static string Escape(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
