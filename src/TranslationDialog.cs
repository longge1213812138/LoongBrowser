// TranslationDialog.cs —— 翻译对话框 UI
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoongBrowser
{
    /// <summary>
    /// 翻译对话框：显示原文和译文，支持语言切换
    /// </summary>
    public class TranslationDialog : Form
    {
        private ComboBox _cboFrom;
        private ComboBox _cboTo;
        private TextBox _txtSource;
        private TextBox _txtTarget;
        private Button _btnTranslate;
        private Button _btnSwap;
        private Button _btnCopy;
        private Button _btnConfig;
        private Label _lblStatus;
        private string _originalText;

        public TranslationDialog(string text)
        {
            _originalText = text ?? "";
            // 统一外壳字体（SimHei 10pt，见 VistaTheme.UIFont）
            Font = VistaTheme.UIFont;
            InitializeComponent();
            LoadLanguages();
            _txtSource.Text = _originalText;

            // 如果文本不为空，自动翻译
            if (!string.IsNullOrWhiteSpace(_originalText))
            {
                BeginInvoke(new Action(() => DoTranslate()));
            }
        }

        private void InitializeComponent()
        {
            Text = "网页翻译 - LoongBrowser";
            Size = new Size(600, 500);
            MinimumSize = new Size(500, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;

            // 语言选择面板
            var panelLang = new Panel();
            panelLang.Dock = DockStyle.Top;
            panelLang.Height = 45;
            panelLang.Padding = new Padding(10, 8, 10, 5);

            _cboFrom = new ComboBox();
            _cboFrom.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboFrom.Width = 120;
            _cboFrom.Location = new Point(10, 10);

            _btnSwap = new Button();
            _btnSwap.Text = "⇄";
            _btnSwap.Size = new Size(40, 25);
            _btnSwap.Location = new Point(140, 9);
            _btnSwap.Click += delegate { SwapLanguages(); };

            _cboTo = new ComboBox();
            _cboTo.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboTo.Width = 120;
            _cboTo.Location = new Point(190, 10);

            _btnConfig = new Button();
            _btnConfig.Text = "⚙ 配置";
            _btnConfig.Size = new Size(70, 25);
            _btnConfig.Location = new Point(330, 9);
            _btnConfig.Click += delegate { ShowConfig(); };

            panelLang.Controls.AddRange(new Control[] { _cboFrom, _btnSwap, _cboTo, _btnConfig });

            // 源文本面板
            var panelSource = new Panel();
            panelSource.Dock = DockStyle.Fill;
            panelSource.Padding = new Padding(10, 5, 10, 5);

            var lblSource = new Label();
            lblSource.Text = "原文:";
            lblSource.Dock = DockStyle.Top;
            lblSource.Height = 20;

            _txtSource = new TextBox();
            _txtSource.Multiline = true;
            _txtSource.ScrollBars = ScrollBars.Vertical;
            _txtSource.Dock = DockStyle.Fill;
            _txtSource.AcceptsReturn = true;

            panelSource.Controls.Add(_txtSource);
            panelSource.Controls.Add(lblSource);

            // 按钮面板
            var panelButtons = new Panel();
            panelButtons.Dock = DockStyle.Bottom;
            panelButtons.Height = 40;
            panelButtons.Padding = new Padding(10, 5, 10, 5);

            _btnTranslate = new Button();
            _btnTranslate.Text = "翻译";
            _btnTranslate.Size = new Size(80, 30);
            _btnTranslate.Location = new Point(10, 5);
            _btnTranslate.Click += delegate { DoTranslate(); };

            _btnCopy = new Button();
            _btnCopy.Text = "复制译文";
            _btnCopy.Size = new Size(80, 30);
            _btnCopy.Location = new Point(100, 5);
            _btnCopy.Click += delegate { CopyTranslation(); };

            _lblStatus = new Label();
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(200, 12);
            _lblStatus.ForeColor = Color.Gray;

            panelButtons.Controls.AddRange(new Control[] { _btnTranslate, _btnCopy, _lblStatus });

            // 目标文本面板
            var panelTarget = new Panel();
            panelTarget.Dock = DockStyle.Bottom;
            panelTarget.Height = 180;
            panelTarget.Padding = new Padding(10, 5, 10, 10);

            var lblTarget = new Label();
            lblTarget.Text = "译文:";
            lblTarget.Dock = DockStyle.Top;
            lblTarget.Height = 20;

            _txtTarget = new TextBox();
            _txtTarget.Multiline = true;
            _txtTarget.ScrollBars = ScrollBars.Vertical;
            _txtTarget.Dock = DockStyle.Fill;
            _txtTarget.ReadOnly = true;
            _txtTarget.BackColor = SystemColors.Window;

            panelTarget.Controls.Add(_txtTarget);
            panelTarget.Controls.Add(lblTarget);

            // 添加控件（注意顺序：Fill 要先加）
            Controls.Add(panelSource);
            Controls.Add(panelLang);
            Controls.Add(panelTarget);
            Controls.Add(panelButtons);
        }

        private void LoadLanguages()
        {
            // 直接添加语言项
            _cboFrom.Items.Add(new LangItem("auto", "自动检测"));
            _cboFrom.Items.Add(new LangItem("zh", "中文"));
            _cboFrom.Items.Add(new LangItem("en", "英语"));
            _cboFrom.Items.Add(new LangItem("ja", "日语"));
            _cboFrom.Items.Add(new LangItem("ko", "韩语"));
            _cboFrom.Items.Add(new LangItem("fr", "法语"));
            _cboFrom.Items.Add(new LangItem("de", "德语"));
            _cboFrom.Items.Add(new LangItem("ru", "俄语"));
            _cboFrom.Items.Add(new LangItem("es", "西班牙语"));
            _cboFrom.Items.Add(new LangItem("pt", "葡萄牙语"));
            _cboFrom.Items.Add(new LangItem("it", "意大利语"));
            _cboFrom.Items.Add(new LangItem("vi", "越南语"));
            _cboFrom.Items.Add(new LangItem("th", "泰语"));

            _cboTo.Items.Add(new LangItem("auto", "自动检测"));
            _cboTo.Items.Add(new LangItem("zh", "中文"));
            _cboTo.Items.Add(new LangItem("en", "英语"));
            _cboTo.Items.Add(new LangItem("ja", "日语"));
            _cboTo.Items.Add(new LangItem("ko", "韩语"));
            _cboTo.Items.Add(new LangItem("fr", "法语"));
            _cboTo.Items.Add(new LangItem("de", "德语"));
            _cboTo.Items.Add(new LangItem("ru", "俄语"));
            _cboTo.Items.Add(new LangItem("es", "西班牙语"));
            _cboTo.Items.Add(new LangItem("pt", "葡萄牙语"));
            _cboTo.Items.Add(new LangItem("it", "意大利语"));
            _cboTo.Items.Add(new LangItem("vi", "越南语"));
            _cboTo.Items.Add(new LangItem("th", "泰语"));

            _cboFrom.SelectedIndex = 0; // 自动检测
            _cboTo.SelectedIndex = 1;    // 中文
        }

        private void SwapLanguages()
        {
            int fromIdx = _cboFrom.SelectedIndex;
            int toIdx = _cboTo.SelectedIndex;

            // 如果源语言是"自动检测"，切换后设为"中文"
            if (fromIdx == 0)
            {
                _cboFrom.SelectedIndex = toIdx;
                _cboTo.SelectedIndex = 1; // 中文
            }
            else
            {
                _cboFrom.SelectedIndex = toIdx;
                _cboTo.SelectedIndex = fromIdx;
            }

            // 交换文本
            string temp = _txtSource.Text;
            _txtSource.Text = _txtTarget.Text;
            _txtTarget.Text = temp;
        }

        private async void DoTranslate()
        {
            string text = _txtSource.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                _lblStatus.Text = "请输入要翻译的文本";
                return;
            }

            if (!TranslationService.IsConfigured)
            {
                _lblStatus.Text = "请先配置百度翻译 API";
                ShowConfig();
                return;
            }

            _btnTranslate.Enabled = false;
            _lblStatus.Text = "翻译中...";
            _lblStatus.ForeColor = Color.Blue;

            string from = ((LangItem)_cboFrom.SelectedItem).Code;
            string to = ((LangItem)_cboTo.SelectedItem).Code;

            // 异步执行翻译
            var result = await System.Threading.Tasks.Task.Run(() =>
                TranslationService.Translate(text, from, to));

            _btnTranslate.Enabled = true;

            if (result.Success)
            {
                _txtTarget.Text = result.TranslatedText;
                _lblStatus.Text = "翻译完成";
                _lblStatus.ForeColor = Color.Green;
            }
            else
            {
                _txtTarget.Text = "";
                _lblStatus.Text = result.ErrorMessage;
                _lblStatus.ForeColor = Color.Red;
            }
        }

        private void CopyTranslation()
        {
            string text = _txtTarget.Text;
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    Clipboard.SetText(text);
                    _lblStatus.Text = "已复制到剪贴板";
                    _lblStatus.ForeColor = Color.Green;
                }
                catch (Exception)
                {
                    _lblStatus.Text = "复制失败";
                    _lblStatus.ForeColor = Color.Red;
                }
            }
        }

        private void ShowConfig()
        {
            using (var dlg = new TranslationConfigDialog())
            {
                dlg.ShowDialog(this);
            }
        }

        private class LangItem
        {
            public string Code;
            public string Name;

            public LangItem(string code, string name)
            {
                Code = code;
                Name = name;
            }

            public override string ToString()
            {
                return Name;
            }
        }
    }

    /// <summary>
    /// 翻译配置对话框
    /// </summary>
    public class TranslationConfigDialog : Form
    {
        private TextBox _txtAppId;
        private TextBox _txtSecretKey;
        private Label _lblStatus;

        public TranslationConfigDialog()
        {
            // 统一外壳字体（SimHei 10pt，见 VistaTheme.UIFont）
            Font = VistaTheme.UIFont;
            InitializeComponent();
            LoadCurrentConfig();
        }

        private void InitializeComponent()
        {
            Text = "百度翻译 API 配置";
            Size = new Size(420, 250);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var lblInfo = new Label();
            lblInfo.Text = "请到百度翻译开放平台申请 API 密钥:\nhttps://fanyi-api.baidu.com/product/11\n\n标准版（免费）每月可翻译 5 万字符";
            lblInfo.Location = new Point(15, 10);
            lblInfo.Size = new Size(380, 60);

            var lblAppId = new Label();
            lblAppId.Text = "App ID:";
            lblAppId.Location = new Point(15, 80);
            lblAppId.AutoSize = true;

            _txtAppId = new TextBox();
            _txtAppId.Location = new Point(90, 77);
            _txtAppId.Width = 300;

            var lblSecret = new Label();
            lblSecret.Text = "密钥:";
            lblSecret.Location = new Point(15, 110);
            lblSecret.AutoSize = true;

            _txtSecretKey = new TextBox();
            _txtSecretKey.Location = new Point(90, 107);
            _txtSecretKey.Width = 300;

            var btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Size = new Size(80, 30);
            btnSave.Location = new Point(90, 150);
            btnSave.Click += delegate { SaveConfig(); };

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Size = new Size(80, 30);
            btnCancel.Location = new Point(180, 150);
            btnCancel.Click += delegate { Close(); };

            _lblStatus = new Label();
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(15, 190);
            _lblStatus.ForeColor = Color.Gray;

            Controls.AddRange(new Control[] { lblInfo, lblAppId, _txtAppId, lblSecret, _txtSecretKey, btnSave, btnCancel, _lblStatus });
        }

        private void LoadCurrentConfig()
        {
            _txtAppId.Text = TranslationService.MaskedAppId;
            // 密钥不显示
        }

        private void SaveConfig()
        {
            string appId = _txtAppId.Text.Trim();
            string secretKey = _txtSecretKey.Text.Trim();

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secretKey))
            {
                _lblStatus.Text = "请输入 App ID 和密钥";
                _lblStatus.ForeColor = Color.Red;
                return;
            }

            // 如果输入的是掩码，则不更新
            if (appId.Contains("***"))
            {
                appId = "";
            }

            if (!string.IsNullOrEmpty(appId))
            {
                TranslationService.SaveConfig(appId, secretKey);
                _lblStatus.Text = "配置已保存";
                _lblStatus.ForeColor = Color.Green;
            }
            else
            {
                _lblStatus.Text = "请输入完整的 App ID";
                _lblStatus.ForeColor = Color.Red;
            }
        }
    }
}
