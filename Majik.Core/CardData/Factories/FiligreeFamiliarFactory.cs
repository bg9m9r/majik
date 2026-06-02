using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Filigree Familiar (Kaladesh, {3}).
///
/// Artifact Creature — Fox 2/2. Oracle text:
///   "When this creature enters, you gain 2 life.
///    When this creature dies, draw a card."
///
/// ## Shape source
/// Card identity (name, {3}, 2/2, Artifact + Creature — Fox) is loaded from
/// <c>Majik.Core/CardData/Cards/filigree-familiar.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two triggered abilities are
/// attached in code below — same posture as the suggested analogue
/// <see cref="SolemnSimulacrumFactory"/> (artifact creature with an ETB value
/// trigger and a dies trigger), but both effects here are unconditional (no
/// "you may"): gain 2 life on ETB, draw 1 on death.
///
/// ## Implemented (v1)
/// - 2/2 Fox with BOTH Artifact and Creature card types (CR 205.2a) — the JSON
///   lists both types; <see cref="CardDefinitionFactory"/> adds the secondary
///   type so artifact-matters effects see it.
/// - <b>ETB trigger (CR 603.6a)</b>: "you gain 2 life." Routed through
///   <see cref="Fx.GainLife"/> (CR 119.3) so life-gain replacement / triggers
///   and the <c>LifeChangedEvent</c> fire.
/// - <b>Dies trigger (CR 603.6c / 700.4)</b>: "draw a card." Fires on
///   Battlefield → Graveyard; active in both zones because
///   <see cref="Majik.Core.Zones.ZoneService"/> stamps the new zone before
///   publishing the <c>CardMovedEvent</c> (mirrors Solemn Simulacrum / Aven
///   Fisher). Draw routed through <see cref="Fx.DrawCards"/> so draw-replacement
///   + empty-library SBA loss fire per CR 121.1 / CR 704.5c.
/// </summary>
[CardName("Filigree Familiar")]
public static class FiligreeFamiliarFactory
{
    public const string CardName = "Filigree Familiar";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("filigree-familiar");

    /// <summary>
    /// Construct Filigree Familiar with both triggers attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Filigree Familiar with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both triggers are
    /// registered so the relevant <c>CardMovedEvent</c> places them on the
    /// stack automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you gain 2 life."  (CR 119.3)
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: you gain 2 life",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.GainLife(controller, 2); // CR 119.3
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Dies triggered ability — CR 603.6c / 700.4.
        //   "When this creature dies, draw a card."
        // Active in Battlefield + Graveyard: ZoneService stamps the zone
        // before publishing the CardMovedEvent (mirrors Solemn Simulacrum).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName} dies: draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1); // CR 121.1
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
