using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Obelisk of Grixis (Alara Reborn) — the Grixis
/// ({U}{B}{R}) member of the "Obelisk" tri-colour mana-rock cycle. Oracle text
/// (verified against Scryfall):
///   "{T}: Add {U}, {B}, or {R}."
///
/// <para>
/// Pure mana rock — no cycling, no other clause. The Artifact shell (type
/// Artifact, mana cost {3}) plus the three colour-specific mana abilities
/// {U}/{B}/{R} (CR 605.1 — mana abilities don't use the stack) is declared
/// declaratively in <c>Majik.Core/CardData/Cards/obelisk-of-grixis.json</c>
/// and materialized via <see cref="CardDefinitionFactory"/>, the same posture
/// as <see cref="ObeliskOfEsperFactory"/> (its WUB sibling in the same cycle).
/// The activator picks a colour by picking the matching mana-ability slot, so
/// no separate colour prompt is needed (CR 605.1).
/// </para>
/// </summary>
[CardName("Obelisk of Grixis")]
public static class ObeliskOfGrixisFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("obelisk-of-grixis");

    /// <summary>Construct Obelisk of Grixis owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
