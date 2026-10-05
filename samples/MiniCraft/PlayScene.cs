using GF.Core;
using GF.Engine;
using GF.UI;
using GF.World;
using GF.World.Entities;
using GF.World.Map;
using GF.World.Tiles;
using GF.World.Voxel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace MiniCraft;

public sealed class PlayScene : UiScene
{
    private const int LowestChunkY = 0;   // la altura (nº de chunks en vertical) sale de WorldSettings.VerticalChunks
    private const float EyeHeight = 1.80f;
    private const float Reach = 6f;
    private const float AutosaveSeconds = 60f;
    private static readonly Vector3 BodySize = new(0.6f, 1.8f, 0.6f);

    private readonly int _seed;
    private readonly SaveData? _save;
    private readonly WorldSettings _settings;
    private readonly WorldScale _scale;
    private readonly ushort[] _hotbarBlocks =
        { Blocks.Grass, Blocks.Dirt, Blocks.Stone, Blocks.Sand, Blocks.Log, Blocks.Leaves, Blocks.Water,
          Blocks.Bush, Blocks.Rock };
    private readonly FirstPersonCamera _camera = new();
    private readonly FrameProfiler _prof = new();

    private World<ushort> _world = null!;
    private ChunkManager<ushort> _chunks = null!;
    private RegionChunkStore<ushort> _store = null!;
    private VoxelWorldRenderer _renderer = null!;
    private SpriteAtlas _sprites = null!;
    private FarTerrainRenderer? _far;   // terreno lejano por niveles de detalle (null si está desactivado)
    private MiniCraftClimate _climate = null!;
    private Task<World<MapCell>> _mapTask = null!;
    private World<MapCell>? _map;
    private EntityStore _entities = null!;
    private NpcSystem _npcs = null!;
    private BoxRenderer _boxes = null!;
    private Hotbar _hotbar = null!;
    private Label _debug = null!, _status = null!, _inspector = null!;
    private Entity? _entityTarget;
    private bool _inspecting;
    private float _inspectTimer;
    private Vec3d _pos = new(0, 64, 0);   // doble precisión: a 100.000 bloques del origen un float pierde 1/128 de bloque
    private Vector3 _vel;
    private bool _onGround, _loaded, _spawned, _needsUnstuck, _entitiesInit;
    private bool _flying, _autoStep;   // modos de movimiento (F = volar, G = escalón automático)
    private float _messageTimer;
    private int _selected, _frames;
    private float _fpsTimer, _fps, _autosave;
    private VoxelHit? _target;

    public PlayScene(int seed, WorldSettings settings, SaveData? save = null)
    {
        _seed = seed;
        _settings = settings;
        _scale = settings.CreateScale();
        _save = save;
    }

