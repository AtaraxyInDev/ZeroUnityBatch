using System;
using Avalonia;

namespace Zub.App
{
    internal static class Program
    {
        /// <summary>Ruta a abrir al arrancar (--open, o el primer argumento que sea carpeta).</summary>
        public static string StartupPath;

        /// <summary>Búsqueda automática para probar el flujo sin intervención (--search).</summary>
        public static string AutoSearch;

        /// <summary>Carpeta de cambios a cargar en la cola al arrancar (--import).</summary>
        public static string AutoImport;
        public static bool AutoSave;

        [STAThread]
        public static void Main(string[] args)
        {
            // Diagnóstico sin ventana: --selftest <juego> <salida.txt>
            if (args.Length >= 3 && args[0] == "--selftest")
            {
                var sb = new System.Text.StringBuilder();
                try
                {
                    using var proj = new UnityProject();
                    proj.Open(args[1], m => sb.AppendLine(m));
                    sb.AppendLine($"unity={proj.UnityVersion} contenedores={proj.LoadedContainers.Count} objetos={proj.Assets.Count}");
                    System.IO.File.WriteAllText(args[2], sb.ToString());
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("ERROR: " + ex);
                    System.IO.File.WriteAllText(args[2], sb.ToString());
                    Environment.Exit(2);
                }
            }

            if (Array.Exists(args, a => a == "--tools"))
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "zub_tools.txt"),
                    Zub.AndroidTools.Resumen() + Environment.NewLine + "portátil=" + Zub.AndroidTools.Portatil);
                Environment.Exit(0);
            }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--open": if (i + 1 < args.Length) StartupPath = args[++i]; break;
                    case "--search": if (i + 1 < args.Length) AutoSearch = args[++i]; break;
                    case "--import": if (i + 1 < args.Length) AutoImport = args[++i]; break;
                    case "--save": AutoSave = true; break;
                }
            }
            if (string.IsNullOrEmpty(StartupPath) && args.Length == 1 && System.IO.Directory.Exists(args[0]))
                StartupPath = args[0];

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
