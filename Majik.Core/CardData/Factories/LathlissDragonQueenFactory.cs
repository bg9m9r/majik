using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lathliss, Dragon Queen (Dominaria, {4}{R}{R}).
/// Legendary Creature — Dragon 6/6. Oracle text (verified against Scryfall):
///   "Flying
///    Whenever another nontoken Dragon you control enters, create a 5/5 red
///    Dragon creature token with flying.
///    {1}{R}: Dragons you control get +1/+0 until end of turn."
///
/// The card's base shape (name, Legendary supertype, Dragon subtype,
/// {4}{R}{R}, 6/6) is materialised from the embedded JSON definition
/// (<c>lathliss-dragon-queen.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (Flying keyword marker, the Dragon-ETB token trigger, and the activated
/// team pump) are layered on top here — the JSON ability schema doesn't
/// express keyword markers, a nontoken-subtype-gated ETB-of-other trigger,
/// or an activated subtype-scoped EOT pump, so they live in the factory
/// (same posture as <see cref="StormscaleScionFactory"/> /
/// <see cref="CastleEmberethFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/> surface
///   the evasion / block-legality properties (same shape as
///   <see cref="StormscaleScionFactory"/>).
/// - <b>"Whenever another nontoken Dragon you control enters, create a 5/5
///   red Dragon creature token with flying." (CR 603.6e / CR 111)</b> — a
///   <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>
///   (Battlefield entry) whose condition gates on FOUR clauses:
///   <list type="bullet">
///     <item><b>another</b> — <c>!ReferenceEquals(e.Card, card)</c>
///       (Lathliss's own entry doesn't fire it, CR 603.6e).</item>
///     <item><b>nontoken</b> — the entering permanent's
///       <see cref="Permanent.IsToken"/> is false (CR 111). This is the
///       load-bearing clause: the created 5/5 token is itself a Dragon, so
///       WITHOUT the nontoken gate it would re-trigger Lathliss and mint
///       tokens unboundedly. Mirrors the <c>nontokenOnly</c> filter on
///       <see cref="WheneverAnotherCreatureDiesTriggerDef"/> (Midnight
///       Reaper).</item>
///     <item><b>Dragon</b> — printed subtype gate (CR 205.3 — same posture
///       as <see cref="WheneverAnotherCreatureEntersTriggerDef.Subtype"/>).</item>
///     <item><b>you control</b> — the entering permanent's controller is
///       Lathliss's controller, resolved LIVE (<c>card.Controller</c>) so a
///       control change carries the trigger (CR 109.5).</item>
///   </list>
///   The resolve effect mints one 5/5 red flying Dragon token via
///   <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, ZoneService?)"/>
///   (same token primitive as <see cref="StormscaleScionFactory"/>'s storm
///   copies). A live <see cref="ZoneService"/> may be threaded so the token
///   entry itself publishes a <see cref="CardMovedEvent"/> (the new token is
///   a token, so it does not re-trigger Lathliss — the nontoken gate above).
/// - <b>"{1}{R}: Dragons you control get +1/+0 until end of turn."
///   (CR 602 / CR 613.1c)</b> — an <see cref="ActivatedAbility"/> with cost
///   <c>[ManaCostCost("{1}{R}")]</c> (no tap, repeatable). Resolution
///   snapshots the controller's battlefield creatures at resolution time
///   (CR 608.2), filters to the <see cref="CardSubtype.Dragon"/> subtype
///   (printed-subtype scope), and registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+1, +0) on each — Layer 7c,
///   end-of-turn cleanup per CR 514.2. Same primitive as
///   <see cref="CastleEmberethFactory"/>'s team pump, restricted to Dragons.
///   The pump includes Lathliss herself (she is a Dragon — the printed
///   wording is "Dragons you control", not "OTHER Dragons").
///
/// ## Notes
/// - The trigger / pump lambdas capture <c>card</c> (not <c>owner</c>) so
///   live controller tracking via <see cref="Card.Controller"/> picks up
///   control-change effects at resolution time (same posture as
///   <see cref="CastleEmberethFactory"/>).
/// - Creatures without a wired <see cref="ContinuousEffectsService"/>
///   (<see cref="Creature.ActiveEffects"/> null in shape-only tests) silently
///   no-op in the pump body rather than NRE'ing (mirrors
///   <see cref="CastleEmberethFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Token-trigger ZoneService threading</b>: the single-arg dispatcher
///   overload mints the token without a live <see cref="ZoneService"/>
///   (direct battlefield placement, CR 111.6) — the token's own entry then
///   publishes no <see cref="CardMovedEvent"/>. Acceptable: the only
///   ETB-of-other observer that would care is Lathliss herself, and the
///   nontoken gate already excludes the token. Same lossy posture as
///   <see cref="StormscaleScionFactory"/>'s storm mint.
/// </summary>
[CardName("Lathliss, Dragon Queen")]
public static class LathlissDragonQueenFactory
{
    public const string CardName = "Lathliss, Dragon Queen";
    public const string Slug = "lathliss-dragon-queen";