    protected override Widget BuildUi()
    {
        _hotbar = new Hotbar(_hotbarBlocks.Select(id => Blocks.Registry.Get(id).Name).ToArray());
        var root = new Panel();
        root.Widgets.Add(new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        root.Widgets.Add(_hotbar.Root);
        _debug = new Label { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), Visible = false };
        root.Widgets.Add(_debug);
        _inspector = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8), Padding = new Thickness(8),
            Background = new SolidBrush(new Color(0, 0, 0, 160)), Visible = false,
        };
        root.Widgets.Add(_inspector);
        _status = new Label { Text = "Generando mundo...", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 60, 0, 0) };
        root.Widgets.Add(_status);
        return root;
    }

    public override void Load()
    {
        if (_save != null)
        {
            _pos = new Vec3d(_save.X, _save.Y, _save.Z);
            _camera.Yaw = _save.Yaw;
            _camera.Pitch = _save.Pitch;
            _selected = Math.Clamp(_save.Selected, 0, _hotbarBlocks.Length - 1);
        }

        // El mismo modelo climático alimenta el terreno de los chunks y el mapamundi.
        _climate = new MiniCraftClimate(_seed, _settings, _scale);
        // Altura y bioma de cada columna se calculan una sola vez y los comparten terreno, árboles y decoración.
        var columns = new TerrainColumnCache(_climate, _scale);
        var generator = new WorldGenerator<ushort>(_seed, _climate.Noise)
            .AddStage(new TerrainStage(_climate, columns))
            .AddStage(new TreeStage(_climate, _scale, columns))
            .AddStage(new DecorationStage(_climate, _scale, columns));

        int cs = _settings.ChunkSize, layers = _settings.VerticalChunks, view = _settings.ViewDistance;
        float viewBlocks = view * cs;
        // Terreno lejano (LOD): si está activo, la niebla y el plano lejano de la cámara se alejan hasta su límite.
        float farBlocks = _settings.FarDistanceChunks * cs;
        bool farEnabled = farBlocks > viewBlocks * 1.25f;
        float fogRange = farEnabled ? farBlocks : viewBlocks;
        float fogStart = fogRange * (farEnabled ? 0.45f : 0.55f), fogEnd = fogRange * (farEnabled ? 0.98f : 0.95f);
        // Hilos de fondo: ~60 % a generar chunks y el resto a mallarlos; se dejan libres un par de núcleos para el juego.
        int workers = _settings.WorkerThreads > 0 ? _settings.WorkerThreads : Math.Max(2, Environment.ProcessorCount - 2);
        int genThreads = Math.Max(1, (int)Math.Round(workers * 0.6));
        int meshThreads = Math.Max(1, workers - genThreads);
        _world = new World<ushort>(new ChunkShape(cs, cs, cs));
        // Guardado por regiones (el chunk -1 y el N-1 son el mismo lugar: comparten dato). Si el mundo se guardó con el
        // formato anterior (un archivo por chunk), esos chunks se siguen leyendo y pasan a regiones cuando se vuelvan a guardar.
        Func<ChunkCoord, ChunkCoord> canonical = c => c with { X = _scale.WrapChunkX(c.X) };
        _store = new RegionChunkStore<ushort>(SaveSystem.RegionsDir, regionChunksX: 16, regionChunksY: 32, regionChunksZ: 16,
            maxOpenRegions: 16, canonicalize: canonical,
            fallback: new FileChunkStore<ushort>(SaveSystem.ChunksDir, canonical));
        _chunks = new ChunkManager<ushort>(_world, generator)
        {
            RadiusX = view, RadiusY = layers, RadiusZ = view,
            MaxConcurrentJobs = genThreads,
            MinChunkY = LowestChunkY, MaxChunkY = layers - 1,
            MinChunkZ = 0, MaxChunkZ = _scale.ChunkCountZ - 1,   // los polos
            Store = _store,
        };
        // Sprites 2D: cada .png de la carpeta sprites/ es un sprite, de cualquier tamaño (ver LEEME).
        _sprites = SpriteLoader.LoadFolder(Game.GraphicsDevice, Path.Combine(AppContext.BaseDirectory, "sprites"), out var spriteMessages);
        foreach (var message in spriteMessages) Console.Error.WriteLine("[sprites] " + message);
        _renderer = new VoxelWorldRenderer(Game.GraphicsDevice, _world, Blocks.Registry, AtlasFactory.Create(Game.GraphicsDevice), _sprites)
        {
            MaxMeshingJobs = meshThreads,
            CanMesh = _chunks.AreNeighborsSettled,   // no mallar hasta que los vecinos estén cargados: cada chunk se malla una vez, no diez
            FogColor = MiniCraftGame.SkyColor, FogStart = fogStart, FogEnd = fogEnd,
        };

        _camera.FarPlane = Math.Max(500f, fogRange * 1.2f);
        if (farEnabled) _camera.NearPlane = 0.15f;   // más precisión de profundidad a distancia

        // Terreno LEJANO: relieve aproximado que llega mucho más lejos que los chunks y gana detalle al acercarte. Dentro de la
        // distancia de chunks lo sustituyen los bloques (con colinas finas, árboles, sprites y entidades).
        if (farEnabled)
        {
            _far = new FarTerrainRenderer(Game.GraphicsDevice, new MiniCraftFarSource(_climate, _scale), _scale.HeightBlocks)
            {
                FarDistance = farBlocks, SplitFactor = _settings.FarDetail,
                FogColor = MiniCraftGame.SkyColor, FogStart = fogStart, FogEnd = fogEnd,
            };
        }

        // Entidades: índice espacial + IA. El almacén se engancha al mundo para activarse al cargar chunks.
        _entities = new EntityStore(_world.Shape, _scale);
        _entities.Attach(_world);
        _entities.Definitions = EntityTypes.Registry;   // cada entidad remite a su tipo y a su prototipo de propiedades
        _npcs = new NpcSystem(_entities, _world, _climate, _scale, () => _map, _seed);
        _boxes = new BoxRenderer(Game.GraphicsDevice, _scale)
        {
            FogColor = MiniCraftGame.SkyColor, FogStart = fogStart, FogEnd = fogEnd,
        };
        if (_save != null && SaveSystem.TryLoadEntities() is { } savedEntities)
            _entities.Import(savedEntities.Entities, EntityTypes.Registry, savedEntities.Time);

        // El mapamundi se calcula en segundo plano (promedia unas 360.000 columnas).
        var scale = _scale; var climate = _climate; int samples = _settings.MapSamplesPerAxis;
        _mapTask = Task.Run(() => WorldMapBuilder.Build(scale, climate, s => climate.Classify(s), Biomes.Registry.Count, samples));

        base.Load();   // construye la UI
        _loaded = true;
        Game.Input.SetMouseCaptured(true);
    }

    public override void OnResumed() => Game.Input.SetMouseCaptured(true);

    public override void Unload()
    {
        if (!_loaded) return;
        Save();
        Game.Input.SetMouseCaptured(false);
        _chunks.Dispose();
        _store.Dispose();
        _renderer.Dispose();
        _boxes.Dispose();
        _sprites.Dispose();
        _far?.Dispose();
    }

    private void Save()
    {
        if (!_spawned) return;   // no guardar una posición provisional
        _chunks.SaveAll();
        _store.Flush();
        SaveSystem.Write(new SaveData
        {
            Seed = _seed, Settings = _settings, X = _pos.X, Y = _pos.Y, Z = _pos.Z,
            Yaw = _camera.Yaw, Pitch = _camera.Pitch, Selected = _selected,
        });
        SaveSystem.WriteEntities(new EntitySave { Time = _entities.Time, Entities = _entities.Export(EntityTypes.Registry) });
    }

    // ------------------------------------------------------------------ viaje

    /// <summary>Lleva al jugador a una columna del mundo. La altura se obtiene del modelo, sin esperar a los chunks.</summary>
    public void TeleportTo(int wx, int wz)
    {
        wx = _scale.WrapX(wx);
        wz = Math.Clamp(wz, 0, _scale.HeightBlocks - 1);
        int ground = MiniCraftClimate.ToBlockHeight(_climate.Sample(wx, wz).Height);
        _pos = new Vec3d(wx + 0.5, Math.Max(ground, _climate.SeaLevel) + 2.0, wz + 0.5);
        _vel = Vector3.Zero;
        _onGround = false;
        _needsUnstuck = true;   // al cargar la columna, si hay un árbol justo ahí, subimos hasta quedar libres
    }

    private bool TrySpawn()
    {
        if (_save != null) { FinishSpawn(); return true; }
        if (_map == null && !_mapTask.IsFaulted) return false;   // esperando al mapa

        if (_map != null && TryFindLandRegion(_map, out int wx, out int wz)) TeleportTo(wx, wz);
        else
        {
            var c = _scale.RegionCenter(_scale.MapWidth / 2, _scale.MapHeight / 2);
            TeleportTo(c.X, c.Z);
        }
        FinishSpawn();
        return true;
    }

    private void FinishSpawn()
    {
        _spawned = true;
        _status.Visible = false;
    }

    /// <summary>Busca en espiral, desde el centro del mapa, una región templada, de tierra y sin montañas.</summary>
    private bool TryFindLandRegion(World<MapCell> map, out int wx, out int wz)
    {
        int cx = _scale.MapWidth / 2, cz = _scale.MapHeight / 2;
        for (int r = 0; r < _scale.MapWidth; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
            int rz = cz + dz;
            if (rz < 0 || rz >= _scale.MapHeight) continue;
            int rx = ((cx + dx) % _scale.MapWidth + _scale.MapWidth) % _scale.MapWidth;

            var c = map.GetCell(new CellCoord(rx, rz, 0));
            if (c.WaterFraction < 0.2f && c.Temperature > 0.3f && c.Biome != Biomes.Mountains && c.Biome != Biomes.SnowPeaks)
            {
                var center = _scale.RegionCenter(rx, rz);
                wx = center.X; wz = center.Z;
                return true;
            }
        }
        wx = wz = 0;
        return false;
    }

    // ------------------------------------------------------------------ update

    public override void Update(GameTime gameTime)
    {
        var input = Game.Input;
        float dt = MathF.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.05f);

        if (input.Pressed("Pause")) { Game.Scenes.Push(new PauseScene()); return; }
        if (input.KeyPressed(Keys.F3)) _debug.Visible = !_debug.Visible;

        if (_map == null && _mapTask.IsCompletedSuccessfully) _map = _mapTask.Result;
        if (!_spawned && !TrySpawn()) return;

        if (_messageTimer > 0 && (_messageTimer -= dt) <= 0) _status.Visible = false;
        if (input.Pressed("Fly"))
        {
            _flying = !_flying;
            _vel.Y = 0;
            ShowMessage(_flying ? "Vuelo activado (Espacio sube, Ctrl o C baja, F para salir)" : "Vuelo desactivado");
        }
        if (input.Pressed("AutoStep"))
        {
            _autoStep = !_autoStep;
            ShowMessage(_autoStep ? "Escalon automatico activado" : "Escalon automatico desactivado");
        }
        if (input.Pressed("Inspect"))
        {
            _inspecting = !_inspecting;
            _inspector.Visible = _inspecting;
            _inspectTimer = 0;
        }

        if (!_entitiesInit && _map != null)
        {
            if (_entities.Count == 0) _npcs.SpawnInitial(_pos);   // mundo nuevo (o guardado sin entidades)
            _entitiesInit = true;
        }

        if (input.Pressed("Map") && _map != null)
        {
            Game.Scenes.Push(new MapScene(_map, _scale, () => new Vector2((float)_pos.X, (float)_pos.Z), TeleportTo, EntityMarkers, _settings.WorldHeight));
            return;
        }

        _autosave += dt;
        if (_autosave >= AutosaveSeconds) { Save(); _autosave = 0; }

        _camera.Rotate(input.MouseDelta.X, input.MouseDelta.Y);

        for (int i = 0; i < _hotbarBlocks.Length; i++)
            if (input.KeyPressed(Keys.D1 + i)) _selected = i;
        if (input.ScrollDelta != 0)
            _selected = (_selected - Math.Sign(input.ScrollDelta) + _hotbarBlocks.Length) % _hotbarBlocks.Length;
        _hotbar.Selected = _selected;

        var fwd = new Vector3(MathF.Sin(_camera.Yaw), 0, -MathF.Cos(_camera.Yaw));
        var right = new Vector3(MathF.Cos(_camera.Yaw), 0, MathF.Sin(_camera.Yaw));
        var wish = Vector3.Zero;
        if (input.IsDown("Forward")) wish += fwd;
        if (input.IsDown("Back")) wish -= fwd;
        if (input.IsDown("Right")) wish += right;
        if (input.IsDown("Left")) wish -= right;
        if (wish != Vector3.Zero) wish.Normalize();
        bool sprint = input.IsDown("Sprint");
        float speed = _flying ? (sprint ? 50f : 10f) : (sprint ? 50f : 5.5f);
        _vel.X = wish.X * speed;
        _vel.Z = wish.Z * speed;
        if (_flying)
        {
            // En vuelo no hay gravedad: Espacio sube, Ctrl/C baja.
            float vertical = (input.IsDown("Jump") ? 1f : 0f) - (input.IsDown("Descend") ? 1f : 0f);
            _vel.Y = vertical * (sprint ? 16f : 8f);
        }

        // Física: solo cuando la columna de chunks bajo el jugador ya está cargada
        if (ColumnLoaded())
        {
            if (_needsUnstuck)
            {
                for (int guard = 0; guard < 64 && VoxelPhysics.Overlaps(_world, Blocks.Registry, _pos, BodySize); guard++)
                    _pos = _pos with { Y = _pos.Y + 1.0 };
                _needsUnstuck = false;
            }

            if (!_flying)
            {
                _vel.Y -= 28f * dt;
                if (_onGround && input.IsDown("Jump")) _vel.Y = 9f;
            }
            // Escalón automático: solo andando y apoyado en el suelo (en el aire o volando no se sube nada solo).
            double stepHeight = _autoStep && !_flying && _onGround ? 1.0 : 0.0;
            _pos = VoxelPhysics.MoveAndCollide(_world, Blocks.Registry, _pos, BodySize,
                new Vec3d(_vel.X * dt, _vel.Y * dt, _vel.Z * dt), out var flags, stepHeight);
            _onGround = (flags & CollisionFlags.Ground) != 0;
            if ((flags & CollisionFlags.Y) != 0) _vel.Y = 0;
        }

        // Mundo cilíndrico: X se envuelve; Z termina en los polos.
        _pos = _pos with { X = _scale.WrapX(_pos.X), Z = Math.Clamp(_pos.Z, 0.5, _scale.HeightBlocks - 0.5) };

        _camera.Position = _pos.Add(new Vector3(0, EyeHeight, 0));
        _camera.SetViewport(Game.GraphicsDevice.Viewport);

        using (_prof.Measure("chunks"))
            _chunks.Update(new CellCoord(IntMath.FloorToInt(_pos.X), IntMath.FloorToInt(_pos.Y), IntMath.FloorToInt(_pos.Z)));
        using (_prof.Measure("malla"))
            _renderer.Update(_camera.Position);
        using (_prof.Measure("lejano"))
            _far?.Update(_camera.Position);
        using (_prof.Measure("entidades"))
        {
            _entities.Update(dt, _pos);
            _npcs.Update(dt, _pos);
        }

        _target = VoxelRaycaster.Raycast(_world, Blocks.Registry, _camera.Position, _camera.Forward, Reach, out var hit) ? hit : null;

        // ¿Hay una entidad más cerca que el bloque apuntado? Manda la más cercana.
        double blockDistance = _target?.Distance ?? Reach;
        _entityTarget = _entities.Raycast(_camera.Position, _camera.Forward.ToVec3d(), Math.Min(Reach, blockDistance), out _);
        if (_entityTarget != null) _target = null;

        if (input.LeftClicked)
        {
            if (_entityTarget != null) Attack(_entityTarget);
            else if (_target is { } mined) Mine(mined.Cell);
        }

        if (_inspecting && (_inspectTimer -= dt) <= 0f)
        {
            _inspectTimer = 0.1f;
            _inspector.Text = _entityTarget != null ? Inspector.Describe(_entityTarget, _scale)
                : _target is { } looked ? Inspector.Describe(_world, Blocks.Registry, looked.Cell)
                : "Apunta a un bloque o a una entidad";
        }
        if (_target is { } t)
        {
            if (input.RightClicked)
            {
                ushort block = _hotbarBlocks[_selected];
                // Si apuntas a un sprite (hierba, flor...), el bloque nuevo lo sustituye; si no, se coloca contra la cara.
                var place = Blocks.Registry.Get(_world.GetCell(t.Cell)).Render != BlockRender.Cube ? t.Cell : t.Previous;
                if (!Blocks.Registry.Get(block).Solid || !IntersectsPlayer(place))
                    _world.SetCell(place, block);
            }
        }
    }

    public override void Draw(GameTime gameTime)
    {
        using (_prof.Measure("dibujo"))
            _renderer.Draw(_camera, () =>
            {
                _far?.Draw(_camera);   // tras los chunks: donde hay bloques, la prueba de profundidad oculta el terreno lejano
                _boxes.Draw(_camera, ActiveBoxes());
            });
        if (_target is { } t) _renderer.DrawOutline(_camera, t.Cell);
        UpdateDebug(gameTime);
        base.Draw(gameTime);   // UI encima
    }

    private string FarInfo() => _far == null ? "desactivado"
        : $"{_far.TilesDrawn} tiles dibujados, {_far.CachedTiles} en cache, {_far.PendingTiles} pendientes, {_far.AverageBuildMs:0.0} ms/tile";

    /// <summary>Un golpe quita 1 de vida. La entidad solo guarda su "hp" nueva (lo demás sigue en el prototipo); al llegar a 0 muere.</summary>
    private void Attack(Entity entity)
    {
        if (entity.Prototype == null) return;
        float hp = entity.GetFloat("hp") - 1f;
        if (hp > 0f) { entity.Set("hp", hp); return; }

        string name = entity.GetText("name");
        ShowMessage($"{entity.GetText("species")}{(name.Length > 0 ? " " + name : "")} ha muerto");
        _entities.Remove(entity);
    }

    /// <summary>
    /// Cada golpe resta 1 a la durabilidad del bloque (stone 3, log 2, el resto 1). Solo el bloque golpeado guarda su valor nuevo;
    /// los demás comparten el del prototipo. Los sprites y otros tipos sin propiedades se rompen de un golpe.
    /// </summary>
    private void Mine(CellCoord cell)
    {
        var block = new BlockRef(_world, Blocks.Registry, cell);
        if (block.TryGetFloat("durability", out float durability) && durability > 1f)
        {
            block.Set("durability", durability - 1f);
            return;
        }
        _world.SetCell(cell, Blocks.Air);   // sustituir el bloque borra también sus cambios de instancia
    }

    private IEnumerable<EntityBox> ActiveBoxes()
    {
        foreach (var e in _entities.Active)
        {
            var def = EntityTypes.Registry.Get(e.TypeId);
            yield return new EntityBox(e.Position, new Vector3(def.Width, def.Height, def.Width), new Color(def.R, def.G, def.B));
        }
    }

    private IEnumerable<MapMarker> EntityMarkers()
    {
        foreach (var e in _entities.All)
        {
            var def = EntityTypes.Registry.Get(e.TypeId);
            yield return new MapMarker((float)e.Position.X, (float)e.Position.Z, new Color(def.R, def.G, def.B), 5f);
        }
    }

    private void UpdateDebug(GameTime gameTime)
    {
        _frames++;
        _fpsTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer < 0.5f) return;
        _fps = _frames / _fpsTimer;
        _frames = 0; _fpsTimer = 0;
        if (!_debug.Visible || !_spawned) return;

        int wx = IntMath.FloorToInt(_pos.X), wz = IntMath.FloorToInt(_pos.Z);
        var (rx, rz) = _scale.RegionOf(wx, wz);
        var chunk = _scale.ChunkOf(wx, wz);
        string biome = Biomes.Registry.Get(_climate.Classify(_climate.Sample(wx, wz))).Name;
        _debug.Text = $"FPS {_fps:0} | Vuelo {(_flying ? "si" : "no")} | Escalon auto {(_autoStep ? "si" : "no")}\nXYZ {_pos.X:0.0} {_pos.Y:0.0} {_pos.Z:0.0}\n" +
                      $"Region ({rx}, {rz}) - {biome} | Chunk ({chunk.X}, {chunk.Z}) | Alto {_settings.WorldHeight}, mar {_climate.SeaLevel}\n" +
                      $"Chunks cargados {_world.LoadedChunkCount}, con malla {_renderer.MeshedChunks}, visibles {_renderer.VisibleChunks}\n" +
                      $"Mallado pendiente {_renderer.PendingMeshes} | Sprites cargados {_sprites.Count}, billboards {_renderer.BillboardsDrawn} | Mapa {(_map != null ? "listo" : "generando")}\n" +
                      $"Entidades {_entities.Count} (activas {_entities.ActiveCount}, aproximadas {_entities.ApproximateCount})\n" +
                      $"Instancias con cambios guardados: bloques {_world.CellOverrideCount}, entidades {_entities.CountWithOverrides()} " +
                      $"(el resto comparte los valores de su prototipo)\n" +
                      $"Regiones abiertas {_store.OpenRegions} (aciertos {_store.CacheHits}, fallos {_store.CacheMisses}) | chunks escritos {_store.ChunksWritten}\n" +
                      $"Generacion {_chunks.AverageGenerationMs:0.0} ms/chunk ({_chunks.RunningJobs} en curso, {_chunks.PendingCount} pedidos) | " +
                      $"Mallado {_renderer.AverageMeshMs:0.0} ms/chunk\n" +
                      $"Terreno lejano: {FarInfo()}\n" +
                      $"Tiempos del hilo principal (ms): {_prof.Report()}";
    }

    /// <summary>Muestra un aviso breve en pantalla (se reutiliza la etiqueta de estado del centro).</summary>
    private void ShowMessage(string text)
    {
        _status.Text = text;
        _status.Visible = true;
        _messageTimer = 2.5f;
    }

    private bool ColumnLoaded()
    {
        var cc = _world.Shape.ToChunk(new CellCoord(IntMath.FloorToInt(_pos.X), 0, IntMath.FloorToInt(_pos.Z)));
        for (int cy = LowestChunkY; cy < _settings.VerticalChunks; cy++)
            if (_world.GetChunk(new ChunkCoord(cc.X, cy, cc.Z)) == null) return false;
        return true;
    }

    private bool IntersectsPlayer(CellCoord c)
    {
        float hx = BodySize.X * 0.5f, hz = BodySize.Z * 0.5f;
        return _pos.X - hx < c.X + 1 && _pos.X + hx > c.X &&
               _pos.Y < c.Y + 1 && _pos.Y + BodySize.Y > c.Y &&
               _pos.Z - hz < c.Z + 1 && _pos.Z + hz > c.Z;
    }
}
