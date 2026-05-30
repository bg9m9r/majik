using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Servo Schematic (Aether Revolt, {2}).
///
/// Artifact. Oracle text (Scryfall, verified):
///   "When this artifact enters or is put into a graveyard from the
///    battlefield, create a 1/1 colorless Servo artifact creature token."
///
/// Closest analogue is <see cref="IchorWellspringFactory"/> — the same
/// symmetric "enters or is put into a graveyard from the battlefield" dual
/// trigger (ETB leg via <see cref="Triggers.OnEnterBattlefieldSelf"/> +
/// LTB-to-graveyard leg via <see cref="Triggers.OnDies"/>) — with the
/// per-leg effect swapped from Ichor Wellspring's "draw a card" to the 1/1
/// colourless Servo artifact-creature token built by
/// <see cref="TokenFactory.CreateOnBattlefield"/>. The Servo token wiring
/// (1/1, <see cref="CardSubtype.Servo"/>, explicit colourless colour set,
/// Artifact type stamped additively) mirrors
/// <see cref="AnimationModuleFactory"/>'s Servo and
/// <see cref="HangarbackWalkerFactory"/>'s dies-creates-token leg.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>ETB token trigger</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> (CR 603.6a — fires on the
///   self anywhere → Battlefield <see cref="CardMovedEvent"/>).
///   <c>activeZones = {Battlefield}</c> (the ability is on the battlefield
///   when it triggers and resolves). Resolves to one Servo token.
/// - <b>LTB token trigger</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> (CR 700.4 / 603.6 — Battlefield →
///   Graveyard self-move; <c>OnDies</c> is permanent-agnostic despite the
///   creature-flavoured name). <c>activeZones = {Battlefield, Graveyard}</c>
///   so the trigger still matches whether the engine evaluates the zone gate
///   just-before the move (source still on battlefield, CR 603.10c
///   last-known-information) or just-after (source already in graveyard) —
///   mirrors Ichor Wellspring's / Chromatic Star's LTB wiring. Resolves to
///   one Servo token.
///
/// Both legs are independent triggered abilities (CR 603.3) — entering and
/// leaving are mutually exclusive events on a single object, so they never
/// stack together off one zone change.
///
/// Each Servo token is a 1/1 colourless <see cref="CardSubtype.Servo"/>
/// creature built via <see cref="TokenFactory.CreateOnBattlefield"/>, then
/// additively stamped <see cref="CardType.Artifact"/> so it reports
/// Artifact + Creature — Servo (CR 111.1 / CR 111.4; same multi-type stamp
/// as Animation Module's Servo and Hangarback Walker's Thopters). When a
/// <see cref="ZoneService"/> is supplied each token's ETB publishes
/// <see cref="CardMovedEvent"/> so downstream listeners (Soul Warden, etc.)
/// fire.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both triggers attached for
///   shape observability; not registered with any <see cref="TriggerManager"/>;
///   tokens enter via the raw zone path. Suitable for shape / dispatcher tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. Both triggers register so the bus surfaces them automatically;
///   token ETBs publish <see cref="CardMovedEvent"/> via ZoneService.
/// </summary>
[CardName("Servo Schematic")]
public static class ServoSchematicFactory
{
    public const string CardName = "Servo Schematic";
    public const string PrintedManaCost = "{2}";
    public const int ServoPower = 1;
    public const int ServoToughness = 1;
    public const string ServoTokenName = "Servo";

    /// <summary>
    /// Construct Servo Schematic with no live trigger-manager wiring. Both
    /// token triggers are attached to <see cref="Card.Abilities"/> so shape
    /// tests can observe them; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null, zones: null);

    /// <summary>
    /// Construct Servo Schematic with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both token triggers are
    /// registered so the bus surfaces them automatically. When
    /// <paramref name="zones"/> is supplied, each Servo token's ETB
    /// publishes <see cref="CardMovedEvent"/> (Soul Warden etc.).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers, ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var schematic = new Artifact(CardName, PrintedManaCost);
        schematic.SetOwner(owner);
        schematic.SetController(owner);

        // ----------------------------------------------------------------
        // When this artifact enters, create a Servo token. CR 603.6a —
        // self-ETB trigger over the (anywhere → Battlefield) CardMovedEvent.
        // activeZones={Battlefield}: the ability resolves while the artifact
        // sits on the battlefield.
        // ----------------------------------------------------------------
        var etbToken = new Effect(
            $"{CardName}: create a Servo token on enter-the-battlefield",
            () => CreateServoToken(schematic, owner, zones));

        var etbTrigger = new TriggeredAbility(
            source: schematic,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(schematic),
            effects: new IEffect[] { etbToken },
            activeZones: new[] { ZoneType.Battlefield });

        schematic.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // ...or is put into a graveyard from the battlefield, create a Servo
        // token. CR 700.4 / 603.6 — Battlefield → Graveyard self-move.
        // Triggers.OnDies is shape-generic over CardMovedEvent
        // (FromZone=Battlefield → ToZone=Graveyard for the source).
        // activeZones={Battlefield, Graveyard} so the gate matches whether
        // the engine evaluates pre- or post-move (CR 603.10c — mirrors Ichor
        // Wellspring's LTB trigger).
        // ----------------------------------------------------------------
        var ltbToken = new Effect(
            $"{CardName}: create a Servo token on LTB battlefield->graveyard",
            () => CreateServoToken(schematic, owner, zones));

        var ltbTrigger = new TriggeredAbility(
            source: schematic,
            controller: owner,
            condition: Triggers.OnDies(schematic),
            effects: new IEffect[] { ltbToken },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        schematic.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return schematic;
    }

    /// <summary>
    /// CR 111.1 / CR 111.4 — create one 1/1 colourless Servo artifact
    /// creature token under the source's current controller. The token is a
    /// <see cref="CardSubtype.Servo"/> creature with an explicit colourless
    /// colour set (so colour-matters subscribers see "no colours" rather
    /// than probing the empty mana cost), then additively stamped
    /// <see cref="CardType.Artifact"/> for the artifact-creature multi-type
    /// (TokenFactory mints a Creature shell). Same Servo wiring as
    /// <see cref="AnimationModuleFactory"/>.
    /// </summary>
    private static void CreateServoToken(Artifact source, Player owner, ZoneService? zones)
    {
        var controller = source.Controller ?? owner;

        var spec = new TokenFactory.TokenSpec(
            Name: ServoTokenName,
            Power: ServoPower,
            Toughness: ServoToughness,
            Subtypes: new[] { CardSubtype.Servo },
            Keywords: null,
            Colors: Array.Empty<ManaColor>());

        var token = TokenFactory.CreateOnBattlefield(spec, controller, zones);

        // CR 111.1 — Servo tokens are artifact creatures. TokenFactory
        // creates a Creature shell; stamp Artifact additively so the token
        // reports both types (same multi-type pattern as Animation Module's
        // Servo and Hangarback Walker's Thopters).
        token.AddCardType(CardType.Artifact);
    }
}
