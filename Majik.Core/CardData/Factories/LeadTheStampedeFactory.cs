using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lead the Stampede (Magic 2013, {2}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Look at the top five cards of your library. You may reveal any number
///    of creature cards from among them and put the revealed cards into your
///    hand. Put the rest on the bottom of your library in any order."
///
/// ## Why a named factory (no template covers it)
/// This is the look-at-top-N / take-matching / bottom-the-rest family — the
/// same shape as <see cref="CollectedCompanyFactory"/> and
/// <see cref="AbundantHarvestFactory"/> — but the kept cards go to HAND
/// (not the battlefield) and the filter is "creature card" with no mana-value
/// cap. No single binder template binds that exact combination, so it gets a
/// thin named factory whose resolve loop is exercised directly.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{G}, green. Card shape comes from the embedded
///   JSON (<c>lead-the-stampede.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Resolve (<see cref="Resolve"/>): look at the top five cards (fewer if the
///   library is short — CR 701.21 "top N" never throws), move every creature
///   card among them to hand, and bottom every non-creature card.
///
/// ## Rules citations
/// - CR 701.21 — "look at the top N cards" is a peek; a short library just
///   yields fewer cards (clean, no throw).
/// - CR 701.16 — reveal: the revealed creature cards are shown, then go to hand.
/// - CR 701.20 — put the remaining cards on the bottom of the library.
/// - CR 117.x / "you may" — revealing creature cards is optional, but revealing
///   every creature is strictly card advantage and never disadvantageous, so v1
///   reveals all of them (the cards you decline would only bottom anyway). The
///   "any order" for both the kept cards and the bottomed pile is the caster's
///   free choice (CR 608.2); we keep reveal order (no observable game effect).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: revealed cards aren't published on a reveal
///   bus yet (same gap as the rest of the look-at-top-N family). No live
///   observer cares yet.
/// - <b>"Any order" choice</b>: the caster cannot reorder the bottomed pile;
///   reveal order is used. Bottoming order has no game-state consequence here.
/// </summary>
[CardName("Lead the Stampede")]
public static class LeadTheStampedeFactory
{
    public const string CardName = "Lead the Stampede";
    public const string Slug = "lead-the-stampede";
    public const string PrintedManaCost = "{2}{G}";

    /// <summary>How many cards are looked at off the top (CR 701.21).</summary>
    public const int LookCount = 5;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Resolve Lead the Stampede against <paramref name="caster"/>'s library:
    /// look at the top <see cref="LookCount"/> cards, put every creature card
    /// among them into hand, and bottom the rest (CR 701.16 / 701.20). Exposed
    /// for direct invocation by tests / bots without driving the full
    /// resolution pipeline (same posture as
    /// <see cref="AbundantHarvestFactory.ResolveChoice"/>).
    /// </summary>
    public static StampedeResolution Resolve(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var library = caster.Zones.Library;

        // CR 701.21 — look at the top N (short library yields fewer; no throw).
        var looked = library.GetCards().Take(LookCount).ToList();
        if (looked.Count == 0)
        {
            return new StampedeResolution(
                LookedAt: Array.Empty<ICard>(),
                PutInHand: Array.Empty<ICard>());
        }

        var toHand = new List<ICard>();
        var toBottom = new List<ICard>();
        foreach (var card in looked)
        {
            if (card.HasType(CardType.Creature))
                toHand.Add(card);
            else
                toBottom.Add(card);
        }

        // CR 701.16 — reveal the creature cards and put them into hand.
        foreach (var card in toHand)
        {
            library.RemoveCard(card);
            caster.Zones.Hand.AddCard(card);
            card.SetZone(ZoneType.Hand);
        }

        // CR 701.20 — bottom the rest, in any order (reveal order here; no
        // game-state consequence). Remove-then-append so the bottomed cards
        // land below any card that was deeper than the looked-at window.
        foreach (var card in toBottom)
        {
            library.RemoveCard(card);
        }
        foreach (var card in toBottom)
        {
            library.AddCard(card); // Append == bottom.
            card.SetZone(ZoneType.Library);
        }

        return new StampedeResolution(
            LookedAt: looked,
            PutInHand: toHand);
    }

    /// <summary>
    /// Observation record for one Lead the Stampede resolution — every card
    /// looked at (in look order) and the creature cards put into hand.
    /// </summary>
    public sealed record StampedeResolution(
        IReadOnlyList<ICard> LookedAt,
        IReadOnlyList<ICard> PutInHand);
}
