using GF.World.Voxel;
using Xunit;

namespace GF.World.Tests;

public class LightChunkTests
{
    [Fact]
    public void RunLengthEncoding_ReadsBackEveryCell_AndShrinksUniformData()
    {
        var data = new ushort[32768];
        for (int i = 0; i < data.Length; i++) data[i] = (ushort)(i < 20000 ? 0 : i < 30000 ? 0x0FFF : (i % 7 == 0 ? 3 : 0x0120));
        var rle = RleU16.Encode(data);
        for (int i = 0; i < data.Length; i++) Assert.Equal(data[i], rle.Get(i));

        var uniform = RleU16.Encode(new ushort[32768]);
        Assert.Equal(1, uniform.RunCount);               // un subsuelo entero a cero: una sola tanda
        Assert.Equal(0, uniform.Get(32767));
        Assert.Equal(1, RleU16.Encode(new ushort[] { 5 }).RunCount);
    }
}
