using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scrapheap Scrounger (Kaladesh, {2}).
///
/// Artifact Creature — Construct 3/2. Oracle text (verified against
/// Scryfall):
///   "This creature can't block.
///    {1}{B}, Exile another creature card from your graveyard: Return this
///    card from your graveyard to the battlefield."
///
/// The base shape (name, Creature + Artifact types, Construct subtype, {2},
/// 3/2) is materialised from the embedded JSON definition
/// (<c>scrapheap-scrounger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="GrimLavamancerFactory"/> / <see cref="StormscaleScionFactory"/>).
/// "Creature" is listed first in the JSON <c>types</c> so
/// <see cref="CardDefinitionFactory.Build"/> mints a <see cref="Creature"/>
/// and adds the Artifact type as a secondary type (same as Adaptive
/// Automaton). The two printed behaviours (can't-block static +
/// graveyard-recursion activated ability) are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express either shape.
///
/// ## Implemented (v1)
///
/// - 3/2 Artifact Creature — Construct at printed cost {2}; owner /
///   controller wired.
///
/// - <b>"This creature can't block." (CR 509.1c)</b> — registered on the
///   supplied <see cref="ContinuousEffectsService"/> as a non-expiring
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> scoped to Scrapheap
///   Scrounger (identical shape to <see cref="GravecrawlerFactory"/> /
///   Bloodghast). <see cref="Majik.Core.Combat.CombatValidator"/> consults
///   the restriction directly. The single-arg <see cref="Create(Player)"/>
///   path attaches the card shape + the activated ability but does NOT
///   register the restriction (no effects service available); use the
///   two-arg overload for production wiring.
///
/// - <b>"{1}{B}, Exile another creature card from your graveyard: Return
///   this card from your graveyard to the battlefield." (CR 602)</b>: an
///   <see cref="ActivatedAbility"/> sourced on this card. The printed
///   {1}{B} mana cost is taken by the cost layer at activation
///   (<see cref="ManaCostCost"/>, CR 602.1b). The "Exile another creature
///   card from your graveyard" additional cost (CR 118 / 601.2g — the
///   generic <see cref="AdditionalCost"/> enum has no exile-from-graveyard
///   payment type) and the "Return this card from your graveyard to the
///   battlefield" effect are performed inside the resolution closure
///   (same posture as <see cref="GrimLavamancerFactory"/>'s
///   exile-from-graveyard cost).
///
///   The ability functions while the source is in the graveyard — the
///   activated-ability validator (<see cref="Majik.Core.Rules.ActionValidator"/>)
///   doesn't gate activation on the source's zone, so an ability whose
///   effect operates on the graveyard-resident source resolves correctly.
///   The resolve closure:
///     1. Verifies Scrounger is still in the owner's graveyard (CR 608.2b —
///        if it has left, the ability does nothing).
///     2. CR 601.2g — pays the exile cost: finds <i>another</i> creature
///        card (not Scrounger itself) in the owner's graveyard and moves it
///        to the owner's exile zone. If no such card exists the cost can't
///        be paid, so the whole body no-ops (no exile, no return) —
///        matching the real-card legality.
///     3. Returns Scrounger from the graveyard to the battlefield under its
///        owner's control (CR 110.1 / 400.7 — direct zone move, same
///        primitive as <see cref="StoneforgeMysticFactory"/>'s
///        hand→battlefield put).
///
/// ## Order of operations
///
/// CR 117.1c — all costs are paid simultaneously from the player's
/// perspective. The mana cost is taken by the cost layer at activation; the
/// graveyard-exile cost + the return-to-battlefield are performed inside the
/// resolution closure with an up-front "another creature card available"
/// guard (CR 601.2g — you can't activate without paying the full cost).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Graveyard-exile cost as a first-class cost surface</b>: there is no
///   <see cref="AdditionalCostType"/> for "exile a creature card from your
///   graveyard", so the cost is paid inside the resolve closure (with an
///   up-front availability guard) rather than gated by the cost layer at
///   activation. Same posture as Grim Lavamancer's "exile two from your
///   graveyard". The observable contract (exactly one other creature card
///   leaves for exile and Scrounger returns, or nothing happens) is
///   preserved.
/// - <b>Which creature card is exiled</b>: the closure exiles the
///   front-most <i>other</i> creature card in the graveyard (insertion
///   order). An agent-driven pick is a future refinement, matching the
///   heuristic-pick posture elsewhere (Grim Lavamancer).
/// - The shape-only <see cref="Create(Player)"/> path skips the
///   <see cref="CombatRestrictionEffect"/> registration (no effects service
///   to register against); production callers thread the live service via
///   the two-arg overload (same posture as <see cref="GravecrawlerFactory"/>).
/// </summary>
[CardName("Scrapheap Scrounger")]
public static class ScrapheapScroungerFactory
{
    public const string CardName = "Scrapheap Scrounger";
    public const string Slug = "scrapheap-scrounger";

