using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sandsteppe Citadel (Khans of Tarkir) — the Abzan
/// "tapped tri-land" cycle. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    {T}: Add {W}, {B}, or {G}."
///
/// <para>
/// Unlike the Ikoria Triomes, Sandsteppe Citadel carries no printed land
/// subtypes and no Cycling — it is a plain colourless Land that taps for
/// one of three colours and enters tapped. The whole shell (Land type +
/// the three mana abilities {W}/{B}/{G}, CR 605.1 — mana abilities don't
/// use the stack) is declared declaratively in
/// <c>Majik.Core/CardData/Cards/sandsteppe-citadel.json</c> and
/// materialized via <see cref="CardDefinitionFactory"/>, the same posture
/// as <see cref="JungleShrineFactory"/> (the Naya member of the same Khans
/// tri-land cycle).
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="JungleShrineFactory"/>).
/// </para>
/// </summary>
[CardName("Sandsteppe Citadel")]
public static class SandsteppeCitadelFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sandsteppe-citadel");

    /// <summary>Construct Sandsteppe Citadel owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
