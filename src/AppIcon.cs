using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CopyFeedback
{
    /// <summary>加载托盘图标：优先使用嵌入的 resources/app.ico，失败时用代码绘制一个简单图标。</summary>
    internal static class AppIcon
    {
        public static Icon Load()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream("CopyFeedback.app.ico"))
                {
                    if (stream != null)
                    {
                        // 取小图标尺寸，托盘看起来最清晰
                        return new Icon(stream, SystemInformation.SmallIconSize);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("加载嵌入图标失败", ex);
            }
            return CreateFallback();
        }

        private static Icon CreateFallback()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        Rectangle rect = new Rectangle(1, 1, 30, 30);
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            path.AddArc(rect.X, rect.Y, 10, 10, 180, 90);
                            path.AddArc(rect.Right - 10, rect.Y, 10, 10, 270, 90);
                            path.AddArc(rect.Right - 10, rect.Bottom - 10, 10, 10, 0, 90);
                            path.AddArc(rect.X, rect.Bottom - 10, 10, 10, 90, 90);
                            path.CloseFigure();
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(37, 99, 235)))
                            {
                                g.FillPath(brush, path);
                            }
                        }
                        using (Pen pen = new Pen(Color.White, 3.6f))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            g.DrawLine(pen, 9f, 17f, 14f, 22f);
                            g.DrawLine(pen, 14f, 22f, 23f, 11f);
                        }
                    }
                    IntPtr handle = bmp.GetHicon();
                    try
                    {
                        using (Icon tmp = Icon.FromHandle(handle))
                        {
                            return (Icon)tmp.Clone();
                        }
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(handle);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("生成备用图标失败", ex);
                return SystemIcons.Application;
            }
        }
    }
}
