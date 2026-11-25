using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Zub
{
    /// <summary>
    /// Aplica un CSV de traducción (archivo,campo,indice,texto_original,traduccion) a los
    /// objetos del proyecto abierto. Sirve para reutilizar traducciones hechas sobre otra
    /// build: se busca el objeto por contenedor+PathID y, si no, por PathID o por el texto.
    /// </summary>
    public static class CsvInjector
    {
        public sealed class Row
        {
            public string Archivo;
            public string Campo;
            public int Indice;
            public string Original;
            public string Traduccion;
        }

        public sealed class Stats
        {
            public int Filas;
            public int Archivos;
            public int ArchivosOk;
            public int FilasAplicadas;
            public int SinObjeto;
            public int SinCampo;
            public List<string> Errores = new List<string>();
            public List<string> SinPareja = new List<string>();

            public string Summary =>
                $"{Filas:N0} filas de {Archivos:N0} archivos · {ArchivosOk:N0} archivos localizados · " +
                $"{FilasAplicadas:N0} textos aplicados · {SinObjeto:N0} sin objeto · {SinCampo:N0} sin campo";
        }

        /// <summary>CSV con cabecera: archivo,campo,indice,texto_original,traduccion</summary>
        public static List<Row> ReadCsv(string path)
        {
            var filas = new List<Row>();
            var lineas = File.ReadAllLines(path, Encoding.UTF8);
            if (lineas.Length == 0)
                return filas;

            for (int i = 1; i < lineas.Length; i++)
            {
                string l = lineas[i];
                if (string.IsNullOrWhiteSpace(l))
                    continue;

                var c = SplitCsv(l);
                if (c.Count < 5)
                    continue;

                filas.Add(new Row
                {
                    Archivo = c[0],
                    Campo = c[1],
                    Indice = int.TryParse(c[2], out int n) ? n : 0,
                    Original = c[3],
                    Traduccion = c[4]
                });
            }

            return filas;
        }

        /// <summary>Parte una línea CSV respetando las comillas dobles.</summary>
        public static List<string> SplitCsv(string linea)
        {
            var campos = new List<string>();
            var sb = new StringBuilder();
            bool entreComillas = false;

            for (int i = 0; i < linea.Length; i++)
            {
                char ch = linea[i];

                if (ch == '"')
                {
                    if (entreComillas && i + 1 < linea.Length && linea[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        entreComillas = !entreComillas;
                    }
                }
                else if (ch == ',' && !entreComillas)
                {
                    campos.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }

            campos.Add(sb.ToString());
            return campos;
        }

        /// <summary>Resuelve el objeto de cada archivo del CSV y aplica los textos.</summary>
        public static (List<RawEdit> edits, Stats stats) Build(UnityProject proj, string csvPath,
            Action<int, int> progress = null)
        {
            var stats = new Stats();
            var filas = ReadCsv(csvPath);
            stats.Filas = filas.Count;

            var pool = proj.Assets
                .Where(a => a.ClassId == AssetTypes.MonoBehaviour || a.ClassId == AssetTypes.TextAsset)
                .ToList();

            var exacto = new Dictionary<(string, long), AssetEntry>();
            var porId = new Dictionary<long, List<AssetEntry>>();

            foreach (var e in pool)
            {
                exacto[(e.ContainerName.ToLowerInvariant(), e.PathId)] = e;

                if (!porId.TryGetValue(e.PathId, out var l))
                    porId[e.PathId] = l = new List<AssetEntry>();
                l.Add(e);
            }

            var grupos = filas.GroupBy(f => f.Archivo).ToList();
            stats.Archivos = grupos.Count;

            var edits = new List<RawEdit>();
            var pendientes = new List<(string contenedor, Row fila)>();
            int i0 = 0;

            foreach (var grupo in grupos)
            {
                if ((i0++ & 0xF) == 0)
                    progress?.Invoke(i0, grupos.Count);

                AssetEntry destino = null;

                var m = System.Text.RegularExpressions.Regex.Match(
                    Path.GetFileNameWithoutExtension(grupo.Key), @"-(-?\d+)$");

                if (m.Success && long.TryParse(m.Groups[1].Value, out long pid))
                {
                    string stem = Path.GetFileNameWithoutExtension(grupo.Key);
                    string prefix = stem.Substring(0, m.Index);
                    int sep = prefix.LastIndexOf('-');
                    string contenedor = sep >= 0 ? prefix.Substring(sep + 1).ToLowerInvariant() : "";

                    if (contenedor.Length > 0 && exacto.TryGetValue((contenedor, pid), out var direct))
                        destino = direct;

                    if (destino == null && porId.TryGetValue(pid, out var cands) && cands.Count == 1)
                        destino = cands[0];
                }

                if (destino == null)
                {
                    foreach (var f in grupo)
                        pendientes.Add((ContenedorDe(grupo.Key), f));
                    continue;
                }

                // aplicar fila a fila sobre el JSON del objeto
                JObject root;

                try
                {
                    root = JObject.Parse(ContentReader.ReadMonoJson(proj, destino));
                }
                catch (Exception ex)
                {
                    stats.SinObjeto += grupo.Count();
                    stats.Errores.Add(Path.GetFileName(grupo.Key) + ": " + ex.Message);
                    continue;
                }

                bool tocado = false;

                foreach (var fila in grupo)
                {
                    bool puesto = SetValue(root, fila.Campo, fila.Indice, fila.Traduccion);

                    // si la ruta no encaja en esta build, se busca el texto original dentro del objeto
                    if (!puesto && !string.IsNullOrEmpty(fila.Original))
                        puesto = ReplaceByText(root, fila.Original, fila.Traduccion);

                    if (puesto)
                    {
                        stats.FilasAplicadas++;
                        tocado = true;
                    }
                    else
                    {
                        pendientes.Add((ContenedorDe(grupo.Key), fila));
                    }
                }

                if (!tocado)
                    continue;

                try
                {
                    byte[] data = ContentReader.BuildMonoBytes(proj, destino, root.ToString(Newtonsoft.Json.Formatting.Indented), out string err);

                    if (data == null)
                    {
                        stats.Errores.Add(Path.GetFileName(grupo.Key) + ": " + err);
                        continue;
                    }

                    stats.ArchivosOk++;
                    edits.Add(new RawEdit { Entry = destino, Data = data, Label = destino.DisplayName });
                }
                catch (Exception ex)
                {
                    stats.Errores.Add(Path.GetFileName(grupo.Key) + ": " + ex.Message);
                }
            }

            // ---- 2ª pasada: buscar el texto original dentro del contenedor ----
            int pendientesAntes = pendientes.Count;
            int aplicadasAntes = stats.FilasAplicadas;

            if (pendientes.Count > 0)
            {
                progress?.Invoke(grupos.Count, grupos.Count + 1);

                var porContenedor = pendientes
                    .GroupBy(x => x.contenedor)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var cont in porContenedor)
                {
                    if (cont.Key.Length == 0)
                        continue;

                    var objetos = pool.Where(e => string.Equals(e.ContainerName, cont.Key,
                        StringComparison.OrdinalIgnoreCase)).ToList();

                    if (objetos.Count == 0)
                        continue;

                    // agrupa las filas de este contenedor por texto original
                    var porTexto = cont.Value
                        .GroupBy(x => x.fila.Original)
                        .Where(g => !string.IsNullOrEmpty(g.Key))
                        .Select(g => new { Texto = g.Key, Filas = g.Select(x => x.fila).ToList() })
                        .ToList();

                    var tokens = porTexto.ToDictionary(f => f.Texto, f => Encoding.UTF8.GetBytes(f.Texto));

                    foreach (var obj in objetos)
                    {
                        if (tokens.Count == 0)
                            break;

                        byte[] raw = BatchImporter.ReadRaw(obj.Inst, InfoOf(obj));
                        if (raw == null)
                            continue;

                        foreach (var f in porTexto.ToList())
                        {
                            if (!Contiene(raw, tokens[f.Texto]))
                                continue;

                            JObject root;

                            try { root = JObject.Parse(ContentReader.ReadMonoJson(proj, obj)); }
                            catch { continue; }

                            bool puesto = false;

                            foreach (var fila in f.Filas)
                            {
                                if (SetValue(root, fila.Campo, fila.Indice, fila.Traduccion) ||
                                    ReplaceByText(root, fila.Original, fila.Traduccion))
                                {
                                    stats.FilasAplicadas++;
                                    puesto = true;
                                }
                            }

                            tokens.Remove(f.Texto);
                            porTexto.Remove(f);

                            if (!puesto)
                                continue;

                            byte[] data = ContentReader.BuildMonoBytes(proj, obj,
                                root.ToString(Newtonsoft.Json.Formatting.Indented), out string err2);

                            if (data == null)
                            {
                                stats.Errores.Add(obj.DisplayName + ": " + err2);
                                continue;
                            }

                            stats.ArchivosOk++;
                            edits.Add(new RawEdit { Entry = obj, Data = data, Label = obj.DisplayName });
                        }
                    }

                    // lo que no apareció en este contenedor cuenta como sin objeto
                    foreach (var f in porTexto)
                    {
                        stats.SinObjeto += f.Filas.Count;
                        if (stats.SinPareja.Count < 4000)
                            stats.SinPareja.Add(f.Texto.Length > 60 ? f.Texto.Substring(0, 60) : f.Texto);
                    }
                }


            }

            stats.SinObjeto = Math.Max(0, pendientesAntes - (stats.FilasAplicadas - aplicadasAntes));

            return (edits, stats);
        }

        private static string ContenedorDe(string archivo)
        {
            string stem = Path.GetFileNameWithoutExtension(archivo);
            var m = System.Text.RegularExpressions.Regex.Match(stem, @"-(-?\d+)$");
            if (!m.Success)
                return "";

            string prefix = stem.Substring(0, m.Index);
            int sep = prefix.LastIndexOf('-');
            return sep >= 0 ? prefix.Substring(sep + 1) : "";
        }

        private static AssetsTools.NET.AssetFileInfo InfoOf(AssetEntry e)
        {
            foreach (var info in e.Inst.file.AssetInfos)
                if (info.PathId == e.PathId)
                    return info;
            return null;
        }

        /// <summary>Búsqueda de bytes directa.</summary>
        private static bool Contiene(byte[] pajar, byte[] aguja)
        {
            if (aguja.Length == 0 || pajar.Length < aguja.Length)
                return false;

            int fin = pajar.Length - aguja.Length;

            for (int i = 0; i <= fin; i++)
            {
                if (pajar[i] != aguja[0])
                    continue;

                int j = 1;
                while (j < aguja.Length && pajar[i + j] == aguja[j])
                    j++;

                if (j == aguja.Length)
                    return true;
            }

            return false;
        }

        /// <summary>Busca el texto original dentro del objeto y lo sustituye.</summary>
        public static bool ReplaceByText(JToken nodo, string original, string traduccion)
        {
            try
            {
                if (nodo is JObject obj)
                {
                    foreach (var prop in obj.Properties())
                    {
                        if (prop.Value is JValue v && v.Type == JTokenType.String &&
                            string.Equals((string)v, original, StringComparison.Ordinal))
                        {
                            v.Value = traduccion;
                            return true;
                        }

                        if (ReplaceByText(prop.Value, original, traduccion))
                            return true;
                    }

                    return false;
                }

                if (nodo is JArray arr)
                {
                    foreach (var hijo in arr)
                        if (ReplaceByText(hijo, original, traduccion))
                            return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Pone un valor en una ruta tipo "Lines.Array.Text" con índice de elemento.</summary>
        public static bool SetValue(JObject root, string campo, int indice, string valor)
        {
            try
            {
                JToken t = root;

                foreach (var parte in campo.Split('.'))
                {
                    if (!(t is JObject obj))
                        return false;

                    var siguiente = obj[parte];
                    if (siguiente == null)
                        return false;

                    t = siguiente;

                    // "Lines.Array.Text": tras la propiedad Array se elige el elemento
                    if (t is JArray arr)
                    {
                        if (indice < 0 || indice >= arr.Count)
                            return false;
                        t = arr[indice];
                    }
                }

                if (t is JValue v)
                {
                    string previo = v.ToString();
                    v.Value = valor;
                    return previo != valor;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