    /// <summary>+P pump magnitude. Lathliss prints +1/+0.</summary>
    public const int PumpPower = 1;
    /// <summary>+T pump magnitude. Lathliss prints +1/+0.</summary>
    public const int PumpToughness = 0;

    /// <summary>The Dragon token's printed P/T (5/5 red flying Dragon).</summary>
    public const int TokenPower = 5;
    public const int TokenToughness = 5;

    /// <summary>
    /// Construct Lathliss with no live zone service. Flying + the Dragon-ETB
    /// token trigger + the activated pump are all attached; the token trigger
    /// fires structurally and mints via direct battlefield placement
    /// (CR 111.6) with no ZoneService. Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zones: null);

    /// <summary>
    /// Construct a fully-wired Lathliss, Dragon Queen.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">Zone service the created Dragon token enters
    /// through (so the entry publishes a <see cref="CardMovedEvent"/>). May
    /// be null — the token is placed directly on the battlefield (CR 111.6).</param>
    public static Creature Create(Player owner, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Dragon, {4}{R}{R}, 6/6). The JSON carries no abilities — Flying /
        // token trigger / pump are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities
        // surfaces evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // "Whenever another nontoken Dragon you control enters, create a 5/5
        //  red Dragon creature token with flying." (CR 603.6e / CR 111).
        // ----------------------------------------------------------------
        card.AddAbility(BuildDragonEntersTrigger(card, owner, zones));

        // ----------------------------------------------------------------
        // "{1}{R}: Dragons you control get +1/+0 until end of turn." (CR 602).
        // No tap in the cost — repeatable.
        // ----------------------------------------------------------------
        card.AddAbility(BuildPumpAbility(card, owner));

        return card;
    }

    /// <summary>
    /// Build the "another nontoken Dragon you control enters" token trigger.
    /// Condition gates on another / nontoken / Dragon / you-control (live
    /// controller); the resolve effect mints one 5/5 red flying Dragon token.
    /// </summary>
    private static TriggeredAbility BuildDragonEntersTrigger(
        Creature card, Player controller, ZoneService? zones)
    {
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            // CR 603.6e — must be an entry onto the battlefield.
            if (e.ToZone != ZoneType.Battlefield) return false;

            // "another" (CR 603.6e) — Lathliss's own entry doesn't fire it.
            if (ReferenceEquals(e.Card, card)) return false;

            // Dragon subtype gate (CR 205.3).
            if (!e.Card.HasType(CardType.Creature)) return false;
            if (!e.Card.HasSubtype(CardSubtype.Dragon)) return false;

            // "nontoken" (CR 111) — exclude token creatures. Load-bearing:
            // the created 5/5 token is itself a Dragon, so this prevents an
            // unbounded re-trigger cascade.
            if (e.Card is Permanent p && p.IsToken) return false;

            // "you control" (CR 109.5) — controller resolved live so a
            // control change carries the trigger.
            return ReferenceEquals(e.Card.Controller, card.Controller ?? controller);
        });

        var tokenEffect = new Effect(
            $"{CardName}: create a {TokenPower}/{TokenToughness} red Dragon creature token with flying (CR 111)",
            () =>
            {
                var bfController = card.Controller ?? controller;

                // CR 111 / CR 111.4 — 5/5 red flying Dragon token.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Dragon",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Dragon },
                    Keywords: new[] { "Flying" },
                    Colors: new[] { ManaColor.Red });

                // The token is a token (IsToken = true via TokenFactory), so
                // its own battlefield entry does NOT re-fire this trigger
                // (nontoken gate above) even when threaded through a live
                // ZoneService that publishes CardMovedEvent.
                TokenFactory.CreateOnBattlefield(spec, bfController, zones);
            });

        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: new IEffect[] { tokenEffect });
    }

    /// <summary>
    /// Build the "{1}{R}: Dragons you control get +1/+0 until end of turn."
    /// activated ability. Snapshots the controller's Dragons at resolution
    /// time (CR 608.2) and registers a +1/+0 EOT pump on each (CR 613.1c
    /// Layer 7c; cleanup CR 514.2).
    /// </summary>
    private static ActivatedAbility BuildPumpAbility(Creature card, Player owner)
    {
        var pumpEffect = new Effect(
            $"{CardName}: Dragons you control get +{PumpPower}/+{PumpToughness} until end of turn",
            () =>
            {
                var controller = card.Controller ?? owner;

                // Snapshot to a list before applying (same posture as
                // CastleEmberethFactory). Filter to the Dragon subtype —
                // "Dragons you control" includes Lathliss herself (the
                // wording is not "OTHER Dragons").
                var dragons = controller.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.HasSubtype(CardSubtype.Dragon))
                    .ToList();

                foreach (var dragon in dragons)
                {
                    // Shape-only safety — without a live ContinuousEffectsService
                    // the pump body silently no-ops rather than NRE'ing.
                    if (dragon.ActiveEffects == null) continue;

                    // CR 613.1c Layer 7c — +1/+0 until end of turn.
                    dragon.ActiveEffects.Register(
                        new PumpUntilEndOfTurnEffect(dragon, PumpPower, PumpToughness));
                }
            });

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{1}{R}") },
            effects: new IEffect[] { pumpEffect });
    }
}
