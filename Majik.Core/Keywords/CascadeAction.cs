using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.85 — Cascade. "When you cast this spell, exile cards from the top
/// of your library until you exile a nonland card whose mana value is less
/// than this spell's mana value. You may cast that spell without paying its
/// mana cost if you cast it from exile this way. Put the exiled cards on the
/// bottom of your library in a random order." (CR 702.85a–b).
///
/// <para>This helper performs the deterministic mechanical side of cascade:
/// exile-from-top until a nonland-with-lower-MV is found (or the library is
/// empty), bottom every exiled card EXCEPT the eligible one (in random
/// order, CR 702.85b), and leave the eligible card sitting in exile so the
/// caller can drive a <see cref="Costs.CastFromExileAlternativeCost"/> cast
/// through <see cref="Game.SpellCastFlow"/> at trigger-resolution time.</para>
///
/// <para>The CR-mandated "you may" decision is surfaced as the
/// <c>willCast</c> predicate on <see cref="Cascade"/>. Default = always
/// cast. When the predicate returns <c>false</c> for the eligible card,
/// that card is bottomed alongside the rest — matching the "if you don't
/// cast it, put it on the bottom of your library in a random order along
/// with the other exiled cards" reading of CR 702.85a.</para>
/// </summary>
public static class CascadeAction
{
    /// <summary>
    /// Outcome of a cascade trigger. <see cref="Exiled"/> is every card the
    /// trigger moved to exile in order (top of library first). <see cref="Eligible"/>
    /// is the first nonland card with mana value &lt; <c>sourceManaValue</c>,
    /// or <c>null</c> if no such card was found (library exhausted or no
    /// match). <see cref="Bottomed"/> is the subset of <see cref="Exiled"/>
    /// that the action moved back to the bottom of the library — i.e.
    /// every exiled card minus the eligible one (when the controller chose
    /// to cast it).
    /// </summary>
    public sealed record CascadeResult(
        IReadOnlyList<ICard> Exiled,
        ICard? Eligible,
        IReadOnlyList<ICard> Bottomed);

    /// <summary>
    /// Run a cascade trigger.
    /// </summary>
    /// <param name="controller">The cascading spell's controller — owns the
    /// library being dug into. CR 702.85a — "exile cards from the top of
    /// your library".</param>
    /// <param name="sourceManaValue">Mana value of the spell that triggered
    /// cascade (CR 202.3). Cards exiled must have a strictly lower MV to
    /// be eligible.</param>
    /// <param name="willCast">Optional "you may cast" decision predicate.
    /// Receives the eligible card (the one we'd cast). Default = always
    /// true. When the predicate returns false, the eligible card is
    /// bottomed with the rest.</param>
    /// <param name="random">Optional RNG for the random-order bottom step
    /// (CR 702.85b). Defaults to a per-call <see cref="GameRandom"/> instance
    /// — tests can pin order by passing a seeded instance.</param>
    public static CascadeResult Cascade(
        Player controller,
        int sourceManaValue,
        Func<ICard, bool>? willCast = null,
        GameRandom? random = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        willCast ??= static _ => true;
        random ??= new GameRandom();

        var library = controller.Zones.Library;
        var exile = controller.Zones.Exile;

        var exiled = new List<ICard>();
        ICard? eligible = null;

        // CR 702.85a — exile from the top until we find a nonland card with
        // mana value < sourceManaValue (or the library runs out).
        while (true)
        {
            var top = library.GetCards().FirstOrDefault();
            if (top == null) break; // library empty; trigger fizzles for the eligibility step.

            library.RemoveCard(top);
            exile.AddCard(top);
            top.SetZone(ZoneType.Exile);
            exiled.Add(top);

            if (IsEligible(top, sourceManaValue))
            {
                eligible = top;
                break;
            }
        }

        // CR 702.85a — "You may cast that spell without paying its mana cost".
        // The cast itself is driven by the caller (via
        // CastFromExileAlternativeCost + SpellCastFlow); here we only honour
        // the "may" decision by deciding whether to keep the eligible card in
        // exile (caller will cast it) or bottom it alongside the rest.
        var keepInExile = eligible != null && willCast(eligible);

        // CR 702.85b — bottom the non-cast exiled cards in random order.
        var toBottom = exiled
            .Where(c => !(keepInExile && ReferenceEquals(c, eligible)))
            .ToList();

        // CR 702.85b — random order. GameRandom.Shuffle is Fisher–Yates.
        random.Shuffle(toBottom);

        foreach (var card in toBottom)
        {
            exile.RemoveCard(card);
            library.AddCard(card); // AddCard appends — that's the bottom.
            card.SetZone(ZoneType.Library);
        }

        return new CascadeResult(
            Exiled: exiled,
            Eligible: keepInExile ? eligible : null,
            Bottomed: toBottom);
    }

    /// <summary>
    /// CR 702.85a eligibility predicate — "nonland card whose mana value is
    /// less than this spell's mana value". Mana value is read off the card's
    /// printed cost (CR 202.3) via <see cref="Card.ManaCostValue"/> when the
    /// concrete <see cref="Card"/> subclass is available; falls back to
    /// parsing the <see cref="ICard.ManaCost"/> string for non-Card ICard
    /// implementers.
    /// </summary>
    private static bool IsEligible(ICard card, int sourceManaValue)
    {
        if (card.HasType(CardType.Land)) return false;

        int manaValue = card is Card concrete
            ? concrete.ManaCostValue.TotalValue
            : Majik.Core.ValueObjects.ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;

        return manaValue < sourceManaValue;
    }
}
