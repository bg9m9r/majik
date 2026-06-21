using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Journeyer's Kite (Coldsnap, {2}).
///
/// Artifact. Oracle text:
///   "{3}, {T}: Search your library for a basic land card, reveal it, put it
///    into your hand, then shuffle."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {2}, owner / controller wiring).
/// - <b>{3}, {T}: Tutor a BASIC land to hand</b> — a single
///   <see cref="ActivatedAbility"/> with two costs: <see cref="ManaCostCost"/>
///   ("{3}") + <see cref="AdditionalCost.Tap"/> on the kite. Resolution
///   consults the controller's agent via <see cref="LibrarySearch.PromptOnlyAsync"/>
///   for the basic-land choice (CR 701.19a; deterministic first-match fallback
///   when no agent is registered — same posture as Expedition Map / every tutor
///   factory), moves the pick to hand, and shuffles via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a).
/// - <b>"basic land card" candidate filter</b>: only a card that is both a Land
///   (CR 305) AND carries the Basic supertype (CR 205.4) is eligible — a
///   nonbasic land (shock / fetch / Tron land) is excluded. This is the EFFECT-
///   side typed-sub-filter the general-typed-subfilter-library-tutor-binder-
///   grammar pay-down also reaches generically in
///   <see cref="OracleActivatedAbilityBinder"/>; the bespoke factory mirrors it
///   so the live card resolves identically.
/// - Decline-to-find is legal: agent returning null = no-op (CR 701.19a).
///   Empty basic-land pile = clean no-op (still shuffles per CR 701.20a).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the picked land moves Library → Hand without
///   publishing a reveal event. Same gap as Expedition Map / Sylvan Scrying /
///   every tutor factory.
/// </summary>
[CardName("Journeyer's Kite")]
public static class JourneyersKiteFactory
{
    public const string CardName = "Journeyer's Kite";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Journeyer's Kite owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var kite = new Artifact(CardName, PrintedManaCost);
        kite.SetOwner(owner);
        kite.SetController(owner);

        // ----------------------------------------------------------------
        // {3}, {T}: Search your library for a basic land card, reveal it,
        // put it into your hand, then shuffle.
        // CR 602 — activated ability with two costs. CR 701.19a — search
        // consults the agent (null = decline; legal). CR 305 + CR 205.4 —
        // a "basic land card" is a Land that also carries the Basic
        // supertype (a nonbasic land is excluded). CR 701.20a — shuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            "Journeyer's Kite: tutor a basic land -> hand",
            async ctx =>
            {
                // CR 109.5 / 400.7 — "your library" = the ability's controller's.
                var searcher = ctx.Controller ?? owner;

                var candidates = searcher.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Land)
                                && c.HasSupertype(CardSupertype.Basic))
                    .ToList();

                // CR 701.19a — prompt the agent even on zero candidates so the
                // human searcher sees the failed search (see LibrarySearch xmldoc).
                var pick = await LibrarySearch.PromptOnlyAsync(
                    ctx, searcher, candidates, "basic land card").ConfigureAwait(false);

                if (pick != null)
                {
                    searcher.Zones.Library.RemoveCard(pick);
                    searcher.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle whether or not a card was found.
                LibraryShuffle.ShuffleLibrary(searcher, "journeyers-kite");
            });

        var tutorAbility = new ActivatedAbility(
            source: kite,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(kite),
            },
            effects: new IEffect[] { tutorEffect });

        kite.AddAbility(tutorAbility);

        return kite;
    }
}
