// HistoryStore.cs —— 浏览历史：数据存取 + 历史记录对话框
//
// 为什么自己记：WebView2 只提供"清除历史"（ClearBrowsingDataAsync），**没有读取历史的 API**，
// 所以要在自己的数据目录里记一份（%APPDATA%\LoongBrowser\history.json），
// 这样才能按时间展示、逐条删除、一键清空。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoongBrowser
{
    public class HistoryItem
    {
        public string Title;
        public string Url;
        /// <summary>访问时间（Unix 秒，UTC）</summary>
        public long VisitedAt;
    }

    public class HistoryStore
    {
        /// <summary>数据文件位置：%APPDATA%\LoongBrowser\history.json（测试可指向临时文件）</summary>
        public static string FilePath = Path.Combine(AppPaths.DataDir, "history.json");

        /// <summary>最多保留多少条（超出丢最旧的），防止文件无限增长</summary>
        public static int MaxItems = 3000;

        /// <summary>
        /// 同一地址在这么多秒内**紧接着**再次访问，只刷新时间/标题，不新增一条
        /// （避免重定向、刷新刷屏）。设为负数 = 关闭去重，每次访问都记。
        /// </summary>
        public static int DedupeSeconds = 30;

        /// <summary>新→旧排列（Items[0] 是最近访问）</summary>
        public List<HistoryItem> Items = new List<HistoryItem>();

        /// <summary>历史发生变化（增/删/清空）</summary>
        public event Action Changed;

        public HistoryStore()
        {
            var data = JsonStore.Load<List<HistoryItem>>(FilePath);
            if (data != null) Items = data;
            Sort();
        }

        public void Save()
        {
            JsonStore.Save(FilePath, Items);
            RaiseChanged();
        }

        /// <summary>
        /// 记一次访问。内部页（about:blank、新标签页）与非 http/https/file 地址不记；
        /// 与最近一条同址且间隔在 DedupeSeconds 内 → 只刷新时间与标题。
        /// </summary>
        public void Add(string title, string url)
        {
            if (!TabManager.IsNavigable(url)) return;
            if (title == null) title = "";
            title = title.Trim();
            if (title.Length == 0) title = url;

            long now = Now();
            HistoryItem head = Items.Count > 0 ? Items[0] : null;
            if (head != null && DedupeSeconds >= 0
                && string.Equals(head.Url, url, StringComparison.OrdinalIgnoreCase)
                && now - head.VisitedAt <= DedupeSeconds)
            {
                head.VisitedAt = now;
                if (head.Title != title) head.Title = title;
                Save();
                return;
            }

            var it = new HistoryItem();
            it.Title = title;
            it.Url = url;
            it.VisitedAt = now;
            Items.Insert(0, it);
            Trim();
            Save();
        }

        /// <summary>
        /// 页面标题晚于导航完成才就绪时，回填到最近一条同址记录上（没有匹配则不动）。
        /// 这样历史里的标题不会是空白或裸网址。
        /// </summary>
        public void TouchTitle(string url, string title)
        {
            if (!TabManager.IsNavigable(url)) return;
            if (string.IsNullOrEmpty(title)) return;
            title = title.Trim();
            if (title.Length == 0) return;
            if (Items.Count == 0) return;
            HistoryItem head = Items[0];
            if (!string.Equals(head.Url, url, StringComparison.OrdinalIgnoreCase)) return;
            if (head.Title == title) return;
            head.Title = title;
            Save();
        }

        public void Remove(HistoryItem item)
        {
            if (item == null) return;
            if (Items.Remove(item)) Save();
        }

        public void Clear()
        {
            Items.Clear();
            Save();
        }

        private void Trim()
        {
            while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
        }

        private void Sort()
        {
            Items.Sort(delegate(HistoryItem a, HistoryItem b)
            {
                if (a == null || b == null) return 0;
                return b.VisitedAt.CompareTo(a.VisitedAt);
            });
        }

        private void RaiseChanged()
        {
            Action h = Changed;
            if (h == null) return;
            try { h(); } catch (Exception) { }
        }

        // ---------- 时间工具 ----------

        public static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        public static DateTime ToLocal(long unixSeconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds).ToLocalTime();
        }

        /// <summary>分组名：今天 / 昨天 / 2026-09-13</summary>
        public static string DayLabel(DateTime local)
        {
            DateTime today = DateTime.Today;
            if (local.Date == today) return "今天";
            if (local.Date == today.AddDays(-1)) return "昨天";
            return local.ToString("yyyy-MM-dd");
        }
    }

    /// <summary>历史记录对话框：按天分组、新→旧展示；可打开、逐条删除、一键清空</summary>
    public class HistoryDialog : Form
    {
        private readonly ListView _list;
        private readonly HistoryStore _store;
        private readonly Action<string> _openInNewTab;

        public HistoryDialog(HistoryStore store, Action<string> openInNewTab)
        {
            _store = store;
            _openInNewTab = openInNewTab;

            Text = "历史记录";
            Size = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = true;
            _list.Dock = DockStyle.Fill;
            _list.ShowGroups = true;
            _list.Columns.Add("时间", 150);
            _list.Columns.Add("标题", 280);
            _list.Columns.Add("网址", 350);
            _list.DoubleClick += delegate { OpenSelected(); };

            var panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 48;
            panel.Padding = new Padding(8);
            panel.WrapContents = false;

            var btnOpen = new Button(); btnOpen.Text = "打开"; btnOpen.Width = 90;
            btnOpen.Click += delegate { OpenSelected(); };
            var btnDelete = new Button(); btnDelete.Text = "删除选中"; btnDelete.Width = 96;
            btnDelete.Click += delegate { DeleteSelected(); };
            var btnClear = new Button(); btnClear.Text = "清空全部"; btnClear.Width = 96;
            btnClear.Click += delegate { ClearAll(); };
            var btnClose = new Button(); btnClose.Text = "关闭"; btnClose.Width = 90;
            btnClose.Click += delegate { Close(); };

            panel.Controls.Add(btnOpen);
            panel.Controls.Add(btnDelete);
            panel.Controls.Add(btnClear);
            panel.Controls.Add(btnClose);

            Controls.Add(_list);
            Controls.Add(panel);

            // 外部（比如主窗口一键清空）改了历史，对话框跟着刷新
            _store.Changed += OnStoreChanged;
            FormClosed += delegate { _store.Changed -= OnStoreChanged; };
            Reload();
        }

        private void OnStoreChanged()
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(Reload));
            }
            catch (Exception) { }
        }

        private void Reload()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                _list.Groups.Clear();
                var groups = new Dictionary<string, ListViewGroup>();
                foreach (var it in _store.Items)
                {
                    if (it == null) continue;
                    DateTime t = HistoryStore.ToLocal(it.VisitedAt);
                    string label = HistoryStore.DayLabel(t);
                    ListViewGroup g;
                    if (!groups.TryGetValue(label, out g))
                    {
                        g = new ListViewGroup(label, HorizontalAlignment.Left);
                        groups[label] = g;
                        _list.Groups.Add(g);
                    }
                    var row = new ListViewItem(t.ToString("HH:mm:ss"));
                    row.SubItems.Add(string.IsNullOrEmpty(it.Title) ? "(无标题)" : it.Title);
                    row.SubItems.Add(it.Url ?? "");
                    row.Tag = it;
                    row.Group = g;
                    row.ToolTipText = (it.Title ?? "") + "\n" + (it.Url ?? "");
                    _list.Items.Add(row);
                }
            }
            finally { _list.EndUpdate(); }

            Text = "历史记录（共 " + _store.Items.Count + " 条）";
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var it = _list.SelectedItems[0].Tag as HistoryItem;
            if (it != null && !string.IsNullOrEmpty(it.Url) && _openInNewTab != null)
            {
                _openInNewTab(it.Url);
                Close();
            }
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var picked = new List<HistoryItem>();
            foreach (ListViewItem row in _list.SelectedItems)
            {
                var it = row.Tag as HistoryItem;
                if (it != null) picked.Add(it);
            }
            if (picked.Count == 0) return;
            var dr = MessageBox.Show(this, "确定要删除选中的 " + picked.Count + " 条记录吗？", "删除记录",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            for (int i = 0; i < picked.Count; i++) _store.Items.Remove(picked[i]);
            _store.Save();
            Reload();
        }

        private void ClearAll()
        {
            if (_store.Items.Count == 0) return;
            var dr = MessageBox.Show(this, "确定要清空全部历史记录吗？此操作不可撤销。", "清空历史记录",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr != DialogResult.Yes) return;
            _store.Clear();
            Reload();
        }
    }
}
