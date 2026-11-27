using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Zub;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.WriteLine("zub scan <juego> [--json]");
                Console.WriteLine("zub export <juego> --type MonoBehaviour|TextAsset --out <carpeta> [--limit N]");
                Console.WriteLine("zub import <juego> --from <carpeta> [--dry-run] [--no-verify] [--no-backup] [--type MonoBehaviour]");
                return 1;
            }

            string cmd = args[0].ToLowerInvariant();
            var opts = ParseArgs(args.Skip(1).ToArray());
            string game = opts.Positionals.FirstOrDefault();

            if (string.IsNullOrEmpty(game))
            {
                Console.Error.WriteLine("Falta la ruta del juego.");
                return 1;
            }

            if (cmd == "compare" || cmd == "comparar")
                return CompareCmd(opts);

            if (cmd == "scan")
                return Scan(game, opts);
            if (cmd == "export")
                return Export(game, opts);
            if (cmd == "import")
                return Import(game, opts);
            if (cmd == "texinfo")
            {
                using var pj = new UnityProject();
                pj.Open(game, m => Console.Error.WriteLine(m));
                long pidT = long.Parse(opts.Get("--pathid") ?? "0");
                var ent = pj.Assets.FirstOrDefault(a => a.ClassId == AssetTypes.Texture2D && a.PathId == pidT);
                Console.WriteLine(ent == null ? "no existe" : TextureReader.Describir(pj, ent));
                return 0;
            }

            if (cmd == "settex")
                return SetTexCmd(game, opts);

            if (cmd == "csv")
                return CsvCmd(game, opts);

            if (cmd == "android")
            {
                Console.WriteLine(Zub.AndroidTools.Resumen());
                return 0;
            }

            if (cmd == "dlls")
            {
                bool ok = Zub.AndroidTools.BuildDlls(game, Console.Error.WriteLine);
                Console.WriteLine(ok ? "DLLs generadas" : "Fallo al generar DLLs");
                return ok ? 0 : 1;
            }

            if (cmd == "apk")
            {
                string salida = opts.Get("--out") ?? Path.Combine(Path.GetDirectoryName(game.TrimEnd(Path.DirectorySeparatorChar)), "firmado.apk");
                string r = Zub.AndroidTools.Repack(game, salida, Console.Error.WriteLine);
                Console.WriteLine(r == null ? "Fallo al empaquetar" : "APK: " + r);
                return r == null ? 1 : 0;
            }

            if (cmd == "gonames")
                return GoNames(game, opts);

            if (cmd == "search")
                return SearchCmd(game, opts);

            if (cmd == "roundtrip")
                return RoundTrip(game, opts);

            if (cmd == "match")
                return MatchCmd(game, opts);

            if (cmd == "find")
                return Find(game, opts);

            if (cmd == "scripts")
                return Scripts(game);
            if (cmd == "dump")
                return Dump(game, opts);

            if (cmd == "tex")
                return Tex(game, opts);

            Console.Error.WriteLine("Comando desconocido: " + cmd);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex);
            return 2;
        }
    }

    private static int Scan(string game, Opts o)
    {
        var sw = Stopwatch.StartNew();
        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var byType = proj.Assets.GroupBy(a => a.ClassId)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        if (o.Has("--json"))
        {
            var payload = new
            {
                unity_version = proj.UnityVersion,
                data_dir = proj.DataDir,
                containers = proj.LoadedContainers.Select(Path.GetFileName).ToArray(),
                total = proj.Assets.Count,
                by_type = byType.ToDictionary(k => proj.TypeNameOf(k.Key), v => v.Value),
                seconds = Math.Round(sw.Elapsed.TotalSeconds, 2)
            };
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.Error.WriteLine("TypeTree: " + proj.ContainersWithTypeTree + "/" + proj.LoadedContainers.Count);
            Console.WriteLine($"Unity {proj.UnityVersion} | {proj.LoadedContainers.Count} contenedores | {proj.Assets.Count} objetos | {sw.Elapsed.TotalSeconds:F1}s");
            foreach (var kv in byType.Take(15))
                Console.WriteLine($"  {AssetTypes.NameOf(kv.Key),-22} {kv.Value}");
        }
        return 0;
    }

    private static int Export(string game, Opts o)
    {
        string type = o.Get("--type", "MonoBehaviour");
        string outDir = o.Get("--out");
        if (string.IsNullOrEmpty(outDir))
        {
            Console.Error.WriteLine("Falta --out");
            return 1;
        }

        int classId = type.Equals("TextAsset", StringComparison.OrdinalIgnoreCase) ? AssetTypes.TextAsset
                    : type.Equals("Texture2D", StringComparison.OrdinalIgnoreCase) ? AssetTypes.Texture2D
                    : AssetTypes.MonoBehaviour;

        int limit = int.TryParse(o.Get("--limit"), out int l) ? l : int.MaxValue;

        var sw = Stopwatch.StartNew();
        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var list = proj.OfType(classId).Take(limit).ToList();
        Directory.CreateDirectory(outDir);

        var report = BatchExporter.Export(proj, list, outDir);

        Console.WriteLine($"Exportados {report.Exported}, fallos {report.Failed} en {report.Seconds:F1}s → {outDir}");
        foreach (var e in report.Errors.Take(5))
            Console.Error.WriteLine("  · " + e);
        return 0;
    }

    private static int Import(string game, Opts o)
    {
        string from = o.Get("--from");
        if (string.IsNullOrEmpty(from) || !Directory.Exists(from))
        {
            Console.Error.WriteLine("Falta --from <carpeta>");
            return 1;
        }

        bool dryRun = o.Has("--dry-run");
        bool verify = !o.Has("--no-verify");
        bool backup = !o.Has("--no-backup");

        string type = o.Get("--type", "MonoBehaviour");
        int classId = type.Equals("TextAsset", StringComparison.OrdinalIgnoreCase) ? AssetTypes.TextAsset
                    : AssetTypes.MonoBehaviour;

        var total = Stopwatch.StartNew();
        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var pool = proj.OfType(classId).ToList();
        var poolAll = proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour ||
                                             a.ClassId == AssetTypes.TextAsset ||
                                             a.ClassId == AssetTypes.Texture2D).ToList();

        var stats = CrossBuildMatcher.Match(proj, from, poolAll);
        var items = stats.Items;
        var unmatched = stats.Missing;

        Console.Error.WriteLine("Emparejado: " + stats.Summary);
        foreach (var u in unmatched.Take(5))
            Console.Error.WriteLine("  sin pareja: " + u);

        if (items.Count == 0)
        {
            Console.Error.WriteLine("Nada que importar.");
            return 1;
        }

        int applied = 0, unchanged = 0, failed = 0;
        bool allVerified = true;

        var edits = BatchImporter.BuildEdits(proj, items, out var buildErrors);
        foreach (var e in buildErrors.Take(5))
            Console.Error.WriteLine("  build: " + e);

        if (dryRun)
        {
            Console.WriteLine($"Simulacion: {edits.Count} cambios validos, {buildErrors.Count} errores, " +
                              $"{items.Count - edits.Count - buildErrors.Count} sin cambios");
            return 0;
        }

        foreach (var group in edits.GroupBy(e => e.Entry.ContainerPath).OrderByDescending(g => g.Count()))
        {
            Console.Error.WriteLine($"-> {Path.GetFileName(group.Key)} ({group.Count()} objetos)");
            var rep = BatchImporter.ApplyToContainer(proj, group.Key, group.ToList(), verify, backup);
            applied += rep.Applied;
            unchanged += rep.Unchanged;
            failed += rep.Failed;
            allVerified &= rep.Verified;
            Console.Error.WriteLine("   " + rep.Summary);
            foreach (var e in rep.Errors.Take(5))
                Console.Error.WriteLine("   - " + e);
        }

        Console.WriteLine($"Aplicados {applied}, sin cambios {unchanged}, fallos {failed}, verificado={allVerified} en {total.Elapsed.TotalSeconds:F1}s");
        return failed == 0 ? 0 : 3;
    }

    private static int GoNames(string game, Opts o)
    {
        int n = int.TryParse(o.Get("--n"), out int k) ? k : 8;

        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        string buscar = o.Get("--find", "");

        if (buscar.Length > 0)
        {
            var gos = proj.Assets.Where(a => a.ClassId == 1)
                .Select(a => (go: a, nombre: Zub.GameObjectNames.ReadGameObjectName(a)))
                .Where(x => x.nombre != null && x.nombre.Contains(buscar, StringComparison.OrdinalIgnoreCase))
                .Take(5).ToList();

            Console.WriteLine($"GameObjects con '{buscar}': {gos.Count}");
            foreach (var g in gos)
                Console.WriteLine($"   '{g.nombre}' ({g.go.ContainerPath} #{g.go.PathId})");
            return 0;
        }

        foreach (var go in proj.Assets.Where(a => a.ClassId == 1).Take(n))
        {
            string raw = Zub.GameObjectNames.ReadGameObjectName(go);
            string typed = Zub.GameObjectNames.ReadGameObjectNameTyped(proj, go);
            Console.WriteLine($"{go.ContainerPath} | {go.PathId} | size={go.Size} | raw='{raw}' typed='{typed}'");
        }
        return 0;
    }

    private static int RoundTrip(string game, Opts o)
    {
        string frag = o.Get("--name", "");
        int n = int.TryParse(o.Get("--n"), out int k) ? k : 3;

        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var list = proj.Assets
            .Where(a => a.ClassId == AssetTypes.MonoBehaviour)
            .Where(a => frag.Length == 0 || (a.DisplayName ?? "").Contains(frag, StringComparison.OrdinalIgnoreCase)
                        || (a.ContainerPath ?? "").Contains(frag, StringComparison.OrdinalIgnoreCase))
            .Where(a => AssetTypes.NameOf(a.ClassId).Length > 0)
            .Take(n).ToList();

        foreach (var e in list)
        {
            try
            {
                string json = ContentReader.ReadMonoJson(proj, e);
                var bytes = ContentReader.BuildMonoBytes(proj, e, json, out string err);
                Console.WriteLine($"{e.DisplayName} | {e.ContainerPath} | {e.PathId} | json {json.Length} | bytes {(bytes == null ? "NULL " + err : bytes.Length.ToString())}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR {e.DisplayName}: {ex}");
            }
        }
        return 0;
    }

    private static int Dump(string game, Opts o)
    {
        string frag = o.Get("--name", "");
        int n = int.TryParse(o.Get("--n"), out int k) ? k : 2;
        long pid = long.TryParse(o.Get("--pathid"), out long pp) ? pp : long.MinValue;

        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s), o.Get("--managed"));
        Console.Error.WriteLine("DLLs: " + proj.ManagedDir);

        var list = proj.Assets
            .Where(a => a.ClassId == AssetTypes.MonoBehaviour)
            .Where(a => pid != long.MinValue ? a.PathId == pid : true)
            .Where(a => pid != long.MinValue ? true :
                        frag.Length == 0 || (a.DisplayName ?? "").Contains(frag, StringComparison.OrdinalIgnoreCase)
                        || (a.ContainerPath ?? "").Contains(frag, StringComparison.OrdinalIgnoreCase))
            .Take(n);

        foreach (var e in list)
        {
            string goName = null;
            string goDbg = "";
            try
            {
                goName = Zub.GameObjectNames.Probe(proj, e, out goDbg);
            }
            catch { }
            Console.WriteLine($"=== {e.DisplayName} | {e.ContainerPath} | {e.PathId} | GO: {goName}{goDbg} | size {e.Size}");
            try
            {
                string json = ContentReader.ReadMonoJson(proj, e);
                Console.WriteLine(json.Length > 1800 ? json.Substring(0, 1800) : json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }
        }
        return 0;
    }

    private static int SetTexCmd(string game, Opts o)
    {
        using var proj = new UnityProject();
        proj.Open(game, m => Console.Error.WriteLine(m));

        if (o.Has("--list"))
        {
            int dentro = 0, fuera = 0;
            foreach (var e in proj.Assets.Where(a => a.ClassId == AssetTypes.Texture2D).Take(400))
            {
                bool externo = TextureReader.EsExterna(proj, e, out int w, out int h, out string fmt2);
                if (!externo)
                {
                    dentro++;
                    Console.WriteLine($"INT	{e.PathId}	{e.ContainerName}	{w}x{h}	{fmt2}");
                }
                else
                {
                    fuera++;
                    if (fuera <= 6)
                        Console.WriteLine($"EXT	{e.PathId}	{e.ContainerName}	{w}x{h}	{fmt2}");
                }
                if (dentro >= 8)
                    break;
            }
            Console.Error.WriteLine($"internas={dentro} externas={fuera}");
            return 0;
        }

        long pathId = long.Parse(o.Get("--pathid") ?? "0");
        string img = o.Get("--img");

        var target = proj.Assets.FirstOrDefault(a => a.ClassId == AssetTypes.Texture2D && a.PathId == pathId);
        if (target == null)
        {
            Console.Error.WriteLine("No hay Texture2D con PathID " + pathId);
            return 2;
        }

        byte[] data = TextureReader.Import(proj, target, img, out string err);
        if (data == null)
        {
            Console.Error.WriteLine("Fallo: " + err);
            return 3;
        }

        Console.WriteLine($"Codificado: {data.Length:N0} bytes ({target.ContainerName})");

        if (o.Has("--dry-run"))
            return 0;

        var rep = BatchImporter.ApplyToContainer(proj, target.ContainerPath,
            new List<RawEdit> { new RawEdit { Entry = target, Data = data, Label = "settex" } }, true, true);

        Console.Error.WriteLine(rep.Summary);
        foreach (var e in rep.Errors.Take(3))
            Console.Error.WriteLine("   " + e);

        return rep.Failed == 0 ? 0 : 4;
    }

    private static int CsvCmd(string game, Opts o)
    {
        string csv = o.Get("--from");
        if (string.IsNullOrEmpty(csv) || !File.Exists(csv))
        {
            Console.Error.WriteLine("Falta --from <archivo.csv>");
            return 1;
        }

        bool dry = o.Has("--dry-run");

        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var (edits, stats) = CsvInjector.Build(proj, csv);

        Console.Error.WriteLine("CSV: " + stats.Summary);
        foreach (var e in stats.Errores.Take(5))
            Console.Error.WriteLine("   " + e);
        foreach (var s2 in stats.SinPareja.Take(5))
            Console.Error.WriteLine("   sin objeto: " + s2);

        if (dry)
        {
            Console.WriteLine("Simulacion: " + edits.Count + " objetos a escribir");
            return 0;
        }

        int ok = 0, fail = 0;

        foreach (var g in edits.GroupBy(e => e.Entry.ContainerPath))
        {
            var rep = BatchImporter.ApplyToContainer(proj, g.Key, g.ToList(), true, true);
            ok += rep.Applied;
            fail += rep.Failed;
            Console.Error.WriteLine($"-> {Path.GetFileName(g.Key)}: {rep.Summary}");
            foreach (var e in rep.Errors.Take(3))
                Console.Error.WriteLine("   " + e);
        }

        Console.WriteLine($"Escritos {ok}, fallos {fail}");
        return fail == 0 ? 0 : 3;
    }

    private static int MatchCmd(string game, Opts o)
    {
        string from = o.Get("--from");
        if (string.IsNullOrEmpty(from) || !Directory.Exists(from))
        {
            Console.Error.WriteLine("Falta --from <carpeta>");
            return 1;
        }

        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var pool = proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour || a.ClassId == AssetTypes.TextAsset).ToList();
        var stats = CrossBuildMatcher.Match(proj, from, pool);

        Console.WriteLine(stats.Summary);
        foreach (var a in stats.Ambiguous.Take(5))
            Console.Error.WriteLine("  ambiguo: " + a);
        foreach (var m in stats.Missing.Take(5))
            Console.Error.WriteLine("  sin pareja: " + m);

        var reporte = o.Get("--out");
        if (!string.IsNullOrEmpty(reporte))
        {
            File.WriteAllLines(reporte, stats.Missing.Concat(stats.Ambiguous));
            Console.WriteLine("informe: " + reporte);
        }
        return 0;
    }

    private static int Scripts(string game)
    {
        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var mbs = proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour).ToList();
        Console.Error.WriteLine($"MonoBehaviours: {mbs.Count:N0}");

        var counts = new Dictionary<string, int>();
        int vacios = 0;
        var muestra = new List<string>();

        foreach (var e in mbs.Take(3000))
        {
            string name = proj.GetScriptClassName(e);
            if (string.IsNullOrEmpty(name)) { vacios++; continue; }
            counts[name] = counts.TryGetValue(name, out int c) ? c + 1 : 1;
            if (muestra.Count < 8) muestra.Add(name + "  (" + System.IO.Path.GetFileName(e.ContainerPath) + "#" + e.PathId + ")");
        }

        Console.WriteLine($"sin nombre de script: {vacios} de 3000");
        foreach (var kv in counts.OrderByDescending(k => k.Value).Take(15))
            Console.WriteLine($"  {kv.Value,5}  {kv.Key}");
        Console.WriteLine("muestra:");
        foreach (var m in muestra) Console.WriteLine("  " + m);
        return 0;
    }

    private static int Find(string game, Opts o)
    {
        string query = o.Positionals.Skip(1).FirstOrDefault();
        if (string.IsNullOrEmpty(query))
        {
            Console.Error.WriteLine("Falta el texto a buscar.");
            return 1;
        }

        using var proj = new UnityProject();
        proj.Open(game, m => Console.Error.WriteLine(m));

        var pool = proj.Assets.Where(a => a.ClassId == AssetTypes.MonoBehaviour ||
                                          a.ClassId == AssetTypes.TextAsset ||
                                          a.ClassId == AssetTypes.Texture2D).ToList();

        var sw = Stopwatch.StartNew();
        var hits = SearchEngine.Search(proj, pool, query, o.Has("--regex"), !o.Has("--case"), 50,
            8L * 1024 * 1024, null, System.Threading.CancellationToken.None);
        sw.Stop();

        Console.WriteLine($"{hits.Count} coincidencias en {sw.Elapsed.TotalSeconds:F2}s");
        foreach (var h in hits.Take(50))
        {
            h.Entry.Name = proj.ReadName(h.Entry, true, false);
            Console.WriteLine($"  {h.Entry.Name}  [{Path.GetFileName(h.Entry.ContainerPath)}#{h.Entry.PathId}] x{h.Matches}");
            Console.WriteLine("     " + h.Snippet);
        }
        return 0;
    }

    private static int Tex(string game, Opts o)
    {
        int limit = int.TryParse(o.Get("--limit"), out int l) ? l : 200;
        string outDir = o.Get("--out");

        using var proj = new UnityProject();
        proj.Open(game, m => Console.Error.WriteLine(m));

        var list = proj.OfType(AssetTypes.Texture2D).Take(limit).ToList();
        var formats = new Dictionary<string, int>();
        int ok = 0, fail = 0;
        var sw = Stopwatch.StartNew();

        foreach (var entry in list)
        {
            var info = TextureReader.Preview(proj, entry, 128);
            string key = info.Format + (info.Decodable ? "" : " [!]");
            formats[key] = formats.TryGetValue(key, out int c) ? c + 1 : 1;

            if (info.Decodable)
            {
                ok++;
                if (!string.IsNullOrEmpty(outDir))
                {
                    Directory.CreateDirectory(outDir);
                    entry.Name = proj.ReadName(entry, true, false);
                    File.WriteAllBytes(Path.Combine(outDir, AssetEntry.Sanitize(entry.Name) + "-" + entry.PathId + ".png"), info.Png);
                }
            }
            else fail++;
        }

        sw.Stop();
        Console.WriteLine($"Texturas: {ok} ok, {fail} sin previsualizar, {sw.Elapsed.TotalSeconds:F2}s");
        foreach (var kv in formats.OrderByDescending(k => k.Value).Take(12))
            Console.WriteLine($"  {kv.Key,-28} {kv.Value}");
        return 0;
    }



    /// <summary>
    /// Compara dos carpetas de exportación (por ejemplo la de UABEA y la nuestra) y dice
    /// exactamente qué objetos faltan y cuáles salieron distintos. Sirve para medir la paridad
    /// con UABEA en CUALQUIER juego.
    /// </summary>
    private static int CompareCmd(Opts o)
    {
        var pos = o.Positionals.Where(x => x != "compare" && x != "comparar").ToList();
        if (pos.Count < 2)
        {
            Console.Error.WriteLine("Uso: zub compare <carpetaA> <carpetaB> [--exact] [--out informe.csv]");
            Console.Error.WriteLine("  A = referencia (UABEA), B = lo nuestro.");
            return 1;
        }

        if (!Directory.Exists(pos[0]) || !Directory.Exists(pos[1]))
        {
            Console.Error.WriteLine("No existe alguna de las carpetas.");
            return 1;
        }

        var res = FolderCompare.Run(pos[0], pos[1], o.Get("--exact") != null);
        Console.Write(res.Resumen(20));

        if (o.Get("--out") is string salida)
        {
            res.GuardarCsv(salida);
            Console.WriteLine();
            Console.WriteLine("Informe: " + salida);
        }

        return res.Identico ? 0 : 3;
    }

    private static int SearchCmd(string game, Opts o)
    {
        string q = o.Positionals.Skip(1).FirstOrDefault() ?? o.Get("--q");
        if (string.IsNullOrWhiteSpace(q))
        {
            Console.Error.WriteLine("Falta el texto a buscar.");
            return 1;
        }

        int limit = int.TryParse(o.Get("--limit"), out int l) ? l : 40;

        using var proj = new UnityProject();
        proj.Open(game, s => Console.Error.WriteLine(s));

        var pool = proj.Assets
            .Where(a => a.ClassId == AssetTypes.MonoBehaviour || a.ClassId == AssetTypes.TextAsset)
            .ToList();

        var sw = Stopwatch.StartNew();
        var hits = SearchEngine.Search(proj, pool, q, false, true, Math.Max(limit, 1000), 8L * 1024 * 1024,
            (d, t) => { if (d % 10000 == 0) Console.Error.WriteLine("  " + d + "/" + t); });

        Console.WriteLine(hits.Count.ToString("N0") + " coincidencias de \"" + q + "\" en " + pool.Count.ToString("N0") + " objetos (" + sw.Elapsed.TotalSeconds.ToString("F1") + "s)");
        foreach (var h in hits.Take(limit))
            Console.WriteLine("  " + Path.GetFileName(h.Entry.ContainerPath) + "#" + h.Entry.PathId + "  [" + h.Entry.TypeText + "]  " + h.Matches + "x  " + h.Snippet);
        return 0;
    }

    private sealed class Opts
    {
        public readonly List<string> Positionals = new List<string>();
        public readonly HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool Has(string f) => Flags.Contains(f);
        public string Get(string k, string def = null) => Values.TryGetValue(k, out var v) ? v : def;
    }

    private static Opts ParseArgs(string[] args)
    {
        var o = new Opts();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--"))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    o.Values[a] = args[i + 1];
                    i++;
                }
                else
                {
                    o.Flags.Add(a);
                }
            }
            else o.Positionals.Add(a);
        }
        return o;
    }
}
