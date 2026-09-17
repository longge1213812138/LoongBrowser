// SameSiteTest.cs —— IsSameSite 判定逻辑单元测试（与 TabManager.cs 中实现保持一致）
using System;

public static class SameSiteLogic
{
    public static string NormHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return host ?? "";
        string[] parts = host.Split('.');
        if (parts.Length <= 2) return host;
        return parts[parts.Length - 2] + "." + parts[parts.Length - 1];
    }

    public static bool IsSameSite(string current, string target)
    {
        try
        {
            if (string.IsNullOrEmpty(target)) return true;
            Uri tu;
            if (!Uri.TryCreate(target, UriKind.Absolute, out tu)) return true;
            if (tu.Scheme == "about" || tu.Scheme == "javascript" || tu.Scheme == "data") return true;
            Uri cu;
            if (string.IsNullOrEmpty(current) || !Uri.TryCreate(current, UriKind.Absolute, out cu)) return true;
            if (cu.Host.Length == 0) return true;
            return NormHost(cu.Host) == NormHost(tu.Host);
        }
        catch { return false; }
    }
}

public static class SameSiteTest
{
    public static int Main()
    {
        int pass = 0;
        object[,] cases = new object[,] {
            { "https://www.bing.com/search?q=a", "https://www.bing.com/images", true,  "same site -> stay in tab" },
            { "https://www.bing.com/search?q=a", "https://cn.bing.com/x",       true,  "same root domain -> stay" },
            { "https://www.bing.com/search?q=a", "https://www.baidu.com/s",     false, "cross site -> new tab" },
            { "https://example.com/page",        "https://sub.example.com/x",   true,  "subdomain -> stay" },
            { "https://example.com/page",        "https://deep.a.b.example.com", true, "deep subdomain -> stay" },
            { "https://www.bing.com/search?q=a", "https://evil-bing.com",       false, "lookalike domain -> new tab" },
            { "about:blank",                     "https://example.com",         true,  "blank start -> stay" },
            { "https://example.com",             "about:blank",                 true,  "about scheme -> stay" },
            { "",                                "https://example.com",         true,  "no current page -> stay" },
            { "https://a.com",                   "not a url",                   true,  "invalid url -> stay" }
        };
        for (int i = 0; i < cases.GetLength(0); i++)
        {
            bool expected = (bool)cases[i, 2];
            bool actual = SameSiteLogic.IsSameSite((string)cases[i, 0], (string)cases[i, 1]);
            bool ok = expected == actual;
            if (ok) pass++;
            Console.WriteLine((ok ? "PASS" : "FAIL") + "  expected=" + expected + " got=" + actual + "  " + cases[i, 3]);
        }
        Console.WriteLine("passed: " + pass + "/" + cases.GetLength(0));
        return pass == cases.GetLength(0) ? 0 : 1;
    }
}
