# GameFramework

Requisitos: .NET 8 SDK.

    dotnet restore
    dotnet test
    dotnet run --project samples/MiniCraft     # voxel 3D
    dotnet run --project samples/RogueDemo     # roguelike 2D

MiniCraft: WASD mover, Espacio saltar, Shift correr, ratón mirar, clic izq. romper,
clic der. colocar, 1-5 / rueda elegir bloque, Esc pausa, F3 info de depuración.

RogueDemo: WASD/flechas/QEZC mover (8 direcciones), Espacio esperar, mover contra un goblin = atacar,
R mapa nuevo, Esc salir.

Las versiones de MonoGame y Myra son flotantes (Directory.Build.props): fija las exactas tras el primer restore.

## Qué se comparte entre los dos demos
IWorld/World/Chunk, WorldGenerator (pipeline por etapas), ChunkManager, Camera, SceneManager, InputService,
GF.UI (Myra). Lo específico de 3D está en GF.World.Voxel y lo de 2D en GF.World.Tiles (renderer) y
GF.World (CellularAutomataCaveStage, Fov, AStar, que no dependen de MonoGame).

## Pendiente / ideas
Greedy meshing, luz por bloque (antorchas, cuevas oscuras), biomas, cuevas 3D, salas/BSP para roguelike, inventario.

## Modelo de hilos
- World y GraphicsDevice se usan SOLO desde el hilo principal.
- Generación: ChunkManager genera en el ThreadPool y entrega los chunks al hilo principal.
- Mallado: VoxelWorldRenderer captura un ChunkSnapshot en el hilo principal, malla en el ThreadPool y sube
  los buffers a GPU en el hilo principal. Los chunks editados se mallan de forma síncrona.
- Todo lo que se ejecute en hilos de fondo (etapas de generación, ChunkMesher, BlockRegistry) debe ser stateless o de solo lectura.

## MiniCraft: características
- Terreno por heightmap con fBm, playas y agua translúcida (segunda pasada con mezcla alfa, ordenada de lejos a cerca).
- Árboles deterministas que cruzan bordes de chunk (TreeStage: la posición depende solo de semilla+columna).
- Ambient occlusion por vértice con volteo de diagonal; el snapshot incluye los 26 vecinos para que sea exacto en los bordes.
- Guardado: %AppData%/MiniCraft/world (world.json + un archivo comprimido por chunk editado). Autoguardado cada 60 s,
  al volver al menú y al salir. Los ids de bloque se guardan por orden de registro: añade bloques nuevos siempre al final.

## Mundo grande y mapamundi (MiniCraft)
- Escala (MiniCraftWorld.Scale): 200 x 50 regiones de 32 x 32 chunks = 102.400 x 25.600 bloques.
- MiniCraftClimate es la fuente única de verdad: genera la altura/temperatura/humedad que usan los chunks
  (TerrainStage, TreeStage) y que WorldMapBuilder promedia para el mapamundi (6 x 6 muestras por región;
  elevación media, relieve, agua y bioma dominante).
- El mundo es cilíndrico: el ruido es periódico en X (Fbm2Periodic), la posición del jugador se envuelve y los
  chunks -1 y N-1 comparten archivo de guardado. En Z hay polos (ChunkManager.MinChunkZ/MaxChunkZ).
- M abre el mapa: rueda = zoom (hasta ver la rejilla de chunks), botón derecho o WASD = mover, clic = viajar al bloque
  exacto bajo el cursor. La altura de destino sale del modelo, no hace falta esperar a los chunks.
- Al empezar un mundo nuevo se calcula el mapa en segundo plano y se elige una región templada de tierra para aparecer.

## Limitaciones conocidas
- Precisión de float: lejos del origen (x ~ 100.000) la cámara y los vértices tienen resolución de ~1/128 de bloque.
  Pendiente: renderizado relativo a la cámara (origen flotante) y posición de cámara en double.
- Las ediciones se comparten entre x=-1 y x=N-1 al guardar, pero no se reflejan en vivo si ambos lados estuvieran cargados.
- Un archivo por chunk editado; para mundos con millones de ediciones habrá que pasar a archivos por región.
