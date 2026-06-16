using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kitchen Imp (Shadows over Innistrad, {3}{B}).
///
/// Creature — Imp 2/2. Oracle text (Scryfall, verified):
///   "Flying, haste
///    Madness {B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// A vanilla-keyword 2/2 Imp body — Flying (CR 702.9) + Haste (CR 702.10) and
/// nothing else. The whole shape (name, Imp subtype, {3}{B}, 2/2, Flying +
/// Haste keywords) is materialised from the embedded JSON definition
/// (<c>kitchen-imp.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (the <c>keywords</c> array carries
/// Flying + Haste). Same thin-wrapper posture as <see cref="AlphaMyrFactory"/>
/// (vanilla body) / <see cref="MarkovBaronFactory"/> (keyword-bearing JSON
/// body).
///
/// ## Madness (CR 702.35) — intrinsic, NOT wired here
/// Madness {B} works engine-wide for every catalogued card: the cost is listed
/// in <see cref="Majik.Core.Keywords.MadnessCatalog"/> ("Kitchen Imp" = {B}) and
/// the central discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>
/// consults it to route a discarded madness card to exile + offer it for its
/// madness cost. No factory code is needed for the madness line.
/// </summary>
[CardName("Kitchen Imp")]
public static class KitchenImpFactory
{
    public const string CardName = "Kitchen Imp";
    public const string Slug = "kitchen-imp";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);
}
