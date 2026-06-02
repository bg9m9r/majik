using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Obstinate Baloth (Magic 2011 / reprints, {2}{G}{G}).
///
/// Creature — Beast 4/4. Oracle text (current Scryfall):
///   "When this creature enters, you gain 4 life.
///    If a spell or ability an opponent controls causes you to discard this
///    card, put it onto the battlefield instead of putting it into your
///    graveyard."
///
/// ## Shape source
/// Card identity (name, {2}{G}{G}, 4/4, Creature — Beast) is loaded from
/// <c>Majik.Core/CardData/Cards/obstinate-baloth.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The ETB lifegain trigger is attached in
/// code below — same posture as <see cref="KitchenFinksFactory"/> (self-ETB
/// "you gain N life") and <see cref="BorderlandRangerFactory"/>.
///
/// ## Implemented (v1)
/// - 4/4 Creature — Beast at {2}{G}{G}.
/// - <b>ETB triggered ability (CR 603.6a + CR 119.3)</b>: "When this creature
///   enters, you gain 4 life." Wired over the self-ETB
///   <see cref="CardMovedEvent"/> (the moved card is this Baloth and the
///   destination is Battlefield); on resolution the controller gains 4 life.
///   Controller is resolved live (<c>card.Controller ?? owner</c>) so a
///   control-change effect routes the gain to the current controller.
///
/// ## Deferred (v1)
/// - <b>Discard-replacement clause</b> ("If a spell or ability an opponent
///   controls causes you to discard this card, put it onto the battlefield
///   instead of putting it into your graveyard." — a CR 614 replacement
///   effect). The engine's discard funnel (a Hand → Graveyard
///   <see cref="Effects.ZoneMoveIntent"/>, the same funnel Madness rides) does
///   NOT record WHO caused the discard: neither
///   <see cref="Effects.ZoneMoveIntent"/> nor <see cref="ZoneMoveReason"/>
///   carries a "discarded because of a spell/ability an opponent controls"
///   marker, and the engine has no concept of opponent-caused-discard
///   attribution. Implementing the replacement unconditionally would be WRONG
///   — it would also fire on your own cleanup discard (CR 514.2) and your own
///   discard effects (Faithless Looting), which the printed clause must not.
///   Deferred until the discard funnel carries cause/controller attribution.
///   The ETB lifegain — the body of the card's value in play — is fully wired.
/// </summary>
[CardName("Obstinate Baloth")]
public static class ObstinateBalothFactory
{
    public const string CardName = "Obstinate Baloth";
    public const int LifeGainAmount = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("obstinate-baloth");

    /// <summary>
    /// Construct Obstinate Baloth with its ETB lifegain trigger attached to the
    /// card shape but NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Obstinate Baloth with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <see cref="CardMovedEvent"/> queues the
    /// ability automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 119.3.
        //   "When this creature enters, you gain 4 life."
        // Fires on every ETB of this Baloth. Controller resolved live so a
        // control-change effect routes the gain to the current controller.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: you gain {LifeGainAmount} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGainAmount);
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
