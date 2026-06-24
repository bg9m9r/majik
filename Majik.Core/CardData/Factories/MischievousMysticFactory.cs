using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for Mischievous Mystic (Wilds of Eldraine, {1}{U}).
/// Creature — Human Wizard 2/1. Oracle text (verified against Scryfall):
///   "Flying
///    Whenever you draw your second card each turn, create a 1/1 blue Faerie
///    creature token with flying."
///
/// The base shape (name, Creature, Human + Wizard subtypes, {1}{U}, 2/1) is
/// materialised from the embedded JSON definition
/// (<c>mischievous-mystic.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flying keyword marker and
/// the "you draw your second card each turn" trigger are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express keyword markers
/// or draw-count triggers (same posture as
/// <see cref="FaerieMastermindFactory"/>, which carries the mirror
/// "opponent draws their second card" counter).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9)</b> — keyword marker via <see cref="KeywordAbility"/>.
///   Block restrictions enforced by <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>"Whenever you draw your second card each turn, create a 1/1 blue Faerie
///   creature token with flying." (CR 603.2 / 603.3 / 121.1)</b> — a
///   <see cref="TriggeredAbility"/> over <see cref="CardDrawnEvent"/>. The
///   controller's own per-turn draw count is held in a closure private to this
///   card instance; the predicate increments the count on every
///   <see cref="CardDrawnEvent"/> whose player IS the controller (CR 109.5 —
///   "you draw") and matches only on the exact transition to the second draw
///   (CR 603.3 — a trigger fires only when its condition becomes true; the
///   third+ draw does not retrigger). Opponents' draws never match. The count
///   resets on a <see cref="TurnStartedEvent"/> (CR 500.1) when an event bus is
///   supplied. Effect = <see cref="TokenFactory.CreateOnBattlefield"/> mints a
///   1/1 blue Faerie token with the Flying keyword (CR 111.4) under the
///   controller.
/// </summary>
[CardName("Mischievous Mystic")]
public static class MischievousMysticFactory
{
    public const string CardName = "Mischievous Mystic";
    public const string Slug = "mischievous-mystic";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Mischievous Mystic with no live runtime wiring (the
    /// dispatcher / shape path). Flying + the second-draw trigger are attached
    /// for shape observability; the per-turn count is never reset and the token
    /// is minted directly onto the battlefield (no ZoneService). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Mischievous Mystic with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus. When supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the per-turn draw count
    /// (CR 500.1). May be null.</param>
    /// <param name="triggers">TriggerManager the second-draw trigger registers
    /// with so a <see cref="CardDrawnEvent"/> lands it on the stack. May be
    /// null.</param>
    /// <param name="zoneService">ZoneService used to mint the Faerie token so a
    /// CardMovedEvent fires on ETB (triggers Soul Warden etc.). May be null.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human +
        // Wizard, {1}{U}, 2/1). No abilities in the JSON — Flying + the trigger
        // are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. Block restrictions enforced by CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        AddSecondDrawTrigger(card, owner, eventBus, triggers, zoneService);

        return card;
    }

    // -----------------------------------------------------------------------
    // "Whenever you draw your second card each turn, create a 1/1 blue Faerie
    // creature token with flying." (CR 603.2 / 603.3 / 121.1.)
    // -----------------------------------------------------------------------
    private static void AddSecondDrawTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        // The controller's draw count this turn. Closure shared between the
        // trigger predicate and the TurnStartedEvent reset handler.
        var drawsThisTurn = 0;

        var condition = new EventTriggerCondition<CardDrawnEvent>((e, _) =>
        {
            // "you draw" — only the controller's own draws match
            // (CR 109.5 / 102.1). Opponents' draws never count.
            if (!ReferenceEquals(e.Player, card.Controller ?? owner)) return false;

            drawsThisTurn++;

            // CR 603.3 — fire only on the exact transition to the second draw;
            // the third+ draw this turn does not retrigger.
            return drawsThisTurn == 2;
        });

        var createTokenEffect = new Effect(
            $"{CardName}: create a 1/1 blue Faerie creature token with flying (you drew your second card this turn)",
            () =>
            {
                // CR 111.4 — 1/1 blue Faerie with Flying under the controller.
                var controller = card.Controller ?? owner;
                var spec = new TokenFactory.TokenSpec(
                    Name: "Faerie",
                    Power: 1,
                    Toughness: 1,
                    Subtypes: new[] { CardSubtype.Faerie },
                    Keywords: new[] { "Flying" },
                    Colors: new[] { ManaColor.Blue });
                TokenFactory.CreateOnBattlefield(spec, controller, zoneService);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { createTokenEffect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 500.1 — reset the per-turn count when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => drawsThisTurn = 0);
        }
    }
}
