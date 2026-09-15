// PipPopupE2ETest.cs —— "点击画中画跳空白页" 的端到端复现与修复验证（真实 WebView2 内核）
//
// 阶段 A：把"修复前"的弹窗逻辑原样搬来对照，确认它确实会把正在浏览的页面顶成空白页；
// 阶段 B：走真实 TabManager，确认修复后当前页不被顶掉、且弹出小窗能拿到真正的窗口；
// 阶段 C：真实探测 B 站页面（画中画支持状态 / 播放器画中画按钮 / 是否走 window.open），失败不影响结论。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LoongBrowser
{
    public static class PipPopupE2ETest
    {
        private static readonly StringBuilder _sb = new StringBuilder();
        private static int _pass;
        private static int _fail;

        private static Form _form;
        private static TabControl _tabs;
        private static TabManager _tm;
        private static BookmarkStore _bookmarks;
        private static WebView2 _legacy;
        private static CoreWebView2Environment _env;
        private static Timer _watchdog;

        private const string HostHtml =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>PIP_HOST</title></head>" +
            "<body><h1>host page</h1></body></html>";

        // window.open 的返回值就是"弹窗是否被允许"的判据：被宿主吞掉时返回 null，允许时返回 WindowProxy
        private const string OpenJs =
            "(function(){ window.__pipWin = window.open('about:blank','pipwin','width=420,height=260');" +
            " return window.__pipWin ? 'opened' : 'null'; })()";

        [STAThread]
        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string ud = Path.Combine(baseDir, "e2e_ud_pip");
            try { if (Directory.Exists(ud)) Directory.Delete(ud, true); } catch (Exception) { }

            _sb.AppendLine("=== 画中画弹窗端到端测试（真实 WebView2）===");

            _form = new Form();
            _form.ShowInTaskbar = false;
            _form.StartPosition = FormStartPosition.Manual;
            _form.Location = new Point(-4000, -4000);
            _form.Size = new Size(1000, 720);

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _form.Controls.Add(_tabs);

            _legacy = new WebView2();          // 阶段 A 用，放一个 1px 宽的位置，不干扰
            _legacy.Dock = DockStyle.Right;
            _legacy.Width = 1;
            _form.Controls.Add(_legacy);

            _watchdog = new Timer();
            _watchdog.Interval = 240000;
            _watchdog.Tick += delegate { Judge("看门狗", false, "整体超时"); Finish(); };
            _watchdog.Start();

            _form.Load += delegate { Boot(ud); };
            _form.Show();
            Application.Run(_form);

            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static async void Boot(string userDataDir)
        {
            try
            {
                var opts = new CoreWebView2EnvironmentOptions(
                    "--disable-popup-blocking --autoplay-policy=no-user-gesture-required");
                _env = await CoreWebView2Environment.CreateAsync(null, userDataDir, opts);
                _sb.AppendLine("内核版本: " + _env.BrowserVersionString);
                _sb.AppendLine("启动参数: --disable-popup-blocking（让脚本触发的 window.open 也能产生弹窗请求，便于自动化复现）");
                _sb.AppendLine();

                await StageLegacy();
                await StageFixed();
                await StageBilibili();
                Finish();
            }
            catch (Exception ex)
            {
                Judge("异常", false, ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        // ---------- 阶段 A：修复前的行为 ----------

        private static async Task StageLegacy()
        {
            _sb.AppendLine("-- A. 复现修复前的行为（旧逻辑：一律接管弹窗 → 在当前标签内导航）--");
            await _legacy.EnsureCoreWebView2Async(_env);
            CoreWebView2 core = _legacy.CoreWebView2;

            string seen = null;
            // ↓↓↓ 以下与修复前 TabManager.Wire 中的写法一致，仅用于对照 ↓↓↓
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;
                string target = e.Uri;
                if (string.IsNullOrEmpty(target)) return;
                string cur = "";
                try { cur = core.Source ?? ""; } catch (Exception) { }
                seen = (target.Length == 0) ? "(空)" : target;
                if (TabManager.IsSameSite(cur, target)) core.Navigate(target);
            };
            // ↑↑↑ 对照代码结束 ↑↑↑

            core.NavigateToString(HostHtml);
            await WaitTitle(core, "PIP_HOST", 8000);
            Judge("A1 宿主页面已就绪", Title(core) == "PIP_HOST", "实际: " + Title(core));

            string probe = await EvalStr(core, OpenJs);
            await Task.Delay(1600);

            Info("A2 弹窗请求里的 Uri = " + (seen == null ? "(未触发)" : seen));
            Info("A3 window.open 返回值 = " + probe);
            Judge("A4 弹窗请求确实被触发（window.open('about:blank')）", seen == "about:blank", "实际: " + seen);
            Judge("A5 旧逻辑下 window.open 拿不到窗口（返回 null）", probe == "null", "实际: " + probe);
            string after = Title(core);
            bool blanked = (after == "about:blank") || string.IsNullOrEmpty(after);
            Judge("A6 旧逻辑把当前页顶成了空白页（复现用户看到的症状）", blanked, "实际标题: " + after);
            _sb.AppendLine();
        }

        // ---------- 阶段 B：修复后的真实路径 ----------

        private static async Task StageFixed()
        {
            _sb.AppendLine("-- B. 修复后：走真实 TabManager --");
            TabManager.SetEnvironment(_env);
            _bookmarks = new BookmarkStore();
            _bookmarks.Items = new List<BookmarkItem>();
            _tm = new TabManager(_tabs);
            _tm.Bookmarks = _bookmarks;
            _tm.NewTab(null);                       // 渲染书签墙（当前标签）

            CoreWebView2 core = null;
            for (int i = 0; i < 60 && core == null; i++)
            {
                await Task.Delay(200);
                try { core = _tm.ActiveCore(); } catch (Exception) { }
            }
            if (core == null) { Judge("B0 内核就绪", false, "拿不到 CoreWebView2"); return; }

            await WaitTitle(core, "新标签页", 8000);
            Judge("B1 当前标签是新标签页（书签墙）", Title(core) == "新标签页", "实际: " + Title(core));

            string probe = await EvalStr(core, OpenJs);
            await Task.Delay(1600);

            Info("B2 window.open 返回值 = " + probe);
            Judge("B3 弹出小窗被内核接受（拿到真正的窗口，而不是 null）", probe == "opened", "实际: " + probe);
            Judge("B4 当前页没有被顶掉", Title(core) == "新标签页", "实际: " + Title(core));

            // 关掉测试弹出的窗口，别留在桌面上
            await EvalStr(core, "try{ if(window.__pipWin) window.__pipWin.close(); }catch(e){} 1");
            await Task.Delay(400);
            _sb.AppendLine();
        }

        // ---------- 阶段 C：真实 B 站探测（容错） ----------

        private static async Task StageBilibili()
        {
            _sb.AppendLine("-- C. 真实探测 B 站（网络/登录受限时跳过，不影响上面的结论）--");
            CoreWebView2 core = null;
            try { core = _tm.ActiveCore(); } catch (Exception) { }
            if (core == null) { Info("C0 跳过：无可用标签页"); return; }

            try
            {
                core.Navigate("https://www.bilibili.com/");
                if (!await WaitSource(core, "bilibili.com", 25000))
                {
                    Info("C0 跳过：无法打开 bilibili.com（网络受限）");
                    return;
                }
                Info("C1 已打开首页，标题=" + Title(core));
                Info("C1b 首页 DOM 长度=" + await EvalStr(core, "String(document.documentElement.innerHTML.length)"));

                // 首页卡片是动态渲染的，先试正则、再让页面自己请求 B 站接口拿一个热门视频号（同源、带 Cookie）
                string bv = await EvalStr(core,
                    "(function(){var m=document.documentElement.innerHTML.match(/BV[0-9A-Za-z]{10}/);" +
                    " return m?m[0]:'';})()");
                if (string.IsNullOrEmpty(bv))
                {
                    await EvalStr(core,
                        "(function(){window.__bv='';fetch('https://api.bilibili.com/x/web-interface/popular?ps=1&pn=1'," +
                        "{credentials:'include'}).then(function(r){return r.json()}).then(function(j){" +
                        "try{window.__bv=String(j.data.list[0].bvid)}catch(e){window.__bv='ERR:'+String(e)}})" +
                        ".catch(function(e){window.__bv='ERR:'+String(e)});return 1})()");
                    for (int i = 0; i < 24; i++)
                    {
                        string got = await EvalStr(core, "window.__bv||''");
                        if (!string.IsNullOrEmpty(got)) { bv = got; break; }
                        await Task.Delay(250);
                    }
                    Info("C2 API 取号结果: " + bv);
                }
                if (string.IsNullOrEmpty(bv) || bv.StartsWith("ERR", StringComparison.Ordinal) || bv.Length != 12)
                {
                    Info("C2 跳过：未能取到有效的 BV 号");
                    return;
                }
                string href = "https://www.bilibili.com/video/" + bv;
                Info("C2 打开视频页: " + href);
                core.Navigate(href);
                await WaitSource(core, "/video/BV", 25000);
                await Task.Delay(2500);          // 等播放器初始化

                string pipEnabled = await EvalStr(core, "String(!!document.pictureInPictureEnabled)");
                string hasVideo = await EvalStr(core, "String(!!document.querySelector('video'))");
                Info("C3 document.pictureInPictureEnabled = " + pipEnabled);
                Info("C4 页面存在 <video> = " + hasVideo);

                // 播放器里找"画中画"相关控件
                string hits = await EvalStr(core,
                    "(function(){var all=document.querySelectorAll('*'),h=[],i,el,s;" +
                    " for(i=0;i<all.length;i++){el=all[i];" +
                    " s=String(el.className||'')+' '+String(el.getAttribute&&el.getAttribute('title')||'')" +
                    "  +' '+String(el.getAttribute&&el.getAttribute('aria-label')||'');" +
                    " if(/画中画|picture.?in.?picture|\\bpip\\b/i.test(s)) h.push(el.tagName+'|'+String(el.className).slice(0,48));}" +
                    " return h.length+'##'+h.slice(0,6).join(' ;; ');})()");
                Info("C5 匹配到画中画相关元素: " + hits);

                // 记录 window.open，并给 requestPictureInPicture 打桩（记录是否被调用、Promise 结果）
                await EvalStr(core,
                    "(function(){ if(window.__pipPatched) return 1; window.__pipPatched=1;"
                    + " window.__openCalls=[];"
                    + " var raw=window.open; window.open=function(u,n,f){"
                    + "   window.__openCalls.push(String(u)+' | '+String(n)+' | '+String(f));"
                    + "   return raw.apply(window,arguments); };"
                    + " window.__pipCalls=[];"
                    + " var proto=HTMLVideoElement.prototype, rp=proto.requestPictureInPicture;"
                    + " if(!rp){ window.__pipCalls.push('API_MISSING'); return 1; }"
                    + " proto.requestPictureInPicture=function(){"
                    + "   var i=window.__pipCalls.length; window.__pipCalls.push('pending');"
                    + "   try{ var p=rp.apply(this,arguments);"
                    + "     if(p&&p.then){ p.then(function(){window.__pipCalls[i]='ok';},"
                    + "       function(e){window.__pipCalls[i]='rejected:'+((e&&e.name)||'?')+':'+((e&&e.message)||'');}); }"
                    + "     else { window.__pipCalls[i]='non-promise'; }"
                    + "     return p; }"
                    + "   catch(e){ window.__pipCalls[i]='threw:'+((e&&e.name)||'?')+':'+((e&&e.message)||''); throw e; } };"
                    + " return 1; })()");
                Info("C5b 视频是否在播放: " + await EvalStr(core,
                    "(function(){var v=document.querySelector('video'); return v?String(!v.paused):'no-video';})()"));
                Info("C5c 画中画按钮是否可用: " + await EvalStr(core,
                    "(function(){var b=document.querySelector('.bpx-player-ctrl-pip');" +
                    " return b?(String(b.className)+' disabled='+String(!!b.disabled)+' aria='+String(b.getAttribute('aria-disabled'))):'no-button';})()"));

                string clicked = await EvalStr(core,
                    "(function(){var b=document.querySelector('.bpx-player-ctrl-pip');" +
                    " if(!b) return 'no-button'; window.__pipErr='';" +
                    " try{ b.click(); }catch(e){ window.__pipErr=String(e); }" +
                    " return 'clicked:'+String(b.className);})()");
                Info("C6 已点击的画中画控件: " + (string.IsNullOrEmpty(clicked) ? "(没找到，未点击)" : clicked));
                await Task.Delay(2500);

                string calls = await EvalStr(core, "(window.__openCalls&&window.__openCalls.length)?window.__openCalls.join(' ;; '):'(无 window.open 调用)'");
                Info("C7 点击后 window.open 的调用记录: " + calls);
                Info("C7b 点击时抛出的异常: " + await EvalStr(core, "window.__pipErr||'(无)'"));
                Info("C7c 是否已进入标准画中画: " + await EvalStr(core, "String(!!document.pictureInPictureElement)"));
                Info("C7d requestPictureInPicture 调用记录: " + await EvalStr(core,
                    "(window.__pipCalls&&window.__pipCalls.length)?window.__pipCalls.join(' ;; '):'(未被调用)'"));

                string src = "";
                try { src = core.Source ?? ""; } catch (Exception) { }
                Info("C8 点击后当前标签地址: " + src);
                if (!string.IsNullOrEmpty(clicked))
                {
                    Judge("C9 点击画中画后当前标签仍停在视频页（没有被顶成空白页）",
                          src.IndexOf("bilibili.com", StringComparison.OrdinalIgnoreCase) >= 0, "实际: " + src);
                }
            }
            catch (Exception ex)
            {
                Info("C 阶段异常（忽略）: " + ex.GetType().Name + " " + ex.Message);
            }
            _sb.AppendLine();
        }

        // ---------- 工具 ----------

        private static string Title(CoreWebView2 core)
        {
            try { return core.DocumentTitle ?? ""; } catch (Exception) { return ""; }
        }

        private static async Task WaitTitle(CoreWebView2 core, string expect, int ms)
        {
            int loops = Math.Max(1, ms / 200);
            for (int i = 0; i < loops; i++)
            {
                if (Title(core) == expect) return;
                await Task.Delay(200);
            }
        }

        private static async Task<bool> WaitSource(CoreWebView2 core, string part, int ms)
        {
            int loops = Math.Max(1, ms / 250);
            for (int i = 0; i < loops; i++)
            {
                string s = "";
                try { s = core.Source ?? ""; } catch (Exception) { }
                if (s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                await Task.Delay(250);
            }
            return false;
        }

        private static async Task<string> EvalStr(CoreWebView2 core, string js)
        {
            try
            {
                string r = await core.ExecuteScriptAsync(js);
                if (r == null) return "";
                r = r.Trim();
                if (r.Length >= 2 && r.StartsWith("\"") && r.EndsWith("\""))
                {
                    r = r.Substring(1, r.Length - 2);
                    r = r.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\").Replace("\\/", "/");
                }
                return r;
            }
            catch (Exception ex) { return "(脚本异常: " + ex.GetType().Name + ")"; }
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }

        private static void Info(string s)
        {
            _sb.AppendLine("       * " + s);
        }

        private static void Finish()
        {
            try { if (_watchdog != null) _watchdog.Stop(); } catch (Exception) { }
            _sb.AppendLine();
            _sb.AppendLine("=== 端到端结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            try { if (_form != null) _form.Close(); } catch (Exception) { }
        }
    }
}
