using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stormbreath Dragon (Theros, {3}{R}{R}).
///
/// Creature — Dragon 4/4. Oracle text:
///   "Flying.
///    Haste.
///    Protection from white.
///    Monstrosity 3 ({5}{R}{R}: If this creature isn't monstrous, put
///    three +1/+1 counters on it and it becomes monstrous.)
///    When Stormbreath Dragon becomes monstrous, if you have seven or
///    more cards in hand, it deals 3 damage to each opponent."
///
/// ## Implemented (v1)
///
/// - 4/4 Creature — Dragon at {3}{R}{R}, owner/controller wired.
/// - <b>Flying (CR 702.9) + Haste (CR 702.10)</b> — keyword markers via
///   <see cref="KeywordAbility"/>, read by the combat/block subsystem
///   the same way Slickshot Show-Off / Arclight Phoenix wire them.
/// - <b>Protection from white (CR 702.16)</b> — single
///   <see cref="ProtectionAbility"/>("white") instance. The Rules.Protection
///   helpers read the quality string; combat / damage / target / attach
///   gates all consult <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>
///   per colour (mirrors <see cref="PhyrexianCrusaderFactory"/>'s "red +
///   white" pair — Stormbreath ships the white half only).
/// - <b>Monstrosity 3 (CR 702.95)</b> — no Monstrosity primitive exists
///   in the engine today, so the keyword is modelled as a bespoke
///   activated ability "{5}{R}{R}: If this creature isn't monstrous,
///   place three +1/+1 counters on Stormbreath Dragon and mark it
///   monstrous." The marker lives on the returned
///   <see cref="StormbreathDragonAbility"/> instance
///   (<see cref="StormbreathDragonAbility.IsMonstrous"/>) — the activated
///   ability self-gates so a second activation no-ops (CR 702.95b — "if
///   this creature isn't monstrous"). A future Monstrosity primitive
///   PR can lift the marker onto <see cref="Creature"/> and reuse the
///   gate.
/// - <b>"Becomes monstrous" intervening-if trigger (CR 603.4 / CR 603.1)
///   </b> — when the monstrosity activation resolves, the closure invokes
///   the becomes-monstrous trigger inline: if Stormbreath Dragon's
///   controller has seven or more cards in hand, the dragon deals 3
///   damage to each opponent. The hand-size check is performed at
///   resolution time of the monstrosity activation (one stage simpler
///   than firing as a separate triggered ability on the stack — v1
///   convenience, see "Deferred" below).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Suitable for
///   dispatcher / structural tests.
/// - <see cref="Create(Player, Func{IReadOnlyList{Player}}?)"/> — supplies
///   an opponents resolver so the becomes-monstrous trigger can iterate
///   the table. Without one the damage hits no opponents (defensive —
///   keeps shape tests from silently dealing damage to a fake opponent
///   list).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Monstrosity as a first-class keyword primitive</b>: no shared
///   "becomes monstrous" event / "is monstrous" flag is published or
///   read by anything else in the engine. The keyword + marker live
///   inside <see cref="StormbreathDragonAbility"/>; cards that care
///   ("Hold the Monastery", etc.) would need to subscribe to a future
///   primitive's event. Same posture as the Plot deferral noted on
///   <see cref="SlickshotShowOffFactory"/>.
/// - <b>Becomes-monstrous trigger as a real <see cref="TriggeredAbility"/>
///   on the stack</b>: today the intervening-if check + damage runs
///   inline at the end of the monstrosity activation resolve. A future
///   PR can split this into a proper triggered ability that queues on
///   the stack after the monstrosity activation finishes resolving,
///   honouring CR 603.6c intervening-if semantics fully (the hand check
///   would happen twice — once when the trigger goes on the stack,
///   again when it resolves; the v1 single-point check is
///   indistinguishable for any card that doesn't change controller's
///   hand size between those two snapshots, which is the typical case).
/// </summary>
[CardName("Stormbreath Dragon")]
public static class StormbreathDragonFactory
{
    public const string CardName = "Stormbreath Dragon";
    public const string PrintedManaCost = "{3}{R}{R}";
    public const int Power = 4;
    public const int Toughness = 4;
    public const string MonstrosityCost = "{5}{R}{R}";
    public const int MonstrosityCounters = 3;
    public const int BecomesMonstrousHandSize = 7;
    public const int BecomesMonstrousDamage = 3;

