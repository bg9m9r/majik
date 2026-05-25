using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Recruitment Officer (Modern Horizons 3, {W}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "Recruitment Officer can block as if it had reach.
///    {2}{W}: Look at the top six cards of your library. You may reveal
///    a creature card with mana value 2 or less from among them and put
///    it into your hand. Put the rest on the bottom of your library in a
///    random order."
///
/// ## Why a named factory
/// Recruitment Officer is the Modern Boros Energy / Mardu Pyromancer
/// "anti-air 1-drop + late-game creature tutor" anchor. Two non-shape
/// behaviours need wiring: (1) the printed "can block as if it had
/// reach" rider — functionally identical to printed Reach for every
/// combat path the engine routes through
/// <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/>
/// (CR 702.17 — Reach lets a creature block flyers; "can block as if
/// it had reach" is the same blocker-legality grant under a different
/// printed phrasing, CR 702.9b); (2) the activated look-6-may-reveal-
/// creature-mv≤2 tutor, structurally a tighter
/// <see cref="AncientStirringsFactory"/> filtered to creature-card +
/// mana value ≤ 2 instead of colourless.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Soldier, mana cost {W}. Both
///   <see cref="CardSubtype.Human"/> and <see cref="CardSubtype.Soldier"/>
///   assigned (CR 205.3 — printed subtypes).
/// - <b>"Can block as if it had reach" rider</b> wired as a
///   <see cref="KeywordAbility"/>("Reach") marker (CR 702.17). The
///   engine's combat path reads Reach via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasReach"/>, which
///   feeds <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/>;
///   for blocker-legality purposes Recruitment Officer is therefore
///   indistinguishable from a printed-Reach creature, matching the
///   printed oracle's intent. Strict CR phrasing distinction (CR 702.9b
///   — Recruitment Officer doesn't <em>have</em> Reach, it blocks
///   <em>as if</em> it had Reach) is structurally invisible to the v1
///   combat surface — the gap only matters for "creature with Reach"
///   triggers (vanishingly rare in Modern; deferred until a card
///   actually cares).
/// - <b>{2}{W}: Look at top 6, may reveal a creature with mana value
///   ≤ 2, put rest on the bottom in a random order</b> — wired as a
///   single <see cref="ActivatedAbility"/> (CR 602) with cost
///   <see cref="ManaCostCost"/>("{2}{W}"). Resolution mirrors
///   <see cref="AncientStirringsFactory.BuildResolveEffect"/>:
///     1. Peek up to top 6 cards of the controller's library.
///     2. Filter to <see cref="CardType.Creature"/> with mana value
///        ≤ 2 (CR 202.3 — mana value of the printed cost, including
///        coloured pips).
///     3. Consult the controller's
///        <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the
///        creature to take (kindLabel "creature card with mana value
///        2 or less"); deterministic first-eligible fallback when no
///        agent registered (same posture as Expedition Map /
///        Stoneforge Mystic / Sylvan Scrying).
///     4. Move the pick to hand (CR 701.19a).
///     5. Re-bottom the remaining peeked cards in a randomised
///        Fisher-Yates order (CR 701.20a — "random order" via
///        <see cref="System.Random.Shared"/>; deterministic RNG seam
///        deferred — same posture as Ancient Stirrings).
/// - <b>Selector seam</b>: <see cref="DefaultPickSelector"/> exposes
///   the same selector signature as
///   <see cref="AncientStirringsFactory.StirringsSelector"/> so tests
///   / future agent wiring can override the default pick + bottom
///   order without touching the live library state. Useful for
///   deterministic regression tests.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the picked creature card moves
///   Library → Hand without publishing a reveal event (the printed
///   "you may reveal" rider). Same gap as Stoneforge Mystic's ETB
///   tutor / Sylvan Scrying / every tutor factory.
/// - <b>Random bottom RNG seam</b>: the bottom shuffle uses
///   <see cref="System.Random.Shared"/>; once the engine exposes a
///   deterministic RNG hook for replay, this should consume it
///   instead — same posture as Ancient Stirrings.
/// - <b>Strict "block as if it had reach" vs. printed Reach</b>:
///   triggers / static abilities that read "creature you control with
///   Reach" would incorrectly include Recruitment Officer with the v1
///   shape. No such trigger exists in the engine's current card pool;
///   if one ships, the right fix is a separate "block-as-if-reach"
///   marker that <see cref="Majik.Core.Combat.CombatAbilities.CanBlockFlying"/>
///   honours but <see cref="Majik.Core.Combat.CombatAbilities.HasReach"/>
///   ignores.
/// - <b>"You may" opt-out</b>: when an eligible creature is in the
///   peeked pile, the default selector always takes it. Real
///   agent-driven opt-out (CR 116.1b) awaits the same Yes/No prompt
///   surface as Esper Sentinel / Voltage Surge.
/// </summary>
[CardName("Recruitment Officer")]
public static class RecruitmentOfficerFactory
{
    public const string CardName = "Recruitment Officer";
    public const string PrintedManaCost = "{W}";
    public const string ActivationCost = "{2}{W}";
    public const int PeekCount = 6;
    public const int MaxManaValue = 2;

    /// <summary>
    /// Construct Recruitment Officer owned and controlled by
    /// <paramref name="owner"/>. The Reach-equivalent block rider +
    /// the {2}{W} look-6 activated ability are wired unconditionally
    /// (no live <see cref="TriggerManager"/> or bus needed — both are
    /// static / activated, not triggered).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Recruitment Officer can block as if it had reach."
        // CR 702.17 — Reach. v1 attaches a KeywordAbility("Reach")
        // marker so CombatAbilities.HasReach / CanBlockFlying treats
        // Recruitment Officer as a valid flying blocker. Strict
        // "as if it had reach" phrasing distinction (CR 702.9b) is
        // structurally invisible to the v1 combat surface — the only
        // observable consumer (CanBlockFlying) gets the right answer.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // ----------------------------------------------------------------
        // {2}{W}: Look at top 6, may reveal a creature card with mana
        // value ≤ 2, put rest on the bottom in a random order.
        // CR 602 — activated ability. Resolution mirrors Ancient
        // Stirrings' look-N-pick-bottom shape.
        // ----------------------------------------------------------------
        var lookEffect = new Effect(
            $"{CardName}: look at top 6, may reveal a creature with mana value ≤ 2 to hand, rest to bottom in random order",
            () => Resolve(card.Controller ?? owner));

        var lookAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivationCost) },
            effects: new IEffect[] { lookEffect });

        card.AddAbility(lookAbility);

        return card;
    }

    /// <summary>Selector signature: from the peeked up-to-6 cards, choose
    /// (toHand, toBottomInOrder). Implementations must return cards that
    /// partition the input — no duplicates, no extras. <c>toHand</c> is
    /// 0 or 1 cards; <c>toBottom</c> is the remainder in the order they
    /// should be re-appended to the bottom of the library. Mirrors
    /// <see cref="AncientStirringsFactory.StirringsSelector"/>.</summary>
    public delegate (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom) PickSelector(
        IReadOnlyList<ICard> peeked);

    /// <summary>
    /// Default selector: pick the first creature card with mana value
    /// ≤ 2 (CR 202.3 — mana value of the printed cost, including
    /// coloured pips); if none qualify, no card moves to hand.
    /// Remaining cards are shuffled (CR 701.20a) before being placed
    /// at the bottom.
    /// </summary>
    public static (IReadOnlyList<ICard> toHand, IReadOnlyList<ICard> toBottom)
        DefaultPickSelector(IReadOnlyList<ICard> peeked)
    {
        ArgumentNullException.ThrowIfNull(peeked);

        ICard? pick = null;
        foreach (var c in peeked)
        {
            if (IsEligible(c))
            {
                pick = c;
                break;
            }
        }

        var toHand = pick == null
            ? Array.Empty<ICard>()
            : new[] { pick };

        var bottom = new List<ICard>(peeked.Count);
        foreach (var c in peeked)
        {
            if (!ReferenceEquals(c, pick)) bottom.Add(c);
        }
        Shuffle(bottom);
        return (toHand, bottom);
    }

    /// <summary>
    /// Build the resolution effect list for tests / direct invocation
    /// without going through the activated-ability cost path. Pass
    /// <see cref="DefaultPickSelector"/> for the printed behaviour, or
    /// a custom selector for deterministic tests.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller,
        PickSelector? selector = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        var pick = selector ?? DefaultPickSelector;
        return new IEffect[]
        {
            new Effect(
                $"{CardName}: look at top 6, may reveal a creature with mana value ≤ 2",
                () => Resolve(controller, pick)),
        };
    }

    /// <summary>
    /// Eligibility predicate: <see cref="CardType.Creature"/> with mana
    /// value ≤ <see cref="MaxManaValue"/>. Surfaced for tests and the
    /// agent-prompt MVP (the kindLabel uses the same wording).
    /// </summary>
    public static bool IsEligible(ICard card)
    {
        if (card == null) return false;
        if (!card.HasType(CardType.Creature)) return false;
        // CR 202.3 — mana value is computed from the printed mana cost.
        // ICard only exposes the printed string; parse it once per
        // candidate (cheap — peeks are bounded to <see cref="PeekCount"/>).
        var mv = ValueObjects.ManaCost.Parse(card.ManaCost ?? string.Empty);
        return mv.TotalValue <= MaxManaValue;
    }

    // -----------------------------------------------------------------
    // Resolution helpers
    // -----------------------------------------------------------------

    private static void Resolve(Player controller, PickSelector? selector = null)
    {
        var pick = selector ?? DefaultPickSelector;
        var lib = controller.Zones.Library;
        var peeked = lib.GetCards().Take(PeekCount).ToList();
        if (peeked.Count == 0) return;

        var (toHand, toBottom) = pick(peeked);

        // Move chosen (0 or 1) to hand.
        foreach (var c in toHand)
        {
            lib.RemoveCard(c);
            controller.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }
        // Re-bottom the rest. Zone.AddCard appends — appending in the
        // (already-shuffled) toBottom order gives the random bottom
        // placement required by the printed text (CR 701.20a).
        foreach (var c in toBottom)
        {
            lib.RemoveCard(c);
        }
        foreach (var c in toBottom)
        {
            lib.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private static void Shuffle<T>(IList<T> list)
    {
        // Fisher-Yates via Random.Shared. Tests that need determinism
        // should pass a custom selector instead of relying on the
        // default. Same RNG posture as Ancient Stirrings.
        var rng = System.Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
