using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wurmcoil Engine (Scars of Mirrodin, {6}).
///
/// Artifact Creature — Phyrexian Wurm 6/6. Oracle text:
///   "Deathtouch, lifelink
///    When Wurmcoil Engine dies, create a 3/3 colorless Phyrexian Wurm
///    artifact creature token with deathtouch and a 3/3 colorless Phyrexian
///    Wurm artifact creature token with lifelink."
///
/// ## Implemented (v1)
/// - 6/6 Artifact Creature — Phyrexian Wurm, mana cost {6}. The shell is a
///   <see cref="Creature"/> with <see cref="CardType.Artifact"/> added via
///   <c>AddCardType</c> so the permanent has both card types (CR 301.1 /
///   302.1 — the "Artifact Creature" multi-type pattern; mirrors Walking
///   Ballista).
/// - Deathtouch + Lifelink wired as <see cref="KeywordAbility"/> markers.
///   <see cref="Majik.Core.Combat.CombatAbilities"/> consumes these in the
///   combat damage / lethal-damage paths.
/// - <b>Dies trigger (CR 603.6c / 700.4)</b>: When Wurmcoil Engine moves
///   from Battlefield to Graveyard, the controller creates two 3/3
///   colorless Phyrexian Wurm artifact creature tokens — one with
///   Deathtouch and one with Lifelink. Token creation routes through
///   <see cref="TokenFactory.CreateOnBattlefield"/> (which marks each
///   creature token as an Artifact via <c>AddCardType</c> below) and uses
///   <see cref="ZoneService"/> when supplied so ETB triggers on the
///   tokens fire (e.g. Soul Warden). The trigger registers
///   activeZones = {Battlefield, Graveyard} so it still matches once the
///   card has been moved to the graveyard by ZoneService prior to the
///   <see cref="CardMovedEvent"/> publish.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: the dies trigger always creates both
///   tokens (the printed text has no "may" — both creation clauses are
///   mandatory, so this is faithful to the oracle, but the engine has
///   no other choices to defer here).
/// </summary>
[CardName("Wurmcoil Engine")]
public static class WurmcoilEngineFactory
{
    /// <summary>
    /// Construct Wurmcoil Engine with no live ZoneService / TriggerManager
    /// wiring (the shape/dispatcher path). The dies trigger is attached but
    /// not registered; token creation uses raw zone moves — suitable for
    /// unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Wurmcoil Engine with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, the dies-trigger token
    /// creation routes through <see cref="TokenFactory.CreateOnBattlefield"/>
    /// using the service so the tokens' battlefield-entry CardMovedEvent
    /// fires for downstream listeners (Soul Warden etc.). When
    /// <paramref name="triggers"/> is supplied, the dies trigger is
    /// registered so a <see cref="CardMovedEvent"/> from battlefield to
    /// graveyard places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Wurmcoil Engine",
            manaCost: "{6}",
            power: 6,
            toughness: 6,
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Wurm });

        // CR 301.1 / 302.1 — Wurmcoil Engine is an Artifact Creature. The
        // base Creature constructor only registers CardType.Creature, so
        // additively flag the Artifact type for HasType-based lookups
        // (mirrors Walking Ballista's multi-type shape).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Evergreen keywords (CR 702.2 Deathtouch, CR 702.15 Lifelink).
        // CombatAbilities.HasDeathtouch / HasLifelink consume these markers.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Deathtouch", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c / 700.4.
        //   "When Wurmcoil Engine dies, create a 3/3 colorless Phyrexian
        //    Wurm artifact creature token with deathtouch and a 3/3
        //    colorless Phyrexian Wurm artifact creature token with
        //    lifelink."
        // Fires once on a Battlefield → Graveyard CardMovedEvent. Both
        // tokens are created — the oracle text has no "may".
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            "Wurmcoil Engine: create deathtouch + lifelink Wurm tokens",
            () => CreateWurmTokens(owner, zoneService));

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // activeZones: Battlefield + Graveyard so the trigger still
            // matches after ZoneService stamps card.Zone = Graveyard
            // before publishing the CardMovedEvent (same pattern as Undying).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// Create the two 3/3 colorless Phyrexian Wurm artifact creature
    /// tokens — one with Deathtouch and one with Lifelink — under
    /// <paramref name="controller"/> on the battlefield. Each token is
    /// flagged with <see cref="CardType.Artifact"/> in addition to its
    /// base <see cref="CardType.Creature"/> so it matches HasType lookups
    /// for both types ("artifact creature token").
    /// </summary>
    private static (Creature deathtouch, Creature lifelink) CreateWurmTokens(
        Player controller, ZoneService? zoneService)
    {
        // CR 105.2c / CR 111.4 — printed "3/3 colorless Phyrexian Wurm
        // artifact creature token". Empty Colors list = explicit
        // colourless.
        var dtSpec = new TokenFactory.TokenSpec(
            Name: "Phyrexian Wurm",
            Power: 3,
            Toughness: 3,
            Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Wurm },
            Keywords: new[] { "Deathtouch" },
            Colors: Array.Empty<Majik.Core.ValueObjects.ManaColor>());

        var llSpec = new TokenFactory.TokenSpec(
            Name: "Phyrexian Wurm",
            Power: 3,
            Toughness: 3,
            Subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Wurm },
            Keywords: new[] { "Lifelink" },
            Colors: Array.Empty<Majik.Core.ValueObjects.ManaColor>());

        var dt = TokenFactory.CreateOnBattlefield(dtSpec, controller, zoneService);
        var ll = TokenFactory.CreateOnBattlefield(llSpec, controller, zoneService);

        // CR 301.1 / 302.1 — flag both tokens as Artifact Creature so
        // HasType(Artifact) returns true (Wurmcoil's printed text mandates
        // "artifact creature token").
        dt.AddCardType(CardType.Artifact);
        ll.AddCardType(CardType.Artifact);

        return (dt, ll);
    }
}
