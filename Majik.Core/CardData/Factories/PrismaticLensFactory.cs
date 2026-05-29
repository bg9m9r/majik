using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Prismatic Lens (Time Spiral, {2}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>{T}: Add {C}</b> — a single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   with no additional cost. <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>
///   folds {C} into the generic bucket (CR 107.4c; see ManaCost.cs:170),
///   the same shape as <see cref="MindStoneFactory"/> / Sol Ring's
///   tap-for-colourless body.
/// - <b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per
///   WUBRG), each carrying the optional additional <c>cost</c> field
///   ("1"). <see cref="CardDefinitionFactory"/> threads that through the
///   additional-cost overload of <see cref="Majik.Core.Abilities.ManaAbility"/>
///   — gating activation on the untapped state plus affordability of the
///   {1} pip, and deducting it from the pool on activation. This is the
///   same "Add one mana of any color" filter posture as
///   <see cref="ChromaticStarFactory"/> / Springleaf Drum: one
///   <c>ManaAbility</c> per colour, the bot's source-picker selecting the
///   colour at payment time. CR 605.1 — both are mana abilities and never
///   use the stack; the {1} on the second ability is part of the
///   activation cost, not a resolution effect.
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/prismatic-lens.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>. Same JSON-driven posture as
/// <see cref="IzzetSignetFactory"/>; the only engine shapes used are the
/// vanilla and additional-cost mana abilities, both already supported.
/// </summary>
[CardName("Prismatic Lens")]
public static class PrismaticLensFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("prismatic-lens");

    /// <summary>Construct Prismatic Lens owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
