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
