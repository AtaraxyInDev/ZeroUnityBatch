using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Zub
{
    /// <summary>Lectura y escritura del contenido editable de cada tipo de objeto.</summary>
    public static class ContentReader
    {
        /// <summary>JSON TypeTree del objeto (mismo formato que UABEA Next).</summary>
        public static string ReadMonoJson(UnityProject proj, AssetEntry entry)
        {
            var tempField = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);
            var value = tempField.MakeValue(entry.Inst.file.Reader, entry.Position);
            return JsonCodec.Export(value);
        }

        internal static byte[] ReadObjectRaw(AssetEntry entry)
        {
            try
            {
                var reader = entry.Inst.file.Reader;
                reader.Position = entry.Position;
                return reader.ReadBytes((int)entry.Size);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Serializa el JSON editado de un MonoBehaviour al cuerpo binario del objeto.</summary>
        public static byte[] BuildMonoBytes(UnityProject proj, AssetEntry entry, string json, out string error)
        {
            var tempField = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);
            return JsonCodec.Import(tempField, json, out error);
        }

        /// <summary>
        /// TextAsset = m_Name (string) + m_Script (string). Devuelve los bytes crudos de m_Script,
        /// sin tocar la codificación.
        /// </summary>
        public static byte[] ReadTextAssetBytes(UnityProject proj, AssetEntry entry, out string name, out string error)
        {
            name = null;
            error = null;
            try
            {
                byte[] raw = ReadObjectRaw(entry);
                if (raw == null)
                {
                    error = "no se pudo leer el objeto";
                    return null;
                }

                if (!TryParse(raw, out string parsedName, out long dataOffset, out int dataLen, out string parseError))
                {
                    error = parseError;
                    return null;
                }

                name = parsedName;
                var data = new byte[dataLen];
                Buffer.BlockCopy(raw, (int)(dataOffset + 4), data, 0, dataLen);
                return data;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// TextAsset = [len][m_Name][posible padding a 4][len][m_Script].
        /// El padding depende del TypeTree, así que se prueban ambas variantes y se valida la longitud.
        /// </summary>
        private static bool TryParse(byte[] raw, out string name, out long dataOffset, out int dataLen, out string error)
        {
            name = null;
            dataOffset = 0;
            dataLen = 0;
            error = null;

            if (raw.Length < 8)
            {
                error = "objeto demasiado pequeño";
                return false;
            }

            int nameLen = BitConverter.ToInt32(raw, 0);
            if (nameLen < 0 || 4 + nameLen + 4 > raw.Length)
            {
                error = "cabecera de TextAsset inesperada";
                return false;
            }

            name = System.Text.Encoding.UTF8.GetString(raw, 4, nameLen);

            long plain = 4 + nameLen;
            long aligned = (plain + 3) & ~3L;

            foreach (long candidate in new[] { plain, aligned })
            {
                if (candidate + 4 > raw.Length)
                    continue;
                int len = BitConverter.ToInt32(raw, (int)candidate);
                if (len < 0 || candidate + 4 + len > raw.Length)
                    continue;

                dataOffset = candidate;
                dataLen = len;
                return true;
            }

            error = "longitud de contenido inválida";
            return false;
        }

        /// <summary>Reconstruye el objeto TextAsset conservando el nombre y metadatos originales.</summary>
        public static byte[] BuildTextAsset(UnityProject proj, AssetEntry entry, byte[] newData, out string error)
        {
            error = null;
            try
            {
                byte[] original = ReadObjectRaw(entry);
                if (original == null)
                {
                    error = "no se pudo leer el objeto original";
                    return null;
                }

                if (!TryParse(original, out _, out long dataOffset, out int originalLen, out error))
                    return null;

                int tailStart = (int)(dataOffset + 4 + originalLen);

                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(original, 0, (int)dataOffset);
                    w.Write(newData.Length);
                    w.Write(newData);
                    // conserva el relleno final del objeto (alineación) tal cual estaba
                    if (tailStart < original.Length)
                        w.Write(original, tailStart, original.Length - tailStart);
                    w.Flush();
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static bool LooksBinary(byte[] data)
        {
            int limit = Math.Min(data.Length, 4096);
            for (int i = 0; i < limit; i++)
            {
                byte b = data[i];
                if (b == 0)
                    return true;
            }
            return false;
        }

        /// <summary>Texto legible para mostrar (UTF-8 con reemplazo de caracteres inválidos).</summary>
        public static string ToDisplayText(byte[] data)
        {
            return Encoding.UTF8.GetString(data);
        }
    }
}
