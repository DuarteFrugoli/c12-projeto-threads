using System.Numerics;
using C12ProjetoCiv.Core;
using Raylib_cs;

namespace C12ProjetoCiv.Rendering;

public static class UserInterface
{
    public static UiCommand ReadCommand()
    {
        int? requestedWorkerCount = null;
        bool togglePause = Raylib.IsKeyPressed(KeyboardKey.Space);
        bool restart = Raylib.IsKeyPressed(KeyboardKey.R);
        bool toggleStress = Raylib.IsKeyPressed(KeyboardKey.S);

        if (Raylib.IsKeyPressed(KeyboardKey.One))
        {
            requestedWorkerCount = 1;
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Two))
        {
            requestedWorkerCount = 2;
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Four))
        {
            requestedWorkerCount = 4;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mouse = Raylib.GetMousePosition();

            if (Contains(UiLayout.OneWorkerButton, mouse))
            {
                requestedWorkerCount = 1;
            }
            else if (Contains(UiLayout.TwoWorkersButton, mouse))
            {
                requestedWorkerCount = 2;
            }
            else if (Contains(UiLayout.FourWorkersButton, mouse))
            {
                requestedWorkerCount = 4;
            }
            else if (Contains(UiLayout.PauseButton, mouse))
            {
                togglePause = true;
            }
            else if (Contains(UiLayout.RestartButton, mouse))
            {
                restart = true;
            }
            else if (Contains(UiLayout.StressButton, mouse))
            {
                toggleStress = true;
            }
        }

        return new UiCommand(requestedWorkerCount, togglePause, restart, toggleStress);
    }

    private static bool Contains(FloatRectangle rectangle, Vector2 point)
    {
        return point.X >= rectangle.X &&
               point.X <= rectangle.X + rectangle.Width &&
               point.Y >= rectangle.Y &&
               point.Y <= rectangle.Y + rectangle.Height;
    }
}
