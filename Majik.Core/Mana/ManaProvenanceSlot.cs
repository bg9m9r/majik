using Majik.Core.Cards;
using Majik.Core.ValueObjects;

namespace Majik.Core.Mana;

/// <summary>
/// One unit of colored floating mana, tagged with the source that produced
/// it — the producing <see cref="Majik.Core.Abilities.IManaAbility"/> (Arena
/// of Glory's exert) or any stable per-source token (Roku's firebending
/// trigger) (CR 106.4 — a mana's provenance is a property of the mana,
/// tracked per-slot, not a player-scoped counter). The ledger of these slots
/// lives on
/// <see cref="Majik.Core.Players.Player"/> and mirrors the colored buckets of
/// the (bucketed, count-only) <see cref="Majik.Core.ValueObjects.ManaPool"/>:
/// one slot per colored unit a provenance-stamped source added.
///
/// <para>Slot-level provenance is what lets a card react to "if THAT mana
/// (the mana this specific source produced) is spent on THIS spell"
/// (Arena of Glory's exert: haste only to the creature the {R}{R} paid for;
/// CR 702.10). The <see cref="OnSpent"/> callback fires from
/// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> when the resolver
/// consumes this slot to pay a cost, carrying the object the mana was spent
/// on (the cast card, or <c>null</c> for a non-spell context such as an
/// ability activation).</para>
///
/// <para>Generic mana is never tagged (no color to match at spend time), so
/// the ledger only ever holds WUBRG slots.</para>
/// </summary>
public sealed class ManaProvenanceSlot
{
    /// <summary>
    /// The source that produced this unit of mana — a
    /// <see cref="Majik.Core.Abilities.IManaAbility"/> for activated mana
    /// (Arena of Glory's exert) or any stable token for trigger-added mana
    /// (Roku's firebending). Matched by reference when consuming/removing
    /// slots.
    /// </summary>
    public object Source { get; }

    /// <summary>Color of the produced mana unit (always a WUBRG color).</summary>
    public ManaColor Color { get; }

    /// <summary>
    /// Optional reaction fired when this slot is spent paying a cost. The
    /// argument is the object the mana was spent on — the cast
    /// <see cref="ICard"/> for a spell, or <c>null</c> for a non-spell
    /// context (ability activation, no card supplied). The card stamping the
    /// slot owns this delegate, so card-specific behavior (grant haste to a
    /// creature spell) stays in the factory, not the resolver.
    /// </summary>
    public Action<ICard?>? OnSpent { get; }

    public ManaProvenanceSlot(object source, ManaColor color, Action<ICard?>? onSpent = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Color = color;
        OnSpent = onSpent;
    }
}
