using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Alpine Meadow (Kaldheim). Member of the Kaldheim
/// snow dual-land cycle. Oracle text:
///
/// <code>
/// ({T}: Add {R} or {W}.)
/// This land enters tapped.
/// </code>
///
/// Type line: <c>Snow Land — Mountain Plains</c>.
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the Mountain and
/// Plains land subtypes (CR 205.3i) and the two mana abilities {R}/{W}
/// (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/alpine-meadow.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture as
/// the Guildgate factories (which supply the Land + dual mana abilities from
/// JSON). The Snow supertype is carried through the
/// <c>"supertypes": ["Snow"]</c> entry exactly like the Snow-Covered basics.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the Guildgate factories).
/// </para>
/// </summary>
[CardName("Alpine Meadow")]
public static class AlpineMeadowFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("alpine-meadow");

    /// <summary>Construct Alpine Meadow owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
