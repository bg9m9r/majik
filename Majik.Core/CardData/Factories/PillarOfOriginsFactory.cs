using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pillar of Origins (Ixalan).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "As this artifact enters, choose a creature type.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    creature spell of the chosen type."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/pillar-of-origins.json</c> and builds
/// through <see cref="CardDefinitionFactory"/>. The "{T}: Add one mana of
/// any color" ability is modelled as five
/// <see cref="Majik.Core.Abilities.ManaAbility"/> instances, one per WUBRG
/// — the same "any color" pattern used by
/// <see cref="DelightedHalflingFactory"/>, <see cref="CavernOfSoulsFactory"/>,
/// and the Treasure token. The source-picker satisfies any single colour
/// pip via this artifact.
///
/// CR 605.1 — these are mana abilities: they do not use the stack.
///
/// ## Deferred (v1 gaps — shared with Cavern of Souls / Delighted Halfling)
/// - <b>"As this artifact enters, choose a creature type."</b> (CR 614.12 —
///   the choice is made as part of the as-enters replacement). The engine
///   has no ChooseSubtype agent prompt yet, so the chosen-type slot is not
///   captured here. Cavern of Souls models the same choice by eagerly
///   resolving it via an optional <c>typeChooser</c> closure at factory
///   time; that pattern can be layered on when the prompt system lands,
///   without changing this JSON.
/// - <b>Spend-restriction</b>: "Spend this mana only to cast a creature
///   spell of the chosen type." Enforcement requires per-mana-pool-entry
///   tagging and a spend-restriction check in the cast-payment flow
///   (today <see cref="Majik.Core.ValueObjects.ManaPool"/> stores bucketed
///   colour counts only — no per-slot provenance). Same deferral posture
///   as Delighted Halfling's "legendary spell" rider and Cavern of Souls'
///   chosen-type rider — the mana is still strictly produced; the
///   payment-gate lands once the pool grows tags.
/// </summary>
[CardName("Pillar of Origins")]
public static class PillarOfOriginsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("pillar-of-origins");

    /// <summary>Construct Pillar of Origins owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Artifact)CardDefinitionFactory.Build(Definition, owner);
    }
}
