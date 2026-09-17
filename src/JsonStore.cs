// JsonStore.cs —— 极简 JSON 读写工具 + 应用目录约定
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

        /// <summary>
        /// 内核数据目录（Cookie / LocalStorage / 缓存在此）落盘前的准备工作：
        /// 目录不存在则创建，并尝试打开 EFS 加密（NTFS 文件系统级加密，对用户与内核完全透明）。
        ///
        /// 说明两点边界：
        ///   1) 目录打上加密标记只影响**之后新建**的文件，已经存在的旧文件不会被回溯加密
        ///      （内核正占用它们，强行递归加密会失败且拖慢启动）。首次安装时目录为空，等于全部生效。
        ///   2) Windows 家庭版、非 NTFS 分区不支持 EFS，设置会失败 —— 这里静默跳过，不影响正常使用。
        ///      该文件夹本身受用户账户 ACL 保护，仍优于"完全明文"。
        /// </summary>
        /// <param name="dir">目标目录，默认即 UserDataDir（测试可传入临时目录）</param>
        /// <returns>是否成功开启了加密</returns>
        public static bool EnsureUserDataDirEncrypted(string dir = null)
        {
            if (string.IsNullOrEmpty(dir)) dir = UserDataDir;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var di = new DirectoryInfo(dir);
                if ((di.Attributes & FileAttributes.Encrypted) != 0) return true;
                di.Attributes = di.Attributes | FileAttributes.Encrypted;
                return (new DirectoryInfo(dir).Attributes & FileAttributes.Encrypted) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>基于 .NET 自带 JavaScriptSerializer 的极简 JSON 存取（无第三方依赖）</summary>
    public static class JsonStore
    {
        /// <summary>加密文件的魔数 "LBE1"：用来区分加密后的数据与升级前的明文 JSON</summary>
        private static readonly byte[] Magic = new byte[] { 0x4C, 0x42, 0x45, 0x31 };

        /// <summary>DPAPI 附加熵：让密文只对本应用有意义（同一用户下其它程序解不开）</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LoongBrowser.LocalData.v1");

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

        // ---------- 加密存取（DPAPI，当前用户） ----------

        /// <summary>
        /// 以 DPAPI（DataProtectionScope.CurrentUser）加密后落盘。
        /// 用途：浏览历史这类隐私数据不应以明文 JSON 躺在 %APPDATA% 里 ——
        /// 否则任何以该用户运行的程序/脚本都能直接读走完整访问记录（含带 token 的查询串）。
        /// 加密密钥由系统按用户账户派生，同一用户、同一台机器上可无缝读回，不增加任何用户操作。
        /// </summary>
        public static void SaveProtected(string path, object obj)
        {
            string json;
            try
            {
                var ser = new JavaScriptSerializer();
                json = ser.Serialize(obj);
            }
            catch (Exception) { return; }

            try
            {
                byte[] body = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy,
                    DataProtectionScope.CurrentUser);
                byte[] all = new byte[Magic.Length + body.Length];
                Buffer.BlockCopy(Magic, 0, all, 0, Magic.Length);
                Buffer.BlockCopy(body, 0, all, Magic.Length, body.Length);
                AppPaths.EnsureDataDir();
                File.WriteAllBytes(path, all);
            }
            catch (Exception)
            {
                // 加密不可用时（极少见）退回明文，宁可弱保护也不要丢数据
                Save(path, obj);
            }
        }

        /// <summary>
        /// 读取 <see cref="SaveProtected"/> 写出的文件。
        /// 兼容两种格式：带魔数的密文，以及升级前遗留的明文 JSON（下次保存会自动转成密文）。
        /// 解密失败（换了系统用户、文件被外部改坏）时返回 null，不抛异常。
        /// </summary>
        public static T LoadProtected<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] all = File.ReadAllBytes(path);
                if (all.Length < Magic.Length) return null;

                var ser = new JavaScriptSerializer();
                if (!HasMagic(all))
                    return ser.Deserialize<T>(Encoding.UTF8.GetString(all));    // 旧明文文件

                byte[] body = new byte[all.Length - Magic.Length];
                Buffer.BlockCopy(all, Magic.Length, body, 0, body.Length);
                byte[] json = ProtectedData.Unprotect(body, Entropy, DataProtectionScope.CurrentUser);
                return ser.Deserialize<T>(Encoding.UTF8.GetString(json));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>文件是否是加密格式（供测试与诊断用）</summary>
        public static bool IsEncrypted(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] head = new byte[Magic.Length];
                    if (fs.Read(head, 0, head.Length) != Magic.Length) return false;
                    return HasMagic(head);
                }
            }
            catch (Exception) { return false; }
        }

        private static bool HasMagic(byte[] data)
        {
            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) return false;
            return true;
        }
    }
}
