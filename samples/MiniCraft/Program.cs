using System.Runtime;
using MiniCraft;

// Menos pausas del recolector de basura durante el juego.
GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

using var game = new MiniCraftGame();
game.Run();