    /// <summary>CR 602 — printed activation mana cost: {1}{B}.</summary>
    public const string ActivationManaCost = "{1}{B}";

    /// <summary>
    /// Construct Scrapheap Scrounger with no continuous-effects service. The
    /// card has the correct shape (name, types, Construct subtype, {2}, 3/2)
    /// and the graveyard-recursion activated ability is attached, but the
    /// "can't block" restriction is NOT registered (no service to register
    /// against). Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Scrapheap Scrounger with an optional
    /// <see cref="ContinuousEffectsService"/>. When the service is supplied
    /// the "can't block" rider is registered as a non-expiring
    /// <see cref="CombatRestrictionEffect"/> bound to Scrounger so
    /// <see cref="Majik.Core.Combat.CombatValidator"/> rejects block
    /// declarations naming it (CR 509.1c). The graveyard-recursion activated
    /// ability is attached regardless of the service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service. May be null — the
    /// can't-block restriction is then skipped (shape only).</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact, Construct subtype, {2}, 3/2). "Creature" is listed first
        // so Build mints a Creature; Artifact is added as a secondary type.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "This creature can't block." — CR 509.1c.
        // Permanent restriction (expiresAtEndOfTurn = false) registered on
        // the ContinuousEffectsService so CombatValidator.CanBlock returns
        // false for Scrounger. Mirrors Gravecrawler's permanent can't-block
        // restriction (same shape, same gate).
        // ----------------------------------------------------------------
        effects?.Register(new CombatRestrictionEffect(
            CombatRestriction.CannotBlock,
            target: card,
            expiresAtEndOfTurn: false));

        // ----------------------------------------------------------------
        // {1}{B}, Exile another creature card from your graveyard:
        //   Return this card from your graveyard to the battlefield.
        // CR 602 — activated ability. The {1}{B} mana cost is taken by the
        // cost layer at activation; the "exile another creature card" cost
        // (no enum member exists for it) + the return are performed in the
        // resolve closure. The closure short-circuits if Scrounger has left
        // the graveyard (CR 608.2b) or if no OTHER creature card is
        // available to exile (the cost can't be paid — CR 601.2g).
        // ----------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: exile another creature card from graveyard, return this from graveyard to the battlefield",
            () =>
            {
                var graveyard = owner.Zones.Graveyard;

                // CR 608.2b — Scrounger must still be in the graveyard for
                // the "Return this card from your graveyard" effect to do
                // anything.
                if (!graveyard.GetCards().Contains(card))
                {
                    return;
                }

                // CR 601.2g — pay the exile cost: another creature card
                // (not Scrounger itself) from the owner's graveyard. If none
                // is available the cost can't be paid: no exile, no return.
                var fuel = graveyard.GetCards()
                    .FirstOrDefault(c => !ReferenceEquals(c, card)
                        && c.HasType(CardType.Creature));

                if (fuel is null)
                {
                    return;
                }

                // Exile the fuel card (CR 118 / 701.10 — owner's exile zone).
                graveyard.RemoveCard(fuel);
                owner.Zones.Exile.AddCard(fuel);
                fuel.SetZone(ZoneType.Exile);

                // Return Scrounger from graveyard to the battlefield under
                // its owner's control (CR 110.1 / 400.7 — direct zone move,
                // same primitive as Stoneforge Mystic's hand→battlefield put).
                graveyard.RemoveCard(card);
                owner.Zones.Battlefield.AddCard(card);
                card.SetZone(ZoneType.Battlefield);
                card.SetController(owner);
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationManaCost) },
            effects: new IEffect[] { returnEffect }));

        return card;
    }
}
