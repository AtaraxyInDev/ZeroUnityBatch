using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Zub
{
    public sealed class ImportItem
    {
        public AssetEntry Entry;
        public string SourceFile;
    }

    /// <summary>Objeto con su nuevo cuerpo binario ya serializado.</summary>
    public sealed class RawEdit
    {
        public AssetEntry Entry;
        public byte[] Data;
        public string Label = "";
    }

    public sealed class ImportReport
    {
        public string Container = "";
        public int Matched;
        public int Applied;
        public int Unchanged;
        public int Failed;
        public double Seconds;
        public int BytesBefore;
        public int BytesAfter;
        public bool Verified;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Notes = new List<string>();

        public string Summary =>
            $"{Path.GetFileName(Container)}: {Applied} aplicados, {Unchanged} sin cambios, {Failed} fallos, " +
            $"{Seconds:F2}s" + (Verified ? ", verificado" : "");
    }

    public static class BatchImporter
    {
        private static readonly Regex PathIdRegex = new Regex(@"-(-?\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// Empareja los archivos de una carpeta con los objetos del proyecto.
        /// Identidad real = contenedor + PathID; el nombre solo se usa como pista.
        /// </summary>
        public static List<ImportItem> MatchFolder(UnityProject proj, string folder, IEnumerable<AssetEntry> pool,
            out List<string> unmatched)
        {
            unmatched = new List<string>();

            var byContainerAndId = new Dictionary<(string, long), AssetEntry>();
            var byId = new Dictionary<long, List<AssetEntry>>();

            foreach (var e in pool)
            {
                byContainerAndId[(e.ContainerName.ToLowerInvariant(), e.PathId)] = e;
                if (!byId.TryGetValue(e.PathId, out var list))
                    byId[e.PathId] = list = new List<AssetEntry>();
                list.Add(e);
            }

            var items = new List<ImportItem>();

            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                Match m = PathIdRegex.Match(stem);
                if (!m.Success)
                {
                    unmatched.Add(Path.GetFileName(file) + " (sin PathID en el nombre)");
                    continue;
                }

                if (!long.TryParse(m.Groups[1].Value, out long pathId))
                {
                    unmatched.Add(Path.GetFileName(file) + " (PathID inválido)");
                    continue;
                }

                string prefix = stem.Substring(0, m.Index);
                int sep = prefix.LastIndexOf('-');
                string containerName = sep >= 0 ? prefix.Substring(sep + 1) : "";

                AssetEntry entry = null;
                if (!string.IsNullOrEmpty(containerName))
                    byContainerAndId.TryGetValue((containerName.ToLowerInvariant(), pathId), out entry);

                if (entry == null && byId.TryGetValue(pathId, out var candidates) && candidates.Count == 1)
                    entry = candidates[0];

                if (entry == null)
                {
                    unmatched.Add(Path.GetFileName(file) + " (no encontrado en el juego)");
                    continue;
                }

                items.Add(new ImportItem { Entry = entry, SourceFile = file });
            }

            return items;
        }

        /// <summary>Empareja un único archivo (para "reimportar este JSON").</summary>
        public static AssetEntry MatchOne(string filePath, IEnumerable<AssetEntry> pool)
        {
            string stem = Path.GetFileNameWithoutExtension(filePath);
            Match m = PathIdRegex.Match(stem);
            if (!m.Success || !long.TryParse(m.Groups[1].Value, out long pathId))
                return null;

            string prefix = stem.Substring(0, m.Index);
            int sep = prefix.LastIndexOf('-');
            string containerName = sep >= 0 ? prefix.Substring(sep + 1).ToLowerInvariant() : "";

            AssetEntry single = null;
            int count = 0;
            foreach (var e in pool)
            {
                if (e.PathId != pathId)
                    continue;
                count++;
                if (!string.IsNullOrEmpty(containerName) &&
                    e.ContainerName.Equals(containerName, StringComparison.OrdinalIgnoreCase))
                    return e;
                single = e;
            }
            return count == 1 ? single : null;
        }

        /// <summary>Convierte los archivos de una carpeta en cuerpos binarios listos para escribir.</summary>
        public static List<RawEdit> BuildEdits(UnityProject proj, IEnumerable<ImportItem> items, out List<string> errors,
            Action<int, int> progress = null)
        {
            errors = new List<string>();
            var list = items.ToList();
            var edits = new RawEdit[list.Count];
            var fallos = new string[list.Count];
            var plantillas = new AssetTypeTemplateField[list.Count];
            var pesados = new HashSet<int>();

            // fase 1 (serie): plantillas (usan las cachés de AssetsManager, no son reentrantes)
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i].Entry;
                if (entry.ClassId == AssetTypes.TextAsset || entry.ClassId == AssetTypes.Texture2D)
                {
                    pesados.Add(i);
                    continue;
                }

                try
                {
                    plantillas[i] = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);
                }
                catch (Exception ex)
                {
                    fallos[i] = $"{Path.GetFileName(list[i].SourceFile)}: {ex.Message}";
                }

                if (progress != null && (i % 25 == 0 || i == list.Count - 1))
                    progress(i + 1, list.Count);
            }

            // fase 2 (paralela): leer JSON + serializar el objeto
            int hilos = Math.Max(1, Math.Min(Environment.ProcessorCount, 16));
            if (int.TryParse(Environment.GetEnvironmentVariable("ZUB_THREADS"), out int forzados))
                hilos = Math.Max(1, Math.Min(forzados, 64));

            var pendientes = new List<int>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (!pesados.Contains(i) && fallos[i] == null)
                    pendientes.Add(i);
            }

            System.Threading.Tasks.Parallel.For(0, pendientes.Count,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = hilos }, k =>
            {
                int i = pendientes[k];
                var item = list[i];
                try
                {
                    string json = File.ReadAllText(item.SourceFile);
                    byte[] data = JsonCodec.Import(plantillas[i], json, out string err);
                    if (data == null)
                        fallos[i] = $"{Path.GetFileName(item.SourceFile)}: {err}";
                    else
                        edits[i] = new RawEdit { Entry = item.Entry, Data = data, Label = Path.GetFileName(item.SourceFile) };
                }
                catch (Exception ex)
                {
                    fallos[i] = $"{Path.GetFileName(item.SourceFile)}: {ex.Message}";
                }
            });

            // fase 3 (serie): TextAsset y texturas (usan los readers compartidos)
            foreach (int i in pesados)
            {
                var item = list[i];
                try
                {
                    AssetEntry entry = item.Entry;
                    byte[] data;

                    if (entry.ClassId == AssetTypes.TextAsset)
                    {
                        byte[] fileBytes = File.ReadAllBytes(item.SourceFile);
                        data = ContentReader.BuildTextAsset(proj, entry, fileBytes, out string err);
                        if (data == null)
                        {
                            fallos[i] = $"{Path.GetFileName(item.SourceFile)}: {err}";
                            continue;
                        }
                    }
                    else
                    {
                        data = TextureReader.Import(proj, entry, item.SourceFile, out string err);
                        if (data == null)
                        {
                            fallos[i] = $"{Path.GetFileName(item.SourceFile)}: {err}";
                            continue;
                        }
                    }

                    edits[i] = new RawEdit { Entry = entry, Data = data, Label = Path.GetFileName(item.SourceFile) };
                }
                catch (Exception ex)
                {
                    fallos[i] = $"{Path.GetFileName(item.SourceFile)}: {ex.Message}";
                }
            }

            var lista = new List<RawEdit>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (fallos[i] != null)
                    errors.Add(fallos[i]);
                else if (edits[i] != null)
                    lista.Add(edits[i]);
            }

            if (progress != null)
                progress(list.Count, list.Count);

            return lista;
        }

        /// <summary>Importa una carpeta completa (una sola escritura por contenedor).</summary>
        public static List<ImportReport> ImportFolder(UnityProject proj, IEnumerable<ImportItem> items,
            bool verify, bool keepBackup, out List<string> buildErrors, Action<int, int> progress = null)
        {
            var edits = BuildEdits(proj, items, out buildErrors, progress);
            return ApplyRaw(proj, edits, verify, keepBackup);
        }

        /// <summary>Aplica un cuerpo binario a un solo objeto (editar y probar en el juego).</summary>
        public static ImportReport ApplySingle(UnityProject proj, AssetEntry entry, byte[] data,
            bool verify = true, bool keepBackup = true)
        {
            return ApplyToContainer(proj, entry.ContainerPath,
                new List<RawEdit> { new RawEdit { Entry = entry, Data = data, Label = entry.Name } },
                verify, keepBackup);
        }

        /// <summary>Agrupa por contenedor y escribe cada contenedor una sola vez.</summary>
        public static List<ImportReport> ApplyRaw(UnityProject proj, List<RawEdit> edits, bool verify, bool keepBackup)
        {
            var reports = new List<ImportReport>();
            foreach (var group in edits.GroupBy(e => e.Entry.ContainerPath).OrderByDescending(g => g.Count()))
                reports.Add(ApplyToContainer(proj, group.Key, group.ToList(), verify, keepBackup));
            return reports;
        }

        public static ImportReport ApplyToContainer(UnityProject proj, string containerPath, List<RawEdit> edits,
            bool verify, bool keepBackup)
        {
            var report = new ImportReport { Container = containerPath };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // un objeto no puede repetirse: el CSV puede traer varias filas del mismo archivo
            edits = edits.GroupBy(e => e.Entry.PathId).Select(g => g.Last()).ToList();

            // serializados troceados en .splitN: se unen, se reescriben y se vuelven a partir
            if (proj.Splits.TryGetValue(containerPath, out var split))
                return SplitWriter.Apply(proj, containerPath, split, edits, verify, keepBackup);

            // assets dentro de un AssetBundle: se reempaqueta el bundle
            if (proj.Bundles.TryGetValue(containerPath, out var bunLoc))
                return BundleWriter.Apply(proj, containerPath, bunLoc, edits, verify, keepBackup);

            string tmpPath = containerPath + ".zub_new";
            AssetsManager am = null;
            var expected = new Dictionary<long, byte[]>();

            try
            {
                am = proj.CreateManager();
                var inst = am.LoadAssetsFile(containerPath, true);
                var infos = new Dictionary<long, AssetFileInfo>();
                foreach (var info in inst.file.AssetInfos)
                    infos[info.PathId] = info;

                var replacers = new List<AssetsReplacer>();
                report.Matched = edits.Count;

                foreach (RawEdit edit in edits)
                {
                    long pathId = edit.Entry.PathId;
                    if (!infos.TryGetValue(pathId, out var info))
                    {
                        report.Failed++;
                        report.Errors.Add($"{edit.Label}: PathID {pathId} ya no existe en el contenedor");
                        continue;
                    }

                    try
                    {
                        byte[] original = ReadRaw(inst, info);
                        if (original != null && Sha(original) == Sha(edit.Data))
                        {
                            report.Unchanged++;
                            continue;
                        }

                        replacers.Add(new AssetsReplacerFromMemory(pathId, info.TypeId,
                            inst.file.GetScriptIndex(info), edit.Data));
                        expected[pathId] = edit.Data;
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        report.Errors.Add($"{edit.Label}: {ex.Message}");
                    }
                }

                report.BytesBefore = replacers.Count;

                if (replacers.Count == 0)
                {
                    report.Notes.Add("Nada que escribir (todos iguales o con error).");
                    report.Verified = true;
                    return report;
                }

                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);

                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.ReadWrite))
                using (var w = new AssetsFileWriter(fs))
                {
                    inst.file.Write(w, 0, replacers);
                }

                report.BytesAfter = (int)(new FileInfo(tmpPath).Length - new FileInfo(containerPath).Length);
                report.Applied = replacers.Count;

                // libera el original antes de reemplazarlo
                am.UnloadAllAssetsFiles(true);
                am = null;
                proj.ReleaseContainer(containerPath);

                if (verify)
                {
                    report.Verified = VerifyContainer(proj, tmpPath, expected, report);
                    if (!report.Verified)
                    {
                        report.Notes.Add("Verificación fallida: el original NO se ha modificado.");
                        try { File.Delete(tmpPath); } catch { }
                        return report;
                    }
                }
                else
                {
                    report.Verified = true;
                }

                if (keepBackup)
                {
                    string bak = containerPath + ".zub_bak";
                    if (!File.Exists(bak))
                        File.Copy(containerPath, bak);
                }

                try
                {
                    File.Replace(tmpPath, containerPath, null);
                }
                catch
                {
                    File.Copy(tmpPath, containerPath, true);
                    File.Delete(tmpPath);
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add("Contenedor: " + ex.Message);
                report.Failed += Math.Max(1, edits.Count - report.Applied);
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
            finally
            {
                if (am != null)
                {
                    try { am.UnloadAllAssetsFiles(true); } catch { }
                }
            }

            sw.Stop();
            report.Seconds = sw.Elapsed.TotalSeconds;
            return report;
        }

        private static bool VerifyContainer(UnityProject proj, string path, Dictionary<long, byte[]> expected,
            ImportReport report)
        {
            AssetsManager am = proj.CreateManager();
            try
            {
                var inst = am.LoadAssetsFile(path, true);
                var infos = new Dictionary<long, AssetFileInfo>();
                foreach (var info in inst.file.AssetInfos)
                    infos[info.PathId] = info;

                int bad = 0;
                foreach (var kv in expected)
                {
                    if (!infos.TryGetValue(kv.Key, out var info))
                    {
                        bad++;
                        report.Errors.Add($"Verificación: falta el PathID {kv.Key}");
                        continue;
                    }

                    byte[] actual = ReadRaw(inst, info);
                    if (actual == null || Sha(actual) != Sha(kv.Value))
                    {
                        bad++;
                        report.Errors.Add($"Verificación: bytes distintos en PathID {kv.Key}");
                    }
                }

                if (bad > 0)
                {
                    report.Failed += bad;
                    report.Applied -= bad;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                report.Errors.Add("Verificación: " + ex.Message);
                return false;
            }
            finally
            {
                try { am.UnloadAllAssetsFiles(true); } catch { }
            }
        }

        internal static byte[] ReadRaw(AssetsFileInstance inst, AssetFileInfo info)
        {
            try
            {
                var reader = inst.file.Reader;
                reader.Position = info.AbsoluteByteStart;
                return reader.ReadBytes((int)info.ByteSize);
            }
            catch
            {
                return null;
            }
        }

        internal static string Sha(byte[] data)
        {
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(data));
        }
    }
}
