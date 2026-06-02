using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Satyr Wayfinder (Journey into Nyx, {1}{G}).
///
/// Creature — Satyr 1/1. Oracle text (verified against Scryfall):
///   "When this creature enters, reveal the top four cards of your library.
///    You may put a land card from among them into your hand. Put the rest
///    into your graveyard."
///
/// Satyr Wayfinder is the land-flavoured, ETB-trigger cousin of
/// <see cref="MalevolentRumbleFactory"/> (Sorcery, "reveal top four, may put
/// a permanent into hand, rest into graveyard") — the same
/// <see cref="RevealAndChoose.RevealTopAndChooseAsync"/> primitive, but the
/// eligible reveal pool is filtered to <i>land</i> cards (CR 305) instead of
/// any permanent, and the effect fires from an ETB triggered ability
/// (CR 603.6a) rather than on spell resolution. The land-to-hand half is the
/// same destination as <see cref="CivicWayfinderFactory"/>; the
/// rest-into-graveyard half mirrors Malevolent Rumble's self-mill.
///
/// The base shape (name, Creature, Satyr subtype, {1}{G}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>satyr-wayfinder.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <c>AbilityDefinition</c> schema does not express the
/// reveal-top-N → choose-by-type → rest-to-graveyard effect, so the ETB
/// trigger is layered on here (same posture as
/// <see cref="CivicWayfinderFactory"/> / <see cref="GlintNestCraneFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 <see cref="Creature"/> — Satyr at {1}{G}; owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with
///   ActiveZones = Battlefield. On resolution it reveals the top four cards
///   (clamped to library size — CR 701.21; empty library → clean no-op),
///   may put a <b>land card</b> (CR 305 — matched by the Land card type, so
///   basics, nonbasics, and land-typed duals all qualify) from among them
///   into the controller's <b>hand</b>, and puts the rest into the
///   controller's <b>graveyard</b>. Routed through the shared
///   <see cref="RevealAndChoose.RevealTopAndChooseAsync"/> primitive — the
///   identical closure <see cref="MalevolentRumbleFactory"/> uses, which
///   handles library underflow, the "you may" opt-out (CR 116.1b — an agent
///   returning null declines; the agentless path defaults to first eligible
///   land to preserve the deck-fix tempo line), and routes the zone moves
///   through the registered <c>ZoneService</c>.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: no <c>CardsRevealedEvent</c> is published — the
///   reveal is folded into the peek. Same gap as every reveal-and-choose
///   factory (Malevolent Rumble, Ancient Stirrings); no live observer cares
///   yet.
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + ETB trigger attached (not
///   registered with any <see cref="TriggerManager"/>). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — also registers the
///   ETB trigger so a qualifying
///   <see cref="Majik.Core.Events.CardMovedEvent"/> lands the ability on the
///   stack automatically (CR 603.2).
/// </summary>
[CardName("Satyr Wayfinder")]
public static class SatyrWayfinderFactory
{
    public const string CardName = "Satyr Wayfinder";
    public const string Slug = "satyr-wayfinder";

    /// <summary>How many cards are revealed off the top of the library.</summary>
    public const int RevealCount = 4;

    /// <summary>
    /// Shape overload — attaches the ETB trigger without registering it with
    /// a <see cref="TriggerManager"/>. The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Satyr Wayfinder with its ETB reveal-four / land-to-hand /
    /// rest-to-graveyard ability attached and optionally registered against
    /// the supplied <paramref name="triggers"/> manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> automatically
    /// queues the ability on the stack (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Satyr
        // subtype, {1}{G}, 1/1). The JSON carries no abilities — the ETB
        // reveal effect is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, reveal the top four cards of your
        //    library. You may put a land card from among them into your
        //    hand. Put the rest into your graveyard."
        //
        // Eligible pool: Land card type (CR 305) — basics, nonbasics, and
        // land-typed duals all qualify (the oracle says "a land card", no
        // Basic supertype restriction, unlike Civic Wayfinder). Routed
        // through the shared RevealAndChoose primitive (reveal top 4 → may
        // put a land into hand → rest into graveyard), the identical closure
        // Malevolent Rumble uses. CR 116.1b "you may" opt-out + deterministic
        // first-eligible fallback when no agent is registered.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: reveal top {RevealCount}, may put a land card into hand, " +
            "rest into graveyard",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await RevealAndChoose.RevealTopAndChooseAsync(
                    ctx: ctx,
                    caster: controller,
                    count: RevealCount,
                    eligiblePredicate: c => c.HasType(CardType.Land),
                    optional: true,
                    label: "Land to put into hand",
                    pickedDestination: ZoneType.Hand,
                    restDestination: ZoneType.Graveyard,
                    sourceTag: Slug).ConfigureAwait(false);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
