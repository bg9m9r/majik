using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Obelisk of Esper (Shards of Alara) — the Esper
/// ({W}{U}{B}) three-colour mana rock. Oracle text (verified against Scryfall):
///   "{T}: Add {W}, {U}, or {B}."
///
/// <para>
/// The whole card — type Artifact, mana cost {3}, plus the three colour-specific
/// mana abilities {W}/{U}/{B} (CR 605.1 — mana abilities don't use the stack) —
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/obelisk-of-esper.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>, the same posture as
/// <see cref="IndathaCrystalFactory"/> (the identical tri-colour "{T}: Add A, B,
/// or C" rock, just on the Abzan colour triple and with Cycling on top, which
/// Obelisk of Esper lacks). The activator picks a colour by picking the matching
/// mana-ability slot, so no separate colour prompt is needed (CR 605.1).
/// </para>
/// </summary>
[CardName("Obelisk of Esper")]
public static class ObeliskOfEsperFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("obelisk-of-esper");

    /// <summary>Construct Obelisk of Esper owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
