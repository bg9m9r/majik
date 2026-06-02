using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Frontier Bivouac (Khans of Tarkir) — the Temur
/// "tapped tri-land" cycle. Oracle text:
///   "This land enters tapped.
///    {T}: Add {G}, {U}, or {R}."
///
/// <para>
/// Like <see cref="JungleShrineFactory"/> (and unlike the Ikoria Triomes),
/// Frontier Bivouac carries no printed land subtypes and no Cycling — it is
/// a plain colourless Land that taps for one of three colours and enters
/// tapped. The whole shell (Land type + the three mana abilities
/// {G}/{U}/{R}, CR 605.1 — mana abilities don't use the stack) is declared
/// declaratively in <c>Majik.Core/CardData/Cards/frontier-bivouac.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="JungleShrineFactory"/>).
/// </para>
/// </summary>
[CardName("Frontier Bivouac")]
public static class FrontierBivouacFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("frontier-bivouac");

    /// <summary>Construct Frontier Bivouac owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
