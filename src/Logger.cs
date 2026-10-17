using System;
using System.IO;
using System.Text;
using System.Threading;

namespace CopyFeedback
{
    /// <summary>
    /// 轻量日志。
    ///  - --debug 时写入 %LOCALAPPDATA%\CopyFeedback\debug.log（每次启动覆盖），供排查与自动化测试使用；
    ///  - 异常始终追加写入 %LOCALAPPDATA%\CopyFeedback\error.log；
    ///  - 绝不记录剪贴板内容明文（只记录类型 / 长度 / 是否截断等元数据）。
    /// </summary>
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static StreamWriter _debugWriter;
        private static bool _debugEnabled;
        private static string _errorPath;

        public static void Init(bool debug)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CopyFeedback");
                Directory.CreateDirectory(dir);
                _errorPath = Path.Combine(dir, "error.log");
                _debugEnabled = debug;
                if (debug)
                {
                    string path = Path.Combine(dir, "debug.log");
                    _debugWriter = new StreamWriter(path, false, new UTF8Encoding(false));
                    _debugWriter.AutoFlush = true;
                }
            }
            catch
            {
                // 日志不可用时不能影响主功能
                _debugEnabled = false;
                _debugWriter = null;
            }
        }

        public static bool DebugEnabled
        {
            get { return _debugEnabled; }
        }

        public static void Info(string message)
        {
            WriteInfo(message);
        }

        public static void Debug(string message)
        {
            if (!_debugEnabled) return;
            WriteInfo(message);
        }

        private static void WriteInfo(string message)
        {
            if (!_debugEnabled || _debugWriter == null) return;
            lock (Sync)
            {
                try
                {
                    _debugWriter.WriteLine(Timestamp() + " [" + Thread.CurrentThread.ManagedThreadId + "] " + message);
                }
                catch
                {
                    // 忽略日志写入失败
                }
            }
        }

        public static void Error(string message)
        {
            Error(message, null);
        }

        public static void Error(string message, Exception ex)
        {
            string line = Timestamp() + " [" + Thread.CurrentThread.ManagedThreadId + "] ERROR " + message;
            if (ex != null)
            {
                line += " :: " + ex.GetType().FullName + ": " + ex.Message;
            }
            lock (Sync)
            {
                try
                {
                    if (_debugEnabled && _debugWriter != null)
                    {
                        _debugWriter.WriteLine(line);
                    }
                    AppendError(line);
                }
                catch
                {
                    // 忽略日志写入失败
                }
            }
        }

        private static void AppendError(string line)
        {
            if (_errorPath == null) return;
            try
            {
                FileInfo fi = new FileInfo(_errorPath);
                if (fi.Exists && fi.Length > 256 * 1024)
                {
                    File.Delete(_errorPath);
                }
                File.AppendAllText(_errorPath, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch
            {
                // 忽略日志写入失败
            }
        }

        private static string Timestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                try
                {
                    if (_debugWriter != null)
                    {
                        _debugWriter.Flush();
                        _debugWriter.Dispose();
                    }
                }
                catch
                {
                }
                _debugWriter = null;
                _debugEnabled = false;
            }
        }
    }
}
