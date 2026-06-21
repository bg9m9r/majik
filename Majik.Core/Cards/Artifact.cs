using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Represents an artifact card/permanent.
/// </summary>
public class Artifact : Permanent
{
    public Artifact(string name, string manaCost, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
        : base(name, manaCost, new[] { CardType.Artifact }, supertypes, subtypes)
    {
    }

    /// <summary>Simulation copy constructor. Chains through
    /// <see cref="Permanent(Permanent)"/> for all base + Permanent runtime
    /// state. <see cref="Artifact"/> has no additional mutable runtime fields.
    /// </summary>
    protected Artifact(Artifact src) : base(src) { }

    /// <inheritdoc cref="Card.CloneForSim"/>
    internal override Card CloneForSim() => new Artifact(this);
}
