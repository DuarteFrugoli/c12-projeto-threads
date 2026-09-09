using System.Numerics;
using C12ProjetoCiv.Core;
using Raylib_cs;

namespace C12ProjetoCiv.Rendering;

public static class UiLayout
{
    public static readonly FloatRectangle OneWorkerButton = new(20, 125, 105, 34);
    public static readonly FloatRectangle TwoWorkersButton = new(135, 125, 105, 34);
    public static readonly FloatRectangle FourWorkersButton = new(250, 125, 105, 34);
    public static readonly FloatRectangle PauseButton = new(375, 125, 105, 34);
    public static readonly FloatRectangle RestartButton = new(490, 125, 105, 34);
    public static readonly FloatRectangle StressButton = new(605, 125, 130, 34);
    public static readonly FloatRectangle FullscreenButton = new(745, 125, 135, 34);

    public static Camera2D CreateVirtualCamera(SimulationConfig config)
    {
        float scale = MathF.Min(
            Raylib.GetScreenWidth() / (float)config.WindowWidth,
            Raylib.GetScreenHeight() / (float)config.WindowHeight);
        float offsetX = (Raylib.GetScreenWidth() - (config.WindowWidth * scale)) / 2f;
        float offsetY = (Raylib.GetScreenHeight() - (config.WindowHeight * scale)) / 2f;

        return new Camera2D
        {
            Offset = new Vector2(offsetX, offsetY),
            Target = Vector2.Zero,
            Rotation = 0f,
            Zoom = scale,
        };
    }

    public static Vector2 ScreenToVirtual(Vector2 screenPosition, SimulationConfig config)
    {
        return Raylib.GetScreenToWorld2D(screenPosition, CreateVirtualCamera(config));
    }
}
