using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Knight-Errant of Eos (Modern Horizons 3,
/// {3}{G}{W}).
///
/// Creature — Elf Knight 4/4. Oracle text:
///   "Convoke
///    When Knight-Errant of Eos enters, look at the top six cards of your
///    library. You may reveal up to two creature cards with mana value 2
///    or less from among them and put them into your hand. Put the rest
///    on the bottom of your library in a random order."
///
/// ## Why a named factory
/// The ETB peek-six-take-up-to-two-creatures-mv≤2 effect is not template-
/// covered: it's a select-up-to-N from a peeked pool with a printed
/// predicate (creature card, mana value ≤ 2), with the rest going to the
/// bottom in random order. Shape lines up with
/// <see cref="AtraxaGrandUnifierFactory"/>'s top-10 reveal-and-pick walk
/// — same peek / sort-into-keep-vs-bottom / Fisher-Yates re-bottom
/// pattern — but with a printed predicate filter and an "up to N" cap
/// instead of "one per card type".
///
/// ## Implemented (v1)
/// - 4/4 Creature — Elf Knight at {3}{G}{W}, owner / controller wired.
/// - <b>Convoke</b> (CR 702.51) keyword marker via
///   <see cref="KeywordAbility"/>. Surfaced via
///   <see cref="BuildAlternativeCost"/> for cost-flow integration —
///   marker-only today (same posture as <see cref="ChordOfCallingFactory"/>).
///   The pure-function reducer
///   <see cref="ConvokeAlternativeCost.ReduceCost"/> can be exercised in
///   isolation; full reduction wiring lands when
///   <see cref="Majik.Core.Game.SpellCastFlow"/> grows a Convoke-aware
///   reduction hook (the shared deferred gap with Chord of Calling /
///   Kappa Cannoneer).
/// - <b>ETB trigger (CR 603.6a)</b> "When Knight-Errant of Eos enters,
///   look at the top six cards of your library. You may reveal up to two
///   creature cards with mana value 2 or less from among them and put
///   them into your hand. Put the rest on the bottom of your library in
///   a random order." Resolution:
///     1. Peek up to the top 6 cards of the controller's library.
///     2. Pre-filter to creature cards with printed mana value ≤ 2
///        (<see cref="ManaCost.Parse"/> + <see cref="ManaCost.TotalValue"/>).
///     3. Up to two picks via two sequential
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> calls on the
///        narrowed candidate list — the controller's registered agent
///        picks (or declines via <see langword="null"/>). Each picked
///        card is removed from the candidate pool before the next
///        prompt so the agent doesn't pick the same card twice.
///     4. Picks move to the controller's hand (CR 701.16 zone-move via
///        direct mutation — same posture as the rest of the peek-and-pick
///        factories; reveal-event publication is the shared deferred gap
///        per <see cref="AtraxaGrandUnifierFactory"/>'s class xmldoc).
///     5. The remainder of the peeked six (whatever wasn't chosen) is
///        re-bottomed in a random order via the active
///        <see cref="GameRandom"/> (CR 701.20a — Fisher-Yates).
///   The trigger is attached to the card; tests invoke
///   <see cref="ResolveEtb"/> directly without TriggerManager registration
///   (same single-arg pattern as
///   <see cref="AtraxaGrandUnifierFactory.ResolveEtb"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Convoke cost reduction</b> — marker-only; shares the deferred
///   reduction wiring with <see cref="ChordOfCallingFactory"/>.
/// - <b>Reveal-event publication</b>: picks are read from the peeked pool
///   directly; no <c>CardsRevealedEvent</c> fires. No live observer cares
///   in v1 (shared gap with Ancient Stirrings, Atraxa, Mystical Tutor,
///   Goblin Matron — see <see cref="AtraxaGrandUnifierFactory"/> for the
///   tracking note).
/// - <b>Trigger-on-bus</b>: the ETB trigger is attached for shape but not
///   registered against a live <see cref="TriggerManager"/> — tests invoke
///   <see cref="ResolveEtb"/> directly. Same single-arg dispatcher posture
///   as <see cref="AtraxaGrandUnifierFactory.Create"/>.
/// </summary>
[CardName("Knight-Errant of Eos")]
public static class KnightErrantOfEosFactory
{
    public const string CardName = "Knight-Errant of Eos";
    public const string PrintedManaCost = "{3}{G}{W}";
    public const int Power = 4;
    public const int Toughness = 4;
    public const int PeekCount = 6;
    public const int MaxPicks = 2;
    public const int ManaValueCeiling = 2;

    /// <summary>
    /// Construct Knight-Errant of Eos owned and controlled by
    /// <paramref name="owner"/>. The ETB trigger is attached to the card
    /// for shape inspection but not registered with a TriggerManager —
    /// tests / callers invoke <see cref="ResolveEtb"/> directly to drive
    /// the peek-and-pick (mirrors
    /// <see cref="AtraxaGrandUnifierFactory.Create"/>).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.51 — Convoke keyword marker. Reduction wiring is
        // marker-only today; see class xmldoc.
        card.AddAbility(new KeywordAbility("Convoke", card, owner));

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a). The effect closure captures `owner` as
        // the controller (controller may change later via Threaten-style
        // effects; the ETB-time controller is whoever owns Knight-Errant
        // at the moment it enters the battlefield).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: peek top {PeekCount}, take up to {MaxPicks} creature cards mv≤{ManaValueCeiling} to hand, rest to bottom random",
            ctx => ResolveEtbAsync(owner, ctx));

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);

        return card;
    }

    /// <summary>
    /// Build the marker-only Convoke alt-cost for this Knight-Errant.
    /// Returns the printed cost unchanged in v1 — see class xmldoc for
    /// the shared Convoke reduction gap.
    /// </summary>
    public static ConvokeAlternativeCost BuildAlternativeCost() =>
        new(ManaCost.Parse(PrintedManaCost));

    /// <summary>
    /// Build the per-cast Convoke additional cost given the caller-selected
    /// tapped creatures. Mirrors
    /// <see cref="ChordOfCallingFactory.BuildAdditionalCost"/>; the cast
    /// flow taps the chosen creatures and folds the per-tap reduction
    /// into the mana payment when Convoke reduction lands.
    /// </summary>
    public static ConvokeAdditionalCost BuildAdditionalCost(
        ICard card, IReadOnlyList<Creature> tappedCreatures) =>
        new(card, tappedCreatures);

    /// <summary>
    /// Execute Knight-Errant of Eos's ETB resolution against
    /// <paramref name="controller"/>'s library + hand. Public so tests and
    /// bots can invoke the effect directly without going through
    /// TriggerManager. Walks up to <see cref="PeekCount"/> cards from the
    /// top of the library, filters to creature cards with mana value ≤
    /// <see cref="ManaValueCeiling"/>, prompts the controller's agent
    /// (registered via <see cref="AgentRegistry"/>) twice in sequence for
    /// up to <see cref="MaxPicks"/> picks, moves picks to hand, and
    /// re-bottoms the remainder in a randomised order via the active
    /// <see cref="Random.GameRandom"/>.
    /// </summary>
    public static async ValueTask ResolveEtbAsync(Player controller, ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;
        var peeked = library.GetCards().Take(PeekCount).ToList();
        if (peeked.Count == 0) return;

        // CR 202.3b — mana value is computed from the printed cost. Filter
        // peeked cards to creatures with mv ≤ 2 (the "up to two creature
        // cards with mana value 2 or less" predicate).
        var eligible = peeked
            .Where(c => c.HasType(CardType.Creature)
                && ManaCost.Parse(c.ManaCost).TotalValue <= ManaValueCeiling)
            .ToList();

        var picks = new List<ICard>(MaxPicks);
        var agent = ctx.Agent ?? AgentRegistry.Get(controller);

        // Up to MaxPicks sequential prompts. "Up to two" — the agent may
        // decline at any iteration by returning null; once eligible is
        // empty we stop early.
        for (int i = 0; i < MaxPicks && eligible.Count > 0; i++)
        {
            ICard? pick;
            if (agent is not null)
            {
                pick = await agent.ChooseLibraryPickAsync(
                    ctx.Game,
                    candidates: eligible,
                    kindLabel: $"creature card with mana value {ManaValueCeiling} or less (pick {i + 1} of up to {MaxPicks})")
                    .ConfigureAwait(false);
            }
            else
            {
                // No agent registered — deterministic "take the first
                // eligible card". Matches the IPlayerAgent default
                // implementation of ChooseLibraryPickAsync. Mirrors the
                // <see cref="ChordOfCallingFactory"/> agent-null path.
                pick = eligible[0];
            }
            if (pick is null) break;  // agent declined — stop.
            picks.Add(pick);
            eligible.Remove(pick);
        }

        // Move picks to hand (preserving peek order so test output is
        // deterministic).
        foreach (var c in peeked)
        {
            if (!picks.Contains(c)) continue;
            library.RemoveCard(c);
            controller.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }

        // CR 701.20a — remainder of the peeked pool to bottom in a random
        // order. Pulls from the active GameRandom for replay determinism
        // (same posture as <see cref="AtraxaGrandUnifierFactory.Shuffle"/>).
        var remainder = peeked.Where(c => !picks.Contains(c)).ToList();
        foreach (var c in remainder) library.RemoveCard(c);
        Majik.Core.Random.GameRandomRegistry.Default.Shuffle(remainder);
        foreach (var c in remainder)
        {
            library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
