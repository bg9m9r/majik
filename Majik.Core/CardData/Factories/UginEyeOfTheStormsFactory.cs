using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Random;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ugin, Eye of the Storms (Tarkir: Dragonstorm,
/// {7}).
///
/// Legendary Planeswalker — Ugin. Starting loyalty 7.
/// Oracle text (Scryfall, verified):
///   "When you cast this spell, exile up to one target permanent that's
///    one or more colors.
///    Whenever you cast a colorless spell, exile up to one target
///    permanent that's one or more colors.
///    +2: You gain 3 life and draw a card.
///    0: Add {C}{C}{C}.
///    −11: Search your library for any number of colorless nonland cards,
///         exile them, then shuffle. Until end of turn, you may cast those
///         cards without paying their mana costs."
///
/// The card's base shape (name, Legendary Planeswalker — Ugin, {7},
/// loyalty 7) is materialised from the embedded JSON definition
/// (<c>ugin-eye-of-the-storms.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The five printed behaviours
/// (two cast triggers + three loyalty abilities) are layered on top here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express loyalty
/// abilities, cast triggers, targeted exile, or tutor/free-cast, so they
/// live in the factory (same posture as <see cref="StormscaleScionFactory"/>
/// and the loyalty-ability sibling <see cref="UginTheSpiritDragonFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Cast-this-spell trigger (CR 603.3 + CR 601.2 + CR 701.21)</b>:
///   "When you cast this spell, exile up to one target permanent that's one
///   or more colors." Wired as a <see cref="TriggeredAbility"/> over
///   <see cref="SpellCastEvent"/> gated to this card, <c>activeZones =
///   Stack</c> (a cast trigger functions on the stack as the spell that
///   cast it — CR 603.3). The "up to one target" coloured-permanent exile
///   uses the deterministic-resolver posture from
///   <see cref="UginTheSpiritDragonFactory"/> (LoyaltyAbility / cast-trigger
///   targets aren't agent-prompted yet — same gap Karn / Liliana / Ugin
///   Spirit Dragon share). With no resolver wired the clause is a silent
///   no-op ("up to one" → zero).
/// - <b>Whenever-you-cast-a-colorless-spell trigger (CR 603.2 + CR 105.2c +
///   CR 701.21)</b>: "Whenever you cast a colorless spell, exile up to one
///   target permanent that's one or more colors." Wired as a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>,
///   controller-scoped (<c>e.Spell.Controller == this controller</c>),
///   colourless filter (<see cref="CardColors.GetColors"/> empty), gated to
///   the battlefield (<c>activeZones = Battlefield</c>). Shares the same
///   coloured-permanent exile body as the cast-this-spell trigger.
/// - <b>+2: You gain 3 life and draw a card (CR 606 + CR 119.3 + CR 121)</b>:
///   <see cref="Fx.GainLife"/> then <see cref="Fx.DrawCards"/> for the
///   controller, ordered (CR 608.2c).
/// - <b>0: Add {C}{C}{C} (CR 606 + CR 605.1a / mana ability)</b>: adds three
///   colourless mana to the controller's pool via
///   <see cref="Player.AddManaToPool"/>. Modelled inline on the
///   <see cref="LoyaltyAbility"/> (a "0:" loyalty ability that produces mana
///   is still a loyalty ability — CR 606.3 — not a free-standing mana
///   ability, so it goes on the stack like the other loyalty abilities).
/// - <b>−11: Search library for any number of colorless nonland cards,
///   exile them, then shuffle; until end of turn you may cast those cards
///   without paying their mana costs (CR 606 + CR 400.7 + CR 701.21 +
///   CR 118.9)</b>: deterministic v1 — exiles EVERY colourless nonland card
///   from the controller's library ("any number" auto-accepted as "all";
///   the opt-down awaits the agent prompt), shuffles when a
///   <see cref="GameRandom"/> is supplied, then stamps a runtime exile-cast
///   grant (<see cref="Card.GrantRuntimeExileCast"/>) at a free
///   (<see cref="ManaCost.Zero"/>) cost — "without paying their mana costs"
///   (CR 118.9) — for each exiled card, with the same end-of-turn
///   bus-cleanup posture as <see cref="RagavanNimblePilfererFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Loyalty / cast-trigger target prompts</b>: neither
///   <see cref="LoyaltyAbility"/> nor the cast triggers declare
///   <see cref="Majik.Core.Targeting.TargetRequest"/>s; the coloured-permanent
///   exile picks deterministically via the supplied resolver (first
///   candidate). Same gap as Karn / Liliana / Ugin Spirit Dragon.
/// - <b>−11 "any number" opt-down</b>: auto-takes all colourless nonland
///   cards rather than prompting for a subset. Rules-correct as a maximal
///   choice; the partial choice awaits the agent prompt system.
/// - <b>ZoneService routing</b>: the exile paths use raw zone manipulation,
///   so <see cref="CardMovedEvent"/> isn't published via this path (same
///   posture as Ugin Spirit Dragon / Karn / Ragavan's no-bus overload).
/// </summary>
[CardName("Ugin, Eye of the Storms")]
public static class UginEyeOfTheStormsFactory
{
    public const string CardName = "Ugin, Eye of the Storms";
    public const string Slug = "ugin-eye-of-the-storms";
    public const int StartingLoyalty = 7;
    public const int Plus2LifeGain = 3;
    public const int Plus2DrawCount = 1;
    public const int ZeroColorlessManaProduced = 3;
    public const int UltimateLoyaltyCost = -11;

    /// <summary>
    /// Construct Ugin with no resolvers / bus / random wired — the cast
    /// triggers attach structurally but exile nothing (no resolver), the
    /// +2 / 0 still run, and −11 exiles + grants free-cast but does not
    /// shuffle (no random) nor schedule EOT cleanup (no bus). Suitable for
    /// shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, colouredPermanentResolver: null, eventBus: null, random: null);

    /// <summary>
    /// Construct Ugin, Eye of the Storms.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="colouredPermanentResolver">Returns candidate coloured
    /// permanents for the two cast triggers' "exile up to one target
    /// permanent that's one or more colors" clause. v1 picks the first
    /// still-coloured, still-on-battlefield candidate. May be null — the
    /// clause then no-ops ("up to one" → zero).</param>
    /// <param name="eventBus">Bus used to schedule the −11 grant's
    /// end-of-turn cleanup (CR 514.2). May be null — the grant then
    /// persists until a caller clears it (tests).</param>
    /// <param name="random">Shuffle source for the −11 "then shuffle"
    /// (CR 400.7). May be null — the library is left in order (a no-op
    /// shuffle is rules-immaterial for the observable contract).</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Card>>? colouredPermanentResolver,
        IEventBus? eventBus,
        GameRandom? random)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // Planeswalker — Ugin, {7}, loyalty 7). The JSON carries no
        // abilities — the two cast triggers + three loyalty abilities are
        // layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var ugin = (Planeswalker)CardDefinitionFactory.Build(definition, owner);

        // -- When you cast this spell, exile up to one target permanent
        //    that's one or more colors. -----------------------------------
        // CR 603.3 — a cast trigger; functions on the stack as the spell
        // that cast it. Gated to this card.
        ugin.AddAbility(new TriggeredAbility(
            source: ugin,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Card, ugin)),
            effects: new[] { BuildExileColouredPermanentEffect(ugin, colouredPermanentResolver) },
            activeZones: new[] { ZoneType.Stack }));

        // -- Whenever you cast a colorless spell, exile up to one target
        //    permanent that's one or more colors. -------------------------
        // CR 603.2 — a state-trigger-free "whenever you cast" trigger,
        // controller-scoped, colourless filter (CR 105.2c — a colorless
        // object has no colors). Functions while Ugin is on the
        // battlefield.
        ugin.AddAbility(new TriggeredAbility(
            source: ugin,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
            {
                // "you cast" — the controller of Ugin at the time the
                // trigger fires. Capture against the source's current
                // controller (CR 603.3a).
                var uginController = ugin.Controller ?? owner;
                if (!ReferenceEquals(e.Spell.Controller, uginController)) return false;
                // The cast-this-spell trigger above already covers Ugin's
                // own cast (Ugin is itself colorless), so exclude it here to
                // avoid double-exile from a single cast event (CR 603.3 —
                // the two clauses are distinct triggers; Ugin isn't on the
                // battlefield when it's being cast, so activeZones already
                // separates them, but the guard makes the intent explicit).
                if (ReferenceEquals(e.Spell.Card, ugin)) return false;
                // "a colorless spell" — CR 105.2c.
                return CardColors.GetColors(e.Spell.Card).Count == 0;
            }),
            effects: new[] { BuildExileColouredPermanentEffect(ugin, colouredPermanentResolver) },
            activeZones: new[] { ZoneType.Battlefield }));

        // -- +2: You gain 3 life and draw a card. --------------------------
        // CR 606 (loyalty) + CR 119.3 (life) + CR 121 (draw). Ordered
        // (CR 608.2c).
        ugin.AddAbility(new LoyaltyAbility(ugin, +2, () =>
        {
            var controller = ugin.Controller ?? owner;
            Fx.GainLife(controller, Plus2LifeGain);
            Fx.DrawCards(controller, Plus2DrawCount);
        }));

        // -- 0: Add {C}{C}{C}. ---------------------------------------------
        // CR 606 + CR 605.1a — a loyalty ability that produces mana. Three
        // colourless mana to the controller's pool.
        ugin.AddAbility(new LoyaltyAbility(ugin, 0, () =>
        {
            var controller = ugin.Controller ?? owner;
            for (var i = 0; i < ZeroColorlessManaProduced; i++)
            {
                controller.AddManaToPool(ManaCost.Parse("{C}"));
            }
        }));

        // -- −11: Search your library for any number of colorless nonland
        //    cards, exile them, then shuffle. Until end of turn, you may
        //    cast those cards without paying their mana costs. ------------
        // CR 606 + CR 400.7 (shuffle) + CR 701.21 (exile) + CR 118.9
        // ("without paying its mana cost" = a {0} alternative cost).
        ugin.AddAbility(new LoyaltyAbility(ugin, UltimateLoyaltyCost, () =>
        {
            var controller = ugin.Controller ?? owner;

            // "any number of colorless nonland cards" — v1 takes ALL of
            // them (maximal choice; the opt-down awaits agent prompts).
            // CR 105.2c — colourless = no colors. CR 110.4a — nonland =
            // not a Land card.
            var picks = controller.Zones.Library.GetCards()
                .Where(c => c is Card card
                            && !card.HasType(CardType.Land)
                            && CardColors.GetColors(card).Count == 0)
                .Cast<Card>()
                .ToList();

            foreach (var card in picks)
            {
                controller.Zones.Library.RemoveCard(card);
                controller.Zones.Exile.AddCard(card);
                card.SetZone(ZoneType.Exile);

                // CR 118.9 — "you may cast those cards without paying their
                // mana costs": grant a runtime exile-cast at a free cost.
                // Same surface as Ragavan's "you may cast that card".
                card.GrantRuntimeExileCast(controller, ManaCost.Zero);
            }

            // "then shuffle" — CR 400.7. Only when a random source is
            // supplied; a no-op shuffle is rules-immaterial for the
            // observable contract (the exiled cards have already left the
            // library).
            if (random != null)
            {
                controller.Zones.Library.Shuffle(random);
            }

            // EOT cleanup — CR 514.2 / CR 514.3. Schedule a one-shot
            // Cleanup-step handler that clears each grant and unsubscribes.
            // Skipped when no bus is wired (tests manage EOT manually).
            if (eventBus != null && picks.Count > 0)
            {
                Action<StepStartedEvent>? handler = null;
                handler = (e) =>
                {
                    if (e.StepType != PhaseStateType.Cleanup) return;
                    foreach (var card in picks) card.ClearRuntimeExileCast();
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            }
        }));

        return ugin;
    }

    /// <summary>
    /// Shared body for both cast triggers' "exile up to one target permanent
    /// that's one or more colors" clause (CR 701.21 + CR 105.2c). v1
    /// deterministic pick: the first candidate from
    /// <paramref name="resolver"/> that is still on the battlefield and still
    /// coloured. With no resolver the "up to one" resolves to zero (silent
    /// no-op). Raw-zone, owner-scoped exile — same posture as Ugin Spirit
    /// Dragon's −X.
    /// </summary>
    private static IEffect BuildExileColouredPermanentEffect(
        Planeswalker ugin, Func<IReadOnlyList<Card>>? resolver)
    {
        return Fx.Inline(
            $"{CardName}: exile up to one target coloured permanent (CR 701.21)",
            () =>
            {
                var candidates = resolver?.Invoke();
                if (candidates == null) return;
                foreach (var c in candidates)
                {
                    if (c == null) continue;
                    if (c.Zone != ZoneType.Battlefield) continue;
                    if (CardColors.GetColors(c).Count == 0) continue;

                    var holder = c.Controller ?? c.Owner;
                    holder?.Zones.Battlefield.RemoveCard(c);
                    var exileOwner = c.Owner ?? ugin.Owner;
                    exileOwner?.Zones.Exile.AddCard(c);
                    c.SetZone(ZoneType.Exile);
                    return; // "up to one target" — a single permanent.
                }
            });
    }
}
