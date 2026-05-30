using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grizzly Bears (Alpha onward, {1}{G}).
///
/// Creature — Bear 2/2. Vanilla — empty oracle text (verified against
/// Scryfall); no printed keywords, triggers, statics, or activated
/// abilities. The canonical vanilla two-drop against which other 2/2s are
/// measured.
///
/// ## Implementation
///
/// - 2/2 <see cref="Creature"/> with subtype <see cref="CardSubtype.Bear"/>
///   (CR 205.3m).
/// - Mana cost {1}{G} — 1 generic + 1 green; mana value 2 (CR 202.3). The
///   single coloured pip makes the card green (CR 105.2).
/// - Thin wrapper that loads
///   <c>Majik.Core/CardData/Cards/grizzly-bears.json</c> and lets
///   <see cref="CardDefinitionFactory"/> build the runtime card — same
///   JSON-backed posture as <see cref="LazotepRecruitFactory"/>. The JSON
///   carries no abilities; nothing is layered on top.
/// - No service wiring — single-arg <see cref="Create(Player)"/> is the
///   canonical entry point.
/// </summary>
[CardName("Grizzly Bears")]
public static class GrizzlyBearsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("grizzly-bears");

    /// <summary>
    /// Construct Grizzly Bears — a vanilla {1}{G} 2/2 Creature — Bear —
    /// owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
