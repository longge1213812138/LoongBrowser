// DownloadStore.cs —— 下载模块：下载事件监听 + 记录持久化 + 管理对话框
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace LoongBrowser
{
    public class DownloadItem
    {
        public string Url;
        public string FileName;
        public string FilePath;
        public string Status;
        public string Time;
    }

    public class DownloadStore
    {
        private static readonly string FilePath = Path.Combine(AppPaths.DataDir, "downloads.json");
        public List<DownloadItem> Items = new List<DownloadItem>();

        public DownloadStore()
        {
            var data = JsonStore.Load<List<DownloadItem>>(FilePath);
            if (data != null) Items = data;
        }

        public void Save()
        {
            JsonStore.Save(FilePath, Items);
        }

        /// <summary>在 DownloadStarting 事件中调用：登记一次下载并跟踪状态</summary>
        public void Track(CoreWebView2DownloadOperation op)
        {
            try
            {
                var item = new DownloadItem();
                item.Url = op.Uri ?? "";
                item.FilePath = op.ResultFilePath ?? "";
                item.FileName = "";
                try
                {
                    if (item.FilePath.Length > 0) item.FileName = Path.GetFileName(item.FilePath);
                }
                catch (Exception) { }
                if (item.FileName.Length == 0) item.FileName = "未命名文件";
                item.Status = "下载中";
                item.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                Items.Insert(0, item);
                Save();

                op.StateChanged += delegate
                {
                    try
                    {
                        if (op.State == CoreWebView2DownloadState.Completed) item.Status = "已完成";
                        else if (op.State == CoreWebView2DownloadState.Interrupted) item.Status = "已中断";
                        Save();
                    }
                    catch (Exception) { }
                };
            }
            catch (Exception) { }
        }
    }

    /// <summary>下载管理对话框：列表展示 + 打开文件/文件夹/删除记录</summary>
    public class DownloadDialog : Form
    {
        private ListView _list;
        private readonly DownloadStore _store;

        public DownloadDialog(DownloadStore store)
        {
            _store = store;

            Text = "下载管理";
            Size = new Size(760, 460);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("文件名", 220);
            _list.Columns.Add("状态", 80);
            _list.Columns.Add("时间", 140);
            _list.Columns.Add("网址", 260);
            _list.DoubleClick += delegate { OpenFile(); };

            var panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 46;
            panel.Padding = new Padding(8);
            var btnOpen = new Button();
            btnOpen.Text = "打开文件";
            btnOpen.Width = 90;
            btnOpen.Click += delegate { OpenFile(); };
            var btnFolder = new Button();
            btnFolder.Text = "打开文件夹";
            btnFolder.Width = 100;
            btnFolder.Click += delegate { OpenFolder(); };
            var btnDelete = new Button();
            btnDelete.Text = "删除记录";
            btnDelete.Width = 90;
            btnDelete.Click += delegate { DeleteSelected(); };
            var btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.Width = 70;
            btnClose.Click += delegate { Close(); };
            panel.Controls.Add(btnOpen);
            panel.Controls.Add(btnFolder);
            panel.Controls.Add(btnDelete);
            panel.Controls.Add(btnClose);

            Controls.Add(_list);
            Controls.Add(panel);
            Reload();
        }

        private void Reload()
        {
            _list.Items.Clear();
            foreach (var d in _store.Items)
            {
                var it = new ListViewItem(d.FileName ?? "");
                it.SubItems.Add(d.Status ?? "");
                it.SubItems.Add(d.Time ?? "");
                it.SubItems.Add(d.Url ?? "");
                it.Tag = d;
                _list.Items.Add(it);
            }
        }

        private DownloadItem Selected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as DownloadItem;
        }

        private void OpenFile()
        {
            var d = Selected();
            if (d == null || string.IsNullOrEmpty(d.FilePath)) return;
            try
            {
                if (File.Exists(d.FilePath)) Process.Start(d.FilePath);
                else MessageBox.Show("文件不存在：" + d.FilePath, "提示");
            }
            catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message, "错误"); }
        }

        private void OpenFolder()
        {
            var d = Selected();
            if (d == null || string.IsNullOrEmpty(d.FilePath)) return;
            try
            {
                string dir = Path.GetDirectoryName(d.FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start("explorer.exe", "/select,\"" + d.FilePath + "\"");
                else MessageBox.Show("文件夹不存在", "提示");
            }
            catch (Exception ex) { MessageBox.Show("打开失败：" + ex.Message, "错误"); }
        }

        private void DeleteSelected()
        {
            var d = Selected();
            if (d != null)
            {
                _store.Items.Remove(d);
                _store.Save();
                Reload();
            }
        }
    }
}
