using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vengeful Tracker (Murders at Karlov Manor, {1}{R}).
///
/// Creature — Human Detective 2/2. Oracle text (Scryfall, verified):
///   "Whenever an opponent sacrifices an artifact, this creature deals 2
///    damage to them."
///
/// ## Pure-JSON factory (declarative opponent-sacrifices trigger + untargeted payoff)
/// Vengeful Tracker is fully declarative — the opponent-scoped sacrifice trigger
/// is expressed by the <c>whenever_an_opponent_sacrifices_permanent</c>
/// (<see cref="WheneverAnOpponentSacrificesPermanentTriggerDef"/>) variant gated
/// to a sacrificed <c>Artifact</c> (CR 205.2), and the "deals 2 damage to them"
/// payoff by the untargeted <c>deal_damage_to_triggering_player</c>
/// (<see cref="DealDamageToTriggeringPlayerEffectDef"/>) verb — which reads "them"
/// (CR 603.3 "that player", the sacrificing opponent the trigger STAMPS onto the
/// resolving ability via
/// <see cref="Majik.Core.Abilities.TriggeredAbility.SetTriggeringPlayer"/>) off
/// <see cref="Majik.Core.Abilities.ResolutionContext.TriggeringPlayer"/> at
/// resolution. No target slot, no agent prompt. All materialised by
/// <see cref="CardDefRuntime"/> from <c>vengeful-tracker.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>.
///
/// This is the first SHIPPED card to consume the <b>opponent-scoped</b>
/// declarative sacrifice trigger surface (Mortician Beetle's
/// <c>whenever_a_player_sacrifices_permanent</c> covers the any-player mirror;
/// It That Betrays' hand-rolled factory covers the opponent-scoped steal). It
/// validates the full
/// <see cref="Majik.Core.Events.PermanentSacrificedEvent"/> → opponent-gate →
/// triggering-player-stamp → untargeted-payoff path end-to-end through the
/// declarative trigger + effect surface (CR 701.16a credits the cost-payer as the
/// sacrificing player on every real sacrifice path).
///
/// - <b>Opponent-sacrifices-an-artifact trigger (CR 603.1 + CR 701.16 +
///   CR 109.5)</b>: fires on the dedicated
///   <see cref="Majik.Core.Events.PermanentSacrificedEvent"/> off a player OTHER
///   than the controller (CR 102.2 — every other player is an opponent; the
///   controller is resolved live so a control change carries the trigger) who
///   sacrifices a permanent with the
///   <see cref="Majik.Core.Cards.Types.CardType.Artifact"/> type. A sacrificed
///   <em>token</em> artifact (a Treasure) fires it too (no nontoken filter —
///   distinct from It That Betrays).
/// - <b>"Deals 2 damage to them" (CR 119 / CR 603.3)</b>: the untargeted
///   <c>deal_damage_to_triggering_player</c> verb punishes the sacrificing
///   opponent the trigger identified — no chosen target, routed through
///   <see cref="Majik.Core.Primitives.Fx.DealDamage(object, int)"/> so the loss
///   feeds Spectacle / Revolt / lifegain observers.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Vengeful Tracker")]
public static class VengefulTrackerFactory
{
    public const string CardName = "Vengeful Tracker";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "vengeful-tracker";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Vengeful Tracker with no live <see cref="TriggerManager"/>
    /// wiring. The opponent-sacrifices trigger is materialised onto the card shape
    /// from the JSON definition for structural / dispatch tests. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Vengeful Tracker with an optional <see cref="TriggerManager"/>
    /// and <see cref="ReplacementBus"/>. When <paramref name="triggers"/> is
    /// supplied the declarative opponent-sacrifices trigger is registered so a
    /// qualifying <see cref="Majik.Core.Events.PermanentSacrificedEvent"/>
    /// auto-queues the ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner, replacements);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        if (triggers != null)
        {
            foreach (var trigger in card.Abilities.OfType<TriggeredAbility>())
            {
                triggers.RegisterTriggeredAbility(trigger);
            }
        }

        return card;
    }
}
