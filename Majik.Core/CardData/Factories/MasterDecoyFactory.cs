using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Master Decoy (Onslaught / 10th Edition, {1}{W}).
///
/// Creature — Human Soldier 1/2. Oracle text (verified against Scryfall):
///   "{W}, {T}: Tap target creature."
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/master-decoy.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="GoldmeadowHarrierFactory"/>, whose oracle line is identical. The
/// single ability is fully declarative JSON:
///
/// - <b>{W}, {T}: Tap target creature</b> — an <c>activated</c> ability with a
///   <c>mana</c> ({W}) cost + a <c>tap_self</c> cost and a <c>tap_target</c>
///   effect (CR 701.21a) over the <c>creature</c> target filter. The shared
///   <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline prompts the
///   activating player's agent (CR 602.2b) for a creature pick, and the effect
///   taps the chosen creature via
///   <see cref="Majik.Core.Primitives.Fx.Tap(Majik.Core.Cards.Permanent, Player?)"/>
///   (CR 608.2b — an illegal target at resolution fizzles cleanly; tapping an
///   already-tapped permanent is a no-op per CR 701.21b).
///
/// This shape is also the one <see cref="OracleActivatedAbilityBinder"/>
/// reconstructs for Agatha's Soul Cauldron's ability-grant (CR 613.1f / 702.49),
/// so an imprinted tapper re-homes onto a grown bearer for free.
/// </summary>
[CardName("Master Decoy")]
public static class MasterDecoyFactory
{
    public const string CardName = "Master Decoy";
    public const string Slug = "master-decoy";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Master Decoy owned and controlled by <paramref name="owner"/>.
    /// The single tap-target-creature activated ability is materialised from the
    /// embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
