using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 715 — Adventure. Cast an adventurer card from hand as its
/// alternative-characteristics (Instant or Sorcery) spell, paying the
/// Adventure mana cost INSTEAD of the printed creature cost (CR 715.3,
/// 715.3a). On resolution the card is exiled instead of going to the
/// graveyard or the battlefield (CR 715.3d); while it remains in exile
/// its owner may cast the main (creature) face from exile for its
/// printed mana cost — modelled via the existing
/// <see cref="Card.RuntimeExileCastAllowedCaster"/> probe surface +
/// <see cref="ExileCastAlternativeCost"/>, same shape Ragavan / Cascade /
/// Suspend already use.
///
/// Legality (see <see cref="CanCastFor"/>):
///   - card carries a non-null <see cref="Card.AdventureSpec"/>
///     (only adventurer cards expose this metadata),
///   - card is in its owner's hand (CR 715.3 — "as a player plays an
///     adventurer card, the player chooses whether they play the card
///     normally or as an Adventure"; "plays" is restricted to the
///     standard cast-from-hand zone unless another effect grants an
///     alternative source),
///   - caster is the card's owner (no opponent-cast permission here;
///     casts from opponent's library via Ragavan etc. take the printed
///     creature path through <see cref="ExileCastAlternativeCost"/>).
///
/// Sorcery-speed gating for sorcery Adventures (CR 117.1 + CR 715.3b —
/// "while on the stack as an Adventure, the spell has only its
/// alternative characteristics") is enforced in
/// <see cref="Majik.Core.Game.SpellCastFlow"/> via
/// <see cref="IsLegalInContext"/> — same pattern Pitch alt-cost uses for
/// its own contextual ("if it's not your turn") gate.
/// </summary>
public sealed class AdventureAlternativeCost : IAlternativeCost
{
    public string Description => $"Adventure {AlternativeManaCost}";
    public ManaCost AlternativeManaCost { get; }

    /// <summary>
    /// CR 715.3d — Adventure spells exile on resolution instead of being
    /// put into their owner's graveyard. Engine plumbing: stamped onto
    /// <see cref="Spells.Spell.PostResolutionZoneOverride"/> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> at cast time; consulted
    /// by <see cref="Majik.Core.Services.StackResolver"/> instead of the
    /// printed-type default.
    /// </summary>
    public ZoneType? PostResolutionZone => ZoneType.Exile;

    /// <summary>True when the Adventure half is a Sorcery (vs Instant).
    /// Drives the sorcery-speed gate.</summary>
    public bool IsSorcerySpeed { get; }

    public AdventureAlternativeCost(ManaCost adventureCost, bool isSorcerySpeed)
    {
        AlternativeManaCost = adventureCost ?? throw new ArgumentNullException(nameof(adventureCost));
        IsSorcerySpeed = isSorcerySpeed;
    }

    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        // CR 715 — only adventurer cards. AdventureSpec lives on the
        // concrete Card type (not the ICard contract) since it is engine-
        // owned mutable metadata, mirroring MdfcState / RuntimeFlashbackCost.
        if (card is not Card concrete || concrete.AdventureSpec == null) return false;
        // CR 715.3 — cast from hand only via this surface.
        if (card.Zone != ZoneType.Hand) return false;
        // Owner-only cast permission.
        return ReferenceEquals(card.Owner, caster);
    }

    /// <summary>
    /// CR 117.1 + CR 715.3b — sorcery-speed gate. For Sorcery Adventures
    /// the cast must occur on the caster's turn during a main phase with
    /// an empty stack (the empty-stack/main-phase check is enforced by
    /// the rest of <see cref="Majik.Core.Game.SpellCastFlow"/>; this
    /// hook only enforces the "caster is active player" half — same
    /// shape <see cref="PitchAlternativeCost.IsLegalInContext"/> uses).
    /// Instant Adventures always return true.
    /// </summary>
    public bool IsLegalInContext(Player activePlayer, Majik.Core.StateMachine.PhaseStateType? currentPhase, bool stackIsEmpty, Player caster)
    {
        if (!IsSorcerySpeed) return true;
        if (!ReferenceEquals(caster, activePlayer)) return false;
        if (currentPhase is { } phase && !phase.IsMain()) return false;
        if (!stackIsEmpty) return false;
        return true;
    }

    public void OnResolved(ICard card, Player caster)
    {
        // CR 715.3d — "while [the card] remains exiled, that player may
        // play it." The card is on the stack at this point (the Spell
        // wrapper's destination handover happens AFTER OnResolved runs,
        // via Spell.PostResolutionZoneOverride). We stamp the runtime
        // exile-cast permission for the printed mana cost so the owner
        // may cast the creature face from exile via the existing
        // ExileCastAlternativeCost probe surface; the actual zone move
        // (Stack → Exile) is performed by StackResolver consulting
        // PostResolutionZoneOverride.
        //
        // The permission lapses when the card leaves exile (typically by
        // being cast from exile — the cast moves Exile → Stack, which
        // clears the exile-zone gate inside ExileCastAlternativeCost.
        // CanCastFor; we additionally clear the runtime grant here in
        // ExileCastAlternativeCost.OnResolved so the permission doesn't
        // dangle if the card returns to exile by some other route).
        if (card is Card concreteCard && concreteCard.ManaCostValue != null)
        {
            concreteCard.GrantRuntimeExileCast(caster, concreteCard.ManaCostValue);
        }
    }
}
