using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arctic Treeline (Kaldheim) — the G/W member of the
/// snow-tapland cycle. Oracle text:
///
/// <code>
/// This land enters tapped.
/// {T}: Add {G} or {W}.
/// </code>
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d), the Forest + Plains land
/// subtypes (CR 205.3i), and the two mana abilities {G}/{W} (CR 605.1 — mana
/// abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/arctic-treeline.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as the
/// Guildgate factories (analogue: Dimir Guildgate) plus the Snow supertype
/// (analogue: Snow-Covered Island).
/// </para>
///
/// <para>
/// The {G}/{W} abilities are declared explicitly in JSON rather than relying
/// on the Forest/Plains intrinsic-basic-land-type mana (CR 305.6): the engine's
/// intrinsic-mana attachment fires only for the Basic supertype, and Arctic
/// Treeline is a non-basic Snow Land, so the abilities are spelled out — the
/// same approach the Guildgates use.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the Guildgate factories).
/// </para>
/// </summary>
[CardName("Arctic Treeline")]
public static class ArcticTreelineFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("arctic-treeline");

    /// <summary>Construct Arctic Treeline owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
