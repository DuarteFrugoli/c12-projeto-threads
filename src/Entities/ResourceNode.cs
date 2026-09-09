using System.Numerics;

namespace C12ProjetoCiv.Entities;

/// <summary>
/// Um ponto imutável e inesgotável. Coletar nunca altera esta estrutura.
/// </summary>
public readonly record struct ResourceNode(Vector2 Position);
