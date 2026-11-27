using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;
using AssetRipper.TextureDecoder.Astc;
using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Etc;

namespace Zub
{
    public sealed class TextureInfo
    {
        public int Width;
        public int Height;
        public int MipCount;
        public string Format = "";
        public bool Decodable;
        public string Error;
        public byte[] Png;
        public long DataBytes;
    }

    /// <summary>Previsualización de Texture2D: decodifica a PNG con decodificadores managed.</summary>
    public static class TextureReader
    {
        public static TextureInfo Preview(UnityProject proj, AssetEntry entry, int maxDimension = 1024)
        {
            var info = new TextureInfo();
            try
            {
                var tempField = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);

                // "image data" se declara como array de UInt8; se fuerza a ByteArray (igual que UABEA)
                var imageDataField = tempField.Children.FirstOrDefault(f => f.Name == "image data");
                if (imageDataField != null)
                    imageDataField.ValueType = AssetValueType.ByteArray;

                var platformBlobField = tempField.Children.FirstOrDefault(f => f.Name == "m_PlatformBlob");
                if (platformBlobField != null && platformBlobField.Children.Count > 0)
                    platformBlobField.Children[0].ValueType = AssetValueType.ByteArray;

                AssetTypeValueField baseField;
                lock (entry.Inst)
                    baseField = proj.ReadField(entry.Inst, tempField, entry.Position);

                TextureFile tex = TextureFile.ReadTextureFile(baseField);

                info.Width = tex.m_Width;
                info.Height = tex.m_Height;
                info.MipCount = tex.m_MipCount;
                info.Format = ((TextureFormat)tex.m_TextureFormat).ToString();

                if (info.Width == 0 || info.Height == 0)
                {
                    info.Error = "textura sin dimensiones (probablemente generada en runtime)";
                    return info;
                }

                // Camino de UABEA: TextureFile.GetTextureData resuelve m_StreamData (archivo suelto o
                // dentro del bundle) y devuelve BGRA32 ya decodificado con el mismo decodificador.
                byte[] bgra = null;
                try
                {
                    lock (entry.Inst)
                        bgra = tex.GetTextureData(entry.Inst);
                }
                catch { }

                bool usarBgra = bgra != null && bgra.Length >= info.Width * info.Height * 4;
                info.DataBytes = bgra?.Length ?? 0;

                Image image;
                if (usarBgra)
                {
                    image = Image.LoadPixelData<Bgra32>(bgra, info.Width, info.Height);
                }
                else
                {
                    // plan B: leer los bytes a mano y decodificar (cubre casos que el helper no maneja)
                    byte[] raw = GetRawData(tex, entry.Inst);
                    if (raw == null || raw.Length == 0)
                    {
                        info.Error = "no se pudo leer el bloque de datos (resS ausente)";
                        return info;
                    }
                    info.DataBytes = raw.Length;

                    byte[] rgba = Decode(raw, info.Width, info.Height, (TextureFormat)tex.m_TextureFormat);
                    if (rgba == null)
                    {
                        info.Error = "formato no soportado para previsualizar: " + info.Format +
                                     (DecodeDetail != null ? "  (" + DecodeDetail + ")" : "");
                        return info;
                    }
                    image = Image.LoadPixelData<Rgba32>(rgba, info.Width, info.Height);
                }

                image.Mutate(i => i.Flip(FlipMode.Vertical));

                if (maxDimension > 0 && (image.Width > maxDimension || image.Height > maxDimension))
                {
                    image.Mutate(i => i.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(maxDimension, maxDimension)
                    }));
                }

                using (var ms = new MemoryStream())
                {
                    image.SaveAsPng(ms);
                    info.Png = ms.ToArray();
                }

