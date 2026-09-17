// ApiDump.cs - dump WebView2 context menu API names via reflection
using System;
using System.Reflection;

public static class ApiDump
{
    public static int Main()
    {
        try
        {
            var asm = Assembly.LoadFrom(@"C:\Users\91533\Desktop\浏览器\libs\Microsoft.Web.WebView2.Core.dll");
            Console.WriteLine("== KIND ENUM ==");
            var t1 = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind");
            if (t1 != null) foreach (var n in Enum.GetNames(t1)) Console.WriteLine("  " + n);
            Console.WriteLine("== ITEM EVENT ==");
            var t2 = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItem");
            if (t2 != null)
                foreach (var e in t2.GetEvents())
                {
                    Console.WriteLine("  EVENT " + e.Name + " : " + e.EventHandlerType.Name);
                    if (e.EventHandlerType.IsGenericType)
                        Console.WriteLine("    ARGS: " + e.EventHandlerType.GetGenericArguments()[0].FullName);
                }
            Console.WriteLine("== ENV CREATE ==");
            var t3 = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment");
            if (t3 != null)
                foreach (var m in t3.GetMethods())
                    if (m.Name.Contains("ContextMenuItem"))
                        Console.WriteLine("  METHOD " + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name)) + ")");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR " + ex.Message);
            return 1;
        }
    }
}
