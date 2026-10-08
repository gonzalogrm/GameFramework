// ChunkFade.fx — efecto de los chunks: textura del atlas + color de vértice + niebla + DESVANECIMIENTO CON TRAMADO.
//
// Los chunks son opacos (escriben profundidad, sin ordenar), así que el desvanecimiento no puede ser una mezcla alfa. En su lugar cada
// píxel se DESCARTA o no según un patrón de ruido ("screen-door transparency"): con fade = 0.3 sobreviven ~30 % de los píxeles. Los píxeles
// descartados dejan ver lo que hay detrás: el terreno lejano (LOD), que se dibuja después.
//
//   fade = (1 - desvanecimiento por distancia) * fundido de aparición del chunk
//     - distancia: 1 cerca, 0 en FadeEnd (distancia horizontal "de cuadrado" desde la cámara, que coincide con el agujero cuadrado del LOD)
//     - aparición: ChunkFade sube de 0 a 1 tras construirse la malla del chunk (lo fija el código, por chunk)
//
// Las posiciones llegan RELATIVAS A LA CÁMARA (origen flotante): World es una traslación pequeña y precisa.

#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 World;
float4x4 ViewProjection;

Texture2D AtlasTexture;
sampler2D AtlasSampler = sampler_state
{
	Texture = <AtlasTexture>;
	MagFilter = Point;
	MinFilter = Point;
	MipFilter = Point;
	AddressU = Clamp;
	AddressV = Clamp;
};

float AlphaCutoff;        // 0 para bloques y agua; 0.5 para sprites (los píxeles con menos alfa se recortan)
float ChunkFade;          // 0..1: aparición de este chunk
float FadeStart;          // distancia (bloques) donde empiezan a desvanecerse
float FadeEnd;            // distancia donde han desaparecido del todo
float3 FogColor;
float FogStart;
float FogEnd;
float2 ViewportSize;      // tamaño de la pantalla en píxeles (para el patrón de tramado)

struct VSInput
{
	float4 Position : POSITION0;
	float4 Color : COLOR0;
	float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 TexCoord : TEXCOORD0;
	float4 Clip : TEXCOORD1;       // posición en espacio de recorte (para obtener el píxel y la profundidad en el PS)
	float Distance : TEXCOORD2;    // distancia horizontal de cuadrado a la cámara
};

VSOutput MainVS(in VSInput input)
{
	VSOutput output;
	float4 worldPos = mul(input.Position, World);            // relativa a la cámara
	output.Position = mul(worldPos, ViewProjection);
	output.Color = input.Color;
	output.TexCoord = input.TexCoord;
	output.Clip = output.Position;
	output.Distance = max(abs(worldPos.x), abs(worldPos.z));
	return output;
}

// Ruido de gradiente entrelazado (Jimenez): un valor [0,1) por píxel, bien repartido; sin matrices ni texturas.
float Dither(float2 pixel)
{
	return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float4 MainPS(VSOutput input) : COLOR0
{
	float4 tex = tex2D(AtlasSampler, input.TexCoord);
	clip(tex.a - AlphaCutoff);

	float fade = (1.0 - smoothstep(FadeStart, FadeEnd, input.Distance)) * ChunkFade;

	// Píxel de pantalla de este fragmento (división perspectiva hecha aquí: es correcta por píxel).
	float2 ndc = input.Clip.xy / input.Clip.w;
	float2 pixel = floor((ndc * float2(0.5, -0.5) + 0.5) * ViewportSize);
	clip(fade > 0.999 ? 1.0 : fade - Dither(pixel) - 0.0001);   // fade = 1: nunca se descarta nada

	float4 color = tex * input.Color;                            // el alfa del agua viene del color de vértice
	float fog = saturate((input.Clip.w - FogStart) / (FogEnd - FogStart));
	color.rgb = lerp(color.rgb, FogColor, fog);
	return color;
}

technique Chunk
{
	pass P0
	{
		VertexShader = compile VS_SHADERMODEL MainVS();
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
