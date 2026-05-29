using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Woodland Chasm (Kaldheim "snow dual" tapland cycle).
///
/// Type line: <c>Snow Land — Swamp Forest</c>. Oracle text:
///
/// <code>
/// ({T}: Add {B} or {G}.)
/// This land enters tapped.
/// </code>
///
/// <para>
/// The Land shell — the Snow supertype (CR 205.4d) plus the printed Swamp and
/// Forest subtypes (CR 205.3) and the two mana abilities {B}/{G} (CR 605.1 —
/// mana abilities don't use the stack) — is declared declaratively in
/// <c>Majik.Core/CardData/Cards/woodland-chasm.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as the Guildgate
/// factories.
/// </para>
///
/// <para>
/// Woodland Chasm is nonbasic, so although its Swamp/Forest subtypes would
/// confer the intrinsic basic-land mana abilities (CR 305.6), the parenthesised
/// "{T}: Add {B} or {G}" line is reminder text; the two mana abilities are
/// declared explicitly here so the card produces {B} or {G} deterministically,
/// matching the other dual-mana land factories.
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as the Guildgate / Refuge tapland factories).
/// </para>
/// </summary>
[CardName("Woodland Chasm")]
public static class WoodlandChasmFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("woodland-chasm");

    /// <summary>Construct Woodland Chasm owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
