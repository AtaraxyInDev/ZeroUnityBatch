using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Zub
{
    /// <summary>
    /// Guarda los objetos modificados de un serializado troceado (.split0, .split1, …).
    /// Unity 2022 en Android parte los assets grandes en bloques de 1 MB que el juego lee
    /// como una concatenación, así que se reescribe el archivo completo y se vuelve a partir.
    /// </summary>
    public static class SplitWriter
    {
        public static ImportReport Apply(UnityProject proj, string key, UnityProject.SplitLocation loc,
            List<RawEdit> edits, bool verify, bool keepBackup)
        {
            var report = new ImportReport { Container = key };

            var inst = proj.GetContainer(key);
            if (inst == null)
            {
                report.Failed = edits.Count;
                report.Errors.Add("El contenedor unido ya no está abierto; vuelve a cargar el proyecto.");
                return report;
            }

            var infos = new Dictionary<long, AssetFileInfo>();
            foreach (var info in inst.file.AssetInfos)
                infos[info.PathId] = info;

            var replacers = new List<AssetsReplacer>();
            var expected = new Dictionary<long, byte[]>();
            report.Matched = edits.Count;

            foreach (RawEdit edit in edits)
            {
                if (!infos.TryGetValue(edit.Entry.PathId, out var info))
                {
                    report.Failed++;
                    report.Errors.Add($"{edit.Label}: PathID {edit.Entry.PathId} ya no existe");
                    continue;
                }

                try
                {
                    byte[] original = BatchImporter.ReadRaw(inst, info);
                    if (original != null && BatchImporter.Sha(original) == BatchImporter.Sha(edit.Data))
                    {
                        report.Unchanged++;
                        continue;
                    }

                    replacers.Add(new AssetsReplacerFromMemory(edit.Entry.PathId, info.TypeId,
                        inst.file.GetScriptIndex(info), edit.Data));
                    expected[edit.Entry.PathId] = edit.Data;
                }
                catch (Exception ex)
                {
                    report.Failed++;
                    report.Errors.Add($"{edit.Label}: {ex.Message}");
                }
            }

            if (replacers.Count == 0)
            {
                report.Notes.Add("Nada que escribir (todos iguales o con error).");
                report.Verified = true;
                return report;
            }

            // 1. contenedor completo nuevo (a un temporal: el writer cierra el stream al salir)
            string tmpUnion = Path.Combine(Path.GetTempPath(), "zub_union_" + Guid.NewGuid().ToString("N") + ".assets");

            try
            {
                using (var fs = new FileStream(tmpUnion, FileMode.Create, FileAccess.ReadWrite))
                using (var w = new AssetsFileWriter(fs))
                    inst.file.Write(w, 0, replacers);
            }
            catch (Exception ex)
            {
                report.Errors.Add("Al reconstruir el contenedor: " + ex.Message);
                report.Failed += replacers.Count;
                try { File.Delete(tmpUnion); } catch { }
                return report;
            }

            var ms = new MemoryStream(File.ReadAllBytes(tmpUnion));
            try { File.Delete(tmpUnion); } catch { }

            report.Applied = replacers.Count;
            report.BytesAfter = (int)ms.Length;

            // 2. verificar releyendo
            if (verify && !Verify(proj, ms, expected, report))
            {
                report.Notes.Add("Verificación fallida: las partes originales NO se han tocado.");
                return report;
            }

            report.Verified = true;

            // 3. volver a partir en bloques
            string dir = Path.GetDirectoryName(loc.BasePath);
            string baseName = Path.GetFileName(loc.BasePath);
            int partSize = loc.PartSize > 0 ? loc.PartSize : 1048576;

            int parts = (int)Math.Max(1, (ms.Length + partSize - 1) / partSize);
            var nuevos = new List<string>();

            try
            {
                ms.Position = 0;
                for (int i = 0; i < parts; i++)
                {
                    string temp = Path.Combine(dir, $"{baseName}.split{i}.zub_new");
                    using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
                    {
                        var buf = new byte[partSize];
                        int leido = 0;
                        while (leido < partSize)
                        {
                            int n = ms.Read(buf, leido, partSize - leido);
                            if (n <= 0) break;
                            leido += n;
                        }
                        fs.Write(buf, 0, leido);
                    }
                    nuevos.Add(temp);
                }

                // 4. respaldo y sustitución de cada parte
                for (int i = 0; i < parts; i++)
                {
                    string final = Path.Combine(dir, $"{baseName}.split{i}");

                    if (keepBackup && File.Exists(final) && !File.Exists(final + ".zub_bak"))
                        File.Copy(final, final + ".zub_bak");

                    if (File.Exists(final)) File.Delete(final);
                    File.Move(nuevos[i], final);
                }

                // partes sobrantes de una versión anterior más grande
                for (int i = parts; ; i++)
                {
                    string sobra = Path.Combine(dir, $"{baseName}.split{i}");
                    if (!File.Exists(sobra)) break;
                    if (keepBackup && !File.Exists(sobra + ".zub_bak"))
                        File.Copy(sobra, sobra + ".zub_bak");
                    File.Delete(sobra);
                }
            }
            catch (Exception ex)
            {
                report.Errors.Add("Al reescribir las partes: " + ex.Message);
                report.Failed += replacers.Count;
            }

            return report;
        }

        private static bool Verify(UnityProject proj, MemoryStream ms, Dictionary<long, byte[]> expected, ImportReport report)
        {
            try
            {
                ms.Position = 0;
                var am = proj.CreateManager();
                var inst = am.LoadAssetsFile(new MemoryStream(ms.ToArray()), "verify", false, null);
                var infos = inst.file.AssetInfos.ToDictionary(i => i.PathId, i => i);

                foreach (var kv in expected)
                {
                    if (!infos.TryGetValue(kv.Key, out var info))
                    {
                        report.Errors.Add($"Verificación: falta el PathID {kv.Key}");
                        return false;
                    }

                    byte[] nuevo = BatchImporter.ReadRaw(inst, info);
                    if (nuevo == null || BatchImporter.Sha(nuevo) != BatchImporter.Sha(kv.Value))
                    {
                        report.Errors.Add($"Verificación: el PathID {kv.Key} no coincide al releer");
                        return false;
                    }
                }

                am.UnloadAllAssetsFiles(true);
                return true;
            }
            catch (Exception ex)
            {
                report.Errors.Add("Verificación: " + ex.Message);
                return false;
            }
        }
    }
}
