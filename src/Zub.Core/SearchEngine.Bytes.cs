using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Zub
{
    public static partial class SearchEngine
    {
        /// <summary>
        /// Búsqueda rápida sobre los bytes del objeto (valores de texto).
        /// No encuentra nombres de campo: para eso está SearchDeep.
        /// </summary>
        public static List<SearchHit> Search(UnityProject proj, IEnumerable<AssetEntry> pool, string query,
            bool useRegex, bool ignoreCase, int maxResults = 5000, long maxObjectSize = 8L * 1024 * 1024,
            Action<int, int> progress = null, CancellationToken cancel = default,
            Func<AssetEntry, string> textOverride = null)
        {
            var hits = new List<SearchHit>();
            if (string.IsNullOrEmpty(query))
                return hits;

            Regex regex = null;
            byte[] needle = null;

            if (useRegex)
            {
                var opts = RegexOptions.Compiled | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
                regex = new Regex(query, opts, TimeSpan.FromSeconds(3));
            }
            else
            {
                needle = Encoding.UTF8.GetBytes(query);
            }

            var list = new List<AssetEntry>(pool);
            int total = list.Count;
            int done = 0;

            foreach (var entry in list)
            {
                if (cancel.IsCancellationRequested)
                    break;

                done++;
                if (progress != null && (done % 500 == 0 || done == total))
                    progress(done, total);

                // cambios preparados: se busca en lo que se va a escribir, no en el disco
                string over = null;
                if (textOverride != null)
                {
                    try { over = textOverride(entry); } catch { }
                }

                if (over != null)
                {
                    int oc = 0;
                    string osn = "";

                    if (needle != null)
                    {
                        string s1 = System.Text.Encoding.UTF8.GetString(needle);
                        int at = over.IndexOf(s1, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                        if (at >= 0)
                        {
                            oc = 1;
                            int st = Math.Max(0, at - 60);
                            osn = Snip(over.Substring(st, Math.Min(over.Length - st, 160)));
                        }
                    }
                    else
                    {
                        var m = regex.Matches(over);
                        oc = m.Count;
                        if (oc > 0)
                        {
                            int at = m[0].Index;
                            int st = Math.Max(0, at - 60);
                            osn = Snip(over.Substring(st, Math.Min(over.Length - st, 160)));
                        }
                    }

                    if (oc > 0)
                    {
                        hits.Add(new SearchHit { Entry = entry, Matches = oc, Snippet = osn });
                        if (hits.Count >= maxResults) break;
                    }
                    continue;
                }

                if (entry.Size == 0 || entry.Size > maxObjectSize)
                    continue;

                byte[] data;
                try
                {
                    var reader = entry.Inst.file.Reader;
                    reader.Position = entry.Position;
                    data = reader.ReadBytes((int)entry.Size);
                }
                catch
                {
                    continue;
                }

                if (data == null || data.Length == 0)
                    continue;

                int count = 0;
                string snippet = "";

                if (needle != null)
                {
                    count = CountBytes(data, needle, ignoreCase);
                    if (count > 0)
                        snippet = SnippetBytes(data, needle, ignoreCase);
                }
                else
                {
                    string text = Encoding.UTF8.GetString(data);
                    var ms = regex.Matches(text);
                    count = ms.Count;
                    if (count > 0)
                    {
                        int at = ms[0].Index;
                        int start = Math.Max(0, at - 60);
                        int len = Math.Min(text.Length - start, 160);
                        snippet = text.Substring(start, len).Replace("\r", " ").Replace("\n", " ").Trim();
                    }
                }

                if (count > 0)
                {
                    hits.Add(new SearchHit { Entry = entry, Matches = count, Snippet = snippet });
                    if (hits.Count >= maxResults)
                        break;
                }
            }

            if (progress != null)
                progress(total, total);

            return hits;
        }

        private static string Snip(string frag)
        {
            frag = frag.Replace((char)13, ' ').Replace((char)10, ' ');
            while (frag.Contains("  "))
                frag = frag.Replace("  ", " ");
            return frag.Trim();
        }

        private static int CountBytes(byte[] data, byte[] needle, bool ignoreCase)
        {
            int count = 0;
            int limit = data.Length - needle.Length;

            for (int i = 0; i <= limit; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++)
                {
                    byte a = data[i + j];
                    byte b = needle[j];
                    if (a == b)
                        continue;

                    if (ignoreCase && ToLower(a) == ToLower(b))
                        continue;

                    break;
                }

                if (j == needle.Length)
                {
                    count++;
                    i += needle.Length - 1;
                }
            }

            return count;
        }

        private static byte ToLower(byte b) => b >= 65 && b <= 90 ? (byte)(b + 32) : b;

        private static string SnippetBytes(byte[] data, byte[] needle, bool ignoreCase)
        {
            int limit = data.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++)
                {
                    byte a = data[i + j], b = needle[j];
                    if (a == b) continue;
                    if (ignoreCase && ToLower(a) == ToLower(b)) continue;
                    break;
                }

                if (j != needle.Length)
                    continue;

                int start = Math.Max(0, i - 60);
                int len = Math.Min(data.Length - start, 160);
                string frag = Encoding.UTF8.GetString(data, start, len);
                frag = frag.Replace("\r", " ").Replace("\n", " ");
                while (frag.Contains("  "))
                    frag = frag.Replace("  ", " ");
                return frag.Trim();
            }

            return "";
        }
    }
}
