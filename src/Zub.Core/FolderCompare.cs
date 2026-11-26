// Compara dos carpetas de exportación (típicamente una hecha con UABEA Next 4 y otra con
// ZeroUnity Batch) para saber, sin opiniones, cuántos objetos coinciden y cuáles no.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Zub
{
    public sealed class CompareResult
    {
        public int TotalA;
        public int TotalB;
        public int Iguales;
        public readonly List<string> Falta = new List<string>();
        public readonly List<string> Sobra = new List<string>();
        public readonly List<string> Distintos = new List<string>();
        /// <summary>De los que "faltan", cuántos son el mismo objeto con otro nombre.</summary>
        public int Renombrados;
        public bool Exacto;
        public string DirA = "";
        public string DirB = "";

        public bool Identico => Falta.Count == 0 && Sobra.Count == 0 && Distintos.Count == 0;

        /// <summary>Resumen de varias líneas, listo para mostrar en un diálogo o en consola.</summary>
        public string Resumen(int ejemplos = 15)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("A (referencia): " + TotalA + " archivos");
            sb.AppendLine("B (nuestra):    " + TotalB + " archivos");
            sb.AppendLine();
            sb.AppendLine("Idénticos: " + Iguales);
            sb.AppendLine("Solo en A (nos falta): " + Falta.Count + (Renombrados > 0 ? "  (" + Renombrados + " son el mismo objeto con otro nombre)" : ""));
            sb.AppendLine("Solo en B (nos sobra): " + Sobra.Count);
            sb.AppendLine("Distintos: " + Distintos.Count + (Exacto ? "  (byte a byte)" : "  (por tamaño)"));

            void Muestra(string titulo, List<string> lista)
            {
                if (lista.Count == 0)
                    return;
                sb.AppendLine();
                sb.AppendLine(titulo + ":");
                foreach (string n in lista.Take(ejemplos))
                    sb.AppendLine("  · " + n);
                if (lista.Count > ejemplos)
                    sb.AppendLine("  … y " + (lista.Count - ejemplos) + " más");
            }

            Muestra("Nos falta", Falta);
            Muestra("Nos sobra", Sobra);
            Muestra("Distintos", Distintos);
            return sb.ToString();
        }

        /// <summary>Guarda el detalle en un CSV (estado,archivo).</summary>
        public void GuardarCsv(string ruta)
        {
            using var w = new StreamWriter(ruta, false, new System.Text.UTF8Encoding(false));
            w.WriteLine("estado,archivo");
            foreach (string n in Falta) w.WriteLine("falta," + n);
            foreach (string n in Sobra) w.WriteLine("sobra," + n);
            foreach (string n in Distintos) w.WriteLine("distinto," + n);
        }
    }

    public static class FolderCompare
    {
        /// <summary>Compara dos carpetas de exportación. exacto = comparar también el contenido.</summary>
        public static CompareResult Run(string dirA, string dirB, bool exacto, Action<int, int> progress = null)
        {
            var res = new CompareResult { DirA = dirA, DirB = dirB, Exacto = exacto };

            Dictionary<string, string> Indexar(string dir)
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    d[Path.GetFileName(f)] = f;
                return d;
            }

            var a = Indexar(dirA);
            var b = Indexar(dirB);
            res.TotalA = a.Count;
            res.TotalB = b.Count;

            foreach (string k in a.Keys.Where(k => !b.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                res.Falta.Add(k);
            foreach (string k in b.Keys.Where(k => !a.ContainsKey(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                res.Sobra.Add(k);

            // mismo objeto exportado con otro nombre: se empareja por el PathID del final
            var porId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string n in res.Sobra)
            {
                string id = PathId(n);
                if (id == null) continue;
                if (!porId.TryGetValue(id, out var l))
                    porId[id] = l = new List<string>();
                l.Add(n);
            }
            foreach (string n in res.Falta)
            {
                string id = PathId(n);
                if (id != null && porId.TryGetValue(id, out var l) && l.Count == 1)
                    res.Renombrados++;
            }

            var comunes = a.Keys.Where(k => b.ContainsKey(k)).ToList();
            for (int i = 0; i < comunes.Count; i++)
            {
                string k = comunes[i];
                var fa = new FileInfo(a[k]);
                var fb = new FileInfo(b[k]);

                bool dif = fa.Length != fb.Length;
                if (!dif && exacto && fa.Length <= 8 * 1024 * 1024)
                    dif = !File.ReadAllBytes(a[k]).AsSpan().SequenceEqual(File.ReadAllBytes(b[k]));

                if (dif)
                    res.Distintos.Add(k);
                else
                    res.Iguales++;

                if (progress != null && (i % 200 == 0 || i == comunes.Count - 1))
                    progress(i + 1, comunes.Count);
            }

            res.Distintos.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        private static readonly Regex IdFinal = new Regex(@"-(-?\d+)\.[A-Za-z0-9]+$", RegexOptions.Compiled);

        private static string PathId(string nombre)
        {
            var m = IdFinal.Match(nombre);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
