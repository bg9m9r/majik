using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Akroan Crusader (Theros, {R}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "Haste.
///    Heroic — Whenever you cast a spell that targets Akroan Crusader,
///    create a 1/1 red Soldier creature token with haste."
///
/// ## Implemented (v1)
///
/// - 1/1 Creature — Human Soldier at {R}, owner/controller wired.
/// - <b>Haste</b> keyword marker (CR 702.10) via
///   <see cref="KeywordAbility"/>.
/// - <b>Heroic trigger (CR 702.85 / CR 603.1)</b> — fires on a
///   <see cref="SpellCastEvent"/> whose
///   <see cref="Majik.Core.Spells.ISpell.Controller"/> matches Crusader's
///   controller AND whose target list contains Crusader itself (same
///   predicate shape as <see cref="FavoredHopliteFactory"/>). On
///   resolution: create one 1/1 red Soldier creature token with Haste via
///   <see cref="TokenFactory.CreateOnBattlefield"/>.
///
/// ## Token (CR 111.4 / CR 105 / CR 702.10)
///
/// - 1/1 red Soldier with Haste keyword marker. Colour stamped via
///   <see cref="TokenFactory.TokenSpec.Colors"/> (one-element list,
///   <see cref="ManaColor.Red"/>). The Haste keyword is wired by
///   <see cref="TokenFactory.CreateOnBattlefield"/> as a
///   <see cref="KeywordAbility"/> marker on the token (same posture as
///   Stormchaser's Talent Mercenary's Flying — the token reads its own
///   keyword list off the card).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for
///   dispatcher visibility but not registered with a
///   <see cref="TriggerManager"/>; tokens produced by direct invocation
///   bypass <see cref="ZoneService"/>. Suitable for shape tests.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> —
///   fully wired. When <paramref name="triggers"/> is supplied the
///   Heroic trigger surfaces on a matching SpellCastEvent; when
///   <paramref name="zoneService"/> is supplied the token's ETB
///   <see cref="Majik.Core.Events.CardMovedEvent"/> fires via the
///   service.
/// </summary>
[CardName("Akroan Crusader")]
public static class AkroanCrusaderFactory
{
    public const string CardName = "Akroan Crusader";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Akroan Crusader with no live wiring. Heroic trigger is
    /// attached to the card for shape observability; no TriggerManager
    /// registration and tokens produced bypass <see cref="ZoneService"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Akroan Crusader with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the Heroic trigger is
    /// registered so a qualifying <see cref="SpellCastEvent"/>
    /// automatically queues the ability. When <paramref name="zoneService"/>
    /// is supplied the Soldier token is placed onto the battlefield via
    /// the service so its
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> fires for
    /// downstream ETB listeners.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste keyword marker.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // Heroic trigger — CR 702.85 / CR 603.1.
        //   "Whenever you cast a spell that targets Akroan Crusader,
        //    create a 1/1 red Soldier creature token with haste."
        // Predicate: spell.Controller is Crusader's controller AND at
        // least one chosen target references Crusader (CR 115.6).
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (!ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)) return false;
            return FavoredHopliteFactory.SpellTargetsCreature(e.Spell.Targets, card);
        });

        var tokenEffect = new Effect(
            $"{CardName}: heroic — create a 1/1 red Soldier creature token with haste",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateSoldierToken(controller, zoneService);
            });

        var heroicTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { tokenEffect },
            // CR 603.6a — only active while Crusader is on the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(heroicTrigger);
        triggers?.RegisterTriggeredAbility(heroicTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.4 — create one 1/1 red Soldier creature token with
    /// Haste under <paramref name="controller"/>'s control. Exposed for
    /// tests; mirrors the closure baked into the Heroic effect.
    /// </summary>
    public static Creature CreateSoldierToken(
        Player controller,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var spec = new TokenFactory.TokenSpec(
            Name: "Soldier",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Soldier },
            Keywords: new[] { "Haste" },
            // CR 105 / 111.4 — printed "1/1 red Soldier creature token
            // with haste". Single-colour red token.
            Colors: new[] { ManaColor.Red });

        return TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
    }
}
