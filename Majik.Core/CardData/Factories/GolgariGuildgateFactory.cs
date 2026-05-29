using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Golgari Guildgate (Return to Ravnica and many
/// reprints) — a member of the ten-card Guildgate cycle. Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {G}."
///
/// <para>
/// The Land shell — the printed <see cref="Cards.Types.CardSubtype.Gate"/>
/// subtype (CR 205.3i / 305.6 — Gate is a land subtype, not Basic) plus the
/// two mana abilities {B}/{G} (CR 605.1 — mana abilities don't use the
/// stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/golgari-guildgate.json</c> and materialized
/// via <see cref="CardDefinitionFactory"/>, the same data-driven posture as
/// <see cref="ZagothTriomeFactory"/>. Unlike the Triome cycle, Guildgates
/// carry no cycling ability — they are plain enters-tapped dual lands — so
/// no <see cref="Majik.Core.Keywords.CyclingFactory"/> wiring is attached.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the Triome factories).
/// </para>
/// </summary>
[CardName("Golgari Guildgate")]
public static class GolgariGuildgateFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("golgari-guildgate");

    /// <summary>Construct Golgari Guildgate owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
