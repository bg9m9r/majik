using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sanguine Syphoner (Innistrad: Crimson Vow, {1}{B}).
///
/// Creature — Vampire Warlock 1/3. Oracle text (Scryfall verified):
///   "Whenever this creature attacks, each opponent loses 1 life and you gain
///    1 life."
///
/// ## Shape source
/// Card identity (name, {1}{B}, 1/3, Creature — Vampire Warlock) is loaded from
/// <c>Majik.Core/CardData/Cards/sanguine-syphoner.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The attack-trigger drain is attached in
/// code below — the attack-trigger (CR 508.1f) shape of
/// <see cref="ArchonOfCrueltyFactory"/> combined with the each-opponent /
/// life-gain drain of <see cref="MaraudingBlightPriestFactory"/> /
/// <see cref="CliffhavenVampireFactory"/>.
///
/// ## Implemented (v1)
/// - 1/3 Creature — Vampire Warlock (CR 205.3m) at {1}{B}, owner / controller
///   wired.
/// - <b>Attack triggered ability (CR 508.1f / CR 603.6a / CR 119.3)</b>:
///   "Whenever this creature attacks, each opponent loses 1 life and you gain
///   1 life." Wired via <see cref="Triggers.OnAttackSelf"/> consuming
///   <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/> filtered
///   to this card. No targets — "each opponent" is global (CR 109.5), read from
///   the LIVE resolution context via <see cref="ContextOpponents.Of"/> (the
///   resolver-null bug-class fix; mirrors Marauding Blight-Priest / Cliffhaven
///   Vampire). Each opponent loses 1 life (CR 119.3) and the controller gains
///   1 life (CR 119.3).
///
/// ## Lifecycle
/// - Single-arg <see cref="Create(Player)"/> attaches the trigger for shape
///   inspection but registers nothing. On the routed prod build the trigger is
///   auto-registered by zone (TriggerManager.BindCard) and reads opponents off
///   the live context at resolution.
/// - Full overload accepts an <see cref="IEventBus"/> + <see cref="TriggerManager"/>
///   so domain-fired <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>s
///   auto-queue the trigger onto the stack (CR 603.3).
/// </summary>
[CardName("Sanguine Syphoner")]
public static class SanguineSyphonerFactory
{
    public const string CardName = "Sanguine Syphoner";
    public const int LifeLossPerOpponent = 1;
    public const int LifeGain = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sanguine-syphoner");

    /// <summary>
    /// Construct Sanguine Syphoner with no live runtime services. The attack
    /// trigger is attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sanguine Syphoner. When <paramref name="triggers"/> is supplied,
    /// the trigger is registered so a self-keyed
    /// <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/> places it
    /// on the stack automatically (CR 603.3). "Each opponent" is read from the
    /// live resolution context at resolution (<see cref="ContextOpponents"/>), so
    /// the drain is correct on the production routed build.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / 603.6a / 119.3.
        //   "Whenever this creature attacks, each opponent loses 1 life and
        //    you gain 1 life."
        // Triggers.OnAttackSelf fires on CreatureAttacksEvent matching this
        // card. No targets — "each opponent" is global (CR 109.5), read from
        // the LIVE resolution context (resolver-null bug-class fix; mirrors
        // Marauding Blight-Priest / Cliffhaven Vampire).
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: each opponent loses {LifeLossPerOpponent} life and you gain {LifeGain} life",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(LifeLossPerOpponent);
                }
                controller.GainLife(LifeGain);
                return ValueTask.CompletedTask;
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        _ = eventBus;

        return card;
    }
}
