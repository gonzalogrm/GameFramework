using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GF.World.Voxel;

/// <summary>
/// Vértice de la malla de chunks (32 bytes). La luz va SEPARADA en dos capas para que el ciclo día/noche no obligue a remallar:
///   Color.RGB  brillo de la luz de CIELO (0..1 por canal, ya con la curva de niveles; el vidrio de color la tiñe). Color.A = alfa (agua).
///   Data.xy    coordenadas de textura.
///   Data.z     luz de BLOQUES emisores R y G (0..255), empaquetadas en un float: R*256 + G (16 bits: exacto en cualquier GPU).
///   Data.w     luz de BLOQUES emisores B y sombreado de cara x oclusión (0..255): B*256 + sombreado.
/// Solo usa las semánticas POSITION0, COLOR0 y TEXCOORD0 (las de VertexPositionColorTexture), que funcionan en cualquier plataforma.
/// El vertex shader (ChunkFade.fx) desempaqueta la luz de bloques y el pixel shader calcula
///   color = textura * max(cielo * SkyLight, bloques, AmbientLight) * sombreado     cada frame.
/// </summary>
public struct VoxelVertex : IVertexType
{
    public Vector3 Position;
    public Color Color;
    public Vector4 Data;

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(16, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0));

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    /// <param name="sky">RGB = luz de cielo, A = alfa.</param>
    /// <param name="light">RGB = luz de bloques, A = sombreado de cara x oclusión.</param>
    public VoxelVertex(Vector3 position, Color sky, Vector2 uv, Color light)
    {
        Position = position;
        Color = sky;
        Data = new Vector4(uv.X, uv.Y, light.R * 256f + light.G, light.B * 256f + light.A);
    }
}
