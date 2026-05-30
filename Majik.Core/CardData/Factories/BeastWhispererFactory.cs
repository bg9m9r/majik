using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Beast Whisperer (Guilds of Ravnica, {2}{G}{G}).
/// Creature — Elf Druid 2/3. Oracle text (verified against Scryfall):
///   "Whenever you cast a creature spell, draw a card."
///
/// The base shape (name, Creature, Elf + Druid subtypes, {2}{G}{G}, 2/3)
/// is materialised from the embedded JSON definition
/// (<c>beast-whisperer.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed
/// cast-trigger is layered on here — the JSON <c>AbilityDefinition</c>
/// schema doesn't yet express cast-triggered draw, so it lives in the
/// factory (same posture as <see cref="StormscaleScionFactory"/> /
/// <see cref="EmberheartChallengerFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Cast trigger</b> (CR 603.1, fires off <see cref="SpellCastEvent"/>):
///   predicate is <c>spell.Controller == this card's controller</c> AND the
///   spell's card has the Creature card type (CR 302.1). Effect draws one
///   card under the controller via
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/> — routes through the
///   controller's replacement bus when one is attached (CR 614, Dredge /
///   etc.). Identical shape to <see cref="SramSeniorEdificerFactory"/>'s
///   cast trigger, with the subtype gate widened to "any creature spell".
/// - <b>Self-trigger</b>: casting Beast Whisperer itself does NOT trigger
///   its own ability — the trigger is only active while Beast Whisperer is
///   on the battlefield (CR 603.6a; <c>activeZones = {Battlefield}</c>) and
///   the card is still on the stack when its own <see cref="SpellCastEvent"/>
///   fires (same posture as <see cref="BygoneBishopFactory"/> / Sram /
///   Monastery Mentor).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Trigger is attached for
///   inspection; not registered (no live trigger manager).
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. Trigger registered with <paramref name="triggers"/> so
///   <see cref="SpellCastEvent"/>s published on the bus route through it.
///
/// ## Deferred (v1 gaps)
/// - <b>Draw-replacement target</b>: the printed text says "draw a card"
///   unconditionally; replacement effects (e.g. Dredge) attached to the
///   controller are already honoured inside
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/>.
/// </summary>
[CardName("Beast Whisperer")]
public static class BeastWhispererFactory
{
    public const string CardName = "Beast Whisperer";
    public const string Slug = "beast-whisperer";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Beast Whisperer with no live wiring. The cast-trigger is
    /// attached to the card shape; not registered (no trigger manager
    /// supplied). Suitable for dispatcher / shape tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Beast Whisperer with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the cast-trigger is
    /// registered so <see cref="SpellCastEvent"/>s published on the bus
    /// route through it.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf + Druid subtypes, {2}{G}{G}, 2/3). The JSON carries no
        // abilities — the cast trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1.
        //   "Whenever you cast a creature spell, draw a card."
        // "You cast" → the spell's controller is this card's controller
        // (CR 109.5). Any creature spell qualifies (CR 302.1) — Artifact
        // Creatures / Enchantment Creatures still carry the Creature type
        // so HasType(Creature) covers them.
        // ----------------------------------------------------------------
        var castCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 109.5 — controller match for the printed "you cast".
            // Compare to the card's current controller at evaluation time
            // (owner closure is a safe fallback — Beast Whisperer cannot
            // change controller in any printed effect).
            var liveController = card.Controller ?? owner;
            if (!ReferenceEquals(e.Spell.Controller, liveController))
            {
                return false;
            }

            // CR 302.1 — creature spell filter. HasType reads the spell
            // card's full type set (additive Artifact/Enchantment Creatures
            // still qualify).
            return e.Spell.Card.HasType(CardType.Creature);
        });

        var drawEffect = new Effect(
            $"{CardName}: draw a card (whenever you cast a creature spell)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Majik.Core.Primitives.Fx.DrawCards(controller, 1);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { drawEffect },
            // CR 603.6a — cast trigger only active while Beast Whisperer is
            // on the battlefield (this also explains why casting Beast
            // Whisperer itself does NOT trigger: it is on the stack when its
            // own SpellCastEvent fires).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
