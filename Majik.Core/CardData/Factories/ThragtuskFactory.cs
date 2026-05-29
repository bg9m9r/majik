using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thragtusk (Magic 2013, {4}{G}).
///
/// Creature — Beast 5/3. Oracle text (verified against Scryfall):
///   "When this creature enters, you gain 5 life.
///    When this creature leaves the battlefield, create a 3/3 green
///    Beast creature token."
///
/// Thragtusk is the archetypal "value-on-both-ends" midrange creature:
/// a five-life swing on the way in, and a 3/3 body left behind however
/// it leaves the battlefield (dies / bounce / exile / flicker all
/// qualify under "leaves the battlefield").
///
/// The card's base shape (name, Creature, Beast subtype, {4}{G}, 5/3)
/// is materialised from the embedded JSON definition
/// (<c>thragtusk.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed triggered
/// abilities are layered on top here — the JSON <c>AbilityDefinition</c>
/// schema doesn't yet express ETB life-gain or LTB token-creation, so
/// they live in the factory (same posture as the other JSON-backed
/// cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 5/3 Creature — Beast at {4}{G}, owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> over the standard
///   "when ~ enters" condition (<see cref="Triggers.OnEnterBattlefieldSelf"/>).
///   On resolution the controller gains 5 life via
///   <see cref="Player.GainLife"/> (CR 119.3). No targets.
/// - <b>LTB triggered ability (CR 603.6c / CR 603.10c)</b>: fires
///   whenever Thragtusk moves OUT of the battlefield to any destination
///   (the printed "leaves the battlefield" — dies, bounce, exile,
///   flicker all qualify), filtered via
///   <see cref="CardMovedEvent"/> with <c>FromZone == Battlefield</c>
///   (same shape as <see cref="SkyclaveApparitionFactory"/>'s LTB and
///   <see cref="WurmcoilEngineFactory"/>'s death tokens). On resolution
///   the controller creates one 3/3 green Beast creature token (CR 111.4
///   — green stamped via <see cref="TokenFactory.TokenSpec.Colors"/>).
///   <c>activeZones = Battlefield</c> matches CR 603.6d's "looks back in
///   time" semantics for leaves-the-battlefield abilities.
///
/// ## Notes
/// - The token's controller is captured as Thragtusk's controller at
///   trigger resolution (<c>card.Controller ?? owner</c>) — the printed
///   "create a … token" puts it under the ability's controller (CR 111.4).
/// - <b>LTB unregister</b>: when Thragtusk leaves, the trigger's
///   <c>activeZones</c> Battlefield guard short-circuits any further
///   firing (CR 603.6d), so no explicit unregister is required.
/// </summary>
[CardName("Thragtusk")]
public static class ThragtuskFactory
{
    public const string CardName = "Thragtusk";
    public const string Slug = "thragtusk";
    public const int LifeGain = 5;
    public const int TokenPower = 3;
    public const int TokenToughness = 3;

    /// <summary>
    /// Construct Thragtusk with no live wiring. Both triggered abilities
    /// are attached for shape observability; neither is registered with a
    /// <see cref="TriggerManager"/>, and the LTB token bypasses
    /// <see cref="ZoneService"/> when the effect is invoked manually.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Thragtusk with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the LTB Beast token's ETB
    /// routes through <see cref="ZoneService.MoveCardTo"/> so its
    /// <see cref="CardMovedEvent"/> publishes for downstream ETB
    /// subscribers (Soul Warden etc.).</param>
    /// <param name="triggers">When supplied, both triggered abilities
    /// register with the bus so their respective events land them on the
    /// stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Beast subtype, {4}{G}, 5/3). The JSON carries no abilities — the
        // ETB life-gain and LTB token are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 119.3.
        //   "When this creature enters, you gain 5 life."
        // No targets — pure life-gain on the controller.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName} — you gain {LifeGain} life (CR 119.3)",
            () => (card.Controller ?? owner).GainLife(LifeGain));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 603.6a — ETB trigger active while on the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c / CR 111.4.
        //   "When this creature leaves the battlefield, create a 3/3 green
        //    Beast creature token."
        // Fires whenever Thragtusk moves OUT of Battlefield (any
        // destination — dies / bounce / exile / flicker), same FromZone ==
        // Battlefield shape as Skyclave Apparition's LTB.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName} — create a 3/3 green Beast creature token (CR 111.4)",
            () =>
            {
                var controller = card.Controller ?? owner;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Beast",
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Beast },
                    // CR 105 / CR 111.4 — printed "green" token.
                    Colors: new[] { ManaColor.Green });
                TokenFactory.CreateOnBattlefield(spec, controller, zones);
            });

        var ltb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — leaves-the-battlefield abilities look back in
            // time at the game state just before the event; ActiveZones =
            // Battlefield matches Skyclave Apparition / Wurmcoil Engine.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltb);
        triggers?.RegisterTriggeredAbility(ltb);

        return card;
    }
}
