using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Roots (Mirrodin and many reprints).
///
/// Creature — Plant Wall, mana cost {1}{G}, 0/5.
/// Oracle text:
///   "Defender.
///    Put a -0/-1 counter on Wall of Roots: Add {G}. Activate only once
///    each turn."
///
/// ## Implemented (v1)
/// - Card identity: 0/5 Creature — Plant Wall, mana cost {1}{G}.
/// - <b>Defender keyword</b> (CR 702.3) — wired as a
///   <see cref="KeywordAbility"/> marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces it (combat block legality treats the card as a blocker only).
/// - <b>Mana ability</b>: "Put a -0/-1 counter on Wall of Roots: Add {G}."
///   <see cref="ManaAbility"/> built on the new no-tap overload (the
///   permanent's printed cost contains no {T}; the entire cost is the
///   place-counter-on-self side-effect). The
///   <c>additionalCostPayer</c> stamps one
///   <see cref="CounterType.MinusZeroMinusOne"/> on Wall of Roots, which
///   under <see cref="Majik.Core.Effects.ContinuousEffectsService"/>'s
///   layer 7c rule reduces its toughness by 1 (CR 122.1g — counter type
///   handler extended this PR). The <c>canActivateCheck</c> gates on
///   <c>!usedThisTurn</c>; once-per-turn enforcement lives in a closure
///   shared with the <see cref="TurnStartedEvent"/> reset handler
///   (mirrors <see cref="LedgerShredderFactory"/> /
///   <see cref="NaduWingedWisdomFactory"/>).
/// - <b>"Activate only once each turn"</b> (CR 602.5e) — modelled as a
///   <c>int[1] { 0 }</c> per-turn-counter closure. The activate path
///   stamps <c>usedThisTurn[0] = 1</c> after applying the cost; the
///   <see cref="TurnStartedEvent"/> handler installed by the
///   <c>(owner, eventBus)</c> overload resets it to 0 at the start of
///   each new turn (CR 500.1). The single-arg dispatcher path attaches
///   the mana ability with the gate active but never resets the closure
///   — suitable for shape / single-activation tests.
///
/// ## Source-of-truth — toughness reduction
/// The toughness reduction on a -0/-1 counter is layered (CR 613 layer
/// 7c) — to observe it, the Wall must have an
/// <see cref="Majik.Core.Effects.ContinuousEffectsService"/> wired into
/// <see cref="Creature.ActiveEffects"/>. Without a service the printed
/// 0/5 surfaces unmodified (same posture as +1/+1 / -1/-1 counters on a
/// vanilla creature — see <c>CounterPTTests</c>). The SBA layer treats
/// the working toughness uniformly (<see cref="Creature.IsDead"/> reads
/// <see cref="Creature.Toughness"/>), so a Wall with 5 -0/-1 counters
/// and a wired <c>ActiveEffects</c> service reads toughness 0 and dies
/// on the next SBA pass (CR 704.5f).
///
/// ## Deferred (v1 gaps)
/// - <b>Player-driven activation prompt</b>: the once-per-turn gate
///   only enforces legality, not willingness — the bot's source-picker
///   may pick the ability when it needs {G}, same posture as Aether
///   Hub's {T},Pay{E} mana ability. CR 605.1 mana ability — no stack,
///   no priority pass.
/// - <b>Cleanup-step counter removal</b>: -0/-1 counters persist
///   permanently on Wall of Roots (Wall of Roots' printed wording does
///   NOT cycle them off in the cleanup step — they accumulate across
///   turns). No deferred work; behaviour matches the printed card.
/// </summary>
[CardName("Wall of Roots")]
public static class WallOfRootsFactory
{
    public const string CardName = "Wall of Roots";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 0;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Wall of Roots with no event bus wiring. The once-per-turn
    /// activation gate is attached but the closure is never reset — first
    /// activation succeeds, subsequent activations within the same Creature
    /// lifetime are blocked. Suitable for card-shape / dispatcher tests
    /// and single-turn scenarios.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Wall of Roots with optional <see cref="TurnStartedEvent"/>
    /// reset wiring. When <paramref name="eventBus"/> is supplied, the
    /// per-turn activation lock is reset at the start of every turn
    /// (CR 500.1) — mirrors <see cref="LedgerShredderFactory"/>'s
    /// per-turn-count subscribe pattern.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus that publishes
    /// <see cref="TurnStartedEvent"/>. May be null — the closure is then
    /// never reset and a second-activation-same-turn test is the only
    /// path the gate exercises.</param>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Plant, CardSubtype.Wall });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block-legality
        // (BlockLegality.cs reads this via the KeywordAbility-marker
        // fallback path).
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // ----------------------------------------------------------------
        // Per-turn activation lock (CR 602.5e — "Activate only once each
        // turn"). Closure shared between the canActivateCheck and the
        // TurnStartedEvent reset handler.
        // ----------------------------------------------------------------
        var usedThisTurn = new int[] { 0 };

        // ----------------------------------------------------------------
        // Mana ability — "Put a -0/-1 counter on Wall of Roots: Add {G}."
        //
        // CR 605.1 — mana ability (no stack). The activation cost is the
        // place-counter-on-self side-effect; there is NO {T} component
        // (the no-tap ManaAbility overload). The
        // canActivateCheck gates the once-per-turn limit. The
        // additionalCostPayer stamps a -0/-1 counter and flips the
        // per-turn lock; layer 7c on the wired ContinuousEffectsService
        // reduces toughness by 1 (CR 122.1g).
        // ----------------------------------------------------------------
        card.AddAbility(new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => usedThisTurn[0] == 0,
            additionalCostPayer: _ =>
            {
                card.Counters.Add(CounterType.MinusZeroMinusOne, 1);
                usedThisTurn[0] = 1;
            },
            tapsAsCost: false));

        // CR 500.1 — reset the per-turn activation lock at the start of
        // each turn. Mirrors LedgerShredderFactory's TurnStartedEvent
        // subscription. When no event bus is supplied the closure
        // remains permanently set after the first activation —
        // acceptable for shape / single-turn tests.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => usedThisTurn[0] = 0);
        }

        return card;
    }
}
