// EncryptedStoreTest.cs —— H2 加密加固的回归测试（DPAPI 落盘 + EFS 目录加密）
//
// 覆盖两件事：
//   1) 浏览历史（history.json）不再明文落盘：磁盘上读不到 URL/标题，但应用能正常读回；
//   2) 升级前遗留的明文 history.json 仍要能读（否则老用户一升级历史就"消失"）。
// 全部走真实的 JsonStore / HistoryStore，不复制逻辑。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace LoongBrowser
{
    public static class EncryptedStoreTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();
        private static string _dir;

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "enc_tmp");
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (Exception) { }
            Directory.CreateDirectory(_dir);

            _sb.AppendLine("=== 本地数据加密（H2）回归测试 ===");
            _sb.AppendLine("临时目录: " + _dir);
            _sb.AppendLine();

            A_DpapiRoundTrip();
            B_LegacyPlainText();
            C_Robustness();
            D_HistoryStoreEndToEnd();
            E_UserDataDirEncryption();

            try { Directory.Delete(_dir, true); } catch (Exception) { }
            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        // ---------- A. DPAPI 往返 ----------

        private static void A_DpapiRoundTrip()
        {
            _sb.AppendLine("-- A. 加密落盘与读回 --");
            string path = Path.Combine(_dir, "a.json");

            var src = new List<HistoryItem>();
            src.Add(new HistoryItem { Title = "示例站点", Url = "https://secret-track.invalid/a?token=abc123", VisitedAt = 1700000000 });
            src.Add(new HistoryItem { Title = "Bilibili", Url = "https://www.bilibili.com/video/BV1xx411c7mD", VisitedAt = 1700000001 });

            JsonStore.SaveProtected(path, src);

            Judge("A1 文件已写出", File.Exists(path), path);
            Judge("A2 文件被标记为加密格式", JsonStore.IsEncrypted(path), "IsEncrypted=false");

            string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Judge("A3 磁盘上读不到明文网址",
                  raw.IndexOf("secret-track.invalid", StringComparison.OrdinalIgnoreCase) < 0,
                  "密文里竟然能搜到网址");
            Judge("A4 磁盘上读不到 token",
                  raw.IndexOf("token=abc123", StringComparison.OrdinalIgnoreCase) < 0,
                  "密文里竟然能搜到 token");
            Judge("A5 磁盘上读不到中文标题",
                  raw.IndexOf("示例站点", StringComparison.Ordinal) < 0,
                  "密文里竟然能搜到中文标题");
            // 注意：不能靠"密文里有没有 { 字符"来判断 —— 密文是随机字节，几百字节里撞出 '{' 的概率很高，
            // 断言会随机失败。要看的是"整体能不能被当 JSON 解析"。
            bool parsesAsJson = false;
            try
            {
                var probe = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .Deserialize<List<HistoryItem>>(raw);
                parsesAsJson = (probe != null);
            }
            catch (Exception) { parsesAsJson = false; }
            Judge("A6 已不是可直接解析的 JSON", !parsesAsJson, "密文竟能被当 JSON 解析");

            var back = JsonStore.LoadProtected<List<HistoryItem>>(path);
            Judge("A7 能读回对象", back != null, "返回 null");
            if (back != null)
            {
                Judge("A8 条数一致", back.Count == 2, "实际 " + back.Count);
                Judge("A9 网址一致", back[0].Url == src[0].Url, back[0].Url);
                Judge("A10 中文标题一致", back[0].Title == "示例站点", back[0].Title);
                Judge("A11 时间戳一致", back[1].VisitedAt == 1700000001, back[1].VisitedAt.ToString());
            }

            // 同一用户下重复存取应稳定
            JsonStore.SaveProtected(path, back);
            var again = JsonStore.LoadProtected<List<HistoryItem>>(path);
            Judge("A12 二次存取仍然正确", again != null && again.Count == 2 && again[0].Title == "示例站点", "");
            _sb.AppendLine();
        }

        // ---------- B. 兼容升级前的明文文件 ----------

        private static void B_LegacyPlainText()
        {
            _sb.AppendLine("-- B. 旧明文文件的兼容（老用户升级）--");
            string path = Path.Combine(_dir, "legacy.json");
            string legacy = "[{\"Title\":\"旧记录\",\"Url\":\"https://legacy.invalid/p\",\"VisitedAt\":1699000000}]";
            File.WriteAllText(path, legacy);

            Judge("B1 明文文件不被识别为加密格式", !JsonStore.IsEncrypted(path), "");
            var data = JsonStore.LoadProtected<List<HistoryItem>>(path);
            Judge("B2 旧明文历史仍能读出", data != null && data.Count == 1, data == null ? "null" : data.Count.ToString());
            if (data != null)
                Judge("B3 旧记录内容正确", data[0].Title == "旧记录" && data[0].Url == "https://legacy.invalid/p",
                      data[0].Title + " / " + data[0].Url);

            JsonStore.SaveProtected(path, data);
            Judge("B4 保存后自动转为加密格式", JsonStore.IsEncrypted(path), "仍是明文");
            var reread = JsonStore.LoadProtected<List<HistoryItem>>(path);
            Judge("B5 迁移后仍能读回", reread != null && reread.Count == 1 && reread[0].Title == "旧记录", "");
            _sb.AppendLine();
        }

        // ---------- C. 异常输入不崩 ----------

        private static void C_Robustness()
        {
            _sb.AppendLine("-- C. 异常输入 --");
            Judge("C1 文件不存在返回 null",
                  JsonStore.LoadProtected<List<HistoryItem>>(Path.Combine(_dir, "nope.json")) == null, "");

            string empty = Path.Combine(_dir, "empty.bin");
            File.WriteAllBytes(empty, new byte[0]);
            Judge("C2 空文件返回 null", JsonStore.LoadProtected<List<HistoryItem>>(empty) == null, "");

            string junk = Path.Combine(_dir, "junk.bin");
            File.WriteAllBytes(junk, new byte[] { 0x4C, 0x42, 0x45, 0x31, 0x99, 0x98, 0x97 });
            Judge("C3 带魔数但内容损坏 → 返回 null 且不抛异常",
                  JsonStore.LoadProtected<List<HistoryItem>>(junk) == null, "");

            string shortFile = Path.Combine(_dir, "short.bin");
            File.WriteAllBytes(shortFile, new byte[] { 0x41, 0x42 });
            Judge("C4 小于魔数长度的短文件 → 返回 null",
                  JsonStore.LoadProtected<List<HistoryItem>>(shortFile) == null, "");
            _sb.AppendLine();
        }

        // ---------- D. HistoryStore 端到端 ----------

        private static void D_HistoryStoreEndToEnd()
        {
            _sb.AppendLine("-- D. 浏览历史端到端（真实 HistoryStore）--");
            string saved = HistoryStore.FilePath;
            try
            {
                string path = Path.Combine(_dir, "history.json");
                HistoryStore.FilePath = path;

                var store = new HistoryStore();
                store.Add("加密后的历史", "https://private-visit.invalid/x?sid=zzz");
                // 时间戳精度是秒，同一秒内的两条记录重载后排序不稳定（既有行为），
                // 这里拉开一秒，才能确定性地验证"新→旧"
                System.Threading.Thread.Sleep(1100);
                store.Add("第二个站点", "https://second.invalid/y");

                Judge("D1 数据文件已写出", File.Exists(path), path);
                Judge("D2 落盘是密文格式", JsonStore.IsEncrypted(path), "IsEncrypted=false");
                string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
                Judge("D3 磁盘上读不到访问过的网址",
                      raw.IndexOf("private-visit.invalid", StringComparison.OrdinalIgnoreCase) < 0,
                      "历史仍以明文落盘");

                var reloaded = new HistoryStore();
                Judge("D4 重新载入条数一致", reloaded.Items.Count == 2, "实际 " + reloaded.Items.Count);
                if (reloaded.Items.Count == 2)
                {
                    Judge("D5 顺序仍为新→旧",
                          reloaded.Items[0].Url.IndexOf("second.invalid", StringComparison.OrdinalIgnoreCase) >= 0,
                          reloaded.Items[0].Url);
                    Judge("D6 中文标题完整保留", reloaded.Items[1].Title == "加密后的历史", reloaded.Items[1].Title);
                }

                reloaded.Clear();
                var after = new HistoryStore();
                Judge("D7 清空后落盘也生效", after.Items.Count == 0, "实际 " + after.Items.Count);
            }
            finally
            {
                HistoryStore.FilePath = saved;
            }
            _sb.AppendLine();
        }

        // ---------- E. 内核数据目录的 EFS 加密 ----------

        private static void E_UserDataDirEncryption()
        {
            _sb.AppendLine("-- E. 内核数据目录 EFS 加密 --");
            string dir = Path.Combine(_dir, "udf");
            bool ok = false;
            bool threw = false;
            try { ok = AppPaths.EnsureUserDataDirEncrypted(dir); }
            catch (Exception ex) { threw = true; _sb.AppendLine("       异常: " + ex.Message); }

            Judge("E1 调用不抛异常", !threw, "");
            Judge("E2 目录不存在时会自动创建", Directory.Exists(dir), dir);

            if (ok)
            {
                var di = new DirectoryInfo(dir);
                Judge("E3 目录已带加密属性", (di.Attributes & FileAttributes.Encrypted) != 0,
                      "Attributes=" + di.Attributes);
                // 加密标记应影响之后新建的文件
                string f = Path.Combine(dir, "new.bin");
                File.WriteAllBytes(f, new byte[] { 1, 2, 3 });
                bool fileEncrypted = false;
                try { fileEncrypted = (new FileInfo(f).Attributes & FileAttributes.Encrypted) != 0; }
                catch (Exception) { }
                Judge("E4 新建文件自动继承加密", fileEncrypted, "新文件未加密");
                bool again = AppPaths.EnsureUserDataDirEncrypted(dir);
                Judge("E5 重复调用幂等", again, "第二次返回 false");
            }
            else
            {
                _sb.AppendLine("       [SKIP] 本机不支持 EFS（家庭版/非 NTFS），已按设计静默跳过");
            }
            _sb.AppendLine();
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
