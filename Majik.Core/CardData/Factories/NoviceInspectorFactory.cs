using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Novice Inspector (Murders at Karlov Manor, {W}).
///
/// Creature — Human Detective 1/2. Oracle text (verified against Scryfall):
///   "When this creature enters, investigate. (Create a Clue token. It's
///    an artifact with '{2}, Sacrifice this token: Draw a card.')"
///
/// Novice Inspector is a Murders at Karlov Manor reprint of Thraben
/// Inspector — same one-mana 1/2 body, same ETB Investigate — the only
/// difference is the printed second creature type (Detective rather than
/// Soldier), which is mechanically inert flavour. It shares the Clue
/// primitive with Thraben Inspector / Bygone Bishop / Tireless Tracker.
///
/// ## Implemented (v1)
/// - 1/2 Creature — Human Detective at {W}. The base shape (name, Creature
///   type, Human + Detective subtypes, cost, P/T) is materialised from the
///   embedded JSON definition (<c>novice-inspector.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>. The JSON carries no
///   abilities — the ETB Investigate trigger is layered on below.
/// - <b>ETB triggered ability (CR 603.6a)</b> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>: on resolve it
///   investigates (CR 701.39), creating one Clue token under the
///   controller through the shared <see cref="TokenFactory.CreateClue"/>
///   helper. The Clue is a colourless artifact carrying the built-in
///   "{2}, Sacrifice this token: Draw a card." activated ability — the
///   same Clue primitive used by Thraben Inspector / Tireless Tracker.
///
/// Investigate is the only printed ability, so there are no deferred gaps —
/// the card is fully expressed by the existing engine.
///
/// ## Wiring
/// - Single-arg <c>Create(Player)</c>: trigger attached for shape
///   inspection; the Clue the effect would create bypasses
///   <see cref="ZoneService"/> when invoked manually. Suitable for
///   dispatcher / shape tests. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - Three-arg overload: when <paramref name="triggers"/> is supplied the
///   ETB trigger is registered with the manager so a matching enter event
///   automatically queues the Investigate effect; when
///   <paramref name="zoneService"/> is supplied the Clue is placed onto
///   the battlefield via the ZoneService so its arrival event fires.
/// </summary>
[CardName("Novice Inspector")]
public static class NoviceInspectorFactory
{
    public const string CardName = "Novice Inspector";
    public const string Slug = "novice-inspector";

    /// <summary>
    /// Construct Novice Inspector with no live runtime services. The ETB
    /// Investigate trigger is attached for shape inspection.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Novice Inspector. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered so bus events queue it automatically; when
    /// <paramref name="zoneService"/> is supplied the Clue token is placed
    /// via the ZoneService.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature
        // type, Human + Detective subtypes, {W}, 1/2). The JSON carries no
        // abilities — the ETB Investigate trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB — "When this creature enters, investigate." CR 603.6a /
        // CR 701.39 (Investigate): create one Clue token under the
        // controller via the shared TokenFactory.CreateClue helper.
        // ----------------------------------------------------------------
        var investigateEffect = new Effect(
            $"{CardName}: ETB — investigate (create a Clue token, CR 701.39)",
            () => TokenFactory.CreateClue(card.Controller ?? owner, zoneService));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { investigateEffect },
            // CR 603.6a — ETB trigger only active while on the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
