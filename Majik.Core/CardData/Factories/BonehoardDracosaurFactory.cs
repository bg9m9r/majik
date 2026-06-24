using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bonehoard Dracosaur (The Lost Caverns of Ixalan,
/// {3}{R}{R}).
///
/// Creature — Dinosaur Dragon 5/5. Oracle text (Scryfall-verified
/// 2026-06-24):
///   "Flying, first strike
///    At the beginning of your upkeep, exile the top two cards of your
///    library. You may play them this turn. If you exiled a land card this
///    way, create a 3/1 red Dinosaur creature token. If you exiled a nonland
///    card this way, create a Treasure token."
///
/// ## Implemented (v1)
///
/// - <b>5/5 Creature — Dinosaur Dragon, {3}{R}{R}, Flying + First strike</b>.
///   Base shape (name, types, subtypes, mana cost, P/T, and the Flying +
///   First strike keyword markers — CR 702.9 / 702.7) loaded from the embedded
///   JSON definition (<see cref="CardDefinitionLoader.FromEmbeddedResource"/>)
///   and built through <see cref="CardDefinitionFactory"/>, same posture as
///   <see cref="KembaKhaRegentFactory"/>. The upkeep trigger is layered on
///   below.
/// - <b>Your-upkeep trigger (CR 603.1 / CR 500.4)</b>: "At the beginning of
///   your upkeep, …". Modelled as a <see cref="TriggeredAbility"/> over
///   <see cref="Majik.Core.Events.StepStartedEvent"/> filtered to the
///   controller's own Upkeep step via <see cref="Triggers.OnStepBegin"/>,
///   identical to Kemba's your-upkeep shape.
/// - <b>"exile the top two cards of your library. You may play them this
///   turn." (CR 701.20 / CR 118.9 / CR 514.2)</b>: on resolution the trigger
///   exiles the top two cards of the controller's library and stamps a
///   temporary play permission on each via
///   <see cref="ExilePlayPermission.GrantUntil"/> with
///   <see cref="ExilePlayExpiry.EndOfTurn"/> ("this turn"). The grant is the
///   card's printed mana cost (no alternative-cost rider). Because the upkeep
///   trigger resolves during the controller's own turn, EndOfTurn clears at
///   the FIRST Cleanup the controller owns — i.e. the end of this turn. The
///   land half of the grant (a land is PLAYED, not cast) is handled inside
///   <see cref="ExilePlayPermission.GrantUntil"/>.
/// - <b>"If you exiled a land card this way, create a 3/1 red Dinosaur
///   creature token. If you exiled a nonland card this way, create a Treasure
///   token." (CR 111.4 / CR 614)</b>: the two clauses are INDEPENDENT — if the
///   two exiled cards split land + nonland, BOTH tokens are created. Resolved
///   by inspecting the cards actually exiled this resolution: any land
///   (<see cref="CardType.Land"/>) mints one 3/1 red Dinosaur token; any
///   nonland mints one Treasure token. Each clause makes at most ONE token
///   regardless of how many of the two cards match (the oracle says "a … token",
///   not "for each"). Token creation routes through the bus-aware
///   <see cref="ReplacementBus"/> overload when supplied so token doublers
///   (Doubling Season, etc. — CR 616.1c) rewrite the count first.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only (the dispatcher path). The
///   upkeep trigger is attached for shape inspection but not registered with a
///   <see cref="TriggerManager"/>; the exile-play grants persist (no bus) and
///   token creation uses the raw-zone fallback with no doubler bus.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, ZoneService?, ReplacementBus?)"/>
///   — fully wired. <paramref name="triggers"/> registers the upkeep trigger so
///   <see cref="Majik.Core.Events.StepStartedEvent"/> auto-queues it;
///   <paramref name="eventBus"/> schedules the "this turn" revocation of the
///   play grants (CR 514.2); <paramref name="zones"/> routes token creation
///   through <see cref="ZoneService"/> so ETB triggers fire (CR 603.6a);
///   <paramref name="replacements"/> threads token doublers.
/// </summary>
[CardName(CardName)]
public static class BonehoardDracosaurFactory
{
    public const string CardName = "Bonehoard Dracosaur";
    public const string Slug = "bonehoard-dracosaur";

    /// <summary>Number of cards exiled each upkeep (CR 701.20).</summary>
    public const int CardsExiled = 2;

    /// <summary>Land-payoff token characteristics — 3/1 red Dinosaur (CR 111.4).</summary>
    public const int DinoTokenPower = 3;
    public const int DinoTokenToughness = 1;
    private const string DinoTokenName = "Dinosaur";

    /// <summary>
    /// Construct Bonehoard Dracosaur with no live runtime wiring (the
    /// dispatcher / shape path). The upkeep trigger is attached for shape
    /// observability but not registered with a <see cref="TriggerManager"/>;
    /// the exile-play grants are stamped but never auto-revoked (no bus) and
    /// token creation uses the raw-zone fallback (no <see cref="ZoneService"/>,
    /// no doubler bus).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zones: null, replacements: null);

    /// <summary>
    /// Construct Bonehoard Dracosaur. When <paramref name="triggers"/> is
    /// supplied the your-upkeep trigger is registered so
    /// <see cref="Majik.Core.Events.StepStartedEvent"/> auto-queues it. When
    /// <paramref name="eventBus"/> is supplied the "you may play them this
    /// turn" grants are revoked at the controller's Cleanup (CR 514.2). When
    /// <paramref name="zones"/> is supplied token creation routes through
    /// <see cref="ZoneService"/> so ETB triggers fire (CR 603.6a). When
    /// <paramref name="replacements"/> is supplied token doublers rewrite the
    /// count before minting (CR 616.1c).
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zones,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition: name, Creature,
        // Dinosaur + Dragon, {3}{R}{R}, 5/5, Flying + First strike keywords.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // --------------------------------------------------------------------
        // Your-upkeep trigger — CR 603.1 / CR 500.4.
        //   "At the beginning of your upkeep, exile the top two cards of your
        //    library. You may play them this turn. If you exiled a land card
        //    this way, create a 3/1 red Dinosaur creature token. If you exiled
        //    a nonland card this way, create a Treasure token."
        // --------------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: exile top 2 (may play this turn) + land→3/1 Dino / nonland→Treasure",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 701.20 — exile the top two cards of the controller's
                // library; CR 118.9 / 514.2 — stamp "you may play them this
                // turn" on each (EndOfTurn → first Cleanup the controller owns,
                // i.e. the end of this same turn since the trigger resolves on
                // the controller's turn).
                var exiled = new List<Card>(CardsExiled);
                for (var i = 0; i < CardsExiled; i++)
                {
                    var top = controller.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null) break; // library underflow — no SBA flag for exile

                    controller.Zones.Library.RemoveCard(top);
                    controller.Zones.Exile.AddCard(top);
                    top.SetZone(ZoneType.Exile);

                    if (top is Card concrete)
                    {
                        ExilePlayPermission.GrantUntil(
                            concrete, controller, concrete.ManaCostValue,
                            ExilePlayExpiry.EndOfTurn, eventBus);
                        exiled.Add(concrete);
                    }
                }

                if (exiled.Count == 0) return;

                // CR 614 — the two payoff clauses are INDEPENDENT. A land/nonland
                // split mints BOTH tokens; each clause makes at most ONE token
                // ("a … token", not "for each"). CR 111.4 token characteristics.
                if (exiled.Any(c => c.HasType(CardType.Land)))
                {
                    var dinoSpec = new TokenFactory.TokenSpec(
                        Name: DinoTokenName,
                        Power: DinoTokenPower,
                        Toughness: DinoTokenToughness,
                        Subtypes: new[] { CardSubtype.Dinosaur },
                        Keywords: null,
                        Colors: new[] { ManaColor.Red });
                    TokenFactory.CreateOnBattlefield(dinoSpec, controller, 1, zones, replacements);
                }

                if (exiled.Any(c => !c.HasType(CardType.Land)))
                {
                    // CR 111.4 — a Treasure token (the standard predefined token;
                    // routed through TokenFactory so its mana ability + ETB are
                    // wired identically to every other Treasure source).
                    TokenFactory.CreateTreasure(controller, zones);
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.StepStateType.Upkeep),
            effects: new IEffect[] { effect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        return card;
    }
}
