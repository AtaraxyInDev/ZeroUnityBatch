using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Zub
{
    public sealed class SearchHit
    {
        public AssetEntry Entry;
        public string Snippet = "";
        public int Matches;

        public string DisplayName => string.IsNullOrEmpty(Entry.Name)
            ? "(sin nombre) #" + Entry.PathId
            : (Matches > 1 ? Entry.Name + "  x" + Matches : Entry.Name);
        public string Where => System.IO.Path.GetFileName(Entry.ContainerPath) + "#" + Entry.PathId;
    }

    public static partial class SearchEngine
    {
        /// <summary>
        /// Búsqueda por NOMBRE DE CAMPO (m_text, m_Name, ...).
        /// Esos nombres no existen en los bytes: salen del TypeTree que se genera desde
        /// los assemblies del juego. Por eso se agrupa por script (contenedor + MonoId) y
        /// se inspecciona la plantilla UNA vez por script, no una vez por objeto.
        /// </summary>
        public static List<SearchHit> SearchDeep(UnityProject proj, IEnumerable<AssetEntry> pool, string query,
            bool ignoreCase = true, int maxResults = 3000, Action<int, int> progress = null,
            CancellationToken cancel = default)
        {
            var hits = new List<SearchHit>();
            string needle = Normalize(query);
            if (needle.Length == 0)
                return hits;

            var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            var groups = pool
                .Where(a => a.ClassId == AssetTypes.MonoBehaviour)
                .GroupBy(a => new { File = a.ContainerPath, Script = a.MonoId })
                .ToList();

            int done = 0;
            var cache = new Dictionary<string, bool>();

            foreach (var group in groups)
            {
                if (cancel.IsCancellationRequested)
                    break;

                var first = group.First();
                string cacheKey = group.Key.File + "|" + group.Key.Script;

                bool found;
                if (!cache.TryGetValue(cacheKey, out found))
                {
                    found = false;
                    try
                    {
                        var template = proj.GetTemplateField(first.Inst, first.Position, first.ClassId, first.MonoId);
                        found = TemplateHasField(template, needle, cmp);
                    }
                    catch { }
                    cache[cacheKey] = found;
                }

                if (found)
                {
                    foreach (var entry in group)
                    {
                        if (hits.Count >= maxResults)
                            break;
                        hits.Add(new SearchHit { Entry = entry, Matches = 1, Snippet = "campo: " + needle });
                    }
                }

                done++;
                if (progress != null && done % 50 == 0)
                    progress(done, groups.Count);

                if (hits.Count >= maxResults)
                    break;
            }

            if (progress != null)
                progress(groups.Count, groups.Count);

            return hits;
        }

        /// <summary>
        /// Separa una consulta tipo JSON: `"m_Text": "Continue"` -> campo m_Text, valor Continue.
        /// Si no hay dos puntos, todo es valor (búsqueda normal).
        /// </summary>
        public static void ParseQuery(string query, out string field, out string value)
        {
            field = "";
            value = "";

            string q = (query ?? "").Trim();
            if (q.Length == 0)
                return;

            int colon = -1;
            bool inString = false;
            for (int i = 0; i < q.Length; i++)
            {
                char c = q[i];
                if (c == '\"')
                    inString = !inString;
                else if (c == ':' && !inString)
                {
                    colon = i;
                    break;
                }
            }

            if (colon > 0)
            {
                field = q.Substring(0, colon).Trim().Trim('"', '\'', ' ');
                value = q.Substring(colon + 1).Trim().Trim('"', '\'', ' ');
                return;
            }

            value = q.Trim().Trim('"', '\'', ' ');
        }

        /// <summary>
        /// Deja solo los objetos cuyo script declara el campo indicado.
        /// Se apoya en la plantilla del script (una comprobación por script, no por objeto).
        /// </summary>
        public static List<SearchHit> FilterByField(UnityProject proj, IEnumerable<SearchHit> hits, string field)
        {
            var result = new List<SearchHit>();
            if (hits == null)
                return result;

            string needle = Normalize(field);
            if (needle.Length == 0)
            {
                result.AddRange(hits);
                return result;
            }

            var cache = new Dictionary<string, bool>();

            foreach (var hit in hits)
            {
                var entry = hit.Entry;
                if (entry.ClassId != AssetTypes.MonoBehaviour)
                    continue;

                string cacheKey = entry.ContainerPath + "|" + entry.MonoId;
                bool has;
                if (!cache.TryGetValue(cacheKey, out has))
                {
                    has = false;
                    try
                    {
                        var template = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);
                        has = TemplateHasField(template, needle, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }
                    cache[cacheKey] = has;
                }

                if (has)
                    result.Add(hit);
            }

            return result;
        }

        private static bool TemplateHasField(AssetsTools.NET.AssetTypeTemplateField template, string needle, StringComparison cmp)
        {
            if (template == null)
                return false;

            if (!string.IsNullOrEmpty(template.Name) && template.Name.IndexOf(needle, cmp) >= 0)
                return true;

            foreach (var child in template.Children)
            {
                if (TemplateHasField(child, needle, cmp))
                    return true;
            }

            return false;
        }

        private static string Normalize(string query)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0)
                return q;

            // "m_text":  ->  m_text   (se puede pegar el campo tal cual se ve en el JSON)
            if (q.Length >= 2 && IsQuote(q[0]))
                q = q.Substring(1);
            if (q.Length >= 2 && IsQuote(q[q.Length - 1]))
                q = q.Substring(0, q.Length - 1);

            q = q.TrimEnd(':').Trim();

            if (q.Length >= 2 && IsQuote(q[0]) && IsQuote(q[q.Length - 1]))
                q = q.Substring(1, q.Length - 2);

            q = q.Trim('"', '\'', ' ');

            return q;
        }

        private static bool IsQuote(char c)
        {
            return c == '"' || c == '\'';
        }
    }
}
