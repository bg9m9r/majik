using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sulfurous Mire (a B/R Snow dual land). Oracle text:
///
/// <code>
/// ({T}: Add {B} or {R}.)
/// This land enters tapped.
/// </code>
///
/// <para>
/// Type line: <c>Snow Land — Swamp Mountain</c>. The Snow supertype
/// (CR 205.4d) plus the Swamp / Mountain land subtypes (CR 205.3i) and the
/// two mana abilities {B}/{R} (CR 605.1 — mana abilities don't use the
/// stack) are declared declaratively in
/// <c>Majik.Core/CardData/Cards/sulfurous-mire.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the Ice Tunnel factory
/// (same Snow-dual-land shape, U/B there, B/R here).
/// </para>
///
/// <para>
/// The {T}: Add {B} or {R} text is reminder text for the intrinsic
/// Swamp / Mountain mana abilities; we declare it explicitly in JSON (the
/// same posture as the Guildgate cycle and Ice Tunnel) rather than relying
/// on basic-land mana attachment, since Sulfurous Mire is not a Basic land.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from
/// the printed oracle text; this factory builds the land without it, for
/// test convenience (same posture as the Ice Tunnel / Guildgate factories).
/// </para>
/// </summary>
[CardName("Sulfurous Mire")]
public static class SulfurousMireFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sulfurous-mire");

    /// <summary>Construct Sulfurous Mire owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
