using GF.Engine;
using Xunit;

namespace GF.World.Tests;

public class SpriteTests
{
    private static List<PackItem> Items(params (string, int, int)[] sizes) =>
        sizes.Select(s => new PackItem(s.Item1, s.Item2, s.Item3)).ToList();

    [Fact]
    public void Packer_PlacesEverythingInsideWithoutOverlapping_AndKeepsThePadding()
    {
        var rnd = new Random(5);
        var items = Enumerable.Range(0, 60).Select(i => new PackItem($"s{i}", rnd.Next(4, 70), rnd.Next(4, 70))).ToList();
        const int pad = 1;

        var (placements, w, h) = SpritePacker.Pack(items, pad, 1024);

        Assert.Equal(items.Count, placements.Count);
        Assert.True((w & (w - 1)) == 0 && (h & (h - 1)) == 0, "potencias de dos");
        foreach (var p in placements)
        {
            Assert.True(p.X >= pad && p.Y >= pad && p.X + p.Width + pad <= w && p.Y + p.Height + pad <= h);
            var src = items.Single(i => i.Name == p.Name);
            Assert.Equal((src.Width, src.Height), (p.Width, p.Height));
        }
        for (int i = 0; i < placements.Count; i++)
        for (int j = i + 1; j < placements.Count; j++)
        {
            var a = placements[i]; var b = placements[j];
            bool apart = a.X + a.Width + pad <= b.X || b.X + b.Width + pad <= a.X ||
                         a.Y + a.Height + pad <= b.Y || b.Y + b.Height + pad <= a.Y;
            Assert.True(apart, $"{a.Name} y {b.Name} se solapan o no dejan margen");
        }
    }

    [Fact]
    public void Packer_IsIndependentOfInputOrder()
    {
        var items = Items(("a", 16, 16), ("b", 48, 36), ("c", 16, 32), ("d", 16, 16), ("e", 100, 20));
        var forward = SpritePacker.Pack(items).Placements.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var backward = SpritePacker.Pack(Enumerable.Reverse(items).ToList()).Placements.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void Packer_RejectsASpriteBiggerThanTheTexture() =>
        Assert.Throws<InvalidOperationException>(() => SpritePacker.Pack(Items(("huge", 600, 10)), 1, 512));

    [Fact]
    public void Manifest_DerivesSizeFromPixels_AndAllowsOverrides()
    {
        var m = SpriteManifest.Parse("""
            // comentario
            {
              "pixelsPerBlock": 16,
              "sprites": { "Rock": { "width": 0.9, "height": 0.7 }, "half": { "height": 2 } },
            }
            """);

        Assert.Equal((3f, 2.25f), m.SizeOf("bush_large", 48, 36));    // sin entrada: píxeles / 16
        Assert.Equal((0.9f, 0.7f), m.SizeOf("rock", 16, 16));          // sin distinguir mayúsculas
        Assert.Equal((1f, 2f), m.SizeOf("half", 16, 16));              // solo se cambia el alto
    }

    [Fact]
    public void Manifest_UsesPixelsPerBlock()
    {
        var m = SpriteManifest.Parse("{ \"pixelsPerBlock\": 32 }");
        Assert.Equal((1.5f, 1f), m.SizeOf("x", 48, 32));
    }
}