    /// <summary>
    /// Construct Stormbreath Dragon with no opponents resolver. The
    /// becomes-monstrous damage finds no opponents (defensive no-op).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, opponentsResolver: null);

    /// <summary>
    /// Construct Stormbreath Dragon with an optional opponents resolver
    /// used by the becomes-monstrous trigger.
    /// </summary>
    public static Creature Create(Player owner, Func<IReadOnlyList<Player>>? opponentsResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dragon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.10 — Flying + Haste keyword markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 702.16 — Protection from white. Quality string "white" is
        // read by Rules.Protection.HasProtectionFromColor (same wiring
        // shape as Phyrexian Crusader's red/white pair).
        card.AddAbility(new ProtectionAbility("white"));

        // CR 702.95 — Monstrosity 3. Modelled as a bespoke activated
        // ability since no Monstrosity primitive exists yet. The
        // ability owns its own "monstrous" flag (see
        // StormbreathDragonAbility.IsMonstrous) and self-gates: a second
        // activation no-ops (CR 702.95b — "If this creature isn't
        // monstrous, …").
        StormbreathDragonAbility? monstrosity = null;
        var monstrosityEffect = new Effect(
            $"{CardName}: become monstrous — +3/+3 counters, then check hand size for 3 to each opponent",
            () =>
            {
                if (monstrosity == null) return;
                if (monstrosity.IsMonstrous) return; // CR 702.95b — self-gate

                // CR 702.95a — place +1/+1 counters and mark monstrous.
                card.Counters.Add(CounterType.PlusOnePlusOne, MonstrosityCounters);
                monstrosity.IsMonstrous = true;

                // CR 603.1 / 603.6c — becomes-monstrous trigger with
                // intervening-if "if you have seven or more cards in
                // hand". Resolved inline; the hand snapshot is taken
                // from the live controller hand zone at activation
                // resolution time (see class xmldoc "Deferred" for
                // the split-trigger gap).
                var controller = card.Controller ?? owner;
                if (controller.Zones.Hand.GetCards().Count() < BecomesMonstrousHandSize) return;

                var opponents = opponentsResolver?.Invoke();
                if (opponents == null) return;

                foreach (var opp in opponents)
                {
                    if (ReferenceEquals(opp, controller)) continue; // defensive
                    if (opp.HasLost) continue;
                    opp.LoseLife(BecomesMonstrousDamage);
                }
            });

        monstrosity = new StormbreathDragonAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(MonstrosityCost) },
            effects: new IEffect[] { monstrosityEffect });

        card.AddAbility(monstrosity);

        return card;
    }
}

/// <summary>
/// Bespoke <see cref="ActivatedAbility"/> for Stormbreath Dragon's
/// Monstrosity 3. Owns the "monstrous" marker (<see cref="IsMonstrous"/>)
/// since no Monstrosity primitive yet exists in the engine. The
/// activated effect self-gates on <see cref="IsMonstrous"/> so a second
/// activation no-ops (CR 702.95b — "If this creature isn't monstrous,
/// …"). Exposed publicly so tests / bots can inspect the live
/// monstrous state.
/// </summary>
public sealed class StormbreathDragonAbility : ActivatedAbility
{
    /// <summary>
    /// CR 702.95b — the monstrous marker. Flipped to <c>true</c> by the
    /// Monstrosity 3 activation resolving; never flipped back (CR
    /// 702.95c — once monstrous, a creature stays monstrous as long
    /// as it remains on the battlefield with that identity).
    /// </summary>
    public bool IsMonstrous { get; set; }

    public StormbreathDragonAbility(
        ICard source,
        Player controller,
        ICost[] costs,
        IEffect[] effects)
        : base(source, controller, costs: costs, effects: effects)
    {
    }
}
