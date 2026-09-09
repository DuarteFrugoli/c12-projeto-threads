using System.Numerics;

namespace C12ProjetoCiv.Core;

public readonly record struct FloatRectangle(float X, float Y, float Width, float Height)
{
    public Vector2 Center => new(X + (Width / 2f), Y + (Height / 2f));

    public Vector2 FromRelative(float relativeX, float relativeY)
    {
        return new Vector2(X + (Width * relativeX), Y + (Height * relativeY));
    }
}
