using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET;

namespace Zub
{
    /// <summary>
    /// Nombres reales (m_Name del GameObject) de los componentes.
    /// Sirve para emparejar JSON de otra build (PC → Android), donde los PathID no coinciden.
    /// </summary>
    public static class GameObjectNames
    {
        private static Dictionary<(string, long), AssetEntry> _goIndex;
        private static Dictionary<long, List<AssetEntry>> _goById;

        public static void PrepareGoIndex(UnityProject proj)
        {
            _goIndex = new Dictionary<(string, long), AssetEntry>();
            _goById = new Dictionary<long, List<AssetEntry>>();

            foreach (var a in proj.Assets)
            {
                if (a.ClassId != 1)
                    continue;

                _goIndex[(a.ContainerPath, a.PathId)] = a;

                if (!_goById.TryGetValue(a.PathId, out var l))
                    _goById[a.PathId] = l = new List<AssetEntry>();
                l.Add(a);
            }
        }

        private static AssetEntry FindGo(UnityProject proj, AssetEntry entry, out long pathId, out int fileId,
            out bool local, out bool global)
        {
            local = global = false;
            pathId = 0;
            fileId = 0;

            try
            {
                var reader = entry.Inst.file.Reader;
                reader.Position = entry.Position;
                fileId = reader.ReadInt32();
                pathId = reader.ReadInt64();
            }
            catch
            {
                return null;
            }

            if (pathId == 0)
                return null;

            if (_goIndex == null)
                PrepareGoIndex(proj);

            if (_goIndex.TryGetValue((entry.ContainerPath, pathId), out var go) && go != null)
            {
                local = true;
                return go;
            }

            if (_goById != null && _goById.TryGetValue(pathId, out var otros) && otros.Count > 0)
            {
                global = true;
                return otros[0];
            }

            return null;
        }

        /// <summary>Nombre del componente, con diagnóstico.</summary>
        public static string Probe(UnityProject proj, AssetEntry entry, out string debug)
        {
            debug = "";

            try
            {
                var go = FindGo(proj, entry, out long gpid, out int fid, out bool local, out bool global);
                debug = $" fid={fid} gpid={gpid} local={local} global={global}";

                if (go == null)
                    return null;

                debug += $" goSize={go.Size}";

                string n = ReadGameObjectNameTyped(proj, go);
                debug += $" typed={(n == null ? "null" : "'" + n + "'")}";

                return n ?? ReadGameObjectName(go);
            }
            catch (Exception ex)
            {
                debug += " EX:" + ex.Message;
                return null;
            }
        }

        public static string NameOf(UnityProject proj, AssetEntry entry, Dictionary<string, string> cache)
        {
            try
            {
                var go = FindGo(proj, entry, out long pathId, out _, out _, out _);
                if (go == null)
                    return null;

                string key = go.ContainerPath + "#" + go.PathId;
                if (cache.TryGetValue(key, out var cached))
                    return cached;

                string nombre = ReadGameObjectName(go) ?? ReadGameObjectNameTyped(proj, go);
                cache[key] = nombre;
                return nombre;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Nombre leído con el TypeTree (fiable en cualquier versión de Unity).</summary>
        public static string ReadGameObjectNameTyped(UnityProject proj, AssetEntry go)
        {
            try
            {
                var campo = proj.GetTemplateField(go.Inst, go.Position, go.ClassId, go.MonoId);
                if (campo == null)
                    return null;

                var valor = campo.MakeValue(go.Inst.file.Reader, go.Position);
                if (valor == null)
                    return null;

                var nombre = valor["m_Name"];
                return nombre?.AsString;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>GameObject = [array m_Component][m_Layer int][m_Name string].</summary>
        public static string ReadGameObjectName(AssetEntry go)
        {
            try
            {
                byte[] raw = ReadRaw(go);
                if (raw == null || raw.Length < 12)
                    return null;

                int componentes = BitConverter.ToInt32(raw, 0);
                int off = 4 + componentes * 12 + 4;

                if (off + 4 > raw.Length)
                    return null;

                int len = BitConverter.ToInt32(raw, off);
                off += 4;

                if (len <= 0 || off + len > raw.Length)
                    return null;

                return Encoding.UTF8.GetString(raw, off, len);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ReadRaw(AssetEntry e)
        {
            try
            {
                var reader = e.Inst.file.Reader;
                reader.Position = e.Position;
                return reader.ReadBytes((int)e.Size);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Índice nombre (sin distinguir mayúsculas) → componentes.</summary>
        public static Dictionary<string, List<AssetEntry>> Build(UnityProject proj, IEnumerable<AssetEntry> pool,
            Action<int, int> progress = null)
        {
            var indice = new Dictionary<string, List<AssetEntry>>(StringComparer.OrdinalIgnoreCase);
            var cache = new Dictionary<string, string>();
            var lista = pool.ToList();

            PrepareGoIndex(proj);

            for (int i = 0; i < lista.Count; i++)
            {
                if ((i & 0xFF) == 0)
                    progress?.Invoke(i, lista.Count);

                var e = lista[i];
                string nombre = NameOf(proj, e, cache);

                if (string.IsNullOrWhiteSpace(nombre))
                    continue;

                if (!indice.TryGetValue(nombre, out var l))
                    indice[nombre] = l = new List<AssetEntry>();
                l.Add(e);
            }

            return indice;
        }
    }
}
