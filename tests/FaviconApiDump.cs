// FaviconApiDump.cs - dump WebView2 favicon related API names via reflection
using System;
using System.Reflection;

public static class FaviconApiDump
{
    public static int Main()
    {
        try
        {
            var asm = Assembly.LoadFrom(@"C:\Users\91533\Desktop\浏览器\libs\Microsoft.Web.WebView2.Core.dll");
            Console.WriteLine("asm: " + asm.GetName().Version);

            Console.WriteLine("== types matching Favicon ==");
            foreach (var t in asm.GetExportedTypes())
                if (t.Name.IndexOf("Favicon", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  TYPE " + t.FullName + "  isEnum=" + t.IsEnum);
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2FaviconImageFormat values ==");
            var fmt = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2FaviconImageFormat");
            if (fmt != null) foreach (var n in Enum.GetNames(fmt)) Console.WriteLine("  " + n);
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2 members matching Favicon ==");
            var core = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2");
            foreach (var e in core.GetEvents())
                if (e.Name.IndexOf("Favicon", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  EVENT " + e.Name + " : " + e.EventHandlerType.FullName);
            foreach (var m in core.GetMethods())
                if (m.Name.IndexOf("Favicon", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  METHOD " + m.ReturnType.Name + " " + m.Name + "(" +
                        string.Join(", ", Array.ConvertAll(m.GetParameters(),
                            p => p.ParameterType.Name + " " + p.Name)) + ")");
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2 members matching 'Icon' ==");
            foreach (var m in core.GetMethods())
                if (m.Name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  METHOD " + m.ReturnType.Name + " " + m.Name);
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2 navigate / message members ==");
            foreach (var m in core.GetMethods())
                if (m.Name.IndexOf("Navigate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Name.IndexOf("WebMessage", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  METHOD " + m.ReturnType.Name + " " + m.Name + "(" +
                        string.Join(", ", Array.ConvertAll(m.GetParameters(),
                            p => p.ParameterType.Name + " " + p.Name)) + ")");
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2WebMessageReceivedEventArgs members ==");
            var wm = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs");
            if (wm != null)
            {
                foreach (var p in wm.GetProperties())
                    Console.WriteLine("  PROP " + p.PropertyType.Name + " " + p.Name);
                foreach (var m in wm.GetMethods())
                    if (m.DeclaringType == wm)
                        Console.WriteLine("  METHOD " + m.ReturnType.Name + " " + m.Name + "(" +
                            string.Join(", ", Array.ConvertAll(m.GetParameters(),
                                p => p.ParameterType.Name + " " + p.Name)) + ")");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}
