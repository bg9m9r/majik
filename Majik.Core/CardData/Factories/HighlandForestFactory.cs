using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Highland Forest (Modern Horizons 3). Member of the
/// snow dual-land cycle (analogue of the Kaldheim <c>AlpineMeadowFactory</c>).
/// Oracle text:
///
/// <code>
/// ({T}: Add {R} or {G}.)
/// This land enters tapped.
/// </code>
///
/// Type line: <c>Snow Land — Mountain Forest</c>.
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the Mountain and
/// Forest land subtypes (CR 205.3i) and the two mana abilities {R}/{G}
/// (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/highland-forest.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AlpineMeadowFactory"/>.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="AlpineMeadowFactory"/>).
/// </para>
/// </summary>
[CardName("Highland Forest")]
public static class HighlandForestFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("highland-forest");

    /// <summary>Construct Highland Forest owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
