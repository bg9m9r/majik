using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Obelisk of Jund (Shards of Alara, {3}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "{T}: Add {B}, {R}, or {G}."
///
/// The Jund ({B}{R}{G}) member of the Shards-of-Alara "Obelisk" tri-colour
/// mana-rock cycle — a plain Artifact (no cycling) that taps for one pip of any
/// of its three colours. Mirrors the Ikoria "Crystal" rocks
/// (<see cref="KetriaCrystalFactory"/> / <see cref="IndathaCrystalFactory"/>)
/// minus the cycling clause.
///
/// The shell — type Artifact, mana cost {3}, plus the three colour-specific
/// mana abilities {B}/{R}/{G} (CR 605.1 — mana abilities don't use the stack) —
/// is declared declaratively in
/// <c>Majik.Core/CardData/Cards/obelisk-of-jund.json</c> and materialized via
/// <see cref="CardDefinitionFactory"/>. The activator picks a colour by picking
/// the matching mana-ability slot, so no separate colour prompt is needed
/// (CR 605.1).
/// </summary>
[CardName("Obelisk of Jund")]
public static class ObeliskOfJundFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("obelisk-of-jund");

    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
