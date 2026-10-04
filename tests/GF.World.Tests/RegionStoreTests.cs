using GF.Core;
using GF.World;
using Xunit;

namespace GF.World.Tests;

public sealed class RegionStoreTests : IDisposable
{
    private static readonly ChunkShape Shape = new(16, 16, 16);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gf_rgn_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private RegionChunkStore<ushort> NewStore(int maxOpen = 16, Func<ChunkCoord, ChunkCoord>? canonical = null,
        IChunkStore<ushort>? fallback = null) =>
        new(_dir, regionChunksX: 16, regionChunksY: 16, regionChunksZ: 16, maxOpenRegions: maxOpen,
            canonicalize: canonical, fallback: fallback);

    /// <summary>Chunk con datos pseudoaleatorios (casi incompresibles: ~8 KB comprimidos).</summary>
    private static Chunk<ushort> Filled(ChunkCoord c, int seed)
    {
        var chunk = new Chunk<ushort>(c, Shape);
        var rnd = new Random(seed);
        for (int i = 0; i < chunk.Cells.Length; i++) chunk.Cells[i] = (ushort)rnd.Next(ushort.MaxValue);
        return chunk;
    }

    private static Chunk<ushort> Empty(ChunkCoord c) => new(c, Shape);

    [Fact]
    public void RoundTrip_AndPersistsAcrossInstances()
    {
        var a = Filled(new ChunkCoord(3, 1, -2), 1);   // Z negativa: otra región

        using (var store = NewStore())
        {
            store.Save(a);
            var b = Empty(a.Coord);
            Assert.True(store.TryLoad(b));
            Assert.Equal(a.Cells, b.Cells);
        }
        using (var store = NewStore())
        {
            var c = Empty(a.Coord);
            Assert.True(store.TryLoad(c));
            Assert.Equal(a.Cells, c.Cells);
        }
    }

    [Fact]
    public void ManyChunks_ShareOneRegionFile()
    {
        var coords = new[] { new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 0), new ChunkCoord(0, 2, 5), new ChunkCoord(15, 15, 15) };
        using var store = NewStore();
        for (int i = 0; i < coords.Length; i++) store.Save(Filled(coords[i], i));
        store.Flush();

        Assert.Single(Directory.GetFiles(_dir, "*.rgn"));
        for (int i = 0; i < coords.Length; i++)
        {
            var loaded = Empty(coords[i]);
            Assert.True(store.TryLoad(loaded));
            Assert.Equal(Filled(coords[i], i).Cells, loaded.Cells);
        }
    }

    [Fact]
    public void Overwrites_KeepLatest_AndFileIsCompactedOnClose()
    {
        var coord = new ChunkCoord(0, 0, 0);
        Chunk<ushort> last = null!;
        using (var store = NewStore())
        {
            for (int i = 0; i < 20; i++)
            {
                last = Filled(coord, 100 + i);
                store.Save(last);
            }
        }

        var file = Directory.GetFiles(_dir, "*.rgn").Single();
        Assert.True(new FileInfo(file).Length < 20_000, "20 versiones de ~8 KB deben quedar en una tras compactar");

        using (var store = NewStore())
        {
            var loaded = Empty(coord);
            Assert.True(store.TryLoad(loaded));
            Assert.Equal(last.Cells, loaded.Cells);
        }
    }

    [Fact]
    public void LruCache_EvictsRegions_AndReloadsThemTransparently()
    {
        using var store = NewStore(maxOpen: 2);
        var coords = new[] { 0, 16, 32, 48 }.Select(x => new ChunkCoord(x, 0, 0)).ToArray();   // 4 regiones distintas

        for (int i = 0; i < coords.Length; i++) store.Save(Filled(coords[i], i));
        Assert.True(store.OpenRegions <= 2);

        for (int i = 0; i < coords.Length; i++)
        {
            var loaded = Empty(coords[i]);
            Assert.True(store.TryLoad(loaded));
            Assert.Equal(Filled(coords[i], i).Cells, loaded.Cells);
        }
        Assert.True(store.OpenRegions <= 2);
        Assert.True(store.CacheMisses >= 4);

        long hits = store.CacheHits;
        Assert.True(store.TryLoad(Empty(coords[3])));   // la última región usada sigue abierta
        Assert.True(store.CacheHits > hits);
    }

    [Fact]
    public void MissingChunk_ReturnsFalse_WithoutCreatingFiles()
    {
        using var store = NewStore();
        Assert.False(store.TryLoad(Empty(new ChunkCoord(5, 5, 5))));
        Assert.Empty(Directory.GetFiles(_dir, "*.rgn"));
    }

    [Fact]
    public void CanonicalKeys_ShareData_AcrossTheSeam()
    {
        Func<ChunkCoord, ChunkCoord> wrap = c => c with { X = ((c.X % 64) + 64) % 64 };
        var west = Filled(new ChunkCoord(-1, 0, 5), 9);

        using var store = NewStore(canonical: wrap);
        store.Save(west);

        var east = Empty(new ChunkCoord(63, 0, 5));
        Assert.True(store.TryLoad(east));
        Assert.Equal(west.Cells, east.Cells);
    }

    [Fact]
    public void TruncatedTail_IsDiscarded_WithoutLosingEarlierChunks()
    {
        var a = Filled(new ChunkCoord(0, 0, 0), 1);
        var b = Filled(new ChunkCoord(1, 0, 0), 2);
        using (var store = NewStore())
        {
            store.Save(a);
            store.Save(b);
        }

        var file = Directory.GetFiles(_dir, "*.rgn").Single();
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Write))
            fs.SetLength(fs.Length - 10);   // simula un apagado a mitad de escritura

        using (var store = NewStore())
        {
            var loadedA = Empty(a.Coord);
            Assert.True(store.TryLoad(loadedA));
            Assert.Equal(a.Cells, loadedA.Cells);
            Assert.False(store.TryLoad(Empty(b.Coord)));   // el último registro se perdió: se regenerará
        }
    }

    [Fact]
    public void Fallback_ReadsLegacyChunkFiles_AndRegionWinsAfterSave()
    {
        var legacy = new FileChunkStore<ushort>(Path.Combine(_dir, "legacy"));
        var old = Filled(new ChunkCoord(2, 0, 2), 1);
        legacy.Save(old);

        using var store = NewStore(fallback: legacy);
        var loaded = Empty(old.Coord);
        Assert.True(store.TryLoad(loaded));
        Assert.Equal(old.Cells, loaded.Cells);

        var edited = Filled(old.Coord, 2);
        store.Save(edited);
        var again = Empty(old.Coord);
        Assert.True(store.TryLoad(again));
        Assert.Equal(edited.Cells, again.Cells);
    }

    [Fact]
    public void IncompatibleRegionFile_IsSetAside_AndTheStoreKeepsWorking()
    {
        var coord = new ChunkCoord(0, 0, 0);
        using (var store = NewStore()) store.Save(Filled(coord, 1));

        var small = new ChunkShape(8, 8, 8);
        using (var store = NewStore())
        {
            var chunk = new Chunk<ushort>(coord, small);
            Assert.False(store.TryLoad(chunk));   // otra forma de chunk: el archivo no se reutiliza
            Array.Fill(chunk.Cells, (ushort)7);
            store.Save(chunk);

            var back = new Chunk<ushort>(coord, small);
            Assert.True(store.TryLoad(back));
            Assert.Equal(chunk.Cells, back.Cells);
        }
        Assert.Single(Directory.GetFiles(_dir, "*.bad"));   // el original se conserva
    }
}
