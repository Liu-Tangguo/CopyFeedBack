using System;
using System.Text;
using System.Windows.Forms;

namespace CopyFeedback
{
    internal enum CopyKind
    {
        Text,
        Image,
        Files,
        Other
    }

    /// <summary>一次有效复制的内容摘要（不含完整内容，只保留用于展示的短预览）。</summary>
    internal sealed class CopyInfo
    {
        public CopyKind Kind;
        public string PreviewText;  // 已归一化、供弹窗显示的短预览
        public int RawChars;        // 原始文本长度（仅用于日志元数据）
        public int ShownChars;      // 预览字符数（仅用于日志元数据）
        public bool Truncated;      // 预览是否被长度上限截断
    }

    internal sealed class ClipboardReadResult
    {
        public bool ReadOk;      // 是否成功访问并解析剪贴板
        public bool HasContent;  // 剪贴板中是否存在至少一种数据格式
        public CopyInfo Info;
        public string Error;
    }

    /// <summary>
    /// 读取当前剪贴板内容。只读取当下这次复制的内容本身，不做任何历史记录、不写剪贴板、不上传。
    /// 对非文本内容只判断类型，不做昂贵的数据转换（避免卡顿）。
    /// </summary>
    internal static class ClipboardReader
    {
        private const int PreviewMaxChars = 200;
        private const int NormalizeScanChars = 1000;

        public static ClipboardReadResult TryRead()
        {
            ClipboardReadResult result = new ClipboardReadResult();
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null)
                {
                    result.Error = "剪贴板被其它程序占用";
                    return result;
                }

                string[] formats;
                try
                {
                    formats = data.GetFormats(false);
                }
                catch (Exception ex)
                {
                    result.Error = ex.GetType().Name;
                    return result;
                }

                result.HasContent = formats != null && formats.Length > 0;
                if (!result.HasContent)
                {
                    result.Error = "剪贴板为空";
                    return result;
                }

                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    result.Info = BuildTextInfo(text);
                }
                else if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    result.Info = BuildTextInfo(text);
                }
                else if (HasImageFormat(data))
                {
                    result.Info = new CopyInfo { Kind = CopyKind.Image, PreviewText = "图片", RawChars = 0, ShownChars = 2 };
                }
                else if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    result.Info = new CopyInfo { Kind = CopyKind.Files, PreviewText = "文件", RawChars = 0, ShownChars = 2 };
                }
                else
                {
                    // 其它格式（富文本、自定义格式等）：只提示“已复制”，不展示内容
                    result.Info = new CopyInfo { Kind = CopyKind.Other, PreviewText = "非文本内容", RawChars = 0, ShownChars = 5 };
                }

                result.ReadOk = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
                return result;
            }
        }

        private static bool HasImageFormat(IDataObject data)
        {
            try
            {
                return data.GetDataPresent(DataFormats.Bitmap)
                    || data.GetDataPresent(DataFormats.Dib)
                    || data.GetDataPresent("PNG")
                    || data.GetDataPresent("image/png")
                    || data.GetDataPresent("image/jpeg")
                    || data.GetDataPresent(DataFormats.Tiff)
                    || data.GetDataPresent("GIF");
            }
            catch
            {
                return false;
            }
        }

        private static CopyInfo BuildTextInfo(string text)
        {
            CopyInfo info = new CopyInfo();
            info.Kind = CopyKind.Text;
            if (text == null) text = string.Empty;
            info.RawChars = text.Length;

            string normalized = Normalize(text);
            if (normalized.Length > PreviewMaxChars)
            {
                normalized = normalized.Substring(0, PreviewMaxChars);
                info.Truncated = true;
            }
            if (normalized.Length == 0)
            {
                normalized = "（空白文本）";
            }
            info.PreviewText = normalized;
            info.ShownChars = normalized.Length;
            return info;
        }

        /// <summary>
        /// 把换行、制表符、连续空白折叠为单个空格并修剪首尾。
        /// 先跳过开头的大段空白/控制字符，再最多扫描 1000 个字符，
        /// 避免“正文前面有一大段空白”的长文本被误判为空白文本。
        /// </summary>
        private static string Normalize(string text)
        {
            int start = 0;
            while (start < text.Length && IsBlank(text[start])) start++;
            if (start >= text.Length) return string.Empty;

            int limit = Math.Min(text.Length, start + NormalizeScanChars);
            StringBuilder sb = new StringBuilder(Math.Min(limit - start, NormalizeScanChars));
            bool lastWasSpace = false;
            for (int i = start; i < limit; i++)
            {
                char c = text[i];
                if (IsBlank(c))
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private static bool IsBlank(char c)
        {
            return char.IsWhiteSpace(c) || char.IsControl(c);
        }
    }
}
