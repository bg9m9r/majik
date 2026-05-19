using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.35 — Madness. When this card would be discarded, the player
/// may exile it instead and cast it for its madness cost. The discard
/// → exile + cast window is managed by the discard machinery; this type
/// just validates the cast itself (card in exile, owned by caster) and
/// puts the card into the graveyard after resolution.
/// </summary>
public sealed class MadnessAlternativeCost : IAlternativeCost
{
    public string Description => $"Madness {AlternativeManaCost}";
    public ManaCost AlternativeManaCost { get; }

    public MadnessAlternativeCost(ManaCost madnessCost)
    {
        AlternativeManaCost = madnessCost ?? throw new ArgumentNullException(nameof(madnessCost));
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Exile && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster)
    {
        // Card was in exile during the cast; default resolution destination
        // (graveyard for instant/sorcery, battlefield for permanent) is
        // already handled by StackResolver. Nothing extra needed —
        // OnResolved is a no-op for Madness post-resolution.
    }
}
