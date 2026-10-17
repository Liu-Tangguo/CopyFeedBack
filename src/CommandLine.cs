using System;
using System.Text;

namespace CopyFeedback
{
    /// <summary>命令行参数解析。参数均为可选、大小写不敏感。</summary>
    internal sealed class CommandLine
    {
        public bool Debug;
        public bool Startup;
        public bool Quit;
        public bool EnableAutostart;
        public bool DisableAutostart;
        public bool AutostartStatus;
        public string RawArgs;

        public CommandLine(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i] == null ? string.Empty : args[i].Trim();
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(arg);

                    string lower = arg.ToLowerInvariant();
                    if (lower == "--debug") Debug = true;
                    else if (lower == "--startup") Startup = true;
                    else if (lower == "--quit") Quit = true;
                    else if (lower == "--enable-autostart") EnableAutostart = true;
                    else if (lower == "--disable-autostart") DisableAutostart = true;
                    else if (lower == "--autostart-status") AutostartStatus = true;
                }
            }
            RawArgs = sb.ToString();
        }
    }
}
