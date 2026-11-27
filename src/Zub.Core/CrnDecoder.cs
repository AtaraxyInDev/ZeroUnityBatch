// Descompresor Crunch (crn) para texturas DXT1Crunched / DXT5Crunched / ETC_RGB4Crunched /
// ETC2_RGBA8Crunched: el mismo port que usa UABEA Next 4 (AssetsTools.NET.Texture).
// Los archivos de la carpeta Crn/ son el port a C# de crn_decomp.h de Rich Geldreich (ZLIB) que
// mantiene el proyecto AssetsTools.NET. Ver THIRD-PARTY.md.

using AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
using CrnTextureInfo = AssetsTools.NET.Texture.TextureDecoders.CrnUnity.TextureInfo;

namespace Zub
{
    internal static class CrnDecoder
    {
        /// <summary>Devuelve el bloque DXT/ETC ya descomprimido, o null si los datos no son crn validos.</summary>
        public static byte[] UnpackToBlockData(byte[] crn, int unityFormat, out int baseFormat)
        {
            baseFormat = -1;
            if (crn == null || crn.Length < 16)
                return null;

            CrnTextureInfo info;
            try { info = Decomp.GetTextureInfo(crn); }
            catch { return null; }

            if (info == null || info.Width == 0 || info.Height == 0 || info.Faces == 0)
                return null;

            Unpacker ctx;
            try { ctx = Decomp.UnpackBegin(crn); }
            catch { return null; }

            if (ctx == null)
                return null;

            try
            {
                uint blocksX = (info.Width + 3u) >> 2;
                uint blocksY = (info.Height + 3u) >> 2;
                uint rowPitch = blocksX * info.BytesPerBlock;
                uint faceSize = blocksY * rowPitch;

                var faces = new byte[info.Faces][];
                for (int i = 0; i < faces.Length; i++)
                    faces[i] = new byte[faceSize];

                if (!Decomp.UnpackLevel(ctx, faces, rowPitch, 0))
                    return null;

                // 28 DXT1Crunched - 29 DXT5Crunched - 64 ETC_RGB4Crunched - 65 ETC2_RGBA8Crunched
                baseFormat = unityFormat switch
                {
                    28 => 10,   // DXT1
                    29 => 12,   // DXT5
                    64 => 34,   // ETC_RGB4
                    65 => 47,   // ETC2_RGBA8
                    _ => info.BytesPerBlock == 16 ? 12 : 10
                };

                return faces[0];
            }
            catch { return null; }
            finally { try { Decomp.UnpackEnd(ctx); } catch { } }
        }
    }
}
