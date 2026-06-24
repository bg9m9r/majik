using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wretched Doll (Duskmourn: House of Horror, {1}{B}).
///
/// Artifact Creature — Toy 3/1. Oracle text (verified against Scryfall
/// 2026-06-24):
///   "{B}, {T}: Surveil 1. (Look at the top card of your library. You may put
///   that card into your graveyard.)"
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/wretched-doll.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="SinisterStarfishFactory"/>. The single ability is fully
/// declarative JSON:
///
/// - <b>{B}, {T}: Surveil 1</b> — an <c>activated</c> ability whose costs are a
///   <c>mana</c> cost ({B}) plus a <c>tap_self</c> cost (CR 602.5 — multiple
///   activation costs are paid together), running a <c>surveil_self</c> effect
///   (CR 701.42). At resolution the shared <see cref="CardDefRuntime"/> surveil
///   builder consults the controller's agent (CR 701.42 — look at the top card,
///   may put it into the graveyard), falling back to the all-to-graveyard
///   default when no agent is registered.
///
/// This <c>{cost}: Surveil N.</c> shape is also the one
/// <see cref="OracleActivatedAbilityBinder"/> reconstructs for Agatha's Soul
/// Cauldron's ability-grant (CR 613.1f / 702.49), so an imprinted surveil-on-tap
/// creature re-homes onto a grown bearer for free.
/// </summary>
[CardName("Wretched Doll")]
public static class WretchedDollFactory
{
    public const string CardName = "Wretched Doll";
    public const string Slug = "wretched-doll";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Wretched Doll owned and controlled by <paramref name="owner"/>.
    /// The single surveil-on-{B}-tap activated ability is materialised from the
    /// embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
