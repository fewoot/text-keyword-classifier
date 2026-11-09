using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace WordClassify
{
    /// <summary>读取到的文本内容。</summary>
    public class TextContent
    {
        public string Text = "";
        public string EncodingName = "";
        public bool HasBom;
    }

    /// <summary>老版 .doc（97-2003 二进制格式）无法直接解析。</summary>
    public class OldDocFormatException : Exception
    {
        public OldDocFormatException(string message) : base(message) { }
    }

    /// <summary>纯文本与 Word(.docx) 读取。docx 直接按 zip 解析正文，不依赖 Office。</summary>
    public static class TextReaders
    {
        private static Encoding _gbk;

        private static Encoding Gbk
        {
            get
            {
                if (_gbk == null)
                {
                    try { _gbk = Encoding.GetEncoding(936); }
                    catch (Exception) { _gbk = Encoding.Default; }
                }
                return _gbk;
            }
        }

        public static bool IsTextFile(string path)
        {
            string ext = Path.GetExtension(path);
            return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDocxFile(string path)
        {
            string ext = Path.GetExtension(path);
            return string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOldDocFile(string path)
        {
            string ext = Path.GetExtension(path);
            return string.Equals(ext, ".doc", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSupported(string path)
        {
            return IsTextFile(path) || IsDocxFile(path);
        }

        public static TextContent Read(string path)
        {
            if (IsDocxFile(path)) return ReadDocx(path);
            if (IsTextFile(path)) return ReadText(path);
            if (IsOldDocFile(path))
                throw new OldDocFormatException("老版 .doc 格式无法直接读取，请先用 Word/WPS 另存为 .docx");
            throw new NotSupportedException("不支持的文件类型：" + Path.GetExtension(path));
        }

        /// <summary>读取纯文本：按 BOM 判断，其次严格试 UTF-8，最后退回 GBK（简体中文 Windows 默认编码）。</summary>
        public static TextContent ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            TextContent content = new TextContent();

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                content.Text = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
                content.EncodingName = "UTF-8 (BOM)";
                content.HasBom = true;
                return content;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                content.Text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                content.EncodingName = "UTF-16 LE";
                content.HasBom = true;
                return content;
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                content.Text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
                content.EncodingName = "UTF-16 BE";
                content.HasBom = true;
                return content;
            }

            if (LooksLikeUtf8(bytes))
            {
                content.Text = new UTF8Encoding(false).GetString(bytes);
                content.EncodingName = "UTF-8";
                return content;
            }

            content.Text = Gbk.GetString(bytes);
            content.EncodingName = "GBK";
            return content;
        }

        /// <summary>严格校验：整段字节流能否解码为合法 UTF-8（非法序列会被替换成 U+FFFD）。</summary>
        private static bool LooksLikeUtf8(byte[] bytes)
        {
            try
            {
                string s = new UTF8Encoding(false, true).GetString(bytes);
                return s.IndexOf('\uFFFD') < 0;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// 读取 .docx 正文。zip 条目名可能用反斜杠（某些打包工具），统一归一化后再找 word/document.xml。
        /// </summary>
        public static TextContent ReadDocx(string path)
        {
            TextContent content = new TextContent();
            content.EncodingName = "docx";

            StringBuilder sb = new StringBuilder();
            using (ZipArchive zip = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry entry = null;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/').TrimStart('/');
                    if (string.Equals(name, "word/document.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        entry = e;
                        break;
                    }
                }
                if (entry == null)
                    throw new InvalidDataException("这个 .docx 里找不到 word/document.xml，可能不是有效的 Word 文档");

                using (Stream s = entry.Open())
                    ExtractBodyText(s, sb);
            }
            content.Text = sb.ToString();
            return content;
        }

        /// <summary>从 document.xml 里抽正文：段落用换行分隔，w:tab→制表符，w:br/w:cr→换行。</summary>
        private static void ExtractBodyText(Stream stream, StringBuilder sb)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.DtdProcessing = DtdProcessing.Prohibit;
            settings.XmlResolver = null;
            settings.IgnoreComments = true;

            bool inParagraph = false;
            StringBuilder para = new StringBuilder();

            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.LocalName == "p" && reader.NamespaceURI != null && reader.NamespaceURI.IndexOf("wordprocessingml", StringComparison.Ordinal) >= 0)
                        {
                            inParagraph = true;
                            para.Length = 0;
                            if (reader.IsEmptyElement)
                            {
                                sb.Append('\n');
                                inParagraph = false;
                            }
                        }
                        else if (reader.LocalName == "tab" || reader.LocalName == "ptab")
                        {
                            if (inParagraph) para.Append('\t');
                        }
                        else if (reader.LocalName == "br" || reader.LocalName == "cr")
                        {
                            if (inParagraph) para.Append('\n');
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.SignificantWhitespace)
                    {
                        if (inParagraph) para.Append(reader.Value);
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.LocalName == "p" && inParagraph)
                        {
                            sb.Append(para.ToString()).Append('\n');
                            para.Length = 0;
                            inParagraph = false;
                        }
                    }
                }
            }
            if (inParagraph && para.Length > 0) sb.Append(para.ToString()).Append('\n');
        }
    }
}
