using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CopyFeedback
{
    /// <summary>
    /// 用户级开机自启动管理（HKCU\...\Run）。
    /// 只读写本程序自己的值 CopyFeedback，不触碰任何其它系统设置，不需要管理员权限。
    /// </summary>
    internal static class AutostartManager
    {
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "CopyFeedback";

        public static string CurrentExePath
        {
            get
            {
                try
                {
                    return Application.ExecutablePath;
                }
                catch
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CopyFeedback.exe");
                }
            }
        }

        public static string BuildCommand()
        {
            return "\"" + CurrentExePath + "\" --startup";
        }

        public static string GetRegisteredCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return null;
                    object value = key.GetValue(ValueName);
                    string text = value as string;
                    return text;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取开机自启动设置失败", ex);
                return null;
            }
        }

        public static bool IsEnabled()
        {
            string command = GetRegisteredCommand();
            return !string.IsNullOrEmpty(command) && command.Trim().Length > 0;
        }

        public static bool IsEnabledAndCurrent()
        {
            string command = GetRegisteredCommand();
            if (string.IsNullOrEmpty(command)) return false;
            string exe = ExtractExePath(command);
            if (string.IsNullOrEmpty(exe)) return false;
            try
            {
                bool samePath = string.Equals(
                    Path.GetFullPath(exe),
                    Path.GetFullPath(CurrentExePath),
                    StringComparison.OrdinalIgnoreCase);
                // 自启动项必须带 --startup，登录时才能静默启动（缺少时重复启动会弹“已在运行”提示）
                bool hasStartupArg = command.IndexOf("--startup", StringComparison.OrdinalIgnoreCase) >= 0;
                return samePath && hasStartupArg;
            }
            catch
            {
                return false;
            }
        }

        public static bool Enable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                }
                bool ok = IsEnabledAndCurrent();
                if (!ok)
                {
                    Logger.Error("写入开机自启动设置后校验失败");
                }
                return ok;
            }
            catch (Exception ex)
            {
                Logger.Error("写入开机自启动设置失败", ex);
                return false;
            }
        }

        public static bool Disable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(ValueName);
                        if (value != null)
                        {
                            key.DeleteValue(ValueName, false);
                        }
                    }
                }
                return !IsEnabled();
            }
            catch (Exception ex)
            {
                Logger.Error("删除开机自启动设置失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 程序移动位置后，如果自启动项指向的旧文件已不存在，则把自启动项修正为当前路径。
        /// 如果旧路径文件仍然存在（用户可能保留了两份），则不做改动，避免擅自改变用户配置。
        /// </summary>
        public static void RepairIfBroken()
        {
            try
            {
                if (!IsEnabled()) return;
                if (IsEnabledAndCurrent()) return;
                string command = GetRegisteredCommand();
                string oldExe = ExtractExePath(command);
                bool samePath = false;
                try
                {
                    samePath = !string.IsNullOrEmpty(oldExe) && string.Equals(
                        Path.GetFullPath(oldExe),
                        Path.GetFullPath(CurrentExePath),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                }
                bool oldFileMissing = string.IsNullOrEmpty(oldExe) || !File.Exists(oldExe);
                // 仅修复“旧路径已失效”或“路径相同但缺少 --startup”的情况；
                // 如果指向另一份仍然存在的程序，则尊重用户配置，不改动。
                if (!oldFileMissing && !samePath) return;
                if (Enable())
                {
                    Logger.Info("AUTOSTART repaired=1");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("修复开机自启动路径失败", ex);
            }
        }

        /// <summary>从命令行中提取可执行文件路径（支持带引号 / 不带引号两种写法）。</summary>
        public static string ExtractExePath(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            string text = command.Trim();
            if (text.Length == 0) return null;
            if (text[0] == '"')
            {
                int end = text.IndexOf('"', 1);
                if (end > 1) return text.Substring(1, end - 1);
                return null;
            }
            int space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : text;
        }
    }
}
