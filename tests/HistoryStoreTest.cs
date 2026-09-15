// HistoryStoreTest.cs —— 浏览历史存储的单元测试（直接调用真实的 HistoryStore）
// 数据文件指向测试自己目录，不动用户的 %APPDATA%\LoongBrowser\history.json。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace LoongBrowser
{
    public static class HistoryStoreTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder _sb = new StringBuilder();
        private static string _dir;

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }

            _dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "hist_tmp");
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (Exception) { }
            Directory.CreateDirectory(_dir);
            HistoryStore.FilePath = Path.Combine(_dir, "history.json");

            _sb.AppendLine("=== 浏览历史单元测试 ===");
            _sb.AppendLine("数据文件: " + HistoryStore.FilePath);
            _sb.AppendLine();

            _sb.AppendLine("-- A. 记录与顺序 --");
            var store = new HistoryStore();
            store.Add("Example Domain", "https://example.com/");
            store.Add("Example Org", "https://example.org/");
            store.Add("哔哩哔哩", "https://www.bilibili.com/video/BV1xx411c7mD");
            Judge("A1 三条都已记录", store.Items.Count == 3, "实际 " + store.Items.Count);
            Judge("A2 新→旧排列（最近的在最前）",
                  store.Items[0].Url.IndexOf("bilibili", StringComparison.OrdinalIgnoreCase) >= 0,
                  "Items[0]=" + store.Items[0].Url);
            Judge("A3 最早的在最后",
                  store.Items[2].Url.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0,
                  "Items[2]=" + store.Items[2].Url);
            Judge("A4 记录了时间戳", store.Items[0].VisitedAt > 0, "VisitedAt=" + store.Items[0].VisitedAt);

            _sb.AppendLine();
            _sb.AppendLine("-- B. 只记可导航地址 --");
            int before = store.Items.Count;
            store.Add("空白页", "about:blank");
            store.Add("脚本", "javascript:void(0)");
            store.Add("数据", "data:text/html,x");
            store.Add("空地址", "");
            Judge("B1 内部页/脚本/空地址都不记", store.Items.Count == before, "实际 " + store.Items.Count);
            store.Add("本地文件", "file:///C:/docs/a.html");
            Judge("B2 file:// 会记", store.Items.Count == before + 1, "实际 " + store.Items.Count);

            _sb.AppendLine();
            _sb.AppendLine("-- C. 同址去重（防重定向/刷新刷屏）--");
            // 去重只作用于"紧接着的同址访问"：中间隔了别的地址，就算新的一次访问
            int c0 = store.Items.Count;
            store.Add("去重测试", "https://dup-test.invalid/1");
            Judge("C1 首次访问会记一条", store.Items.Count == c0 + 1, "实际 " + store.Items.Count);

            int c1 = store.Items.Count;
            long cTime = store.Items[0].VisitedAt;
            System.Threading.Thread.Sleep(1100);
            store.Add("去重测试（重复）", "https://dup-test.invalid/1");
            Judge("C2 紧接着同址重复访问不新增记录", store.Items.Count == c1, "实际 " + store.Items.Count);
            Judge("C3 时间戳被刷新", store.Items[0].VisitedAt > cTime, cTime + " → " + store.Items[0].VisitedAt);
            Judge("C4 标题被刷新", store.Items[0].Title == "去重测试（重复）", "实际: " + store.Items[0].Title);

            store.Add("别的地址", "https://other-c.invalid/2");
            int c2 = store.Items.Count;
            store.Add("去重测试", "https://dup-test.invalid/1");
            Judge("C5 中间访问过别的地址后，再回来算新的一次访问", store.Items.Count == c2 + 1,
                  "实际 " + store.Items.Count);

            int saved = HistoryStore.DedupeSeconds;
            HistoryStore.DedupeSeconds = -1;     // 负数 = 关闭去重
            int c3 = store.Items.Count;
            store.Add("去重测试", "https://dup-test.invalid/1");
            Judge("C6 关闭去重后每次访问都记一条", store.Items.Count == c3 + 1, "实际 " + store.Items.Count);
            HistoryStore.DedupeSeconds = saved;

            _sb.AppendLine();
            _sb.AppendLine("-- D. 标题为空的回退与回填 --");
            store.Add("", "https://title-empty.invalid/x");
            Judge("D1 空标题用网址顶上",
                  store.Items[0].Title.IndexOf("title-empty.invalid", StringComparison.OrdinalIgnoreCase) >= 0,
                  "实际标题: " + store.Items[0].Title);
            store.TouchTitle("https://title-empty.invalid/x", "后来才就绪的标题");
            Judge("D2 TouchTitle 回填最近一条同址记录", store.Items[0].Title == "后来才就绪的标题",
                  "实际: " + store.Items[0].Title);
            store.TouchTitle("https://not-the-head.invalid/", "不该生效");
            Judge("D3 TouchTitle 不碰其它记录", store.Items[0].Title == "后来才就绪的标题",
                  "实际: " + store.Items[0].Title);

            _sb.AppendLine();
            _sb.AppendLine("-- E. 删除 / 清空 / 上限 --");
            int n1 = store.Items.Count;
            store.Remove(store.Items[n1 / 2]);
            Judge("E1 删除单条", store.Items.Count == n1 - 1, "实际 " + store.Items.Count);

            int oldMax = HistoryStore.MaxItems;
            HistoryStore.MaxItems = 5;
            for (int i = 0; i < 12; i++) store.Add("第 " + i + " 页", "https://cap" + i + ".invalid/p");
            Judge("E2 超出上限丢最旧的（保留 5 条）", store.Items.Count == 5, "实际 " + store.Items.Count);
            Judge("E3 保留的是最新的",
                  store.Items[0].Url.IndexOf("cap11", StringComparison.OrdinalIgnoreCase) >= 0,
                  "Items[0]=" + store.Items[0].Url);
            HistoryStore.MaxItems = oldMax;

            _sb.AppendLine();
            _sb.AppendLine("-- F. 落盘与重新载入 --");
            Judge("F1 数据文件已写出", File.Exists(HistoryStore.FilePath), HistoryStore.FilePath);
            int count = store.Items.Count;
            var reloaded = new HistoryStore();
            Judge("F2 重新载入条数一致", reloaded.Items.Count == count,
                  count + " → " + reloaded.Items.Count);
            Judge("F3 重新载入后仍按新→旧", reloaded.Items.Count > 1 &&
                  reloaded.Items[0].VisitedAt >= reloaded.Items[reloaded.Items.Count - 1].VisitedAt, "");

            int beforeClear = reloaded.Items.Count;
            reloaded.Clear();
            Judge("F4 清空", reloaded.Items.Count == 0, "清空前 " + beforeClear);
            var afterClear = new HistoryStore();
            Judge("F5 清空后落盘也生效", afterClear.Items.Count == 0, "实际 " + afterClear.Items.Count);

            _sb.AppendLine();
            _sb.AppendLine("-- G. 时间工具 --");
            Judge("G1 今天分组名", HistoryStore.DayLabel(DateTime.Today) == "今天",
                  HistoryStore.DayLabel(DateTime.Today));
            Judge("G2 昨天分组名", HistoryStore.DayLabel(DateTime.Today.AddDays(-1)) == "昨天",
                  HistoryStore.DayLabel(DateTime.Today.AddDays(-1)));
            string d3 = HistoryStore.DayLabel(DateTime.Today.AddDays(-7));
            Judge("G3 更早用日期", d3.Length == 10 && d3.IndexOf("-") > 0, d3);
            long now = HistoryStore.Now();
            Judge("G4 Unix 时间与本地时间互转",
                  Math.Abs((HistoryStore.ToLocal(now) - DateTime.Now).TotalMinutes) < 2, "");

            try { Directory.Delete(_dir, true); } catch (Exception) { }
            _sb.AppendLine();
            _sb.AppendLine("=== 结果：通过 " + _pass + " 项，失败 " + _fail + " 项 ===");
            Console.WriteLine(_sb.ToString());
            return _fail == 0 ? 0 : 1;
        }

        private static void Judge(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _sb.AppendLine((ok ? "[PASS] " : "[FAIL] ") + name);
            if (!ok && !string.IsNullOrEmpty(detail)) _sb.AppendLine("       " + detail);
        }
    }
}
