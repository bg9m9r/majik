using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Twins of Maurer Estate (Innistrad, {4}{B}).
///
/// Creature — Vampire 3/5. Oracle text (Scryfall, verified):
///   "Madness {2}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// Apart from the Madness line the card is a vanilla 3/5 Vampire body — no
/// printed keywords, triggers, statics, or activated abilities. The whole shape
/// (name, Vampire subtype, {4}{B}, 3/5) is materialised from the embedded JSON
/// definition (<c>twins-of-maurer-estate.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; same thin-wrapper posture as
/// <see cref="AlphaMyrFactory"/>.
///
/// ## Madness (CR 702.35) — intrinsic, NOT wired here
/// Madness {2}{B} works engine-wide for every catalogued card: the cost is
/// listed in <see cref="Majik.Core.Keywords.MadnessCatalog"/> ("Twins of Maurer
/// Estate" = {2}{B}) and the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> consults it to route a
/// discarded madness card to exile + offer it for its madness cost. No factory
/// code is needed for the madness line and none is added here.
/// </summary>
[CardName("Twins of Maurer Estate")]
public static class TwinsOfMaurerEstateFactory
{
    public const string CardName = "Twins of Maurer Estate";
    public const string Slug = "twins-of-maurer-estate";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
