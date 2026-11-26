using AssetsTools.NET.Extra;
using AssetsTools.NET;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        // perfilado interno (ZUB_PROF=1)
        internal static long ProfTemplate, ProfRead, ProfJson;

        /// <summary>Exporta un objeto como JSON con el mismo formato que UABEA Next.</summary>
        public static string ExportJson(UnityProject proj, AssetEntry entry)
        {
            long t0 = Stopwatch.GetTimestamp();
            var tempField = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId, AssetReadFlags.None);
            long t1 = Stopwatch.GetTimestamp();
            var value = proj.ReadFieldSafe(entry, tempField);
            long t2 = Stopwatch.GetTimestamp();
            string json = value == null ? "" : JsonCodec.Export(value);
            long t3 = Stopwatch.GetTimestamp();

            ProfTemplate += t1 - t0;
            ProfRead += t2 - t1;
            ProfJson += t3 - t2;

            return json;
        }

        /// <summary>Primera ruta libre para ese nombre (nombres duplicados -> " (2)", " (3)"...).</summary>
        private static string RutaLibre(string outDir, HashSet<string> ocupados, string stem, string ext)
        {
            string file = Path.Combine(outDir, stem + ext);
            int n = 1;
            while (!ocupados.Add(file))
                file = Path.Combine(outDir, $"{stem} ({++n}){ext}");
            return file;
        }

        /// <summary>Un objeto preparado para exportar.</summary>
        private sealed class Job
        {
            public AssetEntry Entry;
            public string Stem = "";
            public string Ext = ".json";
            public AssetTypeTemplateField Temp;
            public byte[] Bytes;
            public string File;
            public string Error;
            public bool Texto;
            public bool Textura;
        }

        /// <summary>
        /// Exporta en lote. Fase 1 lee nombres, plantillas y bytes en serie (el reader de cada
        /// serializado no es reentrante); fase 2 deserializa y escribe JSON en paralelo, cada hilo
        /// con su propio reader sobre la copia en memoria del objeto.
        /// </summary>
        public static ExportReport Export(UnityProject proj, IEnumerable<AssetEntry> entries, string outDir,
            Action<int, int, string> progress = null)
        {
            var report = new ExportReport { OutDir = outDir };
            var sw = Stopwatch.StartNew();
            Directory.CreateDirectory(outDir);

            var reloj = new long[4];
            long c0 = Stopwatch.GetTimestamp();

            var list = entries.ToList();
            var jobs = new Job[list.Count];
            var pesados = new List<Job>();
            var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ---------- fase 1 (serie): nombre, plantilla y bytes del objeto ----------
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                var job = new Job { Entry = entry };
                jobs[i] = job;

                try
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        entry.Name = proj.ReadName(entry);

                    job.Stem = entry.MatchStem;
                    job.File = RutaLibre(outDir, nombres, job.Stem, job.Ext);

                    if (entry.ClassId == AssetTypes.TextAsset)
                    {
                        job.Texto = true;
                        pesados.Add(job);
                    }
                    else if (entry.ClassId == AssetTypes.Texture2D)
                    {
                        job.Textura = true;
                        pesados.Add(job);
                    }
                    else
                    {
                        job.Temp = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId, AssetReadFlags.None);

                        // el RefTypeManager se construye aqui, nunca dentro de los hilos
                        var rm = proj.GetRefManager(entry.Inst);
                        if (entry.Stripped || job.Temp == null || rm == null)
                        {
                            job.Bytes = null;   // se exporta la plantilla con valores por defecto
                        }
                        else
                        {
                            var r = entry.Inst.file.Reader;
                            r.Position = entry.Position;
                            job.Bytes = r.ReadBytes((int)entry.Size);
                        }
                    }
                }
                catch (Exception ex)
                {
                    job.Error = ex.Message;
                }

                if (progress != null && (i % 25 == 0 || i == list.Count - 1))
                    progress(i + 1, list.Count, entry.Name);
            }

            reloj[0] = Stopwatch.GetTimestamp() - c0;

            // ---------- fase 2 (paralelo): deserializar + JSON + escribir ----------
            var errores = new ConcurrentBag<string>();
            int exportados = 0, fallos = 0;
            long tRead = 0, tJson = 0, tWrite = 0;
            bool depurar = Environment.GetEnvironmentVariable("ZUB_DEBUG") == "1";

            void Guardar(Job job, byte[] payload)
            {
                string file = job.File;
                if (file == null)
                {
                    lock (nombres)
                        file = job.File = RutaLibre(outDir, nombres, job.Stem, job.Ext);
                }

                File.WriteAllBytes(file, payload);
            }

            int hilos = Math.Max(1, Math.Min(Environment.ProcessorCount, 16));
            if (int.TryParse(Environment.GetEnvironmentVariable("ZUB_THREADS"), out int forzados))
                hilos = Math.Max(1, Math.Min(forzados, 64));

            var opciones = new ParallelOptions { MaxDegreeOfParallelism = hilos };

            // buffer reutilizable por hilo: se copia el objeto con 64 KB de colchon, porque el
            // TypeTree a veces muerde algunos bytes mas alla de ByteSize (como al leer del archivo)
            var colchon = new ThreadLocal<byte[]>(() => new byte[256 * 1024]);

            Parallel.For(0, jobs.Length, opciones, idx =>
            {
                var job = jobs[idx];
                if (job.Texto || job.Textura)
                    return;   // se resuelven despues en serie (usan readers compartidos)

                try
                {
                    if (job.Error != null)
                        throw new Exception(job.Error);

                    AssetTypeValueField value;
                    if (job.Bytes == null)
                    {
                        value = ValueBuilder.DefaultValueFieldFromTemplate(job.Temp);
                    }
                    else
                    {
                        const int margen = 65536;
                        byte[] buf = colchon.Value;
                        int necesario = job.Bytes.Length + margen;
                        if (buf.Length < necesario)
                        {
                            buf = new byte[necesario];
                            colchon.Value = buf;
                        }

                        Buffer.BlockCopy(job.Bytes, 0, buf, 0, job.Bytes.Length);

                        // el colchón se pone a cero: si el TypeTree lee más allá del objeto, el
                        // resultado no depende de lo que dejara el objeto anterior (determinista)
                        Array.Clear(buf, job.Bytes.Length, margen);

                        long r0 = Stopwatch.GetTimestamp();
                        try
                        {
                            using var ms = new MemoryStream(buf, 0, necesario);
                            var rd = new AssetsFileReader(ms);
                            value = job.Temp.MakeValue(rd, 0, proj.GetRefManager(job.Entry.Inst));
                        }
                        catch
                        {
                            // TypeTree que no encaja con los bytes: se exporta la plantilla vacia
                            value = ValueBuilder.DefaultValueFieldFromTemplate(job.Temp);
                        }
                        Interlocked.Add(ref tRead, Stopwatch.GetTimestamp() - r0);
                    }

                    long j0 = Stopwatch.GetTimestamp();
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(JsonFastWriter.Export(value));
                    Interlocked.Add(ref tJson, Stopwatch.GetTimestamp() - j0);

                    long w0 = Stopwatch.GetTimestamp();
                    Guardar(job, jsonBytes);
                    Interlocked.Add(ref tWrite, Stopwatch.GetTimestamp() - w0);

                    Interlocked.Increment(ref exportados);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref fallos);
                    errores.Add($"{job.Entry.ContainerName}#{job.Entry.PathId}: " + (depurar ? ex.ToString() : ex.Message));
                }
            });

            colchon.Dispose();
            long c2 = Stopwatch.GetTimestamp();
            reloj[1] = c2 - c0 - reloj[0];

            // ---------- fase 3 (serie): TextAsset y texturas ----------
            foreach (var job in pesados)
            {
                var entry = job.Entry;
                try
                {
                    if (job.Error != null)
                        throw new Exception(job.Error);

                    if (job.Texto)
                    {
                        byte[] data = ContentReader.ReadTextAssetBytes(proj, entry, out _, out string err);
                        if (data == null)
                            throw new Exception(err);
                        job.Ext = ContentReader.LooksBinary(data) ? ".bin" : ".txt";
                        job.File = RutaLibre(outDir, nombres, job.Stem, job.Ext);
                        Guardar(job, data);
                    }
                    else
                    {
                        var tex = TextureReader.Preview(proj, entry, 0);
                        if (!tex.Decodable)
                            throw new Exception(tex.Error);
                        job.Ext = ".png";
                        job.File = RutaLibre(outDir, nombres, job.Stem, job.Ext);
                        Guardar(job, tex.Png);
                    }

                    exportados++;
                }
                catch (Exception ex)
                {
                    fallos++;
                    errores.Add($"{entry.ContainerName}#{entry.PathId}: " + (depurar ? ex.ToString() : ex.Message));
                }

                if (progress != null && pesados.Count > 8 && exportados % 25 == 0)
                    progress(list.Count, list.Count, entry.Name);
            }

            report.Exported = exportados;
            report.Failed = fallos;
            report.Errors.AddRange(errores);

            sw.Stop();
            report.Seconds = sw.Elapsed.TotalSeconds;

            if (Environment.GetEnvironmentVariable("ZUB_PROF") == "1")
            {
                double ms(long t) => t * 1000.0 / Stopwatch.Frequency;
                Console.Error.WriteLine($"[prof] fase1={ms(reloj[0]):F1}ms fase2={ms(reloj[1]):F1}ms fase3={ms(Stopwatch.GetTimestamp() - c2):F1}ms total={report.Seconds:F1}s");
                Console.Error.WriteLine($"[prof] hilos={hilos} suma-lectura={ms(tRead)/hilos:F0}ms suma-json={ms(tJson)/hilos:F0}ms suma-escritura={ms(tWrite)/hilos:F0}ms (wall/hilo)");
            }

            return report;
        }
    }
}
