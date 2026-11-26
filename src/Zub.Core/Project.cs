using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel;

namespace Zub
{
    /// <summary>Un objeto Unity (asset) localizado dentro de un contenedor.</summary>
    public sealed class AssetEntry : INotifyPropertyChanged
    {
        public AssetsFileInstance Inst;
        public long PathId;
        public int ClassId;
        public ushort MonoId;
        public long Position;
        public uint Size;
        public string Name = "";
        public string TypeName = "";
        public string ScriptClass = "";
        /// <summary>MonoBehaviour "stripped": no guarda datos, solo el TypeTree (UABEA los exporta vacios).</summary>
        public bool Stripped;

        public string ContainerPath => Inst.path;

        private bool _pending;

        /// <summary>Hay un cambio preparado para este objeto que aún no se ha escrito al juego.</summary>
        public bool Pending
        {
            get => _pending;
            set
            {
                if (_pending == value) return;
                _pending = value;
                Raise(nameof(Pending));
                Raise(nameof(StateText));
            }
        }

        public string StateText => _pending ? "●" : "";

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // propiedades de presentación (usadas por la interfaz)
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : (!string.IsNullOrEmpty(ScriptClass) ? ScriptClass : "(sin nombre)");
        public string PathIdText => PathId.ToString();
        public long PathIdValue => PathId;
        public uint SizeValue => Size;
        public string SizeText => Size.ToString("N0");
        public string TypeText => string.IsNullOrEmpty(TypeName) ? AssetTypes.NameOf(ClassId) : TypeName;
        public string ContainerName => Path.GetFileName(Inst.path);

        // Nombre de archivo compatible con UABEA Next: "<nombre>-<contenedor>-<pathId>.json"
        public string MatchStem
        {
            get
            {
                string name = string.IsNullOrEmpty(Name) ? "Unnamed asset" : Name;
                name = Sanitize(name);
                return $"{name}-{ContainerName}-{PathId}";
            }
        }

