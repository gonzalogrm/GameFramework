using GF.Core;
using GF.Engine;
using GF.UI;
using GF.World;
using GF.World.Cosmos;
using GF.World.Entities;
using GF.World.History;
using GF.World.Items;
using GF.World.Events;
using GF.World.Map;
using GF.World.Voxel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace MiniCraft;

public sealed class PlayScene : UiScene
{
    private const int LowestChunkY = 0;   // la altura (nº de chunks en vertical) sale de WorldSettings.VerticalChunks
    private const float EyeHeight = 1.60f;
    private const float Reach = 6f;
    private const float AutosaveSeconds = 60f;

    private readonly int _seed;
    private readonly SaveData? _save;
    private readonly WorldSettings _settings;
    private readonly WorldScale _scale;
    private readonly ushort[] _hotbarBlocks =
          { Blocks.Grass, Blocks.Dirt, Blocks.Stone, Blocks.Sand, Blocks.Log, Blocks.Leaves,
            Blocks.Lamp, Blocks.LampWarm, Blocks.LampBlue, Blocks.GlassBlue };
    private readonly FirstPersonCamera _camera = new();
    private readonly FrameProfiler _prof = new();
    private readonly PlayerState _player = new();   // vida, inventario y equipamiento del jugador
    private readonly PlayerBody _body;               // posición, movimiento y colisión del jugador
    private PlayerActions _actions = null!;           // golpear, picar, hablar, bola de fuego
    private EnemySpawner _spawner = null!;
    private CityPlanner _cities = null!;
    private PlayerSave? _universalPlayer;
    private WorldHistory _history = null!;
    private LegendaryRegistry _legendary = null!;
    private float _thinkTimer;   // tick de los componentes de lo que lleva equipado el jugador
    private Label _hpLabel = null!;
    private string _hudText = "";
    private Vec3d _respawn;

    private World<ushort> _world = null!;
    private ChunkManager<ushort> _chunks = null!;
    private RegionChunkStore<ushort> _store = null!;
    private VoxelWorldRenderer _renderer = null!;
    private SpriteAtlas _sprites = null!;
    private FarHoleTracker _holes = null!;   // agujero central del terreno lejano
    private FarTerrainRenderer? _far;   // terreno lejano por niveles de detalle (null si está desactivado)
    private MiniCraftClimate _climate = null!;
    private WorldTerrain _terrain = null!;   // terreno: clima, columnas, ciudades y generador de chunks
    private ViewConfig _view = null!;        // distancias de visión e hilos
    private Task<World<MapCell>> _mapTask = null!;
    private World<MapCell>? _map;
    private EntityStore _entities = null!;
    private EventHub _events = null!;
    private NpcSystem _npcs = null!;
    private BoxRenderer _boxes = null!;
    private Hotbar _hotbar = null!;
    private Label _debug = null!, _status = null!, _inspector = null!;
    private Entity? _entityTarget;
    private bool _inspecting;
    private float _inspectTimer;
    private bool _loaded, _spawned, _entitiesInit;
    private float _messageTimer;
    private int _selected, _frames;
    private float _fpsTimer, _fps, _autosave;
    private VoxelHit? _target;
    private LightWatcher _lightWatcher = null!;
    private readonly DayNightCycle _day = new();
    private int _daySpeed;   // 0 normal, 1 rápido, 2 parado

    private FogSystem? _fog;
    private FogRenderer? _fogView;
    private FogWatcher? _fogWatcher;
    private static readonly EventKind FogEnter = new("entra_niebla"), FogLeave = new("sale_niebla");

    private GrenadeKit? _grenades;
    private GasZone? _gas;

    private readonly UniverseSession _session;
    private readonly Planet _planet;
    private readonly string _saveDir;   // carpeta de guardado de ESTE planeta

    /// <param name="seed">Semilla del terreno: la del planeta (PlanetAddress.WorldSeed).</param>
    public PlayScene(int seed, WorldSettings settings, SaveData? save, UniverseSession session, Planet planet, string saveDir)
    {
        _seed = seed;
        _settings = settings;
        _scale = settings.CreateScale();
        _body = new PlayerBody(_scale, settings.VerticalChunks);
        _save = save;
        _session = session;
        _planet = planet;
        _saveDir = saveDir;
    }

