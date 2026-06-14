using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Wurm (Torment, {3}{R}{R}).
///
/// Creature — Wurm 4/4. Oracle text (verified against Scryfall 2026-06-14):
///   "Trample
///    Madness {2}{R}"
///
/// The body is a vanilla 4/4 with the single keyword Trample (CR 702.19). The
/// whole shape (name, Wurm subtype, {3}{R}{R}, 4/4, Trample) is materialised
/// from the embedded JSON definition (<c>reckless-wurm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
/// carries Trample, which <see cref="Definitions.CardDefRuntime"/> turns into a
/// <c>KeywordAbility</c> marker honoured by
/// <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> — so there is no
/// bespoke behaviour to layer on, and the factory is the thin
/// <see cref="AlphaMyrFactory"/>-shaped wrapper.
///
/// <b>Madness {2}{R} (CR 702.35)</b> is intrinsic — the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> consults
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> by name ("Reckless Wurm" is
/// catalogued at {2}{R}). No factory code is needed for it and none is added
/// here.
/// </summary>
[CardName("Reckless Wurm")]
public static class RecklessWurmFactory
{
    public const string CardName = "Reckless Wurm";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("reckless-wurm");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
