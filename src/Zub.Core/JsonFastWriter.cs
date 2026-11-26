// Escritor JSON directo (sin JToken/Newtonsoft) con el MISMO formato que JsonCodec.Export:
// indentación de 2 espacios, "clave": valor, objetos/arrays vacíos en una línea.
// Se usa en la exportación en lote porque construir el árbol JToken para cada objeto es
// la mitad del tiempo y de la basura que genera el GC.
using AssetsTools.NET;
using System;
using System.Globalization;
using System.Text;

namespace Zub
{
    public static class JsonFastWriter
    {
        // Newtonsoft escribe con Environment.NewLine (CRLF en Windows): mismo byte a byte
        private static readonly string NL = Environment.NewLine;

        public static string Export(AssetTypeValueField baseField)
        {
            var sb = new StringBuilder(2048);
            Escribir(sb, baseField, 0);
            return sb.ToString();
        }

        private static void Sangrar(StringBuilder sb, int nivel)
        {
            sb.Append(NL);
            if (nivel > 0)
                sb.Append(' ', nivel * 2);
        }

        private static void Escribir(StringBuilder sb, AssetTypeValueField field, int nivel)
        {
            var template = field.TemplateField;

            if (template != null && template.IsArray)
            {
                if (template.ValueType == AssetValueType.ByteArray)
                {
                    byte[] bytes = field.AsByteArray;
                    if (bytes == null || bytes.Length == 0)
                    {
                        sb.Append("[]");
                        return;
                    }

                    sb.Append('[');
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        if (i > 0)
                            sb.Append(',');
                        Sangrar(sb, nivel + 1);
                        sb.Append(bytes[i].ToString(CultureInfo.InvariantCulture));
                    }
                    Sangrar(sb, nivel);
                    sb.Append(']');
                    return;
                }

                int n = field.Children.Count;
                if (n == 0)
                {
                    sb.Append("[]");
                    return;
                }

                sb.Append('[');
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    Sangrar(sb, nivel + 1);
                    Escribir(sb, field.Children[i], nivel + 1);
                }
                Sangrar(sb, nivel);
                sb.Append(']');
                return;
            }

            if (field.Value != null)
            {
                AssetValueType evt = field.Value.ValueType;

                if (evt != AssetValueType.ManagedReferencesRegistry)
                {
                    EscribirEscalar(sb, field, evt);
                    return;
                }

                EscribirRegistro(sb, field, nivel);
                return;
            }

            int m = field.Children.Count;
            if (m == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            for (int i = 0; i < m; i++)
            {
                if (i > 0)
                    sb.Append(',');
                Sangrar(sb, nivel + 1);
                EscribirCadena(sb, field.Children[i].FieldName);
                sb.Append(": ");
                Escribir(sb, field.Children[i], nivel + 1);
            }
            Sangrar(sb, nivel);
            sb.Append('}');
        }

        private static void EscribirEscalar(StringBuilder sb, AssetTypeValueField field, AssetValueType evt)
        {
            switch (evt)
            {
                case AssetValueType.Bool:
                    sb.Append(field.AsBool ? "true" : "false");
                    break;
                case AssetValueType.Int8:
                case AssetValueType.Int16:
                case AssetValueType.Int32:
                    sb.Append(field.AsInt.ToString(CultureInfo.InvariantCulture));
                    break;
                case AssetValueType.Int64:
                    sb.Append(field.AsLong.ToString(CultureInfo.InvariantCulture));
                    break;
                case AssetValueType.UInt8:
                case AssetValueType.UInt16:
                case AssetValueType.UInt32:
                    sb.Append(field.AsUInt.ToString(CultureInfo.InvariantCulture));
                    break;
                case AssetValueType.UInt64:
                    sb.Append(field.AsULong.ToString(CultureInfo.InvariantCulture));
                    break;
                case AssetValueType.String:
                    EscribirCadena(sb, field.AsString);
                    break;
                case AssetValueType.Float:
                    sb.Append(Numero(field.AsFloat.ToString("R", CultureInfo.InvariantCulture)));
                    break;
                case AssetValueType.Double:
                    sb.Append(Numero(field.AsDouble.ToString("R", CultureInfo.InvariantCulture)));
                    break;
                default:
                    EscribirCadena(sb, "invalid value");
                    break;
            }
        }

