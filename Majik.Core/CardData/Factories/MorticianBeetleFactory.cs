using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mortician Beetle (Conflux, {B}).
///
/// Creature — Insect 1/1. Oracle text (Scryfall, verified):
///   "Whenever a player sacrifices a creature, you may put a +1/+1 counter on
///    Mortician Beetle."
///
/// ## Pure-JSON factory (declarative trigger + free-optional + effect)
/// Mortician Beetle is fully declarative — the any-player sacrifice trigger is
/// expressed by the <c>whenever_a_player_sacrifices_permanent</c>
/// (<see cref="WheneverAPlayerSacrificesPermanentTriggerDef"/>) variant gated to
/// a sacrificed <c>Creature</c> (CR 205.2), the "you may" reflexive clause by the
/// generalized free-optional rider (<c>"optional": true</c> →
/// <see cref="FreeOptionalRider"/>, CR 603.4), and the payoff by the existing
/// <c>put_counter</c> self effect — all materialised by
/// <see cref="CardDefRuntime"/> from <c>mortician-beetle.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build(CardDefinition, Player, ReplacementBus?)"/>.
///
/// This is the first card to consume the <b>any-player</b> sacrifice
/// producer-side primitive declaratively (Mayhem Devil's hand-rolled factory
/// covers the same any-player shape with an any-target damage payoff; Vengeful
/// Tracker / It That Betrays cover the opponent-scoped variant). It validates the
/// full <see cref="Majik.Core.Events.PermanentSacrificedEvent"/> bus plumbing
/// end-to-end through the declarative trigger surface (CR 701.16a credits the
/// cost-payer as the sacrificing player on every real sacrifice path).
///
/// - <b>Any-player sacrifices-a-creature trigger (CR 603.1 + CR 701.16 +
///   CR 700.6)</b>: fires on the dedicated
///   <see cref="Majik.Core.Events.PermanentSacrificedEvent"/> off ANY player's
///   sacrifice (the controller's own included) of a permanent with the
///   <see cref="Majik.Core.Cards.Types.CardType.Creature"/> type — a sacrificed
///   token creature fires it too (no nontoken filter; CR 111.7 is irrelevant
///   here since the payoff acts on Mortician Beetle, not the sacrificed card).
/// - <b>"You may" (CR 603.4)</b>: the free-optional rider prompts the
///   controller's agent yes/no before placing the counter.
/// - <b>"Put a +1/+1 counter on this creature" (CR 122.1)</b>: one
///   <see cref="Majik.Core.Counters.CounterType.PlusOnePlusOne"/> counter via
///   <see cref="CountersService.Add"/> (CR 614 replacements observe the intent).
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
/// </summary>
[CardName("Mortician Beetle")]
public static class MorticianBeetleFactory
{
    public const string CardName = "Mortician Beetle";

    /// <summary>JSON slug for the embedded card definition.</summary>
    public const string Slug = "mortician-beetle";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Mortician Beetle with no live <see cref="TriggerManager"/>
    /// wiring. The sacrifice trigger is materialised onto the card shape from the
    /// JSON definition for structural / dispatch tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Mortician Beetle with an optional <see cref="TriggerManager"/>
    /// and <see cref="ReplacementBus"/>. When <paramref name="triggers"/> is
    /// supplied the declarative sacrifice trigger is registered so a qualifying
    /// <see cref="Majik.Core.Events.PermanentSacrificedEvent"/> auto-queues the
    /// ability. When <paramref name="replacements"/> is supplied the +1/+1
    /// counter placement is routed through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season replacements (CR 614) can rewrite the
    /// count.
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
