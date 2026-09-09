using C12ProjetoCiv.Core;

namespace C12ProjetoCiv.Rendering;

public static class UiLayout
{
    public static readonly FloatRectangle OneWorkerButton = new(20, 125, 105, 34);
    public static readonly FloatRectangle TwoWorkersButton = new(135, 125, 105, 34);
    public static readonly FloatRectangle FourWorkersButton = new(250, 125, 105, 34);
    public static readonly FloatRectangle PauseButton = new(375, 125, 105, 34);
    public static readonly FloatRectangle RestartButton = new(490, 125, 105, 34);
    public static readonly FloatRectangle StressButton = new(605, 125, 130, 34);
}
