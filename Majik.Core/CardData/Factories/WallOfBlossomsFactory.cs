using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Blossoms (Tempest / various reprints,
/// {1}{G}).
///
/// Creature — Plant Wall 0/4. Oracle text (verified against Scryfall):
///   "Defender.
///    When this creature enters, draw a card."
///
/// Functionally identical to <see cref="WallOfOmensFactory"/> — a Defender
/// wall with an ETB "draw a card" trigger; only the colour ({G} vs {W}),
/// the cost ({1}{G}), and the Plant subtype differ.
///
/// The base shape (name, Creature, Plant + Wall subtypes, {1}{G}, 0/4) is
/// materialised from the embedded JSON definition
/// (<c>wall-of-blossoms.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed Defender keyword
/// and the ETB draw trigger are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or
/// card-draw effects, so those live in the factory (same posture as
/// <see cref="TwinSilkSpiderFactory"/>).
///
/// ## Implemented (v1)
/// - 0/4 <see cref="Creature"/> — Plant Wall at {1}{G} (green derived from
///   the mana cost).
/// - <b>Defender (CR 702.3)</b> attached as a <see cref="KeywordAbility"/>
///   marker so combat block-legality surfaces
///   (<c>CombatAbilities.HasDefender</c>) observe it (can block, can't
///   attack) — same shape as <see cref="WallOfOmensFactory"/>'s Defender.
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Wall of Blossoms enters
///   the battlefield, its controller draws a card (top of library → hand).
///   If the library is empty, <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
///   is called so SBAs resolve loss on the next pass (CR 104.3a / CR 704.5b).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — when
///   <paramref name="triggers"/> is supplied the ETB trigger registers so a
///   <see cref="CardMovedEvent"/> to the battlefield lands the ability on
///   the stack automatically (CR 603.3).
/// </summary>
[CardName("Wall of Blossoms")]
public static class WallOfBlossomsFactory
{
    public const string CardName = "Wall of Blossoms";
    public const string Slug = "wall-of-blossoms";
    private const string DefenderKeyword = "Defender";

    /// <summary>
    /// Construct Wall of Blossoms with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Wall of Blossoms with optional event bus and trigger
    /// manager. When <paramref name="triggers"/> is supplied, the ETB
    /// trigger is registered so a <see cref="CardMovedEvent"/> to the
    /// battlefield automatically places it on the stack (CR 603.3).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus — unused in v1 but accepted for API
    /// symmetry with other ETB-draw factories.</param>
    /// <param name="triggers">Trigger manager to register the ETB ability
    /// with. May be null for shape / unit tests.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Plant + Wall subtypes, {1}{G}, 0/4). The JSON carries no
        // abilities — Defender + the ETB draw trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block-legality
        // (the card can block but can't attack).
        card.AddAbility(new KeywordAbility(DefenderKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        // Unconditionally draws 1 card for the controller on entering the
        // battlefield. No targets, no additional cost.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller draws a card on ETB",
            () =>
            {
                var controller = card.Controller ?? owner;
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 104.3a / CR 704.5b — empty library; SBA resolves
                    // loss on the next opportunity.
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }

                // Move Library → Hand (CR 121).
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
