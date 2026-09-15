// BookmarkStore.cs —— 书签模块：数据存取 + 书签管理对话框
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoongBrowser
{
    public class BookmarkItem
    {
        public string Title;
        public string Url;
    }

    public class BookmarkStore
    {
        private static readonly string FilePath = Path.Combine(AppPaths.DataDir, "bookmarks.json");
        public List<BookmarkItem> Items = new List<BookmarkItem>();

        /// <summary>书签增/删/改后触发（新标签页据此刷新）</summary>
        public event Action Changed;

        public BookmarkStore()
        {
            var data = JsonStore.Load<List<BookmarkItem>>(FilePath);
            if (data != null) Items = data;
        }

        public void Save()
        {
            JsonStore.Save(FilePath, Items);
            RaiseChanged();
        }

        public void Add(string title, string url)
        {
            Items.Add(new BookmarkItem { Title = title, Url = url });
            Save();
        }

        /// <summary>删除一个书签</summary>
        public void Remove(BookmarkItem item)
        {
            if (item == null) return;
            if (Items.Remove(item)) Save();
        }

        /// <summary>该地址是否已收藏（宿主用它校验新标签页发来的跳转请求）</summary>
        public bool Contains(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            foreach (var b in Items)
                if (b != null && string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void RaiseChanged()
        {
            Action h = Changed;
            if (h == null) return;
            try { h(); } catch (Exception) { }
        }
    }

    /// <summary>书签管理器对话框：列表展示，双击或"打开"在新标签打开，可删除</summary>
    public class BookmarkDialog : Form
    {
        private ListView _list;
        private readonly BookmarkStore _store;
        private readonly Action<string> _openInNewTab;

        public BookmarkDialog(BookmarkStore store, Action<string> openInNewTab)
        {
            _store = store;
            _openInNewTab = openInNewTab;

            Text = "书签管理";
            Size = new Size(660, 440);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("标题", 280);
            _list.Columns.Add("网址", 320);
            _list.DoubleClick += delegate { OpenSelected(); };

            var panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 46;
            panel.Padding = new Padding(8);
            var btnOpen = new Button();
            btnOpen.Text = "打开";
            btnOpen.Width = 90;
            btnOpen.Click += delegate { OpenSelected(); };
            var btnDelete = new Button();
            btnDelete.Text = "删除";
            btnDelete.Width = 90;
            btnDelete.Click += delegate { DeleteSelected(); };
            var btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Width = 90;
            btnClose.Click += delegate { Close(); };
            panel.Controls.Add(btnOpen);
            panel.Controls.Add(btnDelete);
            panel.Controls.Add(btnClose);

            Controls.Add(_list);
            Controls.Add(panel);
            Reload();
        }

        private void Reload()
        {
            _list.Items.Clear();
            foreach (var b in _store.Items)
            {
                var it = new ListViewItem(string.IsNullOrEmpty(b.Title) ? "(无标题)" : b.Title);
                it.SubItems.Add(b.Url ?? "");
                it.Tag = b;
                _list.Items.Add(it);
            }
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var b = _list.SelectedItems[0].Tag as BookmarkItem;
            if (b != null && !string.IsNullOrEmpty(b.Url) && _openInNewTab != null)
            {
                _openInNewTab(b.Url);
                Close();
            }
        }

        private void DeleteSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var b = _list.SelectedItems[0].Tag as BookmarkItem;
            if (b != null)
            {
                _store.Remove(b);
                Reload();
            }
        }
    }
}