        public static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace('/', '_').Replace('\\', '_');
        }
    }

    public static class AssetTypes
    {
        public const int MonoBehaviour = 114;
        public const int TextAsset = 49;
        public const int Texture2D = 28;

        public static string NameOf(int classId)
        {
            switch (classId)
            {
                case MonoBehaviour: return "MonoBehaviour";
                case TextAsset: return "TextAsset";
                case Texture2D: return "Texture2D";
                default: return "0x" + classId.ToString("X8");
            }
        }
    }

    public sealed class UnityProject : IDisposable
    {
        public AssetsManager Am { get; private set; } = new AssetsManager();
        public string DataDir { get; private set; } = "";
        public string ManagedDir { get; private set; } = "";

        /// <summary>
        /// Carpeta con las DLLs del juego (Mono) o las dummy de Il2CppDumper.
        /// Sin ellas Unity IL2CPP no sabe qué campos tiene cada MonoBehaviour.
        /// </summary>
        private static string ResolveManagedDir(string pedida, string dataDir)
        {
            if (!string.IsNullOrEmpty(pedida) && Directory.Exists(pedida))
                return pedida;

            var candidatas = new List<string>
            {
                Path.Combine(dataDir, "Managed"),
                Path.Combine(dataDir, "Managed", "DummyDll"),
                Path.Combine(dataDir, "DummyDll"),
                Path.Combine(dataDir, "..", "Managed"),
            };

            // junto a la carpeta del juego: <juego>/DummyDll, y el ZubDummy que generamos nosotros
            try
            {
                var padre = Directory.GetParent(dataDir);
                for (int i = 0; i < 5 && padre != null; i++, padre = padre.Parent)
                {
                    candidatas.Add(Path.Combine(padre.FullName, "DummyDll"));
                    candidatas.Add(Path.Combine(padre.FullName, "ZubDummy", "DummyDll"));
                    candidatas.Add(Path.Combine(padre.FullName, "Managed"));
                }
            }
            catch { }

            foreach (var c in candidatas)
            {
                try
                {
                    if (Directory.Exists(c) && Directory.GetFiles(c, "*.dll").Length > 0)
                        return c;
                }
                catch { }
            }

            return Path.Combine(dataDir, "Managed");
        }
        public string UnityVersion { get; private set; } = "";
        public List<AssetEntry> Assets { get; } = new List<AssetEntry>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> LoadedContainers { get; } = new List<string>();
        private readonly List<MemoryStream> _streams = new List<MemoryStream>();

        public int ContainersWithTypeTree { get; private set; }

        /// <summary>Contenedor dentro de un AssetBundle: ruta del bundle e índice dentro de él.</summary>
        public sealed class BundleLocation
        {
            public string BundlePath;
            public int Index;
            public string InternalName;
        }

        public readonly Dictionary<string, BundleLocation> Bundles = new Dictionary<string, BundleLocation>();

        /// <summary>Serializado troceado en partes .splitN (Unity 2022 en Android).</summary>
        public sealed class SplitLocation
        {
            public string BasePath;
            public int Parts;
            public int PartSize;
        }

        public readonly Dictionary<string, SplitLocation> Splits = new Dictionary<string, SplitLocation>();
        public int BundleAssets { get; private set; }
        private readonly Dictionary<string, AssetsFileInstance> instances = new Dictionary<string, AssetsFileInstance>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> monoScriptCache = new Dictionary<string, string>();
        private readonly Dictionary<long, string> scriptNameCache = new Dictionary<long, string>();

        /// <summary>
        /// Limpia la ruta tal como llega del arrastre o del portapapeles (espacios, comillas,
        /// saltos de línea). Si es un archivo, devuelve su carpeta.
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string p = path.Replace("\0", "").Trim();
            p = p.Trim(new char[] { ' ', (char)34, (char)13, (char)10, (char)9 });
            if (p.Length == 0)
                return null;

            try
            {
                if (File.Exists(p))
                    return Path.GetDirectoryName(p);
                if (Directory.Exists(p))
                    return p.TrimEnd('\\', '/');
            }
            catch { }

            return p;
        }

        /// <summary>
        /// ¿La carpeta tiene datos de Unity? NO se exige *_Data ni globalgamemanagers:
        /// también valen un data.unity3d (build de archivo único), assets sueltos o bundles.
        /// </summary>
        public static bool LooksLikeUnityData(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return false;

            try
            {
                if (File.Exists(Path.Combine(dir, "globalgamemanagers")))
                    return true;
                if (File.Exists(Path.Combine(dir, "data.unity3d")))
                    return true;
                if (File.Exists(Path.Combine(dir, "resources.assets")))
                    return true;
                if (File.Exists(Path.Combine(dir, "level0")))
                    return true;
                if (Directory.GetFiles(dir, "*.assets").Length > 0)
                    return true;
                if (Directory.GetFiles(dir, "*.unity3d").Length > 0)
                    return true;
                if (Directory.GetFiles(dir, "*.bundle").Length > 0)
                    return true;
                if (Directory.GetFiles(dir, "*.split0").Length > 0)
                    return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Localiza la carpeta de datos: acepta la carpeta del juego, la del .exe, una subcarpeta
        /// *_Data con cualquier nombre, un data.unity3d o un APK descomprimido (assets/bin/Data).
        /// </summary>
        public static string FindDataDir(string path)
        {
            string start = NormalizePath(path);
            if (start == null || !Directory.Exists(start))
                return null;

            // 1. la propia carpeta ya son los datos
            if (LooksLikeUnityData(start))
                return start;

            // 2. APK descomprimido (apktool): assets/bin/Data
            string android = Path.Combine(start, "assets", "bin", "Data");
            if (LooksLikeUnityData(android))
                return android;

            // 3. búsqueda en anchura (3 niveles) desde la carpeta y desde su padre
            var candidatos = new List<string>();
            var nivel = new List<string> { start };
            var visitados = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };

            var padre = Directory.GetParent(start);
            if (padre != null)
            {
                nivel.Add(padre.FullName);
                visitados.Add(padre.FullName);
            }

            for (int d = 0; d < 3 && nivel.Count > 0; d++)
            {
                var siguiente = new List<string>();
                foreach (var dir in nivel)
                {
                    string[] subs;
                    try { subs = Directory.GetDirectories(dir); } catch { continue; }

                    foreach (var sub in subs)
                    {
                        if (!visitados.Add(sub))
                            continue;

                        if (LooksLikeUnityData(sub))
                            candidatos.Add(sub);
                        else if (d < 2)
                            siguiente.Add(sub);
                    }
                }
                nivel = siguiente;
            }

            if (candidatos.Count == 0)
                return null;

            // primero una *_Data, luego la que tenga globalgamemanagers, luego data.unity3d
            return candidatos
                .OrderByDescending(c => Path.GetFileName(c).EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => File.Exists(Path.Combine(c, "globalgamemanagers")))
                .ThenByDescending(c => File.Exists(Path.Combine(c, "data.unity3d")))
                .ThenBy(c => c.Length)
                .First();
        }

        public static string FindClassDataPublic() => FindClassData();

        public static string FindClassData()
        {
            var candidates = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "classdata.tpk"));
            candidates.Add(Path.Combine(baseDir, "data", "classdata.tpk"));
            try
            {
                string asmDir = Path.GetDirectoryName(typeof(UnityProject).Assembly.Location);
                candidates.Add(Path.Combine(asmDir, "classdata.tpk"));
                candidates.Add(Path.Combine(asmDir, "data", "classdata.tpk"));
            }
            catch { }
            foreach (var c in candidates)
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                    return c;
            return null;
        }

        public void Open(string gamePath, Action<string> log = null, string managedDir = null)
        {
            string data = FindDataDir(gamePath);
            if (data == null)
                throw new Exception("No encuentro datos de Unity en:\n" + gamePath +
                    "\n\nHe mirado la carpeta, su padre y hasta 3 niveles de subcarpetas, buscando " +
                    "globalgamemanagers, *.assets, level*, data.unity3d o *.bundle " +
                    "(y assets/bin/Data si es un APK descomprimido).");

            DataDir = data;
            ManagedDir = ResolveManagedDir(managedDir, data);
            log?.Invoke("Carpeta de datos: " + DataDir);

            Am.MonoTempGenerator = new MonoCecilTempGenerator(ManagedDir);

            if (Directory.Exists(ManagedDir) && Directory.GetFiles(ManagedDir, "*.dll").Length > 0)
                log?.Invoke($"DLLs del juego: {ManagedDir} ({Directory.GetFiles(ManagedDir, "*.dll").Length} DLLs)");
            else
                log?.Invoke("Sin DLLs del juego: los MonoBehaviour mostrarán solo los campos básicos");
            Am.UseTemplateFieldCache = true;

            string classData = FindClassData();
            if (classData != null)
            {
                try
                {
                    Am.LoadClassPackage(classData);
                    log?.Invoke("classdata.tpk cargado: " + classData);
                }
                catch (Exception ex) { log?.Invoke("Aviso classdata: " + ex.Message); }
            }
            else
            {
                log?.Invoke("Aviso: no encuentro classdata.tpk (se usará solo el TypeTree embebido).");
            }

            LoadContainers(log);
            LoadSplits(log);
            LoadBundles(log);

            if (string.IsNullOrEmpty(UnityVersion) && LoadedContainers.Count > 0)
            {
                try { UnityVersion = instances[LoadedContainers[0]].file.Metadata.UnityVersion; }
                catch { }
            }
            if (string.IsNullOrEmpty(UnityVersion))
                UnityVersion = "0.0.0";

            try
            {
                LoadClassDatabaseFor(Am, UnityVersion, log);
            }
            catch (Exception ex)
            {
                log?.Invoke("Aviso: no se pudo cargar la class database (" + ex.Message + ")");
            }

            Index(log);
        }

        private void LoadContainers(Action<string> log)
        {
            var skipExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".resS", ".resource", ".dll", ".json", ".txt", ".xml", ".config", ".info",
                ".pdb", ".mdb", ".exe", ".bat", ".ini", ".dat", ".png", ".jpg", ".ico", ".so", ".dylib",
                ".unity3d", ".bundle"
            };

            int splits = 0;
            var fallos = new Dictionary<string, int>();

            foreach (string file in Directory.GetFiles(DataDir))
            {
                string ext = Path.GetExtension(file);
                if (skipExt.Contains(ext))
                    continue;

                // Unity "split": un SerializedFile troceado en .splitN; AssetsTools no lo puede leer entero
                if (file.Contains(".split", StringComparison.OrdinalIgnoreCase))
                {
                    splits++;
                    continue;
                }

                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith("."))
                    continue;
                if (fileName.EndsWith(".assets.resS", StringComparison.OrdinalIgnoreCase))
                    continue;
                // copias de seguridad y temporales de la propia herramienta
                if (fileName.EndsWith(".zub_bak", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".zub_new", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var inst = Am.LoadAssetsFile(file, false);
                    if (inst != null && inst.file != null)
                    {
                        LoadedContainers.Add(file);
                        instances[file] = inst;
                        RegistrarNombre(file, inst);
                        if (inst.file.Metadata.TypeTreeEnabled)
                            ContainersWithTypeTree++;
                    }
                }
                catch (Exception ex)
                {
                    string k = ext.Length == 0 ? "(sin extensión)" : ext.ToLowerInvariant();
                    fallos[k] = fallos.TryGetValue(k, out int c) ? c + 1 : 1;
                    Warnings.Add(Path.GetFileName(file) + ": " + ex.Message);
                }
            }
            log?.Invoke("Contenedores detectados: " + LoadedContainers.Count);

            foreach (var kv in fallos.OrderByDescending(k => k.Value).Take(3))
                log?.Invoke($"Aviso: {kv.Value:N0} archivos {kv.Key} no se pudieron leer.");
        }

        /// <summary>
        /// Une las partes .splitN de un serializado y lo abre como un único contenedor.
        /// </summary>
        private void LoadSplits(Action<string> log)
        {
            var grupos = new Dictionary<string, List<(int idx, string path)>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string f in Directory.GetFiles(DataDir))
                {
                    int at = f.IndexOf(".split", StringComparison.OrdinalIgnoreCase);
                    if (at < 0)
                        continue;

                    string idxTxt = f.Substring(at + 6);
                    if (!int.TryParse(idxTxt, out int idx))
                        continue;

                    string baseName = Path.GetFileName(f.Substring(0, at));
                    if (!grupos.TryGetValue(baseName, out var lst))
                        grupos[baseName] = lst = new List<(int, string)>();
                    lst.Add((idx, f));
                }
            }
            catch { }

            if (grupos.Count == 0)
                return;

            int ok = 0, bad = 0;
            long bytes = 0;

            foreach (var kv in grupos.OrderBy(k => k.Key))
            {
                try
                {
                    var partes = kv.Value.OrderBy(p => p.idx).ToList();
                    var ms = new MemoryStream();
                    foreach (var parte in partes)
                    {
                        using var fs = File.OpenRead(parte.path);
                        fs.CopyTo(ms);
                    }

                    bytes += ms.Length;
                    ms.Position = 0;

                    var inst = Am.LoadAssetsFile(ms, kv.Key, false, null);
                    if (inst?.file == null) { bad++; continue; }

                    inst.path = kv.Key;
                    LoadedContainers.Add(kv.Key);
                    instances[kv.Key] = inst;
                    RegistrarNombre(kv.Key, inst);
                    _streams.Add(ms);
                    Splits[kv.Key] = new SplitLocation
                    {
                        BasePath = Path.Combine(DataDir, kv.Key),
                        Parts = partes.Count,
                        PartSize = partes.Count > 0 ? (int)new FileInfo(partes[0].path).Length : 1048576
                    };
                    ok++;
                }
                catch
                {
                    bad++;
                }
            }

            log?.Invoke($"Splits: {ok:N0} serializados unidos ({bytes / 1048576.0:N0} MB)" +
                        (bad > 0 ? $", {bad:N0} con error" : ""));
        }

        /// <summary>
        /// Abre los AssetBundles (.bundle) que acompañan al juego: Addressables en Android,
        /// o bundles sueltos en la carpeta de datos.
        /// </summary>
        private void LoadBundles(Action<string> log)
        {
            var dirs = new List<string> { DataDir };

            // Addressables: StreamingAssets\aa\<plataforma> en PC, assets\aa\Android en APK
            var raices = new List<string>();
            try { raices.Add(Path.Combine(DataDir, "StreamingAssets", "aa")); } catch { }
            try { raices.Add(Path.GetFullPath(Path.Combine(DataDir, "..", "..", "aa"))); } catch { }

            foreach (var raiz in raices)
            {
                try
                {
                    if (!Directory.Exists(raiz))
                        continue;

                    dirs.Add(raiz);
                    foreach (var sub in Directory.GetDirectories(raiz, "*", SearchOption.AllDirectories))
                        dirs.Add(sub);
                }
                catch { }
            }

            var bundles = new List<string>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs.Distinct())
            {
                try
                {
                    foreach (var b in Directory.GetFiles(dir, "*.bundle"))
                        if (vistos.Add(b)) bundles.Add(b);
                }
                catch { }
                // builds de archivo único (data.unity3d) y bundles con otra extensión
                try
                {
                    foreach (var b in Directory.GetFiles(dir, "*.unity3d"))
                        if (vistos.Add(b)) bundles.Add(b);
                }
                catch { }
            }

            log?.Invoke($"Bundles buscados en: {string.Join(" | ", dirs.Distinct())} → {bundles.Count}");

            if (bundles.Count == 0)
                return;

            int ok = 0, bad = 0;

            foreach (var path in bundles)
            {
                try
                {
                    var bun = Am.LoadBundleFile(path, true);
                    if (bun?.file == null) { bad++; continue; }

                    var infos = bun.file.BlockAndDirInfo.DirectoryInfos;
                    for (int i = 0; i < infos.Length; i++)
                    {
                        if (!bun.file.IsAssetsFile(i))
                            continue;

                        AssetsFileInstance inst = null;
                        try { inst = Am.LoadAssetsFileFromBundle(bun, i, false); } catch { }

                        if (inst?.file == null)
                            continue;

                        // el nombre interno del serializado (CAB-xxxx en Addressables) es el que usa UABEA
                        string interno = null;
                        try { interno = bun.file.GetFileName(i); } catch { }
                        if (string.IsNullOrEmpty(interno))
                            interno = inst.path;

                        string key = interno;
                        int dup = 1;
                        while (LoadedContainers.Contains(key))
                            key = interno + "#" + (dup++);

                        inst.path = key;
                        LoadedContainers.Add(key);
                        instances[key] = inst;
                        RegistrarNombre(key, inst);
                        RegistrarNombre(interno, inst);
                        Bundles[key] = new BundleLocation
                        {
                            BundlePath = path,
                            Index = i,
                            InternalName = interno
                        };
                        ok++;
                    }
                }
                catch
                {
                    bad++;
                }
            }

            BundleAssets = ok;
            log?.Invoke($"Bundles: {bundles.Count:N0} leídos, {ok:N0} archivos serializados dentro" +
                        (bad > 0 ? $", {bad:N0} con error" : ""));
        }

        private void Index(Action<string> log)
        {
            Assets.Clear();
            int failed = 0;

            foreach (string container in LoadedContainers)
            {
                if (!instances.TryGetValue(container, out AssetsFileInstance inst))
                    continue;
                if (inst == null || inst.file == null)
                    continue;

                foreach (var info in inst.file.AssetInfos)
                {
                    try
                    {
                        var entry = new AssetEntry
                        {
                            Inst = inst,
                            PathId = info.PathId,
                            ClassId = info.TypeId,
                            MonoId = inst.file.GetScriptIndex(info),
                            Position = info.AbsoluteByteStart,
                            Size = info.ByteSize,
                            Stripped = info.Stripped != 0
                        };
                        entry.TypeName = AssetTypes.NameOf(entry.ClassId);
                        Assets.Add(entry);
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }

            log?.Invoke($"Objetos indexados: {Assets.Count}" + (failed > 0 ? $" ({failed} ilegibles)" : ""));
        }

        public List<AssetEntry> OfType(int classId) => Assets.Where(a => a.ClassId == classId).ToList();

        /// <summary>Lee el nombre del objeto (mismo criterio que UABEA).</summary>
        public string ReadName(AssetEntry e, bool usePrefix = true, bool allowScriptLookup = true)
        {
            try
            {
                var file = e.Inst.file;
                var reader = file.Reader;
                long pos = e.Position;

                string typeName = e.TypeName;

                if (file.Metadata.TypeTreeEnabled)
                {
                    TypeTreeType tt = (e.ClassId == 0x72 || e.ClassId < 0)
                        ? file.Metadata.FindTypeTreeTypeByScriptIndex(e.MonoId)
                        : file.Metadata.FindTypeTreeTypeByID(e.ClassId);

                    if (tt != null && tt.Nodes.Count > 0)
                    {
                        typeName = tt.Nodes[0].GetTypeString(tt.StringBuffer);

                        if (tt.Nodes.Count > 1 && tt.Nodes[1].GetNameString(tt.StringBuffer) == "m_Name")
                        {
                            reader.Position = pos;
                            string n = reader.ReadCountStringInt32();
                            return string.IsNullOrEmpty(n) ? "Unnamed asset" : n;
                        }
                        if (typeName == "GameObject")
                        {
                            reader.Position = pos;
                            int size = reader.ReadInt32();
                            int componentSize = file.Header.Version > 0x10 ? 0x0c : 0x10;
                            reader.Position += size * componentSize;
                            reader.Position += 0x04;
                            string n = reader.ReadCountStringInt32();
                            return usePrefix ? "GameObject " + n : n;
                        }
                        if (typeName == "MonoBehaviour")
                        {
                            reader.Position = pos + 0x1c;
                            string n = reader.ReadCountStringInt32();
                            if (!string.IsNullOrEmpty(n))
                                return n;
                            if (!allowScriptLookup)
                                return "";
                            string script = GetScriptClassName(e);
                            return string.IsNullOrEmpty(script) ? "Unnamed asset" : script;
                        }
                    }
                }

                var cldb = Am.ClassDatabase;
                if (cldb != null)
                {
                    ClassDatabaseType type = cldb.FindAssetClassByID(e.ClassId);
                    if (type != null)
                    {
                        typeName = cldb.GetString(type.Name);
                        var nodes = type.GetPreferredNode(false).Children;
                        if (nodes.Count > 1 && cldb.GetString(nodes[0].FieldName) == "m_Name")
                        {
                            reader.Position = pos;
                            string n = reader.ReadCountStringInt32();
                            return string.IsNullOrEmpty(n) ? "Unnamed asset" : n;
                        }
                        if (typeName == "MonoBehaviour")
                        {
                            reader.Position = pos + 0x1c;
                            string n = reader.ReadCountStringInt32();
                            if (!string.IsNullOrEmpty(n))
                                return n;
                            if (!allowScriptLookup)
                                return "";
                            string script = GetScriptClassName(e);
                            return string.IsNullOrEmpty(script) ? "Unnamed asset" : script;
                        }
                    }
                }
            }
            catch { }
            return "Unnamed asset";
        }

        public string GetScriptClassName(AssetEntry e)
        {
            if (e.ClassId != AssetTypes.MonoBehaviour && e.ClassId >= 0)
                return "";

            string cacheKey = e.ContainerPath + "|" + e.MonoId;
            if (monoScriptCache.TryGetValue(cacheKey, out string cached))
                return cached;

            string result = "";

            try
            {
                var reader = e.Inst.file.Reader;

                var monoTemp = Am.GetTemplateBaseField(e.Inst, reader, e.Position, e.ClassId, e.MonoId,
                    AssetReadFlags.ForceFromCldb | AssetReadFlags.SkipMonoBehaviourFields);
                var monoBf = ReadField(e.Inst, monoTemp, e.Position);

                int fileId = monoBf["m_Script"].Children[0].AsInt;
                long pathId = monoBf["m_Script"].Children[1].AsLong;

                AssetsFileInstance target = ResolveDependency(e.Inst, fileId);
                var scriptInfo = target?.file.GetAssetInfo(pathId);

                if (scriptInfo != null)
                {
                    if (scriptNameCache.TryGetValue(pathId, out string byPath))
                    {
                        result = byPath;
                    }
                    else
                    {
                        var scriptTemp = Am.GetTemplateBaseField(target, target.file.Reader,
                            scriptInfo.AbsoluteByteStart, scriptInfo.TypeId, target.file.GetScriptIndex(scriptInfo),
                            AssetReadFlags.None);
                        var scriptBf = ReadField(target, scriptTemp, scriptInfo.AbsoluteByteStart);
                        result = scriptBf["m_ClassName"].AsString ?? "";
                        scriptNameCache[pathId] = result;
                    }
                }
            }
            catch { }

            monoScriptCache[cacheKey] = result;
            return result;
        }

        /// <summary>Genera el TypeTree que se usará para leer y reescribir este objeto.</summary>
        public AssetTypeTemplateField GetTemplateField(AssetsFileInstance inst, long position, int classId, ushort monoId,
            AssetReadFlags flags = AssetReadFlags.None)
        {
            return Am.GetTemplateBaseField(inst, inst.file.Reader, position, classId, monoId, flags);
        }

        private readonly Dictionary<AssetsFileInstance, RefTypeManager> refMans = new Dictionary<AssetsFileInstance, RefTypeManager>();
        private readonly Dictionary<AssetTypeReference, AssetTypeTemplateField> monoRefCache = new Dictionary<AssetTypeReference, AssetTypeTemplateField>();

        /// <summary>
        /// RefTypeManager de cada serializado: sin él, los objetos cuyo TypeTree tiene tipos
        /// referenciados (Unity 6) fallan con "refMan MUST be set to deserialize objects with ref types".
        /// </summary>
        public RefTypeManager GetRefManager(AssetsFileInstance inst)
        {
            if (inst?.file == null)
                return null;

            if (refMans.TryGetValue(inst, out var cached))
                return cached;

            var rm = new RefTypeManager();
            try { rm.FromTypeTree(inst.file.Metadata); } catch { }
            try
            {
                if (Am.MonoTempGenerator != null)
                    rm.WithMonoTemplateGenerator(inst.file.Metadata, Am.MonoTempGenerator, monoRefCache);
            }
            catch { }

            refMans[inst] = rm;
            return rm;
        }

        /// <summary>
        /// Lee el objeto; si está "stripped" o el TypeTree no encaja con los bytes, devuelve la
        /// plantilla con valores por defecto (es lo que hace UABEA al exportar esos objetos).
        /// </summary>
        public AssetTypeValueField ReadFieldSafe(AssetEntry entry, AssetTypeTemplateField temp)
        {
            if (!entry.Stripped)
            {
                try { return ReadField(entry.Inst, temp, entry.Position); }
                catch { }
            }

            try { return ValueBuilder.DefaultValueFieldFromTemplate(temp); }
            catch { return null; }
        }

        /// <summary>Lee el objeto aplicando el RefTypeManager correcto.</summary>
        public AssetTypeValueField ReadField(AssetsFileInstance inst, AssetTypeTemplateField temp, long position)
        {
            return temp.MakeValue(inst.file.Reader, position, GetRefManager(inst));
        }

        private readonly Dictionary<string, AssetsFileInstance> byFileName = new Dictionary<string, AssetsFileInstance>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registra el serializado por su nombre de archivo (para resolver dependencias).</summary>
        private void RegistrarNombre(string key, AssetsFileInstance inst)
        {
            if (string.IsNullOrEmpty(key) || inst == null)
                return;

            try
            {
                string nombre = Path.GetFileName(key);
                if (!string.IsNullOrEmpty(nombre) && !byFileName.ContainsKey(nombre))
                    byFileName[nombre] = inst;

                string sinExt = Path.GetFileNameWithoutExtension(key);
                if (!string.IsNullOrEmpty(sinExt) && !byFileName.ContainsKey(sinExt))
                    byFileName[sinExt] = inst;
            }
            catch { }
        }

        /// <summary>
        /// Resuelve una dependencia externa (m_FileID) también cuando el asset vive dentro de un
        /// bundle: los externals apuntan a "archive:/CAB-xxx/CAB-xxx" y GetDependency no los encuentra.
        /// </summary>
        public AssetsFileInstance ResolveDependency(AssetsFileInstance inst, int fileId)
        {
            if (fileId <= 0)
                return inst;

            int idx = fileId - 1;

            try
            {
                var externals = inst?.file?.Metadata?.Externals;
                if (externals == null || idx < 0 || idx >= externals.Count)
                    return null;

                var ext = externals[idx];
                string path = ext.PathName;
                if (string.IsNullOrEmpty(path)) path = ext.VirtualAssetPathName;
                if (string.IsNullOrEmpty(path)) path = ext.OriginalPathName;
                if (path == null) path = "";
                if (path.Length > 0)
                {
                    var claves = new List<string> { path };
                    if (path.StartsWith("archive:/"))
                        claves.Add(path.Substring(9));
                    claves.Add(Path.GetFileName(path));
                    claves.Add(Path.GetFileNameWithoutExtension(path));

                    foreach (var k in claves)
                    {
                        if (string.IsNullOrEmpty(k))
                            continue;
                        if (instances.TryGetValue(k, out var hit) && hit != null)
                            return hit;
                        if (byFileName.TryGetValue(k, out hit) && hit != null)
                            return hit;
                    }
                }
            }
            catch { }

            try
            {
                var dep = inst.GetDependency(Am, idx);
                if (dep != null)
                    return dep;
            }
            catch { }

            return null;
        }

        public void PrimeNames(IEnumerable<AssetEntry> entries)
        {
            foreach (var e in entries)
                e.Name = ReadName(e);
        }

        /// <summary>Manager independiente para trabajo de escritura (evita cachés y bloqueos).</summary>
        public AssetsManager CreateManager()
        {
            var am = new AssetsManager();
            am.MonoTempGenerator = new MonoCecilTempGenerator(ManagedDir);
            am.UseTemplateFieldCache = true;
            string classData = FindClassData();
            if (classData != null)
            {
                am.LoadClassPackage(classData);
                LoadClassDatabaseFor(am, UnityVersion, null);
            }
            return am;
        }

        /// <summary>
        /// Carga la base de clases probando la versión exacta, luego su familia (mayor.menor)
        /// y, si el .tpk no la tiene, la más parecida. Así sirve cualquier Unity, como UABEA.
        /// </summary>
        private static string LoadClassDatabaseFor(AssetsManager am, string unityVersion, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(unityVersion))
                unityVersion = "2022.3";

            string usado = null;

            try
            {
                if (am.LoadClassDatabaseFromPackage(unityVersion) != null)
                    usado = unityVersion;
            }
            catch { }

            if (usado == null)
            {
                var partes = unityVersion.Split('.');
                if (partes.Length >= 2)
                {
                    string familia = partes[0] + "." + partes[1] + ".0f1";
                    try
                    {
                        if (am.LoadClassDatabaseFromPackage(familia) != null)
                            usado = familia;
                    }
                    catch { }
                }
            }

            if (usado == null)
            {
                // de más nueva a más vieja: la estructura de clases más cercana
                string[] candidatas =
                {
                    "6000.0.0f1", "2023.2.0f1", "2023.1.0f1", "2022.3.0f1", "2022.2.0f1", "2022.1.0f1",
                    "2021.3.0f1", "2021.2.0f1", "2021.1.0f1", "2020.3.0f1", "2020.2.0f1", "2020.1.0f1",
                    "2019.4.0f1", "2019.3.0f1", "2018.4.0f1", "2017.4.0f1", "5.6.0f1"
                };

                foreach (var c in candidatas)
                {
                    try
                    {
                        if (c == unityVersion)
                            continue;
                        if (am.LoadClassDatabaseFromPackage(c) != null)
                        {
                            usado = c;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (log != null)
            {
                if (usado == null)
                    log($"Unity {unityVersion} — sin base de clases compatible en el .tpk (solo campos básicos)");
                else if (usado != unityVersion)
                    log($"Unity {unityVersion} — usando la base de clases {usado} (la más parecida)");
                else
                    log($"Unity {unityVersion} — base de clases exacta");
            }

            return usado;
        }

        /// <summary>Cierra el handle del proyecto sobre un .bundle (para poder reescribirlo).</summary>
        public void ReleaseBundle(string bundlePath)
        {
            var keys = Bundles.Where(kv => kv.Value.BundlePath == bundlePath).Select(kv => kv.Key).ToList();

            try { Am.UnloadBundleFile(bundlePath); } catch { }

            foreach (var k in keys)
            {
                instances.Remove(k);
                LoadedContainers.Remove(k);
            }
        }

        /// <summary>Vuelve a abrir un .bundle ya guardado y refresca los objetos que contenía.</summary>
        public void ReloadBundle(string bundlePath)
        {
            var keys = Bundles.Where(kv => kv.Value.BundlePath == bundlePath).Select(kv => kv.Key).ToList();
            if (keys.Count == 0)
                return;

            var internos = keys.ToDictionary(k => k, k => Bundles[k].InternalName);

            try
            {
                var bun = Am.LoadBundleFile(bundlePath, false);
                if (bun?.file == null)
                    return;

                foreach (var k in keys)
                {
                    int idx = bun.file.GetFileIndex(internos[k]);
                    if (idx < 0)
                        continue;

                    var inst = Am.LoadAssetsFileFromBundle(bun, idx, false);
                    if (inst?.file == null)
                        continue;

                    inst.path = k;
                    instances[k] = inst;
                    LoadedContainers.Add(k);

                    Bundles[k].Index = idx;

                    foreach (var e in Assets)
                        if (e.ContainerPath == k)
                            e.Inst = inst;
                }
            }
            catch { }
        }

        public AssetsFileInstance GetContainer(string path)
        {
            return instances.TryGetValue(path, out var inst) ? inst : null;
        }

        /// <summary>Cierra el handle del proyecto sobre un contenedor para poder reescribirlo.</summary>
        public void ReleaseContainer(string path)
        {
            try { Am.UnloadAssetsFile(path); } catch { }
            instances.Remove(path);
        }

        public void Dispose()
        {
            try { Am.UnloadAllAssetsFiles(true); } catch { }
        }
    }
}
