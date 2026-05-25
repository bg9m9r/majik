using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Random;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Charbelcher (Mirrodin, {4}).
///
/// Artifact. Oracle text:
///   "{3}, {T}: Reveal cards from the top of your library until you reveal
///    a nonland card. Goblin Charbelcher deals damage equal to the number
///    of land cards revealed this way to any target. If all revealed cards
///    are Mountains, double that damage. Then put the revealed cards on
///    the bottom of your library in a random order."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {4}).
/// - <b>{3}, {T}: belch</b> wired as a single <see cref="ActivatedAbility"/>
///   with <see cref="ManaCostCost"/>("{3}") + <see cref="AdditionalCost.Tap"/>
///   and a 1..1 "any target" <see cref="TargetRequest"/>. Resolution walks
///   the controller's library from the top, peeling cards into a local
///   reveal list until a nonland card appears (or the library runs dry,
///   CR 608.2b — clean stop). Lands are counted; non-Mountain lands flip
///   the "all-Mountains" gate. Damage = land-count × (all-Mountains ? 2 : 1)
///   routed through <see cref="Fx.DealDamageAny"/> so Player / Creature /
///   Planeswalker targets all funnel through the right damage shape (CR
///   119.3 / CR 306.7). Then the revealed pile is randomised via
///   <see cref="GameRandom.Shuffle"/> and bottomed onto the controller's
///   library via raw <c>Zone.AddCard</c> appends (CR 701.20 — distinct
///   from a full library shuffle).
/// - The nonland terminator itself is part of the revealed pile (CR oracle
///   text: "Then put the revealed cards on the bottom" — every card seen,
///   including the nonland trigger, gets bottomed). Mirrors how
///   <see cref="TibaltsTrickeryFactory"/> bottoms its full exile pile.
/// - <b>Random library reorder</b> uses
///   <see cref="GameRandomRegistry.Get"/> for seed-stable replay so tests
///   pinning a seed see the same bottom order; tests that don't pin a seed
///   get a fresh RNG per run.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: cards aren't published on a reveal bus
///   (same gap as every other reveal-until factory — Tibalt's Trickery /
///   Ancient Stirrings / Dark Confidant).
/// - <b>Damage-doubling layered shape</b>: the "doubled if all Mountains"
///   clause is a one-shot at resolution (CR 700.2), not a continuous
///   damage-double replacement (CR 614 — Furnace of Rath shape). The
///   Charbelcher text is genuinely one-shot so no layer plumbing is
///   needed.
/// - <b>Activator wiring</b>: target prompts come through
///   <see cref="ActivatedAbility.ChosenTargets"/> at resolve time —
///   illegal-on-resolution target = no damage (CR 608.2b), but the
///   reveal/bottom still runs because the cost was paid.
/// </summary>
[CardName("Goblin Charbelcher")]
public static class GoblinCharbelcherFactory
{
    public const string CardName = "Goblin Charbelcher";
    public const string PrintedManaCost = "{4}";

    /// <summary>
    /// Construct Goblin Charbelcher owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var belcher = new Artifact(CardName, PrintedManaCost);
        belcher.SetOwner(owner);
        belcher.SetController(owner);

        // ----------------------------------------------------------------
        // {3}, {T}: Reveal cards from the top of your library until you
        // reveal a nonland card. ~ deals damage equal to the number of
        // land cards revealed this way to any target. If all revealed
        // cards are Mountains, double that damage. Then put the revealed
        // cards on the bottom of your library in a random order.
        // ----------------------------------------------------------------
        ActivatedAbility? belchAbility = null;
        var belchEffect = new Effect(
            "Goblin Charbelcher: reveal-until-nonland + damage + random-bottom",
            () =>
            {
                object? target = null;
                if (belchAbility != null
                    && belchAbility.ChosenTargets.Count > 0
                    && belchAbility.ChosenTargets[0].Count > 0)
                {
                    target = belchAbility.ChosenTargets[0][0];
                }

                var controller = belcher.Controller ?? owner;
                ResolveBelch(controller, target);
            });

        belchAbility = new ActivatedAbility(
            source: belcher,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(belcher),
            },
            effects: new IEffect[] { belchEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        belcher.AddAbility(belchAbility);

        return belcher;
    }

    /// <summary>
    /// Resolve Goblin Charbelcher's activated ability against the supplied
    /// <paramref name="target"/> (Player / Creature / Planeswalker; null
    /// or illegal at resolution = damage step is skipped per CR 608.2b).
    /// The reveal + random-bottom always runs because the cost was paid.
    /// Exposed for direct invocation by tests / bots without driving the
    /// full activator pipeline.
    /// </summary>
    public static BelcherResolution ResolveBelch(Player controller, object? target)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;
        var revealed = new List<ICard>();
        var landCount = 0;
        var allMountains = true; // vacuously true; flipped by any non-Mountain reveal.

        while (true)
        {
            var top = library.GetCards().FirstOrDefault();
            if (top == null) break; // CR 608.2b — library empty halts the reveal cleanly.

            library.RemoveCard(top);
            revealed.Add(top);

            if (top.HasType(CardType.Land))
            {
                landCount++;
                if (!top.HasSubtype(CardSubtype.Mountain))
                {
                    allMountains = false;
                }
                // Continue revealing — lands don't terminate.
                continue;
            }

            // Nonland reveal — the printed terminator. It still counts as
            // "revealed this way" so it joins the bottom-shuffle pile, but
            // it does NOT contribute to the land count nor satisfy
            // "all Mountains" (a nonland is not a Mountain).
            allMountains = false;
            break;
        }

        // If the reveal exited via library-empty with zero non-Mountains
        // seen, allMountains stays true. The "all revealed cards are
        // Mountains" check is vacuously true with zero reveals too (no
        // counterexample), but in that case damage is 0 × 2 = 0 — same
        // outcome.
        var damage = landCount * (allMountains ? 2 : 1);

        if (target != null && damage > 0)
        {
            Fx.DealDamageAny(target, damage);
        }

        // CR 701.20 — bottom the revealed cards in a random order.
        var random = GameRandomRegistry.Get(controller);
        var toBottom = revealed.ToList();
        random.Shuffle(toBottom);
        foreach (var card in toBottom)
        {
            library.AddCard(card); // Append == bottom.
            card.SetZone(ZoneType.Library);
        }

        return new BelcherResolution(
            Revealed: revealed,
            LandCount: landCount,
            AllMountains: allMountains,
            Damage: damage);
    }

    /// <summary>
    /// Observation record describing one Goblin Charbelcher activation —
    /// the revealed pile, the land tally, the all-Mountains gate, and the
    /// final damage figure. Returned by <see cref="ResolveBelch"/> for
    /// tests / bots that want to inspect the resolution without observing
    /// it through the target.
    /// </summary>
    public sealed record BelcherResolution(
        IReadOnlyList<ICard> Revealed,
        int LandCount,
        bool AllMountains,
        int Damage);
}