                image.Dispose();
                info.Decodable = true;
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            return info;
        }


        /// <summary>Resumen de los campos que afectan a cómo carga Unity la textura.</summary>
        public static string Describir(UnityProject proj, AssetEntry entry)
        {
            try
            {
                var tf = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);
                var bf = proj.ReadField(entry.Inst, tf, entry.Position);

                string v(string n)
                {
                    try
                    {
                        var f = bf[n];
                        if (f == null) return "-";

                        string r = null;
                        try { r = f.AsString; } catch { }
                        if (r == null) { try { r = f.AsUInt.ToString(); } catch { } }
                        if (r == null) { try { r = f.AsInt.ToString(); } catch { } }
                        if (r == null) { try { r = f.AsBool.ToString(); } catch { } }
                        return r ?? "?";
                    }
                    catch { return "?"; }
                }

                var sd = bf["m_StreamData"];
                int datos = 0;
                try { datos = bf["image data"].AsByteArray?.Length ?? 0; } catch { }

                return $"W={v("m_Width")} H={v("m_Height")} formato={v("m_TextureFormat")} mips={v("m_MipCount")} " +
                       $"mipMap={v("m_MipMap")} completo={v("m_CompleteImageSize")} imagen={datos} " +
                       $"streamSize={sd?["size"]} streamPath='{(sd?["path"] != null ? sd["path"].AsString : "")}' " +
                       $"dim={v("m_TextureDimension")} readable={v("m_IsReadable")}";
            }
            catch (Exception ex) { return "error: " + ex.Message; }
        }

        /// <summary>Indica si los bytes viven en un .resS y devuelve tamaño/formato.</summary>
        public static bool EsExterna(UnityProject proj, AssetEntry entry, out int w, out int h, out string formato)
        {
            w = h = 0;
            formato = "";

            try
            {
                var tf = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);
                var bf = proj.ReadField(entry.Inst, tf, entry.Position);
                TextureFile tex = TextureFile.ReadTextureFile(bf);

                w = tex.m_Width;
                h = tex.m_Height;
                formato = ((TextureFormat)tex.m_TextureFormat).ToString();

                return tex.m_StreamData.size != 0 && !string.IsNullOrEmpty(tex.m_StreamData.path);
            }
            catch { return true; }
        }

        /// <summary>Reimporta un PNG/JPG sobre una Texture2D que guarda sus datos dentro del objeto.</summary>
        public static byte[] Import(UnityProject proj, AssetEntry entry, string imagePath, out string error)
        {
            error = null;

            try
            {
                var tempField = proj.GetTemplateField(entry.Inst, entry.Position, entry.ClassId, entry.MonoId);

                var imageDataField = tempField.Children.FirstOrDefault(f => f.Name == "image data");
                if (imageDataField != null)
                    imageDataField.ValueType = AssetValueType.ByteArray;

                var platformBlobField = tempField.Children.FirstOrDefault(f => f.Name == "m_PlatformBlob");
                if (platformBlobField != null && platformBlobField.Children.Count > 0)
                    platformBlobField.Children[0].ValueType = AssetValueType.ByteArray;

                AssetTypeValueField baseField;
                lock (entry.Inst)
                    baseField = proj.ReadField(entry.Inst, tempField, entry.Position);

                TextureFile tex = TextureFile.ReadTextureFile(baseField);

                if (tex.m_Width == 0 || tex.m_Height == 0)
                {
                    error = "textura sin dimensiones (generada en runtime)";
                    return null;
                }

                using var imagen = Image.Load<Rgba32>(imagePath);

                if (imagen.Width != tex.m_Width || imagen.Height != tex.m_Height)
                    imagen.Mutate(i => i.Resize(tex.m_Width, tex.m_Height));

                imagen.Mutate(i => i.Flip(FlipMode.Vertical));

                byte[] rgba = new byte[tex.m_Width * tex.m_Height * 4];
                imagen.CopyPixelDataTo(rgba);

                // el codificador espera BGRA (como System.Drawing)
                for (int i = 0; i + 3 < rgba.Length; i += 4)
                {
                    byte r = rgba[i];
                    rgba[i] = rgba[i + 2];
                    rgba[i + 2] = r;
                }

                var formato = (TextureFormat)tex.m_TextureFormat;
                bool ok = false;

                try
                {
                    tex.SetTextureData(rgba, tex.m_Width, tex.m_Height);
                    ok = Volcar(tex, baseField);
                }
                catch { ok = false; }

                if (!ok)
                {
                    // si el formato original no tiene codificador, se guarda sin compresión
                    try
                    {
                        tex.m_TextureFormat = (int)TextureFormat.RGBA32;
                        baseField["m_TextureFormat"].AsInt = (int)TextureFormat.RGBA32;
                        tex.SetTextureData(rgba, tex.m_Width, tex.m_Height);
                        ok = Volcar(tex, baseField);
                    }
                    catch { ok = false; }
                }

                if (!ok)
                {
                    error = "el formato " + formato + " no se puede codificar (prueba guardando como PNG normal)";
                    return null;
                }

                using var ms = new MemoryStream();
                var aw = new AssetsFileWriter(ms) { BigEndian = false };
                baseField.Write(aw);

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Copia los bytes codificados al campo del objeto (si no, se guardaría lo mismo).</summary>
        private static bool Volcar(TextureFile tex, AssetTypeValueField baseField)
        {
            try
            {
                byte[] datos = tex.pictureData;

                if (datos == null || datos.Length == 0)
                    return false;

                baseField["image data"].AsByteArray = datos;

                // si los datos vivían en un .resS, pasan a estar dentro del objeto
                var stream = baseField["m_StreamData"];
                if (stream != null && stream.Children.Count >= 3)
                {
                    stream["offset"].AsLong = 0;
                    stream["size"].AsUInt = 0;
                    stream["path"].AsString = "";
                }

                var completo = baseField["m_CompleteImageSize"];
                if (completo != null)
                    completo.AsUInt = (uint)datos.Length;

                // el codificador escribe un solo nivel: si el objeto declara mips, Unity
                // espera la cadena completa y la textura no carga (se ve en blanco)
                try
                {
                    var mips = baseField["m_MipCount"];
                    if (mips != null)
                    {
                        int n = 1;
                        try { n = mips.AsInt; } catch { try { n = mips.AsByte; } catch { } }
                        if (n > 1)
                        {
                            try { mips.AsInt = 1; } catch { try { mips.AsByte = 1; } catch { } }
                        }
                    }

                    var mipMap = baseField["m_MipMap"];
                    if (mipMap != null)
                    {
                        try { mipMap.AsBool = false; } catch { }
                    }
                }
                catch { }

                return true;
            }
            catch { return false; }
        }

        /// <summary>Comprueba que el campo "image data" acabó con bytes tras codificar.</summary>
        private static bool CampoConDatos(AssetTypeValueField baseField)
        {
            try
            {
                var f = baseField["image data"];
                return f != null && f.AsByteArray != null && f.AsByteArray.Length > 0;
            }
            catch { return false; }
        }

        private static byte[] GetRawData(TextureFile tex, AssetsFileInstance inst)
        {
            if (tex.m_StreamData.size == 0 || string.IsNullOrEmpty(tex.m_StreamData.path))
                return tex.pictureData;

            string path = tex.m_StreamData.path;
            long offset = (long)tex.m_StreamData.offset;
            int size = (int)tex.m_StreamData.size;

            // 1) el .resS puede estar DENTRO del bundle (Addressables, data.unity3d de Unity 6)
            byte[] dentro = ReadFromBundle(inst, path, offset, size);
            if (dentro != null)
                return dentro;

            // 2) archivo suelto junto al .assets
            string root = Path.GetDirectoryName(inst.path);
            string candidate = path.StartsWith("archive:/") ? Path.GetFileName(path) : path;
            if (!Path.IsPathRooted(candidate) && !string.IsNullOrEmpty(root))
                candidate = Path.Combine(root, candidate);

            try
            {
                if (!File.Exists(candidate))
                {
                    string only = Path.GetFileName(path);
                    string junto = string.IsNullOrEmpty(root) ? only : Path.Combine(root, only);
                    if (File.Exists(junto))
                        candidate = junto;
                    else
                        return null;
                }

                using var fs = File.OpenRead(candidate);
                fs.Position = offset;
                var buf = new byte[size];
                fs.ReadExactly(buf, 0, buf.Length);
                return buf;
            }
            catch { return null; }
        }

        /// <summary>Lee un bloque .resS empaquetado dentro del mismo bundle que el asset.</summary>
        private static byte[] ReadFromBundle(AssetsFileInstance inst, string streamPath, long offset, int size)
        {
            try
            {
                var bun = inst?.parentBundle;
                if (bun?.file == null)
                    return null;

                string wanted = streamPath;
                string only = Path.GetFileName(wanted);

                int idx = -1;
                var dirs = bun.file.BlockAndDirInfo.DirectoryInfos;
                for (int i = 0; i < dirs.Length; i++)
                {
                    string name = dirs[i].Name ?? "";
                    if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(name), only, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx < 0)
                    return null;

                bun.file.GetFileRange(idx, out long start, out long length);
                if (offset < 0 || size <= 0 || offset + size > length)
                    return null;

                var reader = bun.file.DataReader;
                if (reader != null)
                {
                    reader.Position = start + offset;
                    return reader.ReadBytes(size);
                }

                var ds = bun.DataStream;
                if (ds == null)
                    return null;

                ds.Position = start + offset;
                var buf = new byte[size];
                int leido = 0;
                while (leido < size)
                {
                    int n = ds.Read(buf, leido, size - leido);
                    if (n <= 0)
                        break;
                    leido += n;
                }
                return leido == size ? buf : null;
            }
            catch { return null; }
        }

        /// <summary>Motivo del último fallo de decodificación (para el mensaje al usuario).</summary>
        [ThreadStatic] private static string DecodeDetail;

        private static int Bloques(int v) => (v + 3) / 4;

        private static byte[] Decode(byte[] data, int width, int height, TextureFormat format)
        {
            DecodeDetail = null;
            switch (format)
            {
                // Unity deja la marca "crunched" aunque el build guarde el bloque normal en muchos casos:
                // si el tamaño coincide con el del formato base, se decodifica igual.
                case TextureFormat.DXT1Crunched:
                    if (data.Length >= Bloques(width) * Bloques(height) * 8)
                        return DecodeBc(() => BcDecoder.DecompressBC1(data, width, height, out var o) >= 0 ? o : null);
                    DecodeDetail = $"crunch de verdad: {data.Length:N0} B, DXT1 sin comprimir serían {Bloques(width) * Bloques(height) * 8:N0} B";
                    return null;
                case TextureFormat.DXT5Crunched:
                    if (data.Length >= Bloques(width) * Bloques(height) * 16)
                        return DecodeBc(() => BcDecoder.DecompressBC3(data, width, height, out var o) >= 0 ? o : null);
                    DecodeDetail = $"crunch de verdad: {data.Length:N0} B, DXT5 sin comprimir serían {Bloques(width) * Bloques(height) * 16:N0} B";
                    return null;
                case TextureFormat.ETC_RGB4Crunched:
                    if (data.Length >= Bloques(width) * Bloques(height) * 8)
                        return DecodeBc(() => EtcDecoder.DecompressETC(data, width, height, out var o) >= 0 ? o : null);
                    DecodeDetail = $"crunch de verdad: {data.Length:N0} B";
                    return null;
                case TextureFormat.ETC2_RGBA8Crunched:
                    if (data.Length >= Bloques(width) * Bloques(height) * 16)
                        return DecodeBc(() => EtcDecoder.DecompressETC2A1(data, width, height, out var o) >= 0 ? o : null);
                    DecodeDetail = $"crunch de verdad: {data.Length:N0} B";
                    return null;
                case TextureFormat.DXT1:
                    return DecodeBc(() => BcDecoder.DecompressBC1(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.DXT5:
                    return DecodeBc(() => BcDecoder.DecompressBC3(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.BC4:
                    return DecodeBc(() => BcDecoder.DecompressBC4(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.BC5:
                    return DecodeBc(() => BcDecoder.DecompressBC5(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.BC6H:
                    return DecodeBc(() => BcDecoder.DecompressBC6H(data, width, height, false, out var o) >= 0 ? o : null);
                case TextureFormat.BC7:
                    return DecodeBc(() => BcDecoder.DecompressBC7(data, width, height, out var o) >= 0 ? o : null);

                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC_RGB4_3DS:
                    return DecodeBc(() => EtcDecoder.DecompressETC(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.ETC2_RGB4:
                    return DecodeBc(() => EtcDecoder.DecompressETC2(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.ETC2_RGBA1:
                    return DecodeBc(() => EtcDecoder.DecompressETC2A1(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ETC_RGBA8_3DS:
                    return DecodeBc(() => EtcDecoder.DecompressETC2A8(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.EAC_R:
                    return DecodeBc(() => EtcDecoder.DecompressEACRUnsigned(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.EAC_R_SIGNED:
                    return DecodeBc(() => EtcDecoder.DecompressEACRSigned(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.EAC_RG:
                    return DecodeBc(() => EtcDecoder.DecompressEACRGUnsigned(data, width, height, out var o) >= 0 ? o : null);
                case TextureFormat.EAC_RG_SIGNED:
                    return DecodeBc(() => EtcDecoder.DecompressEACRGSigned(data, width, height, out var o) >= 0 ? o : null);

                case TextureFormat.ASTC_RGB_4x4:
                case TextureFormat.ASTC_RGBA_4x4: return Astc(data, width, height, 4, 4);
                case TextureFormat.ASTC_RGB_5x5:
                case TextureFormat.ASTC_RGBA_5x5: return Astc(data, width, height, 5, 5);
                case TextureFormat.ASTC_RGB_6x6:
                case TextureFormat.ASTC_RGBA_6x6: return Astc(data, width, height, 6, 6);
                case TextureFormat.ASTC_RGB_8x8:
                case TextureFormat.ASTC_RGBA_8x8: return Astc(data, width, height, 8, 8);
                case TextureFormat.ASTC_RGB_10x10:
                case TextureFormat.ASTC_RGBA_10x10: return Astc(data, width, height, 10, 10);
                case TextureFormat.ASTC_RGB_12x12:
                case TextureFormat.ASTC_RGBA_12x12: return Astc(data, width, height, 12, 12);

                case TextureFormat.RGBA32:
                    return data.Length >= width * height * 4 ? data : null;

                case TextureFormat.ARGB32:
                {
                    // Los bytes van en orden A,R,G,B (verificado contra UnityPy en Unity 5.3):
                    // al leerlos como B,G,R,A el rojo y el azul salen cambiados y las máscaras
                    // se ven amarillas.
                    if (data.Length < width * height * 4)
                        return null;
                    var output = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        output[i * 4 + 0] = data[i * 4 + 1];
                        output[i * 4 + 1] = data[i * 4 + 2];
                        output[i * 4 + 2] = data[i * 4 + 3];
                        output[i * 4 + 3] = data[i * 4 + 0];
                    }
                    return output;
                }

                case TextureFormat.BGRA32:
                {
                    // en memoria little-endian: B,G,R,A
                    if (data.Length < width * height * 4)
                        return null;
                    var output = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        output[i * 4 + 0] = data[i * 4 + 2];
                        output[i * 4 + 1] = data[i * 4 + 1];
                        output[i * 4 + 2] = data[i * 4 + 0];
                        output[i * 4 + 3] = data[i * 4 + 3];
                    }
                    return output;
                }

                case TextureFormat.RGB24:
                {
                    if (data.Length < width * height * 3)
                        return null;
                    var output = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        output[i * 4 + 0] = data[i * 3 + 0];
                        output[i * 4 + 1] = data[i * 3 + 1];
                        output[i * 4 + 2] = data[i * 3 + 2];
                        output[i * 4 + 3] = 255;
                    }
                    return output;
                }

                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                {
                    if (data.Length < width * height)
                        return null;
                    var output = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        output[i * 4 + 0] = data[i];
                        output[i * 4 + 1] = data[i];
                        output[i * 4 + 2] = data[i];
                        output[i * 4 + 3] = 255;
                    }
                    return output;
                }

                case TextureFormat.RGB565:
                {
                    if (data.Length < width * height * 2)
                        return null;
                    var output = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        ushort v = BitConverter.ToUInt16(data, i * 2);
                        int r = (v >> 11) & 0x1F;
                        int g = (v >> 5) & 0x3F;
                        int b = v & 0x1F;
                        output[i * 4 + 0] = (byte)((r << 3) | (r >> 2));
                        output[i * 4 + 1] = (byte)((g << 2) | (g >> 4));
                        output[i * 4 + 2] = (byte)((b << 3) | (b >> 2));
                        output[i * 4 + 3] = 255;
                    }
                    return output;
                }

                case TextureFormat.ARGB4444:
                case TextureFormat.RGBA4444:
                {
                    if (data.Length < width * height * 2)
                        return null;
                    var output = new byte[width * height * 4];
                    for (int i = 0; i < width * height; i++)
                    {
                        ushort v = BitConverter.ToUInt16(data, i * 2);
                        byte a = (byte)((v >> 12) & 0xF);
                        byte r = (byte)((v >> 8) & 0xF);
                        byte g = (byte)((v >> 4) & 0xF);
                        byte b = (byte)(v & 0xF);
                        output[i * 4 + 0] = (byte)(r * 17);
                        output[i * 4 + 1] = (byte)(g * 17);
                        output[i * 4 + 2] = (byte)(b * 17);
                        output[i * 4 + 3] = (byte)(a * 17);
                    }
                    return output;
                }
            }
            return null;
        }

        private static byte[] DecodeBc(Func<byte[]> decoder)
        {
            try { return decoder(); } catch { return null; }
        }

        private static byte[] Astc(byte[] data, int width, int height, int bw, int bh)
        {
            try
            {
                return AstcDecoder.DecodeASTC(data, width, height, bw, bh, out byte[] output) >= 0 ? output : null;
            }
            catch { return null; }
        }
    }
}
