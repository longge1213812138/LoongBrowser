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
            Console.WriteLine();

            Console.WriteLine("== types matching Picture/Pip/Popup ==");
            foreach (var t in asm.GetExportedTypes())
            {
                string n = t.Name;
                if (n.IndexOf("Picture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Pip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  TYPE " + t.FullName);
            }
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2NewWindowRequestedEventArgs members ==");
            var ne = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs");
            if (ne != null)
            {
                foreach (var p in ne.GetProperties())
                    Console.WriteLine("  PROP " + p.PropertyType.Name + " " + p.Name);
                foreach (var m in ne.GetMethods())
                    if (m.DeclaringType == ne)
                        Console.WriteLine("  METHOD " + m.ReturnType.Name + " " + m.Name + "(" +
                            string.Join(", ", Array.ConvertAll(m.GetParameters(),
                                p => p.ParameterType.Name + " " + p.Name)) + ")");
            }
            Console.WriteLine();
            Console.WriteLine("== deferral types ==");
            foreach (var t in asm.GetExportedTypes())
                if (t.Name.IndexOf("Deferral", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  TYPE " + t.FullName);
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2 members matching Picture/Pip ==");
            foreach (var e2 in core.GetEvents())
                if (e2.Name.IndexOf("Picture", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  EVENT " + e2.Name);
            foreach (var m in core.GetMethods())
                if (m.Name.IndexOf("Picture", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  METHOD " + m.ReturnType.Name + " " + m.Name);
            Console.WriteLine("(以上为空表示 SDK 未暴露画中画相关 API)");
            Console.WriteLine();

            Console.WriteLine("== CoreWebView2WindowFeatures members ==");
            var wf = asm.GetType("Microsoft.Web.WebView2.Core.CoreWebView2WindowFeatures");
            if (wf != null)
            {
                foreach (var p in wf.GetProperties())
                    Console.WriteLine("  PROP " + p.PropertyType.Name + " " + p.Name);
            }
            Console.WriteLine();

            Console.WriteLine("== input / script injection APIs ==");
            foreach (var t in asm.GetExportedTypes())
            {
                bool interesting = t.Name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   t.Name.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!interesting) continue;
                foreach (var m in t.GetMethods())
                {
                    string n = m.Name;
                    if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") || n.StartsWith("remove_")) continue;
                    if (n.IndexOf("Send", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Input", StringComparison.OrdinalIgnoreCase) < 0 &&
                        n.IndexOf("Script", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine("  " + t.Name + "." + m.Name + "(" +
                        string.Join(", ", Array.ConvertAll(m.GetParameters(),
                            p => p.ParameterType.Name + " " + p.Name)) + ")");
                }
            }
            foreach (var m in core.GetMethods())
            {
                string n = m.Name;
                if (n.StartsWith("get_") || n.StartsWith("set_") || n.StartsWith("add_") || n.StartsWith("remove_")) continue;
                if (n.IndexOf("Send", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Input", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  CoreWebView2." + m.Name + "(" +
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