    private Vec3d Pos => _body.Pos;

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
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8),
            Padding = new Thickness(8),
            Background = new SolidBrush(new Color(0, 0, 0, 160)),
            Visible = false,
        };
        root.Widgets.Add(_inspector);
        _status = new Label { Text = "Generando mundo...", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 60, 0, 0) };
        root.Widgets.Add(_status);
        _hpLabel = new Label { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 0, 12) };
        root.Widgets.Add(_hpLabel);
        return root;
    }

    public override void Load()
    {
        SaveSystem.UseDirectory(_saveDir);   // el almacén de chunks y los guardados de este planeta
        RestoreSavedState();

        _terrain = new WorldTerrain(_seed, _settings, _scale);
        _climate = _terrain.Climate;
        _cities = _terrain.Cities;
        _view = ViewConfig.From(_settings);

        CreateChunkSystems();
        CreateRenderers();
        CreateEntities();
        CreateHazards();
        RestoreEntities();

        // El mapamundi se calcula en segundo plano (promedia unas 360.000 columnas).
        var scale = _scale; var climate = _climate; int samples = _settings.MapSamplesPerAxis;
        _mapTask = Task.Run(() => WorldMapBuilder.Build(scale, climate, s => climate.Classify(s), Biomes.Registry.Count, samples));

        _day.Update(0);
        ApplyDayNight();

        base.Load();   // construye la UI
        _loaded = true;
        Game.Input.SetMouseCaptured(true);
    }

    /// <summary>
    /// Catálogo de objetos de ESTE mundo: los del JSON y, además, los creados en partida que se guardaron (antes que entidades e inventarios, que los
    /// referencian por nombre). El personaje es UNIVERSAL: su inventario, equipo y vida vienen del universo, no de este planeta; los objetos de runtime que
    /// lleva (de otros mundos) se recrean aquí junto a los de este mundo.
    /// </summary>
    private void RestoreSavedState()
    {
        _universalPlayer = SaveSystem.TryLoadPlayer();
        var worldItems = SaveSystem.TryLoadItems() ?? new List<RuntimeItemRecord>();
        if (_universalPlayer?.Items != null) worldItems = worldItems.Concat(_universalPlayer.Items).ToList();
        foreach (var problem in ItemTypes.BeginWorld(worldItems)) Console.Error.WriteLine("[items] " + problem);
        if (_save == null) return;

        _body.Pos = new Vec3d(_save.X, _save.Y, _save.Z);
        _camera.Yaw = _save.Yaw;
        _camera.Pitch = _save.Pitch;
        _selected = Math.Clamp(_save.Selected, 0, _hotbarBlocks.Length - 1);
        _day.Time = _save.TimeOfDay;
    }

    /// <summary>El mundo de chunks, su almacén en disco y el gestor que los genera y descarga alrededor del jugador.</summary>
    private void CreateChunkSystems()
    {
        _world = new World<ushort>(new ChunkShape(_view.ChunkSize, _view.ChunkSize, _view.ChunkSize));
        // Guardado por regiones (el chunk -1 y el N-1 son el mismo lugar: comparten dato). Si el mundo se guardó con el
        // formato anterior (un archivo por chunk), esos chunks se siguen leyendo y pasan a regiones cuando se vuelvan a guardar.
        Func<ChunkCoord, ChunkCoord> canonical = c => c with { X = _scale.WrapChunkX(c.X) };
        _store = new RegionChunkStore<ushort>(SaveSystem.RegionsDir, regionChunksX: 16, regionChunksY: 32, regionChunksZ: 16,
            maxOpenRegions: 16, canonicalize: canonical,
            fallback: new FileChunkStore<ushort>(SaveSystem.ChunksDir, canonical));
        _chunks = new ChunkManager<ushort>(_world, _terrain.Generator)
        {
            RadiusX = _view.View,
            RadiusY = _view.Layers,
            RadiusZ = _view.View,
            MaxConcurrentJobs = _view.GenThreads,
            MinChunkY = LowestChunkY,
            MaxChunkY = _view.Layers - 1,
            MinChunkZ = 0,
            MaxChunkZ = _scale.ChunkCountZ - 1,   // los polos
            Store = _store,
        };
    }

    /// <summary>Todo lo que se dibuja: sprites, shader y malla de chunks, niebla, terreno lejano y entidades.</summary>
    private void CreateRenderers()
    {
        // Sprites 2D: cada textura de Content/sprites/ (compilada por MGCB) es un sprite, de cualquier tamaño. Los .png sueltos de una
        // carpeta sprites/ junto al ejecutable los sustituyen o amplían sin recompilar el contenido. Un ContentManager propio,
        // porque las texturas sueltas solo hacen falta para empaquetar el atlas.
        List<string> spriteMessages;
        using (var spriteContent = new ContentManager(Game.Services, Game.Content.RootDirectory))
            _sprites = SpriteLoader.LoadContent(Game.GraphicsDevice, spriteContent, "sprites",
                Path.Combine(AppContext.BaseDirectory, "sprites"), out spriteMessages);
        foreach (var message in spriteMessages) Console.Error.WriteLine("[sprites] " + message);

        // Shader de los chunks (Content/Effects/ChunkFade.fx, compilado por MGCB): aplica la luz de cielo y de bloques (ciclo día/noche) y
        // el desvanecimiento. Sin él el juego arranca, pero sin ciclo día/noche ni luz de bloques/color en los vértices.
        Effect? chunkFade = null;
        try { chunkFade = Game.Content.Load<Effect>("Effects/ChunkFade"); }
        catch (ContentLoadException e)
        {
            Console.Error.WriteLine("[contenido] sin el efecto 'Effects/ChunkFade' (" + e.Message + "): sin ciclo dia/noche ni fundido");
            _view = _view.WithoutFade(_settings);
        }
        _renderer = new VoxelWorldRenderer(Game.GraphicsDevice, _world, Blocks.Registry, AtlasFactory.Load(Game.Content, Game.GraphicsDevice), _sprites, chunkFade)
        {
            MaxMeshingJobs = _view.MeshThreads,
            FadeStart = _view.Fade.Start,
            FadeEnd = _view.Fade.End,
            FadeInSeconds = _view.Fade.Enabled ? _settings.FadeInSeconds : 0,
            CanMesh = _chunks.AreNeighborsSettled,   // no mallar hasta que los vecinos estén cargados: cada chunk se malla una vez, no diez
            FogColor = MiniCraftGame.SkyColor,
            FogStart = _view.FogStart,
            FogEnd = _view.FogEnd,
        };
        _holes = new FarHoleTracker(_renderer, _world, _scale, _settings.VerticalChunks, _view.FarClose);

        if (_settings.Fog.Enabled)
            _fog = new FogSystem(_settings.Fog.ToOptions(), new PerlinNoise(_seed ^ 0xF06),
                FogGround.For(_world, Blocks.Registry, 0, _settings.WorldHeight - 1));
        if (_fog != null) _fog.LightQuery = FogLight.For(_renderer.Light);
        if (_fog != null) _world.CellChanged += (cell, _, _) => { _fog?.Invalidate(cell.X, cell.Z); _gas?.Invalidate(cell.X, cell.Z); };
        if (_fog != null) _fogView = new FogRenderer(Game.GraphicsDevice);

        EnemyAi.Visibility = (from, to) => (_fog?.Sight(from, to) ?? 1f) * (_gas?.Sight(from, to) ?? 1f) * (_grenades?.Sight(from, to) ?? 1f);

        _camera.FarPlane = Math.Max(500f, _view.FogRange * 1.2f);
        if (_view.FarEnabled) _camera.NearPlane = 0.15f;   // más precisión de profundidad a distancia

        // Terreno LEJANO: relieve aproximado que llega mucho más lejos que los chunks y gana detalle al acercarte. Dentro de la
        // distancia de chunks lo sustituyen los bloques (con colinas finas, árboles, sprites y entidades).
        if (_view.FarEnabled)
        {
            _far = new FarTerrainRenderer(Game.GraphicsDevice, new MiniCraftFarSource(_climate, _scale, _cities), _scale.HeightBlocks)
            {
                FarDistance = _view.FarBlocks,
                SplitFactor = _settings.FarDetail,
                FogColor = MiniCraftGame.SkyColor,
                FogStart = _view.FogStart,
                FogEnd = _view.FogEnd,
            };
        }

        _boxes = new BoxRenderer(Game.GraphicsDevice, _scale)
        {
            FogColor = MiniCraftGame.SkyColor,
            FogStart = _view.FogStart,
            FogEnd = _view.FogEnd,
        };
    }

    /// <summary>Entidades y eventos, la historia del mundo con sus objetos legendarios, y el personaje.</summary>
    private void CreateEntities()
    {
        var systems = new EntitySystems(_world, _terrain, _settings, _scale, _seed, _player, () => _map, () => _spawned ? (Vec3d?)Pos : null);
        _entities = systems.Entities;
        _events = systems.Events;
        _npcs = systems.Npcs;
        _spawner = systems.Enemies;
        _events.Message += ShowMessage;
        _events.Delivered += OnDelivered;

        // Historia del mundo (sale de la semilla, no se guarda) y sus objetos legendarios: variantes de armas creadas en partida, con su héroe y su destino.
        _history = WorldHistory.Generate((ulong)(uint)_seed);
        _legendary = LegendaryRegistry.Create(_history, (ulong)(uint)_seed, ItemTypes.Catalog, _scale,
            (x, z) => MiniCraftClimate.ToBlockHeight(_climate.Sample(x, z).Height) > _climate.SeaLevel + _settings.BeachHeight,
            new LegendaryOptions { Components = new[] { "thorns", "regeneration" }, NamePrefix = $"legendary_{(uint)_seed:x8}" });
        _spawner.Legendary = _legendary;
        foreach (var a in _legendary.Artifacts)
            Console.WriteLine($"[historia] {a.Title}: {a.Fate}, columna ({a.ColumnX},{a.ColumnZ}), x {a.X} z {a.Z}, ataque {a.Attack:0.#}");

        // Primero el personaje del universo; si no hay (partidas anteriores), el que guardó este mundo; si no, el inicial.
        if (_universalPlayer != null) _player.Restore(_universalPlayer);
        else if (_save?.Player != null) _player.Restore(_save.Player);
        else _player.GiveStarterKit();
    }

    /// <summary>Luz, niebla y gas como disparadores de eventos, granadas y acciones del jugador.</summary>
    private void CreateHazards()
    {
        _lightWatcher = new LightWatcher(_events, _renderer.Light, _scale, GameEvents.DarkEnter, GameEvents.LightEnter);
        _gas = GasZone.Create(_settings.Gas, _seed, _world, _renderer.Light, _events, _scale, _player, Game.GraphicsDevice, _settings.WorldHeight);
        if (_fog != null) _fogWatcher = new FogWatcher(_events, _fog, _scale, FogEnter, FogLeave);

        _grenades = GrenadeKit.Create(_settings.Smoke, _settings.ToxicGrenade, _settings.Spores, _world,
            Blocks.Registry, _events, _scale, _player, Game.GraphicsDevice);
        _actions = new PlayerActions(_events, _world, _scale, _player, _grenades, ShowMessage);
    }

    /// <summary>Si se continúa una partida, las entidades guardadas (se activarán al cargarse sus chunks).</summary>
    private void RestoreEntities()
    {
        if (_save == null || SaveSystem.TryLoadEntities() is not { } saved) return;
        _entities.Import(saved.Entities, EntityTypes.Registry, saved.Time);
        _spawner.ImportPopulated(saved.Columns);
        _spawner.CountExisting();
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
        _fogView?.Dispose();
        _gas?.Dispose();
        _grenades?.Dispose();
        EnemyAi.Visibility = null;
    }

    private void Save()
    {
        SaveSystem.UseDirectory(_saveDir);   // el mundo guardado es el de este planeta, aunque ya se haya abierto otro
        if (!_spawned) return;   // no guardar una posición provisional
        _chunks.SaveAll();
        _store.Flush();
        SaveSystem.Write(new SaveData
        {
            Seed = _seed,
            Settings = _settings,
            X = Pos.X,
            Y = Pos.Y,
            Z = Pos.Z,
            Yaw = _camera.Yaw,
            Pitch = _camera.Pitch,
            Selected = _selected,
            TimeOfDay = _day.Time,
        });
        SaveSystem.WriteUniverse(_session.ToSave());
        SaveSystem.WriteItems(ItemTypes.Catalog.ExportRuntime());
        SaveSystem.WritePlayer(_player.Export(ItemTypes.Catalog));   // universal: lo lee el siguiente mundo que se abra
        SaveSystem.WriteEntities(new EntitySave { Time = _entities.Time, Entities = _entities.Export(EntityTypes.Registry), Columns = _spawner.ExportPopulated() });
    }

    // ------------------------------------------------------------------ viaje

    /// <summary>Lleva al jugador a una columna del mundo. La altura se obtiene del modelo, sin esperar a los chunks.</summary>
    public void TeleportTo(int wx, int wz)
    {
        wx = _scale.WrapX(wx);
        wz = Math.Clamp(wz, 0, _scale.HeightBlocks - 1);
        int ground = MiniCraftClimate.ToBlockHeight(_climate.Sample(wx, wz).Height);
        _body.Teleport(new Vec3d(wx + 0.5, Math.Max(ground, _climate.SeaLevel) + 2.0, wz + 0.5));
    }

    private bool TrySpawn()
    {
        if (_save != null) { FinishSpawn(); return true; }
        if (_map == null && !_mapTask.IsFaulted) return false;   // esperando al mapa

        if (_map != null && SpawnFinder.TryFindLandRegion(_map, _scale, out int wx, out int wz)) TeleportTo(wx, wz);
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
        _respawn = Pos;   // donde se reaparece al morir
        _status.Visible = false;
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
        HandleToggles(input);
        SpawnInitialEntities();
        if (OpenOverlayScene(input)) return;

        _autosave += dt;
        if (_autosave >= AutosaveSeconds) { Save(); _autosave = 0; }

        MovePlayer(input, dt);
        UpdateWorld(input, gameTime, dt);
        UpdateInteraction(input, dt);
    }

    /// <summary>Interruptores: vuelo, escalón automático e inspector.</summary>
    private void HandleToggles(InputService input)
    {
        if (input.Pressed("Fly"))
        {
            _body.Flying = !_body.Flying;
            _body.Vel.Y = 0;
            ShowMessage(_body.Flying ? "Vuelo activado (Espacio sube, Ctrl o C baja, F para salir)" : "Vuelo desactivado");
        }
        if (input.Pressed("AutoStep"))
        {
            _body.AutoStep = !_body.AutoStep;
            ShowMessage(_body.AutoStep ? "Escalon automatico activado" : "Escalon automatico desactivado");
        }
        if (input.Pressed("Inspect"))
        {
            _inspecting = !_inspecting;
            _inspector.Visible = _inspecting;
            _inspectTimer = 0;
        }
    }

    /// <summary>Mundo nuevo (o guardado sin entidades): aldeanos, perros y caravanas iniciales, cuando el mapa ya está listo.</summary>
    private void SpawnInitialEntities()
    {
        if (_entitiesInit || _map == null) return;
        if (_entities.Count == 0) _npcs.SpawnInitial(Pos);
        _entitiesInit = true;
    }

    /// <summary>Teclas que abren otra escena (universo, historia, inventario, mapa). true = se abrió una: este frame no sigue.</summary>
    private bool OpenOverlayScene(InputService input)
    {
        if (input.Pressed("Universe"))
        {
            Save();
            Game.Scenes.Push(new UniverseScene(_session, _planet.Address, planet =>
            {
                Game.Scenes.Reset(_session.CreateScene(planet));   // el mundo actual se guarda al descargarse (en su propia carpeta)
            }));
            return true;
        }

        if (input.Pressed("History"))
        {
            Game.Scenes.Push(new HistoryScene(_history, _legendary, _entities, _player, _spawner, TeleportTo));
            return true;
        }

        if (input.Pressed("Inventory"))
        {
            Game.Scenes.Push(new InventoryScene(_player));
            return true;
        }

        if (input.Pressed("Map") && _map != null)
        {
            Game.Scenes.Push(new MapScene(_map, _scale, () => new Vector2((float)Pos.X, (float)Pos.Z), TeleportTo,
                () => MapMarkers.Build(_entities, _cities, _scale, _seed), _settings.WorldHeight));
            return true;
        }
        return false;
    }

    /// <summary>Cámara, barra de bloques, vida y movimiento del jugador.</summary>
    private void MovePlayer(InputService input, float dt)
    {
        _camera.Rotate(input.MouseDelta.X, input.MouseDelta.Y);

        for (int i = 0; i < _hotbarBlocks.Length; i++)
            if (input.KeyPressed(Keys.D1 + i)) _selected = i;
        if (input.ScrollDelta != 0)
            _selected = (_selected - Math.Sign(input.ScrollDelta) + _hotbarBlocks.Length) % _hotbarBlocks.Length;
        _hotbar.Selected = _selected;
        UpdatePlayerState(dt);

        _body.Update(input, dt, _camera.Yaw, _world, _entities);

        _camera.Position = Pos.Add(new Vector3(0, EyeHeight, 0));
        _camera.SetViewport(Game.GraphicsDevice.Viewport);
    }

    /// <summary>El mundo alrededor: chunks, malla, día y noche, niebla, gas, granadas, terreno lejano y entidades.</summary>
    private void UpdateWorld(InputService input, GameTime gameTime, float dt)
    {
        using (_prof.Measure("chunks"))
            _chunks.Update(new CellCoord(IntMath.FloorToInt(Pos.X), IntMath.FloorToInt(Pos.Y), IntMath.FloorToInt(Pos.Z)));
        _renderer.Time = gameTime.TotalGameTime.TotalSeconds;   // fundido de entrada de los chunks nuevos
        _day.Update(dt);
        ApplyDayNight();
        using (_prof.Measure("malla"))
            _renderer.Update(_camera.Position);
        _fog?.Update(dt, _camera.Position, gameTime.TotalGameTime.TotalSeconds);
        _gas?.Update(dt, _camera.Position, gameTime.TotalGameTime.TotalSeconds, Pos);

        _grenades?.Update(dt, Pos);
        if (_grenades != null)
        {
            if (input.Pressed("GrenadeType")) ShowMessage("Granada: " + _grenades.CycleType());
            if (input.Pressed("Grenade"))
            {
                var no = _grenades.Throw(_camera.Position, _camera.Forward);
                if (no != null) ShowMessage(no);
            }
        }

        using (_prof.Measure("lejano"))
        {
            if (_far != null)
            {
                _far.Hole = _holes.Compute(Pos);
                _far.Update(_camera.Position);
            }
        }
        using (_prof.Measure("entidades"))
        {
            _entities.Update(dt, Pos);
            _npcs.Update(dt, Pos);
            _lightWatcher.Update(dt, Pos);
            _fogWatcher?.Update(dt, Pos);
        }
    }

    /// <summary>Lo que el jugador apunta y lo que hace con ello: golpear, picar, inspeccionar, hablar, bola de fuego y colocar bloques.</summary>
    private void UpdateInteraction(InputService input, float dt)
    {
        _target = VoxelRaycaster.Raycast(_world, Blocks.Registry, _camera.Position, _camera.Forward, Reach, out var hit) ? hit : null;

        // ¿Hay una entidad más cerca que el bloque apuntado? Manda la más cercana.
        double blockDistance = _target?.Distance ?? Reach;
        _entityTarget = _entities.Raycast(_camera.Position, _camera.Forward.ToVec3d(), Math.Min(Reach, blockDistance), out _);
        if (_entityTarget != null) _target = null;

        if (input.LeftClicked)
        {
            if (_entityTarget != null) _actions.Attack(_entityTarget);
            else if (_target is { } mined) _actions.Mine(mined.Cell);
        }

        if (_inspecting && (_inspectTimer -= dt) <= 0f)
        {
            _inspectTimer = 0.1f;
            _inspector.Text = _entityTarget != null ? Inspector.Describe(_entityTarget, _scale, _events)
                : _target is { } looked ? Inspector.Describe(_world, Blocks.Registry, looked.Cell, _events)
                : "Apunta a un bloque o a una entidad";
        }

        if (input.Pressed("Talk")) _actions.Talk(_entityTarget, _target);
        if (input.Pressed("Night")) CycleDaySpeed();
        if (input.Pressed("Fireball")) _actions.CastFireball(_entityTarget, _target, Pos);
        if (_target is { } t && input.RightClicked) PlaceBlock(t);
    }

    /// <summary>Velocidad del ciclo día/noche: normal, rápido, parado.</summary>
    private void CycleDaySpeed()
    {
        _daySpeed = (_daySpeed + 1) % 3;
        _day.Running = _daySpeed != 2;
        _day.DayLengthSeconds = _daySpeed == 1 ? 30 : 600;
        ShowMessage($"Ciclo dia/noche: {(new[] { "normal", "rapido", "parado" })[_daySpeed]} ({_day.Clock})");
    }

    /// <summary>Coloca el bloque seleccionado. Si apuntas a un sprite (hierba, flor...), el nuevo lo sustituye; si no, se pone contra la cara.</summary>
    private void PlaceBlock(VoxelHit t)
    {
        ushort block = _hotbarBlocks[_selected];
        var place = Blocks.Registry.Get(_world.GetCell(t.Cell)).Render != BlockRender.Cube ? t.Cell : t.Previous;
        if (!Blocks.Registry.Get(block).Solid || !_body.Intersects(place))
            _world.SetCell(place, block);
    }

    private string LightText()
    {
        var l = _renderer.Light.Sample(new CellCoord(IntMath.FloorToInt(Pos.X), IntMath.FloorToInt(Pos.Y + 0.5), IntMath.FloorToInt(Pos.Z)));
        return l.Known ? $"nivel {l.Level:0.0} | cielo {l.Sky.Y:0} | bloque RGB {l.Block.X:0},{l.Block.Y:0},{l.Block.Z:0}" : "desconocida";
    }

    public override void Draw(GameTime gameTime)
    {
        using (_prof.Measure("dibujo"))
            _renderer.Draw(_camera, () =>
            {
                _far?.Draw(_camera);   // tras los chunks: donde hay bloques, la prueba de profundidad oculta el terreno lejano
                _boxes.Draw(_camera, EntityBoxes.Build(_entities, _scale, Pos, _renderer, _world, _day.SkyLight));
            });
        if (_fog != null && _fogView != null)
        {
            _fogView.Tint = Vector3.Max(_day.SkyLight, _day.Ambient);   // luz del dia
            _fogView.Draw(_camera, _fog);
            _gas?.Draw(_camera);
            _grenades?.Draw(_camera, Vector3.Max(_day.SkyLight, _day.Ambient));
        }
        if (_target is { } t) _renderer.DrawOutline(_camera, t.Cell);
        UpdateDebug(gameTime);
        base.Draw(gameTime);   // UI encima
    }

    /// <summary>Solo las conversaciones ignoradas se cuentan al jugador: el resto lo narran los manejadores.</summary>
    private void OnDelivered(Delivery delivery)
    {
        if (delivery.Result == DeliveryResult.Ignored && delivery.Event.Kind.Is(GameEvents.Conversation))
            ShowMessage($"{delivery.Target.Label} recibe '{delivery.Event.Kind.Name}' pero no lo procesa");
    }

    private string FarInfo() => _far == null ? "desactivado"
        : $"{_far.TilesDrawn} tiles dibujados, {_far.CachedTiles} en cache, {_far.PendingTiles} pendientes, {_far.AverageBuildMs:0.0} ms/tile | " +
          $"anillo desde {_holes.Radius} chunks hasta {_settings.FarDistanceChunks} | " +
          $"fundido {(_renderer.FadeActive ? "si" : "no")}, {_renderer.FadingChunks} chunks apareciendo";

    /// <summary>Regeneración de vida, muerte y reaparición, y el texto de vida de la pantalla.</summary>
    private void UpdatePlayerState(float dt)
    {
        _player.SecondsSinceHurt += dt;
        if ((_thinkTimer += dt) >= 1f)   // componentes de lo que lleva equipado (regeneración...)
        {
            ItemBehaviors.Think(_player, _thinkTimer, _events);
            _thinkTimer = 0f;
        }
        if (_player.GraceSeconds > 0f) _player.GraceSeconds -= dt;
        if (_player.Hp > 0f && _player.Hp < _player.MaxHp && _player.SecondsSinceHurt > 6f)
            _player.Hp = MathF.Min(_player.MaxHp, _player.Hp + 0.4f * dt);
        if (_player.Hp <= 0f) Respawn();

        string text = $"{_planet.Name}  |  HP {MathF.Ceiling(_player.Hp):0}/{_player.MaxHp:0}   Ataque {_player.AttackPower:0.#}   Defensa {Combat.Defense(_player):0.#}   [E] inventario  [U] universo  [H] historia";
        if (text != _hudText) { _hudText = text; _hpLabel.Text = text; }
    }

    private void Respawn()
    {
        _player.Hp = _player.MaxHp;
        _player.GraceSeconds = 4f;
        TeleportTo((int)Math.Floor(_respawn.X), (int)Math.Floor(_respawn.Z));
        ShowMessage("Has muerto: reapareces en el punto de inicio");
    }

    /// <summary>Pasa la hora del día al renderer, al terreno lejano, a la niebla y al fondo. No remalla nada.</summary>
    private void ApplyDayNight()
    {
        var sky = _day.Horizon;
        MiniCraftGame.SkyColor = sky;   // fondo de la pantalla
        _renderer.FogColor = sky;
        _renderer.SkyLight = _day.SkyLight;
        _renderer.AmbientLight = _day.Ambient;
        _renderer.Light.SkyColor = _day.SkyLight;     // lo que MIDE la luz (y los eventos de oscuridad) también sigue a la hora
        _renderer.Light.Ambient = _day.Ambient;
        if (_far != null) { _far.FogColor = sky; _far.Lighting = Vector3.Max(_day.SkyLight, _day.Ambient); }
        _boxes.FogColor = sky;
    }

    private void UpdateDebug(GameTime gameTime)
    {
        _frames++;
        _fpsTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer < 0.5f) return;
        _fps = _frames / _fpsTimer;
        _frames = 0; _fpsTimer = 0;
        if (!_debug.Visible || !_spawned) return;

        int wx = IntMath.FloorToInt(Pos.X), wz = IntMath.FloorToInt(Pos.Z);
        var (rx, rz) = _scale.RegionOf(wx, wz);
        var chunk = _scale.ChunkOf(wx, wz);
        string biome = Biomes.Registry.Get(_climate.Classify(_climate.Sample(wx, wz))).Name;
        _debug.Text = $"FPS {_fps:0} | Vuelo {(_body.Flying ? "si" : "no")} | Escalon auto {(_body.AutoStep ? "si" : "no")}\nXYZ {Pos.X:0.0} {Pos.Y:0.0} {Pos.Z:0.0}\n" +
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
                      $"Tiempos del hilo principal (ms): {_prof.Report()}\n" +
                      $"{CityText()}\n" +
                      $"{_fog?.DebugText ?? "Niebla: desactivada"}\n" +
                      $"{_fogView?.Stats}\n" +
                      $"{_gas?.DebugText}\n" +
                      $"{_grenades?.DebugText}\n" +
                      $"Luz aqui: {LightText()}\n";
    }

    /// <summary>Muestra un aviso breve en pantalla (se reutiliza la etiqueta de estado del centro).</summary>
    private void ShowMessage(string text)
    {
        _status.Text = text;
        _status.Visible = true;
        _messageTimer = 2.5f;
    }

    private string CityText()
    {
        var c = _cities.CityAt(IntMath.FloorToInt(Pos.X), IntMath.FloorToInt(Pos.Z));
        return c == null ? "Ciudad: ninguna aqui"
            : $"Ciudad: centro ({c.CenterX}, {c.CenterZ}) radio {c.Radius} suelo {c.GroundY} | conocidas {_cities.Known.Count()}";
    }
}
