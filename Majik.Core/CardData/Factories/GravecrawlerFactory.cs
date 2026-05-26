using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gravecrawler (Dark Ascension, {B}).
///
/// Creature — Zombie 2/1. Oracle text:
///   "Gravecrawler can't block.
///    You may cast Gravecrawler from your graveyard as long as you control
///    a Zombie."
///
/// ## Implemented (v1)
///
/// - 2/1 Zombie with mana cost {B}, owner / controller stamped.
/// - <b>"Gravecrawler can't block." (CR 509.1c)</b> — registered on the
///   supplied <see cref="ContinuousEffectsService"/> as a non-expiring
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> scoped to Gravecrawler
///   (same shape as <see cref="BloodghastFactory"/>'s can't-block rider).
///   <see cref="Majik.Core.Combat.CombatValidator"/> consults the
///   restriction directly. A <see cref="KeywordAbility"/> "Defender"-style
///   "Cannot Block" marker is NOT attached — the only inspection surface
///   is the registered restriction (mirrors Bloodghast). The single-arg
///   <see cref="Create(Player)"/> path attaches the card shape but does
///   NOT register the restriction (no effects service available); use
///   the two-arg overload for production wiring.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"You may cast Gravecrawler from your graveyard as long as you
///   control a Zombie." (CR 112.6 / CR 113.6 — alternate playable zone +
///   predicate gate)</b>: the engine has no general
///   <c>Card.GrantPlayCastFromGraveyard</c> primitive yet, and the only
///   precedents (Snapcaster Mage / Lurrus / Past in Flames /
///   Flashback / Demilich / Wrenn and Six) all route through the
///   <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> or a card-
///   specific runtime grant rather than a "cast from graveyard at printed
///   mana cost" primitive. Adding a general
///   <c>RuntimeGraveyardCastPredicate</c> surface — gated on a controller
///   predicate (e.g. "you control a Zombie") and routing back through
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> with a
///   graveyard-zoned cast — is a follow-up primitive PR that lights up
///   Gravecrawler + a future Bloodghast (landfall return is different —
///   Bloodghast doesn't cast from graveyard, it returns directly via
///   trigger) + Squee, Goblin Nabob / Squee, the Immortal / Reassembling
///   Skeleton / etc. The factory ships shape + can't-block today; when the
///   primitive lands, this factory grows a <c>WireCastFromGraveyard(card,
///   () => controller.Zones.Battlefield.GetCards().Any(c =>
///   c.HasSubtype(CardSubtype.Zombie)))</c> call alongside the existing
///   can't-block registration.
/// - The shape-only <see cref="Create(Player)"/> path skips the
///   <see cref="CombatRestrictionEffect"/> registration entirely (no
///   effects service to register against); production callers thread the
///   live service via the two-arg overload (same posture as
///   <see cref="BlightedAgentFactory"/>'s can't-be-blocked rider).
/// </summary>
[CardName("Gravecrawler")]
public static class GravecrawlerFactory
{
    public const string CardName = "Gravecrawler";
    public const string PrintedManaCost = "{B}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Gravecrawler with no continuous-effects service. The card
    /// has the correct shape (name, type, P/T, mana cost, subtype) but the
    /// can't-block restriction is NOT registered (no service to register
    /// against). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Gravecrawler with an optional
    /// <see cref="ContinuousEffectsService"/>. When the service is supplied
    /// the "can't block" rider is registered as a non-expiring
    /// <see cref="CombatRestrictionEffect"/> bound to Gravecrawler so
    /// <see cref="Majik.Core.Combat.CombatValidator"/> rejects block
    /// declarations naming it (CR 509.1c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null —
    /// the can't-block restriction is then skipped (shape only).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Gravecrawler can't block." — CR 509.1c.
        // Permanent restriction (expiresAtEndOfTurn = false) registered on
        // the ContinuousEffectsService so CombatValidator.CanBlock returns
        // false for this creature. Mirrors Bloodghast's permanent
        // can't-block restriction (same shape, same gate).
        // ----------------------------------------------------------------
        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBlock,
            target: card,
            expiresAtEndOfTurn: false));

        // "You may cast Gravecrawler from your graveyard as long as you
        // control a Zombie." — DEFERRED. See class xmldoc Deferred section
        // for the primitive gap. The card ships with can't-block enforced
        // and the graveyard-cast clause inert until the primitive lands.

        return card;
    }
}
