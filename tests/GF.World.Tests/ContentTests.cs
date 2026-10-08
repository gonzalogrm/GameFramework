using GF.Engine;
using Microsoft.Xna.Framework;
using Xunit;

namespace GF.World.Tests;

public sealed class ContentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gf_content_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void ListContentSprites_ReturnsTheCompiledTexturesSorted_IgnoringOtherFiles()
    {
        Directory.CreateDirectory(_dir);
        foreach (var file in new[] { "rock.xnb", "Bush_large.xnb", "grass_tuft.xnb", "sprites.json", "notes.txt", "rock.png" })
            File.WriteAllBytes(Path.Combine(_dir, file), new byte[] { 0 });

        Assert.Equal(new[] { "Bush_large", "grass_tuft", "rock" }, SpriteLoader.ListContentSprites(_dir));
    }

    [Fact]
    public void ListContentSprites_OfAMissingFolder_IsEmpty() =>
        Assert.Empty(SpriteLoader.ListContentSprites(Path.Combine(_dir, "no_existe")));

    [Fact]
    public void AddOrReplace_LetsLooseSpritesOverrideCompiledOnes_WhileAddStillRejectsDuplicates()
    {
        var builder = new SpriteAtlasBuilder();
        builder.Add("rock", 16, 16, new Color[16 * 16]);
        Assert.Throws<ArgumentException>(() => builder.Add("ROCK", 16, 16, new Color[16 * 16]));

        builder.AddOrReplace("rock", 32, 32, new Color[32 * 32]);   // el suelto sustituye al compilado (sin distinguir mayúsculas)
        Assert.True(builder.Contains("rock"));

        builder.AddOrReplace("new_one", 8, 8, new Color[8 * 8]);     // y si no existía, lo añade
        Assert.True(builder.Contains("new_one"));
    }
}
