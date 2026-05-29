using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glacial Floodplain (Kaldheim). Member of the Kaldheim
/// snow dual-land cycle (sibling of Alpine Meadow, Woodland Chasm). Oracle text:
///
/// <code>
/// ({T}: Add {W} or {U}.)
/// This land enters tapped.
/// </code>
///
/// Type line: <c>Snow Land — Plains Island</c>.
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the Plains and Island
/// land subtypes (CR 205.3i) and the two mana abilities {W}/{U}
/// (CR 605.1 — mana abilities don't use the stack) — is declared declaratively
/// in <c>Majik.Core/CardData/Cards/glacial-floodplain.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="AlpineMeadowFactory"/>. The Snow supertype is carried through the
/// <c>"supertypes": ["Snow"]</c> entry exactly like the Snow-Covered basics.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the printed
/// oracle text; this factory builds the land without it, for test convenience
/// (same posture as <see cref="AlpineMeadowFactory"/>).
/// </para>
/// </summary>
[CardName("Glacial Floodplain")]
public static class GlacialFloodplainFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("glacial-floodplain");

    /// <summary>Construct Glacial Floodplain owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
