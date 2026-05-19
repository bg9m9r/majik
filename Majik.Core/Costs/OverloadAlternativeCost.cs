using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.96 — Overload. Cast the spell for its overload cost INSTEAD of
/// the printed cost; the spell's text changes "target" to "each" (CR
/// 702.96b). The caller's <see cref="Game.SpellDefinition.EffectFactory"/>
/// must inspect <see cref="IsOverloaded"/> to choose the multi-target
/// branch; this class is the cost gate + a flag carrier.
/// </summary>
public sealed class OverloadAlternativeCost : IAlternativeCost
{
    public string Description => $"Overload {AlternativeManaCost}";
    public ManaCost AlternativeManaCost { get; }
    public bool IsOverloaded { get; private set; }

    public OverloadAlternativeCost(ManaCost overloadCost)
    {
        AlternativeManaCost = overloadCost ?? throw new ArgumentNullException(nameof(overloadCost));
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Hand && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster)
    {
        IsOverloaded = true;
        // Card destination defaults (graveyard for instants/sorceries).
    }
}
