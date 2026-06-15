using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temporal Adept (Urza's Saga / 8th–10th Edition,
/// {1}{U}{U}).
///
/// Creature — Human Wizard 1/1. Oracle text (verified against Scryfall):
///   "{U}{U}{U}, {T}: Return target permanent to its owner's hand."
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/temporal-adept.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="MasterDecoyFactory"/>, whose only-ability tapper line is the
/// structural sibling. The single ability is fully declarative JSON:
///
/// - <b>{U}{U}{U}, {T}: Return target permanent to its owner's hand</b> — an
///   <c>activated</c> ability with a <c>mana</c> ({U}{U}{U}) cost + a
///   <c>tap_self</c> cost and a <c>return_to_hand</c> effect (CR 701.20) over
///   the <c>permanent</c> target filter. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   activating player's agent (CR 602.2b) for a permanent pick, and the effect
///   returns the chosen permanent to its owner's hand via
///   <see cref="Majik.Core.Primitives.Fx.BounceToHand(Majik.Core.Cards.ICard, Majik.Core.Services.ZoneService?)"/>
///   (CR 608.2b — an illegal target at resolution fizzles cleanly).
///
/// This shape is also the one <see cref="OracleActivatedAbilityBinder"/>
/// reconstructs for Agatha's Soul Cauldron's ability-grant (CR 613.1f / 702.49),
/// so an imprinted bouncer re-homes onto a grown bearer for free.
/// </summary>
[CardName("Temporal Adept")]
public static class TemporalAdeptFactory
{
    public const string CardName = "Temporal Adept";
    public const string Slug = "temporal-adept";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Temporal Adept owned and controlled by <paramref name="owner"/>.
    /// The single return-target-permanent-to-hand activated ability is
    /// materialised from the embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
