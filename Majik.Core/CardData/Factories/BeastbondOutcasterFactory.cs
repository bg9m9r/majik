using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Beastbond Outcaster (Bloomburrow, {2}{G}).
/// Creature — Human Druid 3/3. Oracle text (verified against Scryfall):
///   "When this creature enters, if you control a creature with power 4 or
///    greater, draw a card.
///    Plot {1}{G} (You may pay {1}{G} and exile this card from your hand.
///    Cast it as a sorcery on a later turn without paying its mana cost.
///    Plot only as a sorcery.)"
///
/// The base shape (name, Creature, Human + Druid subtypes, {2}{G}, 3/3) is
/// materialised from the embedded JSON definition
/// (<c>beastbond-outcaster.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The conditional ETB draw is
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// intervening-if ETB triggers (same posture as
/// <see cref="MischievousMysticFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Conditional ETB draw (CR 603.6a + intervening-if CR 603.4)</b> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>: "When this creature enters,
///   if you control a creature with power 4 or greater, draw a card." The
///   "if you control a creature with power 4 or greater" clause is an
///   intervening-if condition (CR 603.4) — it is checked again on resolution,
///   and the effect does nothing if it is no longer true. The condition is
///   evaluated over the controller's battlefield creatures' effective power
///   (CR 613 Layer 7 — <see cref="Creature.Power"/>), so a pumped creature
///   (or a pumped Outcaster itself) qualifies. The Outcaster's own 3/3 base
///   power does NOT meet the threshold, so an unpumped solo Outcaster draws
///   nothing.
///
/// ## Deferred (v1 gaps)
/// - <b>Plot (CR 718)</b>: the printed "Plot {1}{G}" rider is NOT yet wired.
///   Plot is a Bloomburrow/OTJ mechanic — a cast-from-exile-on-a-later-turn
///   alternative cost with sorcery-speed semantics. No
///   activated-from-hand-with-alt-cost + "plotted card may be cast later for
///   {0}" primitive exists in the engine yet (see
///   <see cref="SlickshotShowOffFactory"/> for the full gap analysis). The
///   conditional ETB body ships; Plot is deferred until its primitive lands.
///   Bot evaluation treats the Outcaster as a vanilla 3/3 with the
///   conditional cantrip rider until Plot ships.
/// </summary>
[CardName("Beastbond Outcaster")]
public static class BeastbondOutcasterFactory
{
    public const string CardName = "Beastbond Outcaster";
    public const string Slug = "beastbond-outcaster";
    public const int PowerThreshold = 4;

    /// <summary>
    /// Construct Beastbond Outcaster with no live trigger-manager wiring. The
    /// ETB trigger is attached for shape inspection. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Beastbond Outcaster with optional event bus + trigger manager.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a battlefield-enter event queues it automatically.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Human +
        // Druid, {2}{G}, 3/3). No abilities in the JSON — the conditional ETB
        // is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "When this creature enters, if you control a creature with power 4
        // or greater, draw a card." CR 603.6a (ETB) + CR 603.4 (intervening
        // if — re-checked on resolution).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — if you control a creature with power {PowerThreshold} or greater, draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 603.4 — intervening-if: only draw if the condition is
                // still true on resolution. Effective power (CR 613 Layer 7)
                // so a pumped creature qualifies.
                var controlsBigCreature = controller.Zones.Battlefield
                    .GetCards()
                    .OfType<Creature>()
                    .Any(c => c.Zone == ZoneType.Battlefield
                              && c.Power >= PowerThreshold);

                if (controlsBigCreature)
                {
                    Fx.DrawCards(controller, 1);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
