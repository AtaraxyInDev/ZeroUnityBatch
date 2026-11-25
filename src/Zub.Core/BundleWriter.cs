using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Zub
{
    /// <summary>
    /// Guarda objetos modificados dentro de un AssetBundle (.bundle): se reescribe el
    /// serializado interno con los cambios y se vuelve a empaquetar el bundle completo.
    /// </summary>
    public static class BundleWriter
    {
        public static ImportReport Apply(UnityProject proj, string key, UnityProject.BundleLocation loc,
            List<RawEdit> edits, bool verify, bool keepBackup)
        {
            var report = new ImportReport { Container = key };

            AssetsManager am = null;
            BundleFileInstance bun = null;

            try
            {
                am = proj.CreateManager();
                bun = am.LoadBundleFile(loc.BundlePath, false);

                if (bun?.file == null)
                {
                    report.Failed = edits.Count;
                    report.Errors.Add("No se pudo abrir el bundle " + Path.GetFileName(loc.BundlePath));
                    return report;
                }

                var inst = am.LoadAssetsFileFromBundle(bun, loc.Index, false);
                if (inst?.file == null)
                {
                    report.Failed = edits.Count;
                    report.Errors.Add("No se pudo abrir el archivo interno del bundle");
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
                        report.Errors.Add($"{edit.Label}: PathID {edit.Entry.PathId} ya no existe en el bundle");
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

                // nombre interno del serializado dentro del bundle
                string interno = loc.InternalName;
                int idx = bun.file.GetFileIndex(interno);
                if (idx >= 0)
                    interno = bun.file.GetFileName(idx);

                string tmpUnc = loc.BundlePath + ".zub_unc";
                string tmp = loc.BundlePath + ".zub_new";
                foreach (var f in new[] { tmpUnc, tmp })
                    if (File.Exists(f)) File.Delete(f);

                // compresión que traía el bundle (LZ4, LZMA o ninguna)
                AssetBundleCompressionType comp = AssetBundleCompressionType.None;
                try { comp = bun.file.GetCompressionType(bun.file.BlockAndDirInfo.BlockInfos); } catch { }

                // 1. serializado nuevo dentro de un bundle sin comprimir
                using (var fs = new FileStream(tmpUnc, FileMode.Create, FileAccess.Write))
                using (var w = new AssetsFileWriter(fs))
                {
                    var bundleReplacers = new List<BundleReplacer>
                    {
                        new BundleReplacerFromAssets(interno, interno, null, replacers, 0)
                    };

                    bun.file.Write(w, bundleReplacers);
                }

                // 2. volver a comprimir con el mismo formato que el original
                try
                {
                    var am2 = new AssetsManager();
                    var bun2 = am2.LoadBundleFile(tmpUnc, true);

                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    using (var w = new AssetsFileWriter(fs))
                    {
                        bun2.file.Pack(bun2.file.Reader, w, comp, true, null);
                    }

                    am2.UnloadAllAssetsFiles(true);
                    am2.UnloadAllBundleFiles();
                }
                catch (Exception ex)
                {
                    report.Errors.Add("Al recomprimir el bundle: " + ex.Message + " (se guarda sin comprimir)");
                    File.Copy(tmpUnc, tmp, true);
                }
                finally
                {
                    try { File.Delete(tmpUnc); } catch { }
                }

                report.Applied = replacers.Count;
                report.BytesAfter = (int)(new FileInfo(tmp).Length - new FileInfo(loc.BundlePath).Length);

                if (verify && !Verify(loc.BundlePath, tmp, interno, expected, report))
                {
                    report.Notes.Add("Verificación fallida: el bundle original NO se ha tocado.");
                    try { File.Delete(tmp); } catch { }
                    return report;
                }

                report.Verified = true;

                // hay que soltar TODOS los handles del bundle antes de reemplazarlo
                try { am.UnloadAllAssetsFiles(true); } catch { }
                try { am.UnloadAllBundleFiles(); } catch { }
                am = null;
                bun = null;
                proj.ReleaseBundle(loc.BundlePath);

                if (keepBackup)
                {
                    string bak = loc.BundlePath + ".zub_bak";
                    if (!File.Exists(bak))
                        File.Copy(loc.BundlePath, bak);
                }

                try
                {
                    File.Replace(tmp, loc.BundlePath, null);
                }
                catch
                {
                    File.Copy(tmp, loc.BundlePath, true);
                    File.Delete(tmp);
                }

                proj.ReloadBundle(loc.BundlePath);
            }
            catch (Exception ex)
            {
                report.Errors.Add("Bundle: " + ex.Message);
                report.Failed += Math.Max(1, edits.Count - report.Applied);
                try { if (File.Exists(loc.BundlePath + ".zub_new")) File.Delete(loc.BundlePath + ".zub_new"); } catch { }
            }
            finally
            {
                try { am?.UnloadAllAssetsFiles(true); } catch { }
                try { am?.UnloadAllBundleFiles(); } catch { }
            }

            return report;
        }

        private static bool Verify(string original, string nuevo, string interno,
            Dictionary<long, byte[]> expected, ImportReport report)
        {
            try
            {
                var am = new AssetsManager();
                var bun = am.LoadBundleFile(nuevo, false);

                int idx = bun.file.GetFileIndex(interno);
                if (idx < 0)
                {
                    report.Errors.Add("Verificación: el serializado no está en el bundle nuevo");
                    return false;
                }

                var inst = am.LoadAssetsFileFromBundle(bun, idx, false);
                var infos = inst.file.AssetInfos.ToDictionary(i => i.PathId, i => i);

                foreach (var kv in expected)
                {
                    if (!infos.TryGetValue(kv.Key, out var info))
                    {
                        report.Errors.Add($"Verificación: falta el PathID {kv.Key}");
                        return false;
                    }

                    byte[] leido = BatchImporter.ReadRaw(inst, info);
                    if (leido == null || BatchImporter.Sha(leido) != BatchImporter.Sha(kv.Value))
                    {
                        report.Errors.Add($"Verificación: el PathID {kv.Key} no coincide al releer");
                        return false;
                    }
                }

                am.UnloadAllAssetsFiles(true);
                am.UnloadAllBundleFiles();
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
