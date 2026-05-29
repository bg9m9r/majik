using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Llanowar Visionary (Dominaria, {2}{G}).
///
/// Creature — Elf Druid 2/2. Oracle text:
///   "When this creature enters, draw a card."
///   "{T}: Add {G}."
///
/// A green-mana "value dork": the ETB draw of
/// <see cref="ElvishVisionaryFactory"/> combined with the {T}: Add {G} mana
/// ability of a Llanowar-style mana producer (see
/// <see cref="IgnobleHierarchFactory"/> for the multi-colour mana-ability
/// pattern).
///
/// ## Shape source
/// Card identity (name, {2}{G}, 2/2, Creature — Elf Druid) is loaded from
/// <c>Majik.Core/CardData/Cards/llanowar-visionary.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Both abilities are attached in code
/// below (the JSON ability schema carries only identity here).
///
/// ## Implemented (v1)
/// - 2/2 Elf Druid (CR 205.3m) at {2}{G}.
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Llanowar Visionary enters
///   the battlefield, its controller draws a card. Routed through
///   <see cref="Fx.DrawCards"/> so the replacement bus and the empty-library
///   SBA loss flag (CR 704.5b) fire correctly per CR 121.1. No targets.
/// - <b>Mana ability (CR 605.1)</b>: <c>{T}: Add {G}</c> — a single
///   <see cref="ManaAbility"/> producing one green mana, gated on
///   <c>!IsTapped</c> (the tap cost lives inside
///   <see cref="ManaAbility.Activate"/>). Mana abilities don't use the stack
///   (CR 605.3a).
/// </summary>
[CardName("Llanowar Visionary")]
public static class LlanowarVisionaryFactory
{
    public const string CardName = "Llanowar Visionary";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("llanowar-visionary");

    /// <summary>
    /// Construct Llanowar Visionary with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>. The {T}: Add {G} mana ability is always
    /// wired. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Llanowar Visionary with optional event bus and trigger
    /// manager. When <paramref name="triggers"/> is supplied, the ETB trigger
    /// is registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// automatically places it on the stack (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        // Routed through Fx.DrawCards so the replacement bus + empty-library
        // SBA loss flag fire per CR 121.1 + CR 704.5b. No targets.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller draws a card on ETB",
            () => Fx.DrawCards(card.Controller ?? owner, 1));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {T}: Add {G} — mana ability (CR 605.1, doesn't use the stack
        // CR 605.3a). The tap cost is applied inside ManaAbility.Activate();
        // the canActivateCheck gates on !IsTapped so duplicate activations
        // are prevented.
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => !card.IsTapped));

        return card;
    }
}
