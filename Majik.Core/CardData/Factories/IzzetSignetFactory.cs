using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Izzet Signet (Guildpact / Ravnica signet cycle).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{1}, {T}: Add {U}{R}."
///
/// The signet cycle is the artifact analogue of the filter-land cycle's
/// "{1}, {T}: Add &lt;pips&gt;" shape — pay one generic mana plus the tap,
/// get two coloured pips back. Like every other signet the net swing is
/// "spend {1}, gain {U}{R}" (a +1 mana, fixing-into-colour swap).
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/izzet-signet.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>. The single mana ability is the
/// JSON "mana" shape with the optional additional <c>cost</c> field
/// ("1") — <see cref="CardDefinitionFactory"/> threads that through the
/// additional-cost overload of <see cref="Majik.Core.Abilities.ManaAbility"/>,
/// the same engine path the filter-land cycle
/// (<see cref="FilterLandCycleFactory"/>) uses for its "{1}, {T}: Add"
/// modes.
///
/// CR 605.1 — this is a mana ability: it does not use the stack. The {1}
/// generic mana is part of the activation cost (paid from the pool
/// alongside the {T} tap), not a resolution effect.
///
/// CR 605.1a — "Add {U}{R}" produces both coloured pips together in a
/// single activation (modelled as one <see cref="Majik.Core.Abilities.ManaAbility"/>
/// emitting {U}{R}), distinct from the talisman cycle's "Add {A} or {B}"
/// modal split.
/// </summary>
[CardName("Izzet Signet")]
public static class IzzetSignetFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("izzet-signet");

    /// <summary>Construct Izzet Signet owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
