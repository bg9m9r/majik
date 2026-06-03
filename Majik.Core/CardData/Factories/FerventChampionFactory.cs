using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fervent Champion (Throne of Eldraine, {R}).
/// Creature — Human Knight, 1/1. Oracle text (verified against Scryfall):
///   "First strike, haste"
///   "Whenever this creature attacks, another target attacking Knight you
///    control gets +1/+0 until end of turn."
///   "Equip abilities you activate that target this creature cost {3} less
///    to activate."
///
/// ## Implemented (v1)
///
/// - <b>1/1 red Human Knight at {R}</b>, owner / controller wired.
/// - <b>First strike (CR 702.7) + Haste (CR 702.10)</b> — two
///   <see cref="KeywordAbility"/> markers so <c>ICard.Abilities</c> reflects
///   the printed keyword line and Scryfall keyword parsing matches. Mirrors
///   <see cref="AshZealotFactory"/> (also First strike + Haste).
/// - <b>Attack trigger (CR 508.2)</b> — "Whenever this creature attacks,
///   another target attacking Knight you control gets +1/+0 until end of
///   turn." A <see cref="Triggers.OnAttackSelf"/>
///   <see cref="TriggeredAbility"/> carrying a single 1..1
///   <see cref="TargetRequest"/>. Legal candidates are read at agent-prompt
///   time from <paramref name="attackingCreaturesSource"/> filtered to
///   <see cref="CardSubtype.Knight"/> creatures the controller controls,
///   excluding Fervent Champion itself ("ANOTHER target attacking Knight").
///   On resolution the chosen Knight gets +1/+0 until end of turn via
///   <see cref="PumpUntilEndOfTurnEffect"/> (CR 514.2 cleanup expiry). Same
///   source-closure posture as <see cref="HonoredCropCaptainFactory"/> /
///   <see cref="GoblinWardriverFactory"/> — when no attackers source is
///   supplied the pump is a no-op (the engine doesn't expose a global
///   "currently attacking creatures" view inside an effect closure).
/// - <b>Equip-cost reduction static (CR 117.7 / 702.6c)</b> — "Equip
///   abilities you activate that target this creature cost {3} less to
///   activate." Wired via the <see cref="EquipCostReductionEffect"/>
///   lifecycle binder (keyed on Fervent Champion as the equip target). The
///   shared equip cost provider
///   (<see cref="PuresteelPaladinFactory.ZeroEquipCostProvider"/>, the default
///   <c>costProvider</c> on every Equipment factory's
///   <see cref="EquipActivatedAbility"/>) consults
///   <see cref="EquipCostReductionEffect.ReductionForTarget"/> for the equip
///   ability's chosen target and subtracts {3} from the printed generic cost
///   (coloured pips untouched; floor at zero). With an
///   <paramref name="eventBus"/> the binder tracks ETB / LTB; the shape-only
///   path constructs the binder and attaches it unconditionally so manual
///   battlefield setup in tests still flips the reducer on.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Trigger-target prompt</b>: when no agent supplies
///   <see cref="TriggeredAbility.ChosenTargets"/> the +1/+0 pump no-ops
///   (same posture as the other attack-trigger factories). A real prompt
///   would pick among legal attacking Knights.
/// </summary>
[CardName("Fervent Champion")]
public static class FerventChampionFactory
{
    public const string CardName = "Fervent Champion";
    public const string Cost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>+1/+0 to the chosen attacking Knight.</summary>
    public const int PumpPower = 1;
    public const int PumpToughness = 0;

    /// <summary>"Equip abilities … cost {3} less."</summary>
    public const int EquipReduction = EquipCostReductionEffect.DefaultReduction;

    /// <summary>
    /// Construct Fervent Champion with no live runtime wiring. Keyword markers
    /// + the attack trigger are attached to the card shape (the pump is a
    /// no-op without an attackers source); the equip-cost-reduction binder is
    /// constructed and attached unconditionally so manual battlefield setup in
    /// shape tests flips the reducer on. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, triggers: null, attackingCreaturesSource: null);

    /// <summary>
    /// Construct Fervent Champion with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, drives the
    /// <see cref="EquipCostReductionEffect"/> ETB / LTB lifecycle so the
    /// reducer turns off when Fervent Champion leaves the battlefield.</param>
    /// <param name="triggers">When supplied, the attack trigger is registered
    /// so a <see cref="CreatureAttacksEvent"/> for Fervent Champion lands it
    /// on the stack automatically.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker creature list, called at trigger resolution to gather legal
    /// "attacking Knight you control" targets. May be null — the pump is then
    /// a no-op.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.7 / 702.10 — First strike + Haste keyword markers.
        card.AddAbility(new KeywordAbility("First strike", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // --------------------------------------------------------------
        // Attack trigger — "Whenever this creature attacks, another target
        // attacking Knight you control gets +1/+0 until end of turn."
        // (CR 508.2 attack trigger; CR 514.2 cleanup expiry)
        // --------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        // "another target attacking Knight you control" — legal candidates are
        // attacking Knights the controller controls, excluding Fervent
        // Champion itself. Read live from the attackers closure at prompt time.
        IReadOnlyList<object> GatherKnights()
        {
            if (attackingCreaturesSource == null) return Array.Empty<object>();
            var attackers = attackingCreaturesSource() ?? Array.Empty<Creature>();
            return attackers
                .Where(c => c != null)
                .Where(c => !ReferenceEquals(c, card))                 // "another"
                .Where(c => c.HasSubtype(CardSubtype.Knight))          // "Knight"
                .Where(c => ReferenceEquals(c.Controller, owner))      // "you control"
                .Cast<object>()
                .ToList();
        }

        var pumpEffect = new Effect(
            $"{CardName}: another target attacking Knight you control gets +1/+0 EOT",
            () =>
            {
                if (attackTrigger == null) return;
                if (attackTrigger.ChosenTargets.Count == 0) return;
                if (attackTrigger.ChosenTargets[0].Count == 0) return;
                if (attackTrigger.ChosenTargets[0][0] is not Creature knight) return;
                // Re-validate at resolution (CR 608.2b) — still another Knight
                // the controller controls.
                if (ReferenceEquals(knight, card)) return;
                if (!knight.HasSubtype(CardSubtype.Knight)) return;
                if (!ReferenceEquals(knight.Controller, owner)) return;
                if (knight.ActiveEffects == null) return;
                knight.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(knight, PumpPower, PumpToughness));
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target attacking Knight you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    CandidateGatherer: _ => GatherKnights()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // --------------------------------------------------------------
        // Equip-cost reduction static — "Equip abilities you activate that
        // target this creature cost {3} less to activate." (CR 117.7 /
        // 702.6c). Keyed on Fervent Champion as the equip TARGET; consumed by
        // the shared equip cost provider via
        // EquipCostReductionEffect.ReductionForTarget. Attached regardless of
        // eventBus presence so shape tests that manually place the card on the
        // battlefield and call Attach() once pick up the current zone.
        // --------------------------------------------------------------
        var reducer = new EquipCostReductionEffect(
            source: card,
            target: card,
            eventBus: eventBus,
            reduction: EquipReduction);
        reducer.Attach();

        return card;
    }
}
