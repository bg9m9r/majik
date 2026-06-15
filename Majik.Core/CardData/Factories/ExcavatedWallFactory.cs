using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Excavated Wall (Dragons of Tarkir, {1}).
///
/// Artifact Creature — Wall 0/4. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "Defender
///    {1}, {T}: Mill a card. (Put the top card of your library into your
///    graveyard.)"
///
/// A thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/excavated-wall.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card — the same posture
/// as <see cref="MasterDecoyFactory"/> / <see cref="BottleGnomesFactory"/>. The
/// card is fully declarative JSON:
///
/// - <b>Defender</b> — a keyword (CR 702.3); the JSON <c>types</c> array also
///   carries Artifact (CR 205.2a — a permanent can have multiple card types) so
///   artifact-matters effects see it.
/// - <b>{1}, {T}: Mill a card</b> — an <c>activated</c> ability with a
///   <c>mana</c> ({1}) cost + a <c>tap_self</c> cost and a <c>mill_self</c>
///   effect (CR 701.13). At resolution the shared <see cref="CardDefRuntime"/>
///   mill builder routes the controller's mill through
///   <see cref="Majik.Core.Primitives.Fx.Mill"/> — no agent decision (the top
///   card moves unconditionally to the graveyard); milling from an empty library
///   is a clean no-op and does not by itself cause the loss (CR 104.3c).
///
/// This <c>{cost}: Mill N.</c> shape is also the one
/// <see cref="OracleActivatedAbilityBinder"/> reconstructs for Agatha's Soul
/// Cauldron's ability-grant (CR 613.1f / 702.49), so an imprinted mill-on-tap
/// creature re-homes onto a grown bearer for free.
/// </summary>
[CardName("Excavated Wall")]
public static class ExcavatedWallFactory
{
    public const string CardName = "Excavated Wall";
    public const string Slug = "excavated-wall";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Excavated Wall owned and controlled by <paramref name="owner"/>.
    /// The Defender keyword + the single mill-on-tap activated ability are
    /// materialised from the embedded JSON definition. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
