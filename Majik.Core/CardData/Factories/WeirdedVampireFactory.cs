using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Weirded Vampire (Shadows over Innistrad, {3}{B}).
///
/// Creature — Vampire Horror 3/3. Oracle text (verified against Scryfall
/// 2026-06-14): its only printed text is the madness keyword —
///   "Madness {2}{B}"
/// — so the printed BODY is a vanilla 3/3 with no triggers, statics, activated
/// abilities, or non-madness keywords.
///
/// The card's entire shape (name, Vampire + Horror subtypes, {3}{B}, 3/3) is
/// materialised from the embedded JSON definition (<c>weirded-vampire.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. There is no behaviour to layer on
/// top — the factory is the thin <see cref="AlphaMyrFactory"/>-shaped wrapper.
///
/// <b>Madness {2}{B} (CR 702.35)</b> is intrinsic — the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> consults
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> by name ("Weirded Vampire"
/// is catalogued at {2}{B}) and routes a discarded madness card to exile +
/// offers it for its madness cost. No factory code is needed for it and none is
/// added here.
/// </summary>
[CardName("Weirded Vampire")]
public static class WeirdedVampireFactory
{
    public const string CardName = "Weirded Vampire";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("weirded-vampire");

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
