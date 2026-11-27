namespace AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
public class Palette
{
    public uint Ofs;
    public uint Size;
    public ushort Num;

    public Palette(AssetsFileReader reader)
    {
        Ofs = Read24(reader);
        Size = Read24(reader);
        Num = reader.ReadUInt16();
    }

    /// <summary>Lee 3 bytes big-endian (ReadUInt24 de la version vieja consume 4).</summary>
    public static uint Read24(AssetsFileReader reader)
    {
        uint v = 0;
        v = (v << 8) | reader.ReadByte();
        v = (v << 8) | reader.ReadByte();
        v = (v << 8) | reader.ReadByte();
        return v;
    }
}
