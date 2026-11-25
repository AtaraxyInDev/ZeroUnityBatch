using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Zub
{
    /// <summary>
    /// Todo lo de Android en un solo sitio: descomprimir el APK, sacar las DLLs del
    /// IL2CPP y volver a empaquetar y firmar. Usa las herramientas del sistema
    /// (apktool, build-tools, uber-apk-signer) que se buscan solas.
    /// </summary>
    public static class AndroidTools
    {
        public static string Java { get; set; }
        public static string Apktool { get; set; }
        public static string Zipalign { get; set; }
        public static string ApkSigner { get; set; }
        public static string UberSigner { get; set; }
        public static string KeyStore { get; set; }
        public static string KeyStorePass { get; set; } = "android";
        public static string Il2CppDumper { get; set; }

        public static bool Listo => !string.IsNullOrEmpty(Apktool) && File.Exists(Apktool) &&
                                    !string.IsNullOrEmpty(Java) && File.Exists(Java);

        /// <summary>Carpeta tools que viaja junto al .exe.</summary>
        public static string ToolsDir =>
            Environment.GetEnvironmentVariable("ZUB_TOOLS") ?? Path.Combine(AppContext.BaseDirectory, "tools");

        private static string EnTools(params string[] partes)
        {
            string p = Path.Combine(ToolsDir, Path.Combine(partes));
            return File.Exists(p) ? p : null;
        }

        public static bool Portatil =>
            EnTools("jre", "bin", "java.exe") != null &&
            EnTools("apktool.jar") != null;

        public static void Detect()
        {
            // 1º lo que viene con el programa (plug and play)
            Java ??= EnTools("jre", "bin", "java.exe");
            Apktool ??= EnTools("apktool.jar");
            Zipalign ??= EnTools("zipalign.exe");
            ApkSigner ??= EnTools("apksigner", "apksigner.jar");
            KeyStore ??= EnTools("debug.keystore");
            UberSigner ??= EnTools("uber-apk-signer.jar");
            Il2CppDumper ??= EnTools("Il2CppDumper", "Il2CppDumper.exe");

            Java ??= Buscar(new[]
            {
                @"C:\Program Files\Eclipse Adoptium\jdk-25.0.3.9-hotspot\bin\java.exe",
                @"C:\Program Files\Eclipse Adoptium\jdk-21.0.5.11-hotspot\bin\java.exe",
                @"C:\Program Files\Java\jdk-21\bin\java.exe"
            }, @"C:\Program Files\Eclipse Adoptium\*\bin\java.exe", @"C:\Program Files\Java\*\bin\java.exe")
                ?? BuscarEn("java.exe");

            Apktool ??= Buscar(new[] { @"C:\Android\ApkTool\apktool.jar", @"C:\Android\apktool.jar" })
                ?? Buscar(null, @"C:\Android\*\apktool*.jar");

            UberSigner ??= Buscar(null, @"C:\Android\UberApkSigner\uber-apk-signer*.jar")
                ?? Buscar(null, @"C:\Android\*\uber-apk-signer*.jar");

            Zipalign ??= Buscar(null, @"C:\Android\build-tools\*\zipalign.exe")
                ?? Buscar(null, @"C:\Users\*\Android\Sdk\build-tools\*\zipalign.exe");

            ApkSigner ??= Buscar(null, @"C:\Android\build-tools\*\apksigner.bat")
                ?? Buscar(null, @"C:\Users\*\Android\Sdk\build-tools\*\apksigner.bat");

            KeyStore ??= Buscar(new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "debug.keystore")
            });

            Il2CppDumper ??= Buscar(new[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools", "Il2CppDumper", "Il2CppDumper.exe"),
                @"C:\Users\mrzero\Documents\Programas Traduccion\Unity\Il2CppDumper-win-v6.7.46\Il2CppDumper.exe"
            });
        }

        public sealed class HerramientaDato
        {
            public string Grupo = "";
            public string Nombre = "";
            public string Ruta;
            public string Nota = "";
            public bool Opcional;
            public bool Ok => !string.IsNullOrEmpty(Ruta) && File.Exists(Ruta);
        }

        /// <summary>Lista agrupada de herramientas para mostrarla en la interfaz.</summary>
        public static List<HerramientaDato> Detalle()
        {
            Detect();

            return new List<HerramientaDato>
            {
                new HerramientaDato { Grupo = "Java", Nombre = "Java (JRE)", Ruta = Java,
                    Nota = "lo usan apktool y apksigner" },

                new HerramientaDato { Grupo = "Empaquetar", Nombre = "apktool", Ruta = Apktool,
                    Nota = "descomprime y recompila el .apk" },
                new HerramientaDato { Grupo = "Empaquetar", Nombre = "zipalign", Ruta = Zipalign,
                    Nota = "alinea el .apk antes de firmar" },

                new HerramientaDato { Grupo = "Firmar", Nombre = "apksigner", Ruta = ApkSigner,
                    Nota = "firma con el keystore" },
                new HerramientaDato { Grupo = "Firmar", Nombre = "keystore", Ruta = KeyStore,
                    Nota = "clave de depuración" },
                new HerramientaDato { Grupo = "Firmar", Nombre = "uber-apk-signer", Ruta = UberSigner,
                    Nota = "alternativa, opcional", Opcional = true },

                new HerramientaDato { Grupo = "Código del juego", Nombre = "Il2CppDumper", Ruta = Il2CppDumper,
                    Nota = "genera las DLLs de un juego IL2CPP" }
            };
        }

        public static string Resumen()
        {
            Detect();
            string modo = Portatil ? "PORTÁTIL (usa tools\\)" : "usando herramientas del sistema";

            return modo + Environment.NewLine + string.Join(Environment.NewLine, new[]
            {
                $"java: {(Java ?? "NO")}",
                $"apktool: {(Apktool ?? "NO")}",
                $"zipalign: {(Zipalign ?? "NO")}",
                $"apksigner: {(ApkSigner ?? "NO")}",
                $"uber-apk-signer: {(UberSigner ?? "NO")}",
                $"keystore: {(KeyStore ?? "NO")}",
                $"Il2CppDumper: {(Il2CppDumper ?? "NO")}"
            });
        }

        private static string Buscar(string[] exactos, params string[] patrones)
        {
            if (exactos != null)
                foreach (var p in exactos)
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                        return p;

            foreach (var patron in patrones)
            {
                try
                {
                    string dir = Path.GetDirectoryName(patron);
                    string file = Path.GetFileName(patron);

                    if (!dir.Contains('*') && Directory.Exists(dir))
                    {
                        var hit = Directory.GetFiles(dir, file).FirstOrDefault();
                        if (hit != null)
                            return hit;
                        continue;
                    }

                    if (dir.Contains('*'))
                    {
                        // expande un solo comodín de directorio
                        int at = dir.IndexOf('*');
                        string raiz = dir.Substring(0, at).TrimEnd('\\');
                        string resto = dir.Substring(at + 1).TrimStart('\\');

                        if (!Directory.Exists(raiz))
                            continue;

                        foreach (var sub in Directory.GetDirectories(raiz))
                        {
                            string candidato = resto.Length == 0 ? sub : Path.Combine(sub, resto);
                            string f = Path.Combine(candidato, file);
                            if (File.Exists(f))
                                return f;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static string BuscarEn(string exe)
        {
            try
            {
                var psi = new ProcessStartInfo("where", exe)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                string salida = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                var primera = salida.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
                return primera;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Ejecuta un programa y va contando lo que escribe.</summary>
        public static int Run(string exe, string argumentos, string trabajo, Action<string> log,
            Action<int> progreso = null, int timeoutMin = 60)
        {
            var psi = new ProcessStartInfo(exe, argumentos)
            {
                WorkingDirectory = trabajo ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            log?.Invoke("» " + Path.GetFileName(exe) + " " + argumentos);

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

            p.OutputDataReceived += (s, e) => { if (e.Data != null) log?.Invoke("  " + e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) log?.Invoke("  " + e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var reloj = Stopwatch.StartNew();

            while (!p.WaitForExit(500))
            {
                if (reloj.Elapsed.TotalMinutes > timeoutMin)
                {
                    try { p.Kill(true); } catch { }
                    log?.Invoke("Tiempo agotado.");
                    return -1;
                }
                progreso?.Invoke((int)reloj.Elapsed.TotalSeconds);
            }

            return p.ExitCode;
        }

        // ------------------------------------------------------------------ APK

        public static bool ExtractApk(string apk, string carpeta, Action<string> log)
        {
            Detect();
            if (!Listo)
            {
                log?.Invoke("Falta java o apktool. Revisa la configuración.");
                return false;
            }

            if (Directory.Exists(carpeta))
                Directory.Delete(carpeta, true);

            int rc = Run(Java, $"-jar \"{Apktool}\" d -f -s -o \"{carpeta}\" \"{apk}\"",
                Path.GetDirectoryName(apk), log);

            return rc == 0 && Directory.Exists(carpeta);
        }

        /// <summary>Genera las DLLs del juego con Il2CppDumper dentro del proyecto.</summary>
        public static bool BuildDlls(string carpetaProyecto, Action<string> log)
        {
            Detect();

            if (string.IsNullOrEmpty(Il2CppDumper) || !File.Exists(Il2CppDumper))
            {
                log?.Invoke("No encuentro Il2CppDumper.exe");
                return false;
            }

            string so = BuscarPrimero(carpetaProyecto, "libil2cpp.so", new[]
            {
                Path.Combine("lib", "arm64-v8a"), Path.Combine("lib", "armeabi-v7a"), "lib"
            });

            string meta = BuscarPrimero(carpetaProyecto, "global-metadata.dat", new[]
            {
                Path.Combine("assets", "bin", "Data", "Managed", "Metadata"),
                Path.Combine("assets", "bin", "Data", "Managed"),
                Path.Combine("assets", "bin", "Data")
            });

            if (so == null || meta == null)
            {
                log?.Invoke($"Faltan archivos: libil2cpp.so={(so ?? "NO")} global-metadata.dat={(meta ?? "NO")}");
                return false;
            }

            string salida = Path.Combine(carpetaProyecto, "ZubDummy");

            // copia del dumper en un temporal: escribe dump.cs y DummyDll en su carpeta
            string trabajo = Path.Combine(Path.GetTempPath(), "zub_il2cpp");
            try
            {
                if (Directory.Exists(trabajo))
                    Directory.Delete(trabajo, true);
                Directory.CreateDirectory(trabajo);

                File.Copy(Il2CppDumper, Path.Combine(trabajo, "Il2CppDumper.exe"), true);

                string cfg = Path.Combine(Path.GetDirectoryName(Il2CppDumper) ?? "", "config.json");
                if (File.Exists(cfg))
                    File.Copy(cfg, Path.Combine(trabajo, "config.json"), true);
            }
            catch (Exception ex)
            {
                log?.Invoke("No se pudo preparar el dumper: " + ex.Message);
                return false;
            }

            log?.Invoke("Descomprimiendo DLLs (Il2CppDumper)…");

            // el dumper escribe siempre en su carpeta: se limpia antes para no copiar restos de otro juego
            try { if (Directory.Exists(Path.Combine(trabajo, "DummyDll"))) Directory.Delete(Path.Combine(trabajo, "DummyDll"), true); } catch { }

            int rc = Run(Path.Combine(trabajo, "Il2CppDumper.exe"), $"\"{so}\" \"{meta}\"", trabajo, log, null, 30);

            string generado = Path.Combine(trabajo, "DummyDll");
            if (!Directory.Exists(generado))
            {
                log?.Invoke("Il2CppDumper no generó la carpeta DummyDll");
                return false;
            }

            if (Directory.Exists(salida))
                Directory.Delete(salida, true);

            Directory.CreateDirectory(salida);
            CopiarCarpeta(generado, Path.Combine(salida, "DummyDll"));

            int n = Directory.GetFiles(Path.Combine(salida, "DummyDll"), "*.dll").Length;
            log?.Invoke($"Listo: {n} DLLs en {salida}");

            try { Directory.Delete(trabajo, true); } catch { }

            return n > 0;
        }

        private static string BuscarPrimero(string raiz, string nombre, string[] relativas)
        {
            foreach (var rel in relativas)
            {
                string p = Path.Combine(raiz, rel, nombre);
                if (File.Exists(p))
                    return p;
            }

            try
            {
                return Directory.GetFiles(raiz, nombre, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static void CopiarCarpeta(string origen, string destino)
        {
            Directory.CreateDirectory(destino);

            foreach (var f in Directory.GetFiles(origen))
                File.Copy(f, Path.Combine(destino, Path.GetFileName(f)), true);

            foreach (var d in Directory.GetDirectories(origen))
                CopiarCarpeta(d, Path.Combine(destino, Path.GetFileName(d)));
        }

        /// <summary>apktool b → zipalign → firma. Devuelve la ruta del APK firmado.</summary>
        public static string Repack(string carpeta, string apkSalida, Action<string> log)
        {
            Detect();

            if (!Listo)
            {
                log?.Invoke("Falta java o apktool.");
                return null;
            }

            string base_ = Path.Combine(Path.GetDirectoryName(apkSalida) ?? ".", Path.GetFileNameWithoutExtension(apkSalida));
            string sinAlinear = base_ + "-sin-align.apk";
            string alineado = base_ + "-alineado.apk";

            foreach (var f in new[] { sinAlinear, alineado, apkSalida })
                try { if (File.Exists(f)) File.Delete(f); } catch { }

            log?.Invoke("Empaquetando con apktool…");
            int rc = Run(Java, $"-jar \"{Apktool}\" b \"{carpeta}\" -o \"{sinAlinear}\"",
                Path.GetDirectoryName(carpeta), log);
            if (rc != 0)
            {
                log?.Invoke("apktool falló (código " + rc + ")");
                return null;
            }

            string fuente = sinAlinear;

            if (!string.IsNullOrEmpty(Zipalign) && File.Exists(Zipalign))
            {
                log?.Invoke("Alineando…");
                rc = Run(Zipalign, $"-p -f 4 \"{sinAlinear}\" \"{alineado}\"", Path.GetDirectoryName(apkSalida), log);
                if (rc == 0)
                    fuente = alineado;
                else
                    log?.Invoke("zipalign falló; se firma sin alinear.");
            }

            bool firmado = false;

            // primero apksigner (ya hemos alineado); uber-apk-signer suele fallar en APKs de 2 GB
            if (!string.IsNullOrEmpty(ApkSigner) && File.Exists(ApkSigner) && File.Exists(KeyStore))
            {
                log?.Invoke("Firmando (apksigner)…");

                string args = $"sign --ks \"{KeyStore}\" --ks-pass pass:{KeyStorePass} " +
                              $"--key-pass pass:{KeyStorePass} --out \"{apkSalida}\" \"{fuente}\"";

                bool esJar = ApkSigner.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);

                rc = esJar
                    ? Run(Java, $"-cp \"{ApkSigner}\" com.android.apksigner.ApkSignerTool {args}",
                        Path.GetDirectoryName(apkSalida), log)
                    : Run(ApkSigner, args, Path.GetDirectoryName(apkSalida), log);

                firmado = rc == 0 && File.Exists(apkSalida);
            }

            if (!firmado && !string.IsNullOrEmpty(UberSigner) && File.Exists(UberSigner))
            {
                log?.Invoke("Firmando (uber-apk-signer)…");
                rc = Run(Java, $"-jar \"{UberSigner}\" -a \"{fuente}\" --ks \"{KeyStore}\" --ksAlias androiddebugkey " +
                                $"--ksPass {KeyStorePass} --ksKeyPass {KeyStorePass} -o \"{Path.GetDirectoryName(apkSalida)}\"",
                    Path.GetDirectoryName(apkSalida), log);
                firmado = rc == 0;
            }

            if (!firmado)
            {
                log?.Invoke("No se pudo firmar.");
                return null;
            }

            if (!File.Exists(apkSalida))
            {
                // uber-apk-signer deja <nombre>-aligned-signed.apk
                var posible = Directory.GetFiles(Path.GetDirectoryName(apkSalida) ?? ".",
                    Path.GetFileNameWithoutExtension(apkSalida) + "*.apk").FirstOrDefault();

                if (posible != null)
                    apkSalida = posible;
            }

            log?.Invoke($"APK: {apkSalida} ({new FileInfo(apkSalida).Length / (1024.0 * 1024.0):N0} MB)");

            // fuera intermedios (ocupan varios GB)
            foreach (var f in new[] { sinAlinear, alineado })
                try { if (File.Exists(f) && !f.Equals(apkSalida, StringComparison.OrdinalIgnoreCase)) File.Delete(f); } catch { }

            try
            {
                foreach (var idsig in Directory.GetFiles(Path.GetDirectoryName(apkSalida) ?? ".", "*.idsig"))
                    File.Delete(idsig);
            }
            catch { }

            return apkSalida;
        }
    }
}
