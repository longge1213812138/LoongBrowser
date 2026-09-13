// JsonStore.cs —— 极简 JSON 读写工具 + 应用目录约定
using System;
using System.IO;
using System.Web.Script.Serialization;

namespace LoongBrowser
{
    /// <summary>应用所有本地数据的存放位置约定</summary>
    public static class AppPaths
    {
        /// <summary>书签、下载记录等用户数据目录：%APPDATA%\LoongBrowser</summary>
        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoongBrowser"); }
        }

        /// <summary>WebView2 用户数据目录（缓存/Cookie 等所在）：%LOCALAPPDATA%\LoongBrowser\WebView2</summary>
        public static string UserDataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoongBrowser", "WebView2"); }
        }

        /// <summary>默认下载目录：系统"下载"文件夹</summary>
        public static string DownloadsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); }
        }

        public static void EnsureDataDir()
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
        }
    }

    /// <summary>基于 .NET 自带 JavaScriptSerializer 的极简 JSON 存取（无第三方依赖）</summary>
    public static class JsonStore
    {
        public static void Save(string path, object obj)
        {
            try
            {
                AppPaths.EnsureDataDir();
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(obj));
            }
            catch (Exception)
            {
                // 保存失败静默处理，不中断主流程
            }
        }

        public static T Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                var ser = new JavaScriptSerializer();
                return ser.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