        /// <summary>Newtonsoft escribe "1.0" (le añade el decimal si el número no lo lleva).</summary>
        private static string Numero(string texto)
        {
            // Newtonsoft escribe los especiales de float como cadena: "NaN", "Infinity", "-Infinity"
            if (texto == "NaN" || texto == "Infinity" || texto == "-Infinity")
                return "\"" + texto + "\"";

            if (texto.IndexOf('.') < 0 && texto.IndexOf('E') < 0 && texto.IndexOf('e') < 0)
                return texto + ".0";
            return texto;
        }

        /// <summary>SerializeReference: mismo formato que UABEA (version + RefIds con rid/type/data).</summary>
        private static void EscribirRegistro(StringBuilder sb, AssetTypeValueField field, int nivel)
        {
            var registry = field.Value.AsManagedReferencesRegistry;

            if (registry.version != 1 && registry.version != 2)
                throw new NotSupportedException($"Registry version {registry.version} not supported!");

            sb.Append('{');
            Sangrar(sb, nivel + 1);
            EscribirCadena(sb, "version");
            sb.Append(": ");
            sb.Append(registry.version.ToString(CultureInfo.InvariantCulture));

            sb.Append(',');
            Sangrar(sb, nivel + 1);
            EscribirCadena(sb, "RefIds");
            sb.Append(": ");

            if (registry.references == null || registry.references.Count == 0)
            {
                sb.Append("[]");
            }
            else
            {
                sb.Append('[');
                for (int i = 0; i < registry.references.Count; i++)
                {
                    AssetTypeReferencedObject refObj = registry.references[i];
                    AssetTypeReference typeRef = refObj.type;

                    if (i > 0)
                        sb.Append(',');
                    Sangrar(sb, nivel + 2);
                    sb.Append('{');

                    if (registry.version == 2)
                    {
                        Sangrar(sb, nivel + 3);
                        EscribirCadena(sb, "rid");
                        sb.Append(": ");
                        sb.Append(refObj.rid.ToString(CultureInfo.InvariantCulture));
                        sb.Append(',');
                    }

                    Sangrar(sb, nivel + 3);
                    EscribirCadena(sb, "type");
                    sb.Append(": {");
                    Sangrar(sb, nivel + 4);
                    EscribirCadena(sb, "class");
                    sb.Append(": ");
                    EscribirCadena(sb, typeRef.ClassName);
                    sb.Append(',');
                    Sangrar(sb, nivel + 4);
                    EscribirCadena(sb, "ns");
                    sb.Append(": ");
                    EscribirCadena(sb, typeRef.Namespace);
                    sb.Append(',');
                    Sangrar(sb, nivel + 4);
                    EscribirCadena(sb, "asm");
                    sb.Append(": ");
                    EscribirCadena(sb, typeRef.AsmName);
                    Sangrar(sb, nivel + 3);
                    sb.Append('}');

                    sb.Append(',');
                    Sangrar(sb, nivel + 3);
                    EscribirCadena(sb, "data");
                    sb.Append(": ");

                    int campos = refObj.data == null || refObj.data.Children == null ? 0 : refObj.data.Children.Count;
                    if (campos == 0)
                    {
                        sb.Append("{}");
                    }
                    else
                    {
                        sb.Append('{');
                        for (int j = 0; j < campos; j++)
                        {
                            AssetTypeValueField child = refObj.data[j];
                            if (j > 0)
                                sb.Append(',');
                            Sangrar(sb, nivel + 4);
                            EscribirCadena(sb, child.FieldName);
                            sb.Append(": ");
                            Escribir(sb, child, nivel + 4);
                        }
                        Sangrar(sb, nivel + 3);
                        sb.Append('}');
                    }

                    Sangrar(sb, nivel + 2);
                    sb.Append('}');
                }
                Sangrar(sb, nivel + 1);
                sb.Append(']');
            }

            Sangrar(sb, nivel);
            sb.Append('}');
        }

        private static void EscribirCadena(StringBuilder sb, string s)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u0085' || c == '\u2028' || c == '\u2029')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
