using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Volatile Fjord (Kaldheim). Member of the Kaldheim
/// snow dual-land cycle. Oracle text:
///
/// <code>
/// ({T}: Add {U} or {R}.)
/// This land enters tapped.
/// </code>
///
/// Type line: <c>Snow Land — Island Mountain</c>.
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the Island and
/// Mountain land subtypes (CR 205.3i) and the two mana abilities {U}/{R}
/// (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/volatile-fjord.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AlpineMeadowFactory"/> and the rest of the snow-dual cycle.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the rest of the cycle).
/// </para>
/// </summary>
[CardName("Volatile Fjord")]
public static class VolatileFjordFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("volatile-fjord");

    /// <summary>Construct Volatile Fjord owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
