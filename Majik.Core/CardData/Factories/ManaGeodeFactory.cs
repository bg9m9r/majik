using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mana Geode (Time Spiral, {3}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall 2026-05):
///   "When this artifact enters, scry 1.
///    {T}: Add one mana of any color."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/mana-geode.json</c> and builds through
/// <see cref="CardDefinitionFactory"/>. The only engine shapes used are an
/// <c>etb_self</c> → <c>scry_self 1</c> triggered ability and free per-colour
/// mana abilities — both already supported.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {3}, owner / controller wiring).
/// - <b>When this artifact enters, scry 1</b> — an <c>etb_self</c> →
///   <c>scry_self 1</c> triggered ability (CR 701.20), the same shape as
///   <see cref="CrystalGrottoFactory"/> / the Theros scry-temples. Scry
///   decision is agent-driven via
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseScryDecisionAsync"/>
///   when registered, otherwise the default all-to-bottom fall-back.
/// - <b>{T}: Add one mana of any color</b> — five
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances (one per
///   WUBRG), each <b>free</b> (only {T}, no {1} rider). This is the same
///   WUBRG fan-out the engine uses for "any color" everywhere else (Pillar
///   of Origins, Springleaf Drum): the activator picks the colour by picking
///   the matching ability slot. CR 605.1 — mana abilities never use the
///   stack. Unlike Prismatic Lens / Crystal Grotto, Mana Geode has no
///   separate {T}: Add {C} mode and no {1} additional cost.
/// </summary>
[CardName("Mana Geode")]
public static class ManaGeodeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mana-geode");

    /// <summary>Construct Mana Geode owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Artifact Create(Player owner) =>
        (Artifact)CardDefinitionFactory.Build(Definition, owner);
}
