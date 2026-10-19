// StartupPage.cs —— 启动页：浏览器打开时显示哪个页面（落盘 + 设置对话框）
//
// 语义边界（用户要求，别改）：
//   · 启动页 = 每次**启动浏览器**时填满窗口的那个页面，可配置成指定网站；
//   · 工具栏的「＋新标签」**始终**显示新标签页（书签墙），不受启动页设置影响。
//     两者刻意分开：启动页是"我每次打开浏览器先看什么"，书签墙是"我要逛收藏"。
//   · 默认仍是空白页 —— 加功能不改既有默认行为，想换是用户的事。
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>启动页设置的落盘形状（%APPDATA%\LoongBrowser\startup.json）</summary>
    public class StartupPageData
    {
        /// <summary>三选一：StartupPageSettings.ModeBlank / ModeWall / ModeUrl</summary>
        public string Mode;
        /// <summary>Mode 为 url 时启动打开的地址</summary>
        public string Url;
    }

    /// <summary>
    /// 启动页设置：读写 startup.json，并把"启动该打开什么"收敛成一个纯函数，
    /// 便于在不启动窗口的情况下测试启动决策。
    /// </summary>
    public static class StartupPageSettings
    {
        /// <summary>空白页（默认，加功能前的既有行为）</summary>
        public const string ModeBlank = "blank";
        /// <summary>新标签页（书签墙）</summary>
        public const string ModeWall = "wall";
        /// <summary>指定网站</summary>
        public const string ModeUrl = "url";

        /// <summary>数据文件位置（测试可指向临时文件）</summary>
        public static string FilePath = Path.Combine(AppPaths.DataDir, "startup.json");

        /// <summary>当前启动方式（三选一，非法值一律回落 blank）</summary>
        public static string Mode = ModeBlank;

        /// <summary>Mode 为 url 时的地址（已归一化）</summary>
        public static string Url;

        /// <summary>启动时是否打开指定网站</summary>
        public static bool HasUrl
        {
            get { return Mode == ModeUrl && !string.IsNullOrEmpty(Url); }
        }

        /// <summary>读盘。文件缺失、内容损坏都按"空白页"处理，绝不抛异常打断启动。</summary>
        public static void Load()
        {
            Mode = ModeBlank;
            Url = null;
            try
            {
                var data = JsonStore.Load<StartupPageData>(FilePath);
                if (data == null) return;
                Mode = NormalizeMode(data.Mode);
                Url = (data.Url == null) ? null : data.Url.Trim();
                if (Url != null && Url.Length == 0) Url = null;
                if (Mode == ModeUrl && Url == null) Mode = ModeBlank;   // 选了网站却没地址 → 空白页
            }
            catch (Exception)
            {
                Mode = ModeBlank;
                Url = null;
            }
        }

        /// <summary>写盘并即刻生效（下次启动读取）</summary>
        public static void Save(string mode, string url)
        {
            Mode = NormalizeMode(mode);
            Url = string.IsNullOrEmpty(url) ? null : url.Trim();
            if (Mode == ModeUrl && Url == null) Mode = ModeBlank;
            JsonStore.Save(FilePath, new StartupPageData { Mode = Mode, Url = Url });
        }

        /// <summary>把未知/空的模式收敛成空白页</summary>
        public static string NormalizeMode(string mode)
        {
            if (mode == ModeWall) return ModeWall;
            if (mode == ModeUrl) return ModeUrl;
            return ModeBlank;
        }

        /// <summary>
        /// 启动窗口该打开哪个地址。
        /// 命令行给了地址（双击文件 / 默认程序打开）→ 用它，忽略启动页设置；
        /// 否则按设置：指定网站 → 那个地址；书签墙 → null（由 TabManager 画墙）；空白页 → about:blank。
        /// </summary>
        public static string InitialUrl(string commandLineUrl)
        {
            if (!string.IsNullOrEmpty(commandLineUrl)) return commandLineUrl;
            if (Mode == ModeUrl && !string.IsNullOrEmpty(Url)) return Url;
            if (Mode == ModeWall) return null;
            return NewTabPage.HomeUrl;
        }

        /// <summary>
        /// 启动窗口该画哪张墙的标题；不是"启动即书签墙"时返回 null。
        /// 与 <see cref="InitialUrl"/> 配对使用：地址为 null 就代表要画墙，此时标题用这里给的。
        /// </summary>
        public static string InitialWallTitle(string commandLineUrl)
        {
            if (!string.IsNullOrEmpty(commandLineUrl)) return null;
            if (Mode == ModeWall) return NewTabPage.StartupTitle;
            return null;
        }

        /// <summary>
        /// 把用户输入归一化成可用的启动页地址。
        /// 复用地址栏那套归一化（补协议、识别本地路径、否则当搜索词），
        /// 但启动页只接受 http / https / file —— 挡住 javascript: / data: / about: 之类。
        /// </summary>
        public static bool TryNormalize(string input, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            string text = (input == null) ? "" : input.Trim();
            if (text.Length == 0)
            {
                error = "请输入要打开的网址";
                return false;
            }

            // 本机地址没写协议时补 http://：NormalizeUrl 会把 localhost:8080 原样返回，
            // 内核之外没人认得这种"协议名叫 localhost"的串，IsSafeUrl 会把它判掉。
            text = CompleteLocalHost(text);

            // 显式伪协议（javascript: / data: / vbscript: / about: ...）直接拒绝。
            // 地址栏那套归一化会把"不像网址"的输入当搜索词，启动页不能照办：
            // 用户敲 javascript: 显然不是想搜这串字符，静默存成一条 Bing 链接更糟。
            string schemeName;
            if (TryReadScheme(text, out schemeName) && !IsAllowedScheme(schemeName))
            {
                error = "不支持 " + schemeName + ": 协议，只能填 http / https / file 地址";
                return false;
            }

            string url;
            try
            {
                url = MainForm.NormalizeUrl(text);
            }
            catch (Exception)
            {
                error = "网址格式无法识别";
                return false;
            }

            if (string.IsNullOrEmpty(url) || !NewTabPage.IsSafeUrl(url))
            {
                error = "只支持 http / https / file 地址";
                return false;
            }

            normalized = url;
            return true;
        }

        /// <summary>本机地址补协议：localhost / 127.0.0.1 / [::1] 后面跟端口或路径时补 http://</summary>
        private static string CompleteLocalHost(string text)
        {
            string lower = text.ToLowerInvariant();
            if (lower.StartsWith("http://") || lower.StartsWith("https://") ||
                lower.StartsWith("file:") || lower.StartsWith("about:")) return text;

            string[] hosts = new string[] { "localhost", "127.0.0.1", "[::1]" };
            for (int i = 0; i < hosts.Length; i++)
            {
                string h = hosts[i];
                if (!lower.StartsWith(h)) continue;
                int next = h.Length;
                if (next == lower.Length) return "http://" + text;                       // 只有主机名
                char c = lower[next];
                if (c == ':' || c == '/') return "http://" + text;                       // host:port 或 host/path
            }
            return text;
        }

        /// <summary>
        /// 取输入里的协议名：冒号之前、形如 scheme 的那一段。
        /// 形如 example.com:8080 的 host:port 不算协议 —— 协议名里不会有点
        /// （RFC 3986 允许点，但真实协议名没有一个带点，带点的必然是主机名）。
        /// 单字母也不算：那是盘符，C:\dir\a.html 是路径不是 C 协议。
        /// </summary>
        private static bool TryReadScheme(string text, out string scheme)
        {
            scheme = null;
            int colon = text.IndexOf(':');
            if (colon <= 0 || colon > 32) return false;

            string head = text.Substring(0, colon);
            if (head.Length == 1) return false;                   // 盘符
            if (head.IndexOf('.') >= 0) return false;             // 主机名，不是协议
            if (!char.IsLetter(head[0])) return false;
            for (int i = 0; i < head.Length; i++)
            {
                char c = head[i];
                bool ok = char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.';
                if (!ok) return false;
            }
            scheme = head.ToLowerInvariant();
            return true;
        }

        private static bool IsAllowedScheme(string scheme)
        {
            return scheme == "http" || scheme == "https" || scheme == "file";
        }
    }

    /// <summary>启动页设置对话框：三选一（空白页 / 书签墙 / 指定网站），确定即落盘</summary>
    public class StartupPageDialog : Form
    {
        private readonly string _currentPageUrl;
        private RadioButton _rbBlank;
        private RadioButton _rbWall;
        private RadioButton _rbUrl;
        private TextBox _txtUrl;
        private Button _btnUseCurrent;
        private Label _lblError;

        public StartupPageDialog() : this(null) { }

        /// <param name="currentPageUrl">当前标签页地址，供「使用当前标签页地址」一键填入；可为空</param>
        public StartupPageDialog(string currentPageUrl)
        {
            _currentPageUrl = currentPageUrl;
            // 统一外壳字体（见 VistaTheme.UIFont）
            Font = VistaTheme.UIFont;
            BuildUi();
            LoadCurrent();
        }

        private void BuildUi()
        {
            Text = "启动页设置 - LoongBrowser";
            ClientSize = new Size(480, 292);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var lblHead = new Label();
            lblHead.Text = "浏览器启动时显示：";
            lblHead.Location = new Point(16, 14);
            lblHead.AutoSize = true;

            _rbBlank = new RadioButton();
            _rbBlank.Text = "空白页";
            _rbBlank.Location = new Point(16, 42);
            _rbBlank.AutoSize = true;
            _rbBlank.CheckedChanged += delegate { SyncEnabled(); };

            _rbWall = new RadioButton();
            _rbWall.Text = "书签墙（全部书签）";
            _rbWall.Location = new Point(16, 68);
            _rbWall.AutoSize = true;
            _rbWall.CheckedChanged += delegate { SyncEnabled(); };

            _rbUrl = new RadioButton();
            _rbUrl.Text = "打开指定网站";
            _rbUrl.Location = new Point(16, 94);
            _rbUrl.AutoSize = true;
            _rbUrl.CheckedChanged += delegate { SyncEnabled(); };

            _txtUrl = new TextBox();
            _txtUrl.Location = new Point(36, 122);
            _txtUrl.Width = 420;

            _btnUseCurrent = new Button();
            _btnUseCurrent.Text = "使用当前标签页地址";
            _btnUseCurrent.Location = new Point(36, 154);
            _btnUseCurrent.Size = new Size(168, 26);
            _btnUseCurrent.Click += delegate { UseCurrentPage(); };

            var lblHint = new Label();
            lblHint.Text = "设置立即保存，下次启动生效。\n工具栏的「＋新标签」始终显示书签墙，不受这里影响。";
            lblHint.Location = new Point(16, 190);
            lblHint.AutoSize = true;
            lblHint.ForeColor = Color.Gray;

            _lblError = new Label();
            _lblError.Location = new Point(16, 226);
            _lblError.AutoSize = true;
            _lblError.ForeColor = Color.Firebrick;

            var btnOk = new Button();
            btnOk.Text = "确定";
            btnOk.Location = new Point(296, 252);
            btnOk.Size = new Size(80, 30);
            btnOk.Click += delegate { Accept(); };

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(384, 252);
            btnCancel.Size = new Size(80, 30);
            btnCancel.DialogResult = DialogResult.Cancel;

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[]
            {
                lblHead, _rbBlank, _rbWall, _rbUrl, _txtUrl, _btnUseCurrent, lblHint, _lblError, btnOk, btnCancel
            });
        }

        private void LoadCurrent()
        {
            if (StartupPageSettings.HasUrl)
            {
                _rbUrl.Checked = true;
                _txtUrl.Text = StartupPageSettings.Url;
            }
            else if (StartupPageSettings.Mode == StartupPageSettings.ModeWall)
            {
                _rbWall.Checked = true;
            }
            else
            {
                _rbBlank.Checked = true;
            }
            SyncEnabled();
        }

        /// <summary>文本框与「使用当前标签页地址」只在"指定网站"这一档有意义</summary>
        private void SyncEnabled()
        {
            bool on = _rbUrl.Checked;
            _txtUrl.Enabled = on;
            _btnUseCurrent.Enabled = on && !string.IsNullOrEmpty(_currentPageUrl);
            if (on) _lblError.Text = "";
        }

        private void UseCurrentPage()
        {
            if (string.IsNullOrEmpty(_currentPageUrl))
            {
                _lblError.Text = "当前标签页没有可用的网页地址";
                return;
            }
            _rbUrl.Checked = true;
            _txtUrl.Text = _currentPageUrl;
            _lblError.Text = "";
        }

        private void Accept()
        {
            if (_rbWall.Checked)
            {
                StartupPageSettings.Save(StartupPageSettings.ModeWall, null);
                DialogResult = DialogResult.OK;
                return;
            }

            if (_rbBlank.Checked)
            {
                StartupPageSettings.Save(StartupPageSettings.ModeBlank, null);
                DialogResult = DialogResult.OK;
                return;
            }

            string normalized, error;
            if (!StartupPageSettings.TryNormalize(_txtUrl.Text, out normalized, out error))
            {
                _lblError.Text = error;
                _txtUrl.Focus();
                return;
            }

            StartupPageSettings.Save(StartupPageSettings.ModeUrl, normalized);
            DialogResult = DialogResult.OK;
        }
    }
}
