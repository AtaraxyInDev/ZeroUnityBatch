// Conversión TypeTree <-> JSON.
// Derivado de UABEAvalonia/Logic/AssetImportExport.cs (MIT, nesrak1/UABEA).
// Se mantiene el mismo formato exacto que exporta UABEA Next para que los
// archivos ya traducidos sigan siendo válidos.
using AssetsTools.NET;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace Zub
{
    public static class JsonCodec
    {
        public static string Export(AssetTypeValueField baseField)
        {
            JToken token = Dump(baseField);
            return token.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static JToken Dump(AssetTypeValueField field)
        {
            AssetTypeTemplateField template = field.TemplateField;

            if (template.IsArray)
            {
                JArray jArray = new JArray();

                if (template.ValueType != AssetValueType.ByteArray)
                {
                    for (int i = 0; i < field.Children.Count; i++)
                        jArray.Add(Dump(field.Children[i]));
                }
                else
                {
                    byte[] bytes = field.AsByteArray;
                    for (int i = 0; i < bytes.Length; i++)
                        jArray.Add(bytes[i]);
                }
                return jArray;
            }

            if (field.Value != null)
            {
                AssetValueType evt = field.Value.ValueType;

                if (evt != AssetValueType.ManagedReferencesRegistry)
                {
                    object value = evt switch
                    {
                        AssetValueType.Bool => field.AsBool,
                        AssetValueType.Int8 or AssetValueType.Int16 or AssetValueType.Int32 => field.AsInt,
                        AssetValueType.Int64 => field.AsLong,
                        AssetValueType.UInt8 or AssetValueType.UInt16 or AssetValueType.UInt32 => field.AsUInt,
                        AssetValueType.UInt64 => field.AsULong,
                        AssetValueType.String => field.AsString,
                        AssetValueType.Float => field.AsFloat,
                        AssetValueType.Double => field.AsDouble,
                        _ => "invalid value"
                    };
                    return (JValue)JToken.FromObject(value);
                }

                ManagedReferencesRegistry registry = field.Value.AsManagedReferencesRegistry;
                if (registry.version == 1 || registry.version == 2)
                {
                    JArray refs = new JArray();
                    foreach (AssetTypeReferencedObject refObj in registry.references)
                    {
                        AssetTypeReference typeRef = refObj.type;
                        JObject jType = new JObject
                        {
                            { "class", typeRef.ClassName },
                            { "ns", typeRef.Namespace },
                            { "asm", typeRef.AsmName }
                        };
                        JObject jData = new JObject();
                        foreach (AssetTypeValueField child in refObj.data)
                            jData.Add(child.FieldName, Dump(child));

                        refs.Add(registry.version == 1
                            ? new JObject { { "type", jType }, { "data", jData } }
                            : new JObject { { "rid", refObj.rid }, { "type", jType }, { "data", jData } });
                    }
                    return new JObject { { "version", registry.version }, { "RefIds", refs } };
                }
                throw new NotSupportedException($"Registry version {registry.version} not supported!");
            }

            JObject jObject = new JObject();
            foreach (AssetTypeValueField child in field)
                jObject.Add(child.FieldName, Dump(child));
            return jObject;
        }

        /// <summary>Serializa el JSON editado al cuerpo binario del objeto.</summary>
        public static byte[] Import(AssetTypeTemplateField tempField, string jsonText, out string error)
        {
            error = null;
            using (MemoryStream ms = new MemoryStream())
            {
                var aw = new AssetsFileWriter(ms) { BigEndian = false };
                try
                {
                    JToken token = JToken.Parse(jsonText);
                    ImportRecurse(tempField, token, aw);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return null;
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Importa el JSON sobre el campo de valor ya leído del asset (con su RefTypeManager).
        /// Se usa para objetos con SerializeReference, que no se pueden reconstruir desde la plantilla:
        /// aquí se conserva el árbol original y solo se sustituyen los valores que cambiaron.
        /// Devuelve null si no hubo cambios.
        /// </summary>
        public static byte[] ImportOverField(AssetTypeValueField baseField, string jsonText, out string error)
        {
            error = null;
            try
            {
                JToken token = JToken.Parse(jsonText);
                if (!ApplyOverField(baseField, token))
                    return null;

                using (MemoryStream ms = new MemoryStream())
                {
                    var aw = new AssetsFileWriter(ms) { BigEndian = false };
                    baseField.Write(aw);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Sustituye en el árbol sólo los valores simples. Devuelve true si algo cambió.</summary>
        private static bool ApplyOverField(AssetTypeValueField field, JToken token)
        {
            if (token == null || field == null)
                return false;

            var tf = field.TemplateField;
            bool changed = false;

            if (tf != null && tf.IsArray && !tf.HasValue && token is JArray arr)
            {
                int n = Math.Min(arr.Count, field.Children.Count);
                for (int i = 0; i < n; i++)
                    changed |= ApplyOverField(field.Children[i], arr[i]);
                return changed;
            }

            bool tieneValor = tf != null && tf.HasValue;
            if (!tieneValor)
            {
                foreach (var child in field.Children)
                {
                    string nombre = child.TemplateField != null ? child.TemplateField.Name : null;
                    if (nombre == null)
                        continue;
                    changed |= ApplyOverField(child, token[nombre]);
                }
                return changed;
            }

            switch (tf.ValueType)
            {
                case AssetValueType.String:
                {
                    string nuevo = token.Type == JTokenType.Null ? "" : (string)token;
                    if (nuevo != field.AsString)
                    {
                        field.AsString = nuevo;
                        return true;
                    }
                    return false;
                }
                case AssetValueType.Bool:
                    if ((bool)token != field.AsBool) { field.AsBool = (bool)token; return true; }
                    return false;
                case AssetValueType.Int32:
                    if ((int)token != field.AsInt) { field.AsInt = (int)token; return true; }
                    return false;
                case AssetValueType.UInt32:
                    if ((uint)token != field.AsUInt) { field.AsUInt = (uint)token; return true; }
                    return false;
                case AssetValueType.Int64:
                    if ((long)token != field.AsLong) { field.AsLong = (long)token; return true; }
                    return false;
                case AssetValueType.Float:
                    if (Math.Abs((float)token - field.AsFloat) > 1e-9) { field.AsFloat = (float)token; return true; }
                    return false;
                case AssetValueType.Double:
                    if (Math.Abs((double)token - field.AsDouble) > 1e-12) { field.AsDouble = (double)token; return true; }
                    return false;
                default:
                    return false;   // ByteArray y demás: no se tocan
            }
        }

        private static void ImportRecurse(AssetTypeTemplateField tempField, JToken token, AssetsFileWriter aw)
        {
            bool align = tempField.IsAligned;

            if (!tempField.HasValue && !tempField.IsArray)
            {
                foreach (AssetTypeTemplateField childTempField in tempField.Children)
                {
                    JToken childToken = token[childTempField.Name];
                    if (childToken == null)
                        throw new Exception($"Falta el campo '{childTempField.Name}' en el JSON (padre {tempField.Type} {tempField.Name}).");
                    ImportRecurse(childTempField, childToken, aw);
                }
                if (align)
                    aw.Align();
                return;
            }

            if (tempField.HasValue && tempField.ValueType == AssetValueType.ManagedReferencesRegistry)
                throw new NotImplementedException("SerializeReference no está soportado en la importación JSON.");

            switch (tempField.ValueType)
            {
                case AssetValueType.Bool: aw.Write((bool)token); break;
                case AssetValueType.UInt8: aw.Write((byte)token); break;
                case AssetValueType.Int8: aw.Write((sbyte)token); break;
                case AssetValueType.UInt16: aw.Write((ushort)token); break;
                case AssetValueType.Int16: aw.Write((short)token); break;
                case AssetValueType.UInt32: aw.Write((uint)token); break;
                case AssetValueType.Int32: aw.Write((int)token); break;
                case AssetValueType.UInt64: aw.Write((ulong)token); break;
                case AssetValueType.Int64: aw.Write((long)token); break;
                case AssetValueType.Float: aw.Write((float)token); break;
                case AssetValueType.Double: aw.Write((double)token); break;
                case AssetValueType.String:
                    align = true;
                    aw.WriteCountStringInt32((string)token ?? "");
                    break;
                case AssetValueType.ByteArray:
                {
                    JArray arr = (JArray)token ?? new JArray();
                    byte[] data = new byte[arr.Count];
                    for (int i = 0; i < arr.Count; i++)
                        data[i] = (byte)arr[i];
                    aw.Write(data.Length);
                    aw.Write(data);
                    break;
                }
            }

            if (tempField.IsArray && tempField.ValueType != AssetValueType.ByteArray)
            {
                AssetTypeTemplateField childTempField = tempField.Children[1];
                JArray tokenArray = (JArray)token;
                if (tokenArray == null)
                    throw new Exception($"El campo '{tempField.Name}' no es un array en el JSON.");
                aw.Write(tokenArray.Count);
                foreach (JToken childToken in tokenArray.Children())
                    ImportRecurse(childTempField, childToken, aw);
            }

            if (align)
                aw.Align();
        }
    }
}
