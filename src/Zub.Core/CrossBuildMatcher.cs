using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Zub
{
    /// <summary>
    /// Empareja JSON exportados de otra build (p. ej. la de PC) con los objetos del proyecto
    /// abierto. Los PathID cambian entre PC y Android, así que se apoya en el nombre del
    /// GameObject y, si hace falta, en el PathID; siempre validando que el JSON encaje.
    /// </summary>
    public static class CrossBuildMatcher
    {
        public sealed class Stats
        {
            public int Files;
            public int Exact;
            public int ByName;
            public int ByPathId;
            public int Duplicados;
            public int Unmatched;
            public List<string> Missing = new List<string>();
            public List<string> Ambiguous = new List<string>();
            public List<ImportItem> Items = new List<ImportItem>();

            public string Summary =>
                $"{Files:N0} archivos · {Exact:N0} exactos · {ByName:N0} por nombre · " +
                $"{ByPathId:N0} por PathID · {Duplicados:N0} copias · {Unmatched:N0} sin pareja";
        }

        public static Stats Match(UnityProject proj, string folder, IEnumerable<AssetEntry> pool,
            Action<int, int> progress = null)
        {
            var stats = new Stats();
            var poolList = pool.ToList();

            var byContainerAndId = new Dictionary<(string, long), AssetEntry>();
            var byId = new Dictionary<long, List<AssetEntry>>();

            foreach (var e in poolList)
            {
                byContainerAndId[(e.ContainerName.ToLowerInvariant(), e.PathId)] = e;

                if (!byId.TryGetValue(e.PathId, out var l))
                    byId[e.PathId] = l = new List<AssetEntry>();
                l.Add(e);
            }

            var vistos = new HashSet<string>();

            void Add(AssetEntry entry, string file)
            {
                string k = entry.ContainerPath + "#" + entry.PathId;
                if (vistos.Add(k))
                    stats.Items.Add(new ImportItem { Entry = entry, SourceFile = file });
            }

            var pendientes = new List<(string file, string nombre, string contenedor, long pid)>();
            var archivos = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                                    .OrderBy(f => f).ToList();

            int i0 = 0;

            foreach (string file in archivos)
            {
                if ((i0++ & 0x1F) == 0)
                    progress?.Invoke(i0, archivos.Count);

                string stem = Path.GetFileNameWithoutExtension(file);
                Match m = PathIdRegex.Match(stem);

                if (!m.Success || !long.TryParse(m.Groups[1].Value, out long pathId))
                {
                    stats.Unmatched++;
                    stats.Missing.Add(Path.GetFileName(file) + " (sin PathID en el nombre)");
                    continue;
                }

                stats.Files++;

                string prefix = stem.Substring(0, m.Index);
                int sep = prefix.LastIndexOf('-');
                string contenedor = sep >= 0 ? prefix.Substring(sep + 1) : "";
                string nombre = sep >= 0 ? prefix.Substring(0, sep) : prefix;

                if (contenedor.Length > 0 &&
                    byContainerAndId.TryGetValue((contenedor.ToLowerInvariant(), pathId), out var direct) &&
                    CanApply(proj, direct, file, out _))
                {
                    stats.Exact++;
                    Add(direct, file);
                    continue;
                }

                pendientes.Add((file, nombre, contenedor, pathId));
            }

            if (pendientes.Count == 0)
                return stats;

            // 2ª pasada: por nombre del GameObject (independiente de los PathID)
            var indice = GameObjectNames.Build(proj, poolList, progress);

            foreach (var p in pendientes)
            {
                var candidatos = new List<AssetEntry>();

                if (!string.IsNullOrWhiteSpace(p.nombre) && indice.TryGetValue(p.nombre, out var porNombre))
                    candidatos.AddRange(porNombre.Where(c => CanApply(proj, c, p.file, out _)));

                if (candidatos.Count > 0)
                {
                    if (candidatos.Count == 1) stats.ByName++;
                    else stats.Duplicados++;

                    foreach (var c in candidatos)
                        Add(c, p.file);
                    continue;
                }

                // 3ª pasada: mismo PathID en otro contenedor
                if (byId.TryGetValue(p.pid, out var otros))
                {
                    var validos = otros.Where(c => CanApply(proj, c, p.file, out _)).ToList();

                    if (validos.Count > 0)
                    {
                        if (validos.Count == 1) stats.ByPathId++;
                        else stats.Duplicados++;

                        foreach (var c in validos)
                            Add(c, p.file);
                        continue;
                    }
                }

                stats.Unmatched++;
                if (stats.Missing.Count < 4000)
                    stats.Missing.Add(Path.GetFileName(p.file) + " (no encontrado en esta build)");
            }

            return stats;
        }

        private static bool CanApply(UnityProject proj, AssetEntry e, string file, out string error)
        {
            error = null;

            try
            {
                if (e.ClassId == AssetTypes.TextAsset)
                {
                    byte[] data = ContentReader.BuildTextAsset(proj, e, File.ReadAllBytes(file), out error);
                    return data != null;
                }

                if (e.ClassId == AssetTypes.Texture2D)
                {
                    byte[] img = TextureReader.Import(proj, e, file, out error);
                    return img != null;
                }

                string json = File.ReadAllText(file);
                byte[] bytes = ContentReader.BuildMonoBytes(proj, e, json, out error);
                return bytes != null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static readonly Regex PathIdRegex = new Regex(@"-(-?\d+)$", RegexOptions.Compiled);
    }
}
