using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rimewood Falls (Kaldheim). Member of the Kaldheim
/// snow dual-land cycle. Oracle text:
///
/// <code>
/// ({T}: Add {G} or {U}.)
/// This land enters tapped.
/// </code>
///
/// Type line: <c>Snow Land — Forest Island</c>.
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the Forest and
/// Island land subtypes (CR 205.3i) and the two mana abilities {G}/{U}
/// (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/rimewood-falls.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture as
/// the sibling snow dual <see cref="AlpineMeadowFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the sibling snow duals).
/// </para>
/// </summary>
[CardName("Rimewood Falls")]
public static class RimewoodFallsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("rimewood-falls");

    /// <summary>Construct Rimewood Falls owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
