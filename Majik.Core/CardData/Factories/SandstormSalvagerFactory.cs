using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sandstorm Salvager (The Brothers' War, {2}{G}).
/// Creature — Human Artificer 1/1. Oracle text (verified against Scryfall):
///   "When this creature enters, create a 3/3 colorless Golem artifact
///    creature token.
///    {2}, {T}: Put a +1/+1 counter on each creature token you control. They
///    gain trample until end of turn."
///
/// The base shape (name, Creature, Human/Artificer subtypes, {2}{G}, 1/1) is
/// materialised from the embedded JSON definition (<c>sandstorm-salvager.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON carries no abilities —
/// the <c>AbilityDefinition</c> schema doesn't express token-creation effects
/// nor a "{cost},{T}: pump each creature token you control" group-pump, so both
/// printed behaviours are layered on here (same posture as
/// <see cref="BladeSplicerFactory"/> — whose 3/3 Golem-token ETB this card cribs).
///
/// ## Implemented (v1)
/// - <b>ETB triggered ability (CR 603.6a / CR 111.4 / CR 301.1)</b>: over a
///   <see cref="CardMovedEvent"/> filtered to (this card, ToZone =
///   Battlefield). Resolution creates one 3/3 colourless Golem <i>artifact</i>
///   creature token under the controller via <see cref="CreateGolemToken"/>.
///   Identical Golem shape to <see cref="BladeSplicerFactory"/>'s, minus the
///   Phyrexian subtype (this token is just a "Golem").
/// - <b>"{2}, {T}: Put a +1/+1 counter on each creature token you control.
///   They gain trample until end of turn." (CR 602 activated ability)</b>: an
///   <see cref="ActivatedAbility"/> whose costs are <see cref="ManaCostCost"/>
///   ("{2}") plus the {T} symbol (<see cref="Primitives.Costs.TapSelf"/> — CR
///   605.1a). On resolution it enumerates every creature token the controller
///   controls (CR 111 — <see cref="Permanent.IsToken"/>) on its battlefield and,
///   for each: puts one <see cref="CounterType.PlusOnePlusOne"/> counter via
///   <see cref="CountersService.Add"/> (routed through the optional
///   <see cref="ReplacementBus"/> so Hardened Scales / Doubling Season — CR 614 —
///   rewrite the count) and registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Trample") (CR 702.19 /
///   613.1c Layer 6) against the shared <see cref="ContinuousEffectsService"/>,
///   expiring in the cleanup step (CR 514.2). "Creature token you control" is a
///   snapshot taken at resolution (CR 608.2) — newly created tokens this turn are
///   included, non-token creatures are not. No sorcery-speed gate is printed
///   (CR 602.5a instant speed); the ability is repeatable.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both abilities are attached for
///   shape observability; without a <see cref="TriggerManager"/> the ETB isn't
///   bus-driven, and without a <see cref="ContinuousEffectsService"/> the
///   trample grant no-ops (the +1/+1 counters still land). This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?, ContinuousEffectsService?, ReplacementBus?, IEventBus?)"/>
///   — fully wired.
/// </summary>
[CardName("Sandstorm Salvager")]
public static class SandstormSalvagerFactory
{
    public const string CardName = "Sandstorm Salvager";
    public const string Slug = "sandstorm-salvager";

    /// <summary>Mana portion of the {2},{T} pump cost.</summary>
    public const string PumpManaCost = "{2}";

    public const int TokenPower = 3;
    public const int TokenToughness = 3;

    /// <summary>+1/+1 counters placed on each controlled token (CR 121.1).</summary>
    public const int CounterAmount = 1;

    /// <summary>Keyword granted until end of turn (CR 702.19).</summary>
    public const string GrantedKeyword = "Trample";

    /// <summary>
    /// Construct Sandstorm Salvager with no live wiring. The ETB Golem trigger
    /// and the {2},{T} group-pump ability are attached for shape observability;
    /// the ETB is not bus-registered (no <see cref="TriggerManager"/>) and the
    /// trample grant no-ops (no <see cref="ContinuousEffectsService"/>).
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, continuousEffects: null,
               replacements: null, eventBus: null);

    /// <summary>
    /// Construct Sandstorm Salvager with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the Golem token's ETB routes through
    /// <see cref="ZoneService.MoveCardTo"/> so <see cref="CardMovedEvent"/>
    /// publishes for any zone-change subscribers.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with the
    /// bus so the corresponding <see cref="CardMovedEvent"/> lands the ability on
    /// the stack automatically (CR 603.2).</param>
    /// <param name="continuousEffects">Layers service the trample grant is
    /// registered against (one <see cref="GrantKeywordUntilEndOfTurnEffect"/> per
    /// controlled token). Null → no trample grant (counters still land).</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> routed
    /// through <see cref="CountersService.Add"/> for the +1/+1 placement.</param>
    /// <param name="eventBus">Routed through <see cref="CountersService.Add"/> so
    /// each +1/+1 placement publishes <see cref="CounterAddedEvent"/>.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects,
        ReplacementBus? replacements = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Artificer subtypes, {2}{G}, 1/1). The JSON carries no abilities —
        // ETB token + {2},{T} group-pump are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 111 (Token).
        //   "When this creature enters, create a 3/3 colorless Golem artifact
        //    creature token." No targets — pure token-creation.
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: create a 3/3 colourless Golem artifact creature token",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateGolemToken(controller, zones);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // "{2}, {T}: Put a +1/+1 counter on each creature token you control.
        //  They gain trample until end of turn." CR 602 activated ability.
        // Costs = ManaCostCost("{2}") + TapSelf (CR 605.1a). No target — it
        // operates on a snapshot of every creature token the controller
        // controls at resolution (CR 608.2 / CR 111).
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on each creature token you control; " +
            "they gain trample until end of turn",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 608.2 — snapshot the affected set before mutating
                // (a fresh token can't be created mid-iteration here, but the
                // snapshot keeps the intent explicit and the iteration safe).
                var tokens = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.IsToken)
                    .ToList();

                foreach (var token in tokens)
                {
                    // CR 121.1 / CR 614 — one +1/+1 counter, routed so
                    // Hardened Scales / Doubling Season rewrite the count.
                    CountersService.Add(
                        token, CounterType.PlusOnePlusOne, CounterAmount,
                        replacements, eventBus);

                    // CR 702.19 / 613.1c Layer 6 — grant trample until end of
                    // turn (CR 514.2). One effect per token; no-op when no
                    // continuous-effects service is wired.
                    continuousEffects?.Register(
                        new GrantKeywordUntilEndOfTurnEffect(token, GrantedKeyword));
                }
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpManaCost), Primitives.Costs.TapSelf(card) },
            effects: new IEffect[] { pumpEffect }));

        return card;
    }

    /// <summary>
    /// CR 111 / CR 111.4 / CR 301.1 — create one 3/3 colourless Golem
    /// <i>artifact</i> creature token under <paramref name="controller"/>'s
    /// control. The Artifact card type is flagged via
    /// <see cref="Card.AddCardType"/> (same Artifact-Creature token pattern as
    /// <see cref="BladeSplicerFactory.CreatePhyrexianGolemToken"/>); the explicit
    /// empty colour list stamps the colourless override.
    /// </summary>
    public static Creature CreateGolemToken(
        Player controller,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Golem",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Golem },
            // CR 105 / CR 111.4 — printed "colourless" token.
            Colors: Array.Empty<ManaColor>());

        var golem = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // CR 301.1 / 302.1 — flag the token as an Artifact Creature so
        // HasType(Artifact) returns true ("3/3 colorless Golem artifact
        // creature token").
        golem.AddCardType(CardType.Artifact);

        return golem;
    }
}
