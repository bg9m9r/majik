using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Incorrigible Youths (Shadows over Innistrad,
/// {3}{R}{R}).
///
/// Creature — Vampire 4/3. Oracle text (verified against Scryfall 2026-06-14):
///   "Haste
///    Madness {2}{R}"
///
/// The body is a vanilla 4/3 with the single keyword Haste (CR 702.10). The
/// whole shape (name, Vampire subtype, {3}{R}{R}, 4/3, Haste) is materialised
/// from the embedded JSON definition (<c>incorrigible-youths.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON <c>keywords</c> array
/// carries Haste, which <see cref="Definitions.CardDefRuntime"/> turns into a
/// <c>KeywordAbility</c> marker honoured by
/// <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> — so there is no
/// bespoke behaviour to layer on, and the factory is the thin
/// <see cref="AlphaMyrFactory"/>-shaped wrapper.
///
/// <b>Madness {2}{R} (CR 702.35)</b> is intrinsic — the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> consults
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> by name ("Incorrigible
/// Youths" is catalogued at {2}{R}). No factory code is needed for it and none
/// is added here.
/// </summary>
[CardName("Incorrigible Youths")]
public static class IncorrigibleYouthsFactory
{
    public const string CardName = "Incorrigible Youths";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("incorrigible-youths");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
