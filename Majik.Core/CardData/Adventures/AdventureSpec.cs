using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Adventures;

/// <summary>
/// CR 715 — alternative-characteristics descriptor for the Adventure half
/// of an adventurer card. Attached to the underlying <see cref="Cards.Card"/>
/// via <see cref="Cards.Card.AdventureSpec"/>; consumed by
/// <see cref="Costs.AdventureAlternativeCost"/> and
/// <see cref="Game.SpellCastFlow"/> to drive the cast-as-Adventure path.
///
/// MVP characteristics tracked:
///   - <see cref="Name"/> — Adventure's printed name (CR 715.2 / 715.5).
///   - <see cref="ManaCost"/> — Adventure's printed mana cost
///     (CR 715.3a — only the alternative characteristics are evaluated to
///     see if it can be cast).
///   - <see cref="AdventureType"/> — Instant or Sorcery (CR 715.3b — while
///     on the stack as an Adventure, the spell has only its alternative
///     characteristics, which gates sorcery-speed timing via CR 117.1).
///   - <see cref="BuildDefinition"/> — factory producing a
///     <see cref="SpellDefinition"/> for the Adventure half's effects.
///     The closure receives the caster + a target resolver so it can build
///     the resolution effects without knowing about the engine plumbing.
///
/// Deferred (v1): full Layer-system swap of card characteristics while on
/// the stack as Adventure (subtypes/types/colour-identity per CR 715.2b).
/// The cast pipeline routes through alt-cost + Spell.PostResolutionZoneOverride,
/// so the engine never needs to re-interpret the card's printed type during
/// resolution — it just exiles per CR 715.3d.
/// </summary>
public sealed record AdventureSpec(
    string Name,
    ManaCost ManaCost,
    CardType AdventureType,
    Func<Player, Func<object, object>, SpellDefinition> BuildDefinition)
{
    /// <summary>Is the Adventure half a sorcery (vs an instant)?</summary>
    public bool IsSorcery => AdventureType == CardType.Sorcery;
}
