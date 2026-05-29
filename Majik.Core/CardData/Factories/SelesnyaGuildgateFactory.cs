using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Selesnya Guildgate (Return to Ravnica / Guilds of
/// Ravnica / reprints). Member of the Guildgate cycle. Oracle text:
///
/// <code>
/// This land enters tapped.
/// {T}: Add {G} or {W}.
/// </code>
///
/// <para>
/// The Land shell — the printed <see cref="Majik.Core.Cards.Types.CardSubtype.Gate"/>
/// subtype (CR 205.3m) plus the two mana abilities {G}/{W} (CR 605.1 — mana
/// abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/selesnya-guildgate.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same posture as the
/// Boros/Azorius/Izzet/Golgari Guildgate factories.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the other Guildgate factories).
/// </para>
/// </summary>
[CardName("Selesnya Guildgate")]
public static class SelesnyaGuildgateFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("selesnya-guildgate");

    /// <summary>Construct Selesnya Guildgate owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
