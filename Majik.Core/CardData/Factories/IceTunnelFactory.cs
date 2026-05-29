using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ice Tunnel (Kaldheim). Oracle text:
///
/// <code>
/// ({T}: Add {U} or {B}.)
/// This land enters tapped.
/// </code>
///
/// <para>
/// Type line: <c>Snow Land — Island Swamp</c>. The Snow supertype
/// (CR 205.4d) plus the Island / Swamp land subtypes (CR 205.3i) and the
/// two mana abilities {U}/{B} (CR 605.1 — mana abilities don't use the
/// stack) are declared declaratively in
/// <c>Majik.Core/CardData/Cards/ice-tunnel.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, mirroring the Dimir Guildgate
/// factory (same U/B dual-mana shape) with the Snow supertype carried by
/// the SnowCoveredIsland analogue.
/// </para>
///
/// <para>
/// The {T}: Add {U} or {B} text on Ice Tunnel is reminder text for the
/// intrinsic Island / Swamp mana abilities; we declare it explicitly in
/// JSON (the same posture as the Guildgate cycle) rather than relying on
/// basic-land mana attachment, since Ice Tunnel is not a Basic land.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from
/// the printed oracle text; this factory builds the land without it, for
/// test convenience (same posture as the Guildgate factories).
/// </para>
/// </summary>
[CardName("Ice Tunnel")]
public static class IceTunnelFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ice-tunnel");

    /// <summary>Construct Ice Tunnel owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
