// FaviconCache.cs —— 网站图标缓存
//
// 取图优先级（新标签页的图标来源）：
//   1) WebView2 官方接口：FaviconChanged 事件 + GetFaviconAsync()，拿到的是站点声明的图标，最准；
//   2) 兜底联网抓 https://<host>/favicon.ico（可用 FaviconCache.EnableRemoteFetch 关闭）；
//   3) 都没有 → 用内置的空白图标（%APPDATA%\LoongBrowser\favicons\_default.png，运行时生成）。
//      站点明确返回 404 或拿到的是非法图片时，会直接把空白图标记在该 host 名下，
//      这样打开新标签页不必反复重试网络，也不会出现空位。
//
// 统一归一化：不管来源是 PNG/JPEG/GIF/BMP/ICO，都缩放成 32×32 PNG 落盘。
// 这样新标签页内联的 data: URI 每个只有 1~3KB，100 个书签也不会把页面撑爆；
// 站点原始图标常常是 64×64 甚至 256×256，直接内联会让 HTML 体积失控。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace LoongBrowser
{
    public static class FaviconCache
    {
        /// <summary>图标边长（像素）：新标签页按 32×32 显示</summary>
        public const int IconSize = 32;

        /// <summary>单个图标源数据体积上限（512KB），超过直接放弃</summary>
        public const int MaxSourceBytes = 512 * 1024;

        /// <summary>是否允许在官方接口没给图标时联网抓 /favicon.ico</summary>
        public static bool EnableRemoteFetch = true;

        /// <summary>联网抓取超时（毫秒）</summary>
        public static int RemoteTimeoutMs = 6000;

        /// <summary>图标缓存目录：%APPDATA%\LoongBrowser\favicons（一个 host 一个 PNG）</summary>
        public static string Dir
        {
            get { return Path.Combine(AppPaths.DataDir, "favicons"); }
        }

        /// <summary>兜底空白图标文件名</summary>
        private const string DefaultIconName = "_default.png";

        private static readonly object _lock = new object();
        private static readonly HashSet<string> _inFlight = new HashSet<string>();

        static FaviconCache()
        {
            // 老一点的 .NET Framework 默认不协商 TLS 1.2，抓 https 图标会直接握手失败
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception) { }
        }

        // ---------- 查询 ----------

        /// <summary>书签 URL → 归属站点（host）。http/https 之外（file://、about: 等）没有可抓的图标，返回 null</summary>
        public static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                Uri u = new Uri(url);
                if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return null;
                if (string.IsNullOrEmpty(u.Host)) return null;
                return u.Host.ToLowerInvariant();
            }
            catch (Exception) { return null; }
        }

        /// <summary>某 URL 对应的图标文件路径（不一定存在）</summary>
        public static string IconPath(string url)
        {
            string host = HostOf(url);
            if (host == null) return null;
            return Path.Combine(Dir, Hash(host) + ".png");
        }

        /// <summary>已缓存则返回图标文件路径，否则 null</summary>
        public static string GetCached(string url)
        {
            string p = IconPath(url);
            if (p == null) return null;
            try { return File.Exists(p) ? p : null; }
            catch (Exception) { return null; }
        }

        /// <summary>图标文件 → data:image/png;base64,...（内联进新标签页：离线可用、无跨源问题）</summary>
        public static string ToDataUri(string iconFile)
        {
            if (string.IsNullOrEmpty(iconFile)) return null;
            try
            {
                if (!File.Exists(iconFile)) return null;
                byte[] b = File.ReadAllBytes(iconFile);
                if (b.Length == 0) return null;
                return "data:image/png;base64," + Convert.ToBase64String(b);
            }
            catch (Exception) { return null; }
        }

        // ---------- 兜底空白图标 ----------

        /// <summary>兜底空白图标的路径（首次使用时自动生成）</summary>
        public static string DefaultIconFile
        {
            get { return EnsureDefaultIcon(); }
        }

        /// <summary>
        /// 把"该站点拿不到合法图标"记成空白图标：
        /// 这样打开新标签页时不必反复重试，也不会出现空位。
        /// </summary>
        public static void MarkNoIcon(string url)
        {
            string p = IconPath(url);
            if (p == null) return;
            byte[] png = BuildBlankIconPng();
            if (png != null) Save(p, png);
        }

        private static string EnsureDefaultIcon()
        {
            string p = Path.Combine(Dir, DefaultIconName);
            try
            {
                if (File.Exists(p)) return p;
            }
            catch (Exception) { return null; }
            byte[] png = BuildBlankIconPng();
            if (png == null) return null;
            return Save(p, png) ? p : null;
        }

        /// <summary>生成 32×32 空白图标：浅灰圆角方块 + 细边框，深浅色背景下都可见</summary>
        private static byte[] BuildBlankIconPng()
        {
            try
            {
                using (var bmp = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    var rect = new Rectangle(2, 2, IconSize - 5, IconSize - 5);
                    using (var path = RoundedRect(rect, 6))
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(255, 234, 237, 241)))
                            g.FillPath(brush, path);
                        using (var pen = new Pen(Color.FromArgb(255, 205, 211, 219), 1f))
                            g.DrawPath(pen, path);
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception) { return null; }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- 来源 1：WebView2 官方接口 ----------

        /// <summary>
        /// 从正在浏览的页面里捕获官方图标（由 CoreWebView2.FaviconChanged 触发）。
        /// onDone 在后台线程回调，调用方需自行 marshal 回 UI 线程。
        /// </summary>
        public static void CaptureFromWebView(CoreWebView2 core, Action onSaved)
        {
            if (core == null) return;
            string host;
            string path;
            try
            {
                host = HostOf(core.Source);
                if (host == null) return;
                path = Path.Combine(Dir, Hash(host) + ".png");
                if (File.Exists(path)) return;                 // 已有图标就不覆盖，避免反复写盘
                string favUri = null;
                try { favUri = core.FaviconUri; } catch (Exception) { }
                if (string.IsNullOrEmpty(favUri)) return;      // 站点没声明图标 → 交给来源 2/3
            }
            catch (Exception) { return; }
            GetFaviconAsync(core, path, onSaved);
        }

        private static async void GetFaviconAsync(CoreWebView2 core, string path, Action onSaved)
        {
            try
            {
                using (Stream s = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                {
                    if (s == null) return;
                    byte[] png = Normalize(ReadLimited(s, MaxSourceBytes));
                    if (png == null) return;
                    if (!Save(path, png)) return;
                }
                if (onSaved != null) { try { onSaved(); } catch (Exception) { } }
            }
            catch (Exception) { }
        }

        // ---------- 来源 2：兜底抓 /favicon.ico ----------

        /// <summary>
        /// 联网抓 https://<host>/favicon.ico（同一 host 只发一次，异步不阻塞 UI）。
        /// onDone 在后台线程回调，调用方需自行 marshal 回 UI 线程。
        /// </summary>
        public static void FetchRemote(string url, Action onDone)
        {
            if (!EnableRemoteFetch) return;
            string host = HostOf(url);
            if (host == null) return;
            string path = Path.Combine(Dir, Hash(host) + ".png");

            try
            {
                if (File.Exists(path))
                {
                    if (onDone != null) { try { onDone(); } catch (Exception) { } }
                    return;
                }
            }
            catch (Exception) { }

            lock (_lock)
            {
                if (_inFlight.Contains(host)) return;   // 同一站点只发一次请求
                _inFlight.Add(host);
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool saved = false;
                try
                {
                    byte[] png = Normalize(DownloadIcon(host));
                    if (png != null) saved = Save(path, png);
                    else { MarkNoIcon(url); saved = true; }     // 拿到了但不是合法图片 → 记空白图标
                }
                catch (WebException wex)
                {
                    // 站点明确没有该图标（404/403 等）→ 记空白图标，省得每次重试；
                    // 网络不通/超时则不落盘，留待下次再试
                    if (wex.Response != null) { MarkNoIcon(url); saved = true; }
                }
                catch (Exception) { }
                finally
                {
                    lock (_lock) { _inFlight.Remove(host); }
                }
                if (saved && onDone != null) { try { onDone(); } catch (Exception) { } }
            });
        }

        private static byte[] DownloadIcon(string host)
        {
            string url = "https://" + host + "/favicon.ico";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = RemoteTimeoutMs;
            req.ReadWriteTimeout = RemoteTimeoutMs;
            req.AllowAutoRedirect = true;
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LoongBrowser";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != HttpStatusCode.OK) return null;
                using (Stream s = resp.GetResponseStream())
                    return ReadLimited(s, MaxSourceBytes);
            }
        }

        // ---------- 规范化与落盘 ----------

        /// <summary>任意图片字节 → 32×32 PNG（等比缩放居中，透明底）；无法识别返回 null</summary>
        private static byte[] Normalize(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(raw))
                using (Image src = LoadImage(ms))
                {
                    if (src == null) return null;
                    using (var bmp = new Bitmap(IconSize, IconSize, PixelFormat.Format32bppArgb))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.Clear(Color.Transparent);
                        double scale = Math.Min((double)IconSize / src.Width, (double)IconSize / src.Height);
                        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
                        int h = Math.Max(1, (int)Math.Round(src.Height * scale));
                        g.DrawImage(src, new Rectangle((IconSize - w) / 2, (IconSize - h) / 2, w, h));
                        using (var outMs = new MemoryStream())
                        {
                            bmp.Save(outMs, ImageFormat.Png);
                            return outMs.ToArray();
                        }
                    }
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>解码图片：ICO 走 Icon 解析，其余交给 Bitmap（不支持的格式会抛异常→返回 null）</summary>
        private static Image LoadImage(Stream s)
        {
            try
            {
                if (IsIco(s))
                {
                    s.Position = 0;
                    // 指定尺寸个别 ICO 会抛异常，退回默认尺寸再试一次
                    try
                    {
                        using (var ico = new Icon(s, new Size(IconSize, IconSize)))
                            return new Bitmap(ico.ToBitmap());
                    }
                    catch (Exception)
                    {
                        s.Position = 0;
                        using (var ico2 = new Icon(s))
                            return new Bitmap(ico2.ToBitmap());
                    }
                }
                s.Position = 0;
                return new Bitmap(s);
            }
            catch (Exception) { return null; }
        }

        /// <summary>ICO 文件头：00 00 01 00</summary>
        private static bool IsIco(Stream s)
        {
            try
            {
                s.Position = 0;
                int b0 = s.ReadByte(), b1 = s.ReadByte(), b2 = s.ReadByte(), b3 = s.ReadByte();
                s.Position = 0;
                return b0 == 0 && b1 == 0 && b2 == 1 && b3 == 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>读取流，超过 max 字节直接放弃（防止把大文件读进内存）</summary>
        private static byte[] ReadLimited(Stream s, int max)
        {
            if (s == null) return null;
            using (var ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > max) return null;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        private static bool Save(string path, byte[] png)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(path, png);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>清空图标缓存（下次浏览/打开新标签页会重新抓取）</summary>
        public static void Clear()
        {
            try
            {
                if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            }
            catch (Exception) { }
        }

        private static string Hash(string s)
        {
            using (var sha = new SHA1CryptoServiceProvider())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(h.Length * 2);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
