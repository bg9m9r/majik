using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rampaging Baloths (Zendikar, {4}{G}{G}).
///
/// Creature — Beast 6/6. Oracle text (verified against Scryfall):
///   "Trample
///    Landfall — Whenever a land you control enters, create a 4/4 green
///    Beast creature token."
///
/// Same landfall trigger plumbing as <see cref="ScuteSwarmFactory"/> /
/// <see cref="SteppeLynxFactory"/> (the shared
/// <see cref="Triggers.OnLandEntersUnderControl"/> predicate, CR 603.6a;
/// base shape — including the printed Trample keyword — materialised from the
/// embedded JSON definition <c>rampaging-baloths.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>). The resolve body mints a
/// vanilla 4/4 green Beast token (CR 111) — simpler than Scute Swarm's
/// self-copy branch: there is no intervening "if", so a fixed-shape token is
/// created on every controller land drop.
///
/// ## Implemented (v1)
/// - 6/6 Creature — Beast, mana cost {4}{G}{G}, Trample, owner/controller
///   wired (Trample carried on the JSON-built body via
///   <see cref="KeywordAbility"/>).
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 702.142) —
///   fires on a <see cref="Majik.Core.Events.CardMovedEvent"/> filtered to
///   "a land entering the battlefield under the controller's control" via the
///   shared <see cref="Triggers.OnLandEntersUnderControl"/> predicate. No
///   <see cref="TargetRequest"/>: the token creation names no target
///   (CR 603.6a).
/// - <b>Resolve — mint a 4/4 green Beast token</b> (CR 111 / CR 111.4): one
///   4/4 green Beast creature token is created via
///   <see cref="TokenFactory.CreateOnBattlefield"/>, threading the optional
///   <see cref="ZoneService"/> so the token's own ETB
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires for downstream
///   listeners.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the shape-only <see cref="Create(Player)"/>
///   path attaches the landfall trigger for inspection but does not register
///   it with a bus. Use the
///   <see cref="Create(Player, ZoneService, TriggerManager)"/> overload for
///   live firing.
/// </summary>
[CardName("Rampaging Baloths")]
public static class RampagingBalothsFactory
{
    public const string CardName = "Rampaging Baloths";
    public const string Slug = "rampaging-baloths";
    public const int Power = 6;
    public const int Toughness = 6;

    /// <summary>Name of the 4/4 green Beast token minted on each landfall.</summary>
    public const string BeastTokenName = "Beast";

    /// <summary>Power/toughness of the minted Beast token (CR 111.4).</summary>
    public const int TokenPower = 4;
    public const int TokenToughness = 4;

    /// <summary>
    /// Construct Rampaging Baloths with no live <see cref="ZoneService"/> /
    /// <see cref="TriggerManager"/> wiring. The landfall trigger is attached
    /// for shape inspection but not registered with a bus, and the minted
    /// token bypasses ZoneService. Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Rampaging Baloths. When <paramref name="triggers"/> is
    /// supplied the landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering under
    /// the controller's control automatically queues the ability. When
    /// <paramref name="zones"/> is supplied the minted token routes through
    /// <see cref="TokenFactory.CreateOnBattlefield"/> using the service so it
    /// publishes <see cref="Majik.Core.Events.CardMovedEvent"/> on entry.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zones, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Beast
        // subtype, {4}{G}{G}, 6/6, Trample). The JSON carries no triggered
        // abilities — the landfall trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 702.142.
        //   "Whenever a land you control enters, create a 4/4 green Beast
        //    creature token."
        // Predicate shared with Scute Swarm / Steppe Lynx / Lotus Cobra.
        // No target: the token creation names no target. On resolve, mint
        // one 4/4 green Beast token (CR 111 / CR 111.4).
        // ----------------------------------------------------------------
        var landfallEffect = new Effect(
            $"{CardName}: landfall — create a {TokenPower}/{TokenToughness} green Beast creature token",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateOnBattlefield(BeastTokenSpec, controller, zones);
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { landfallEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }

    /// <summary>CR 111.4 — the 4/4 green Beast token spec minted on each
    /// controller landfall.</summary>
    private static TokenFactory.TokenSpec BeastTokenSpec => new(
        Name: BeastTokenName,
        Power: TokenPower,
        Toughness: TokenToughness,
        Subtypes: new[] { CardSubtype.Beast },
        Colors: new[] { ManaColor.Green });
}
