using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vexing Bauble (Modern Horizons 3).
///
/// Artifact — {1}. Oracle text:
///   "Whenever a player casts a spell, if no mana was spent to cast it,
///    counter that spell.
///    {1}, {T}, Sacrifice this artifact: Draw a card."
///
/// Now a thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/vexing-bauble.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. The
/// activated ability is fully JSON: <c>{1}</c> + <c>{T}</c> + sacrifice
/// → draw a card.
///
/// ## Deferred (v1 gaps)
/// - <b>"Counter free spells" triggered ability</b>: "Whenever a player
///   casts a spell, if no mana was spent to cast it, counter that spell"
///   requires (a) tracking per-cast mana-spent metadata on the stack
///   object, (b) a triggered condition that inspects that metadata, and
///   (c) a counter-spell effect. Deferred until the stack carries
///   cast-cost provenance.
/// </summary>
public static class VexingBaubleFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("vexing-bauble");

    /// <summary>
    /// Construct Vexing Bauble owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
