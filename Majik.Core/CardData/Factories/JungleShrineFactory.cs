using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jungle Shrine (Shards of Alara) — the Naya
/// "tapped tri-land" cycle. Oracle text:
///   "This land enters tapped.
///    {T}: Add {R}, {G}, or {W}."
///
/// <para>
/// Unlike the Ikoria Triomes, Jungle Shrine carries no printed land
/// subtypes and no Cycling — it is a plain colourless Land that taps for
/// one of three colours and enters tapped. The whole shell (Land type +
/// the three mana abilities {R}/{G}/{W}, CR 605.1 — mana abilities don't
/// use the stack) is declared declaratively in
/// <c>Majik.Core/CardData/Cards/jungle-shrine.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="ZagothTriomeFactory"/> (minus subtypes and cycling).
/// </para>
///
/// <para>
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the
/// printed oracle text; this factory builds the land without it, for test
/// convenience (same posture as <see cref="ZagothTriomeFactory"/>).
/// </para>
/// </summary>
[CardName("Jungle Shrine")]
public static class JungleShrineFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("jungle-shrine");

    /// <summary>Construct Jungle Shrine owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
