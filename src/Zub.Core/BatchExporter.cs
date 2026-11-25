using AssetsTools.NET.Extra;
using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Zub
{
    public sealed class ExportReport
    {
        public int Exported;
        public int Failed;
        public double Seconds;
        public string OutDir = "";
        public readonly List<string> Errors = new List<string>();
    }

    public static class BatchExporter
    {
        /// <summary>Exporta un objeto como JSON con el mismo formato que UABEA Next.</summary>
        public static string ExportJson(UnityProject proj, AssetEntry entry)
        {
            var tempField = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId, AssetReadFlags.None);
            var value = tempField.MakeValue(entry.Inst.file.Reader, entry.Position);
            return JsonCodec.Export(value);
        }

        public static ExportReport Export(UnityProject proj, IEnumerable<AssetEntry> entries, string outDir,
            Action<int, int, string> progress = null)
        {
            var report = new ExportReport { OutDir = outDir };
            var sw = Stopwatch.StartNew();
            Directory.CreateDirectory(outDir);

            var list = entries.ToList();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                try
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        entry.Name = proj.ReadName(entry);

                    string stem = entry.MatchStem;
                    byte[] payload;
                    string ext;

                    if (entry.ClassId == AssetTypes.TextAsset)
                    {
                        byte[] data = ContentReader.ReadTextAssetBytes(proj, entry, out _, out string err);
                        if (data == null)
                            throw new Exception(err);
                        ext = ContentReader.LooksBinary(data) ? ".bin" : ".txt";
                        payload = data;
                    }
                    else if (entry.ClassId == AssetTypes.Texture2D)
                    {
                        var tex = TextureReader.Preview(proj, entry, 0);
                        if (!tex.Decodable)
                            throw new Exception(tex.Error);
                        ext = ".png";
                        payload = tex.Png;
                    }
                    else
                    {
                        ext = ".json";
                        payload = System.Text.Encoding.UTF8.GetBytes(ExportJson(proj, entry));
                    }

                    string file = Path.Combine(outDir, stem + ext);

                    // nombres duplicados dentro del mismo contenedor (no debería pasar, pero no pisar)
                    int n = 1;
                    while (!written.Add(file))
                        file = Path.Combine(outDir, $"{stem} ({++n}){ext}");

                    File.WriteAllBytes(file, payload);
                    report.Exported++;
                }
                catch (Exception ex)
                {
                    report.Failed++;
                    report.Errors.Add($"{entry.ContainerName}#{entry.PathId}: " + (Environment.GetEnvironmentVariable("ZUB_DEBUG") == "1" ? ex.ToString() : ex.Message));
                }

                if (progress != null && (i % 25 == 0 || i == list.Count - 1))
                    progress(i + 1, list.Count, entry.Name);
            }

            sw.Stop();
            report.Seconds = sw.Elapsed.TotalSeconds;
            return report;
        }
    }
}
