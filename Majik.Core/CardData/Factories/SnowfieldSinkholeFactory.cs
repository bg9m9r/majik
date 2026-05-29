using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snowfield Sinkhole (Kaldheim). Member of the Kaldheim
/// snow dual-land cycle. Oracle text:
///
/// <code>
/// ({T}: Add {W} or {B}.)
/// This land enters tapped.
/// </code>
///
/// Type line: <c>Snow Land — Plains Swamp</c>.
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the Plains and
/// Swamp land subtypes (CR 205.3i) and the two mana abilities {W}/{B}
/// (CR 605.1 — mana abilities don't use the stack) — is declared
/// declaratively in <c>Majik.Core/CardData/Cards/snowfield-sinkhole.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>, the same posture
/// as <see cref="AlpineMeadowFactory"/> and the Guildgate factories. The Snow
/// supertype is carried through the <c>"supertypes": ["Snow"]</c> entry exactly
/// like the Snow-Covered basics.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the Guildgate factories).
/// </para>
/// </summary>
[CardName("Snowfield Sinkhole")]
public static class SnowfieldSinkholeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("snowfield-sinkhole");

    /// <summary>Construct Snowfield Sinkhole owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
