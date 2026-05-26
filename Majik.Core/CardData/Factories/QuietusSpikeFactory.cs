using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quietus Spike (Zendikar, {3}).
///
/// Artifact — Equipment. Oracle text:
///   "Whenever equipped creature deals combat damage to a player, that
///    player loses half their life, rounded up."
///   "Equip {3}."
///
/// ## Implementation
///
/// - <b>Artifact — Equipment {3}</b> — vanilla <see cref="Artifact"/> shell
///   with <see cref="CardSubtype.Equipment"/>.
/// - <b>Combat-damage-to-a-player trigger (CR 510 / CR 603.1)</b>:
///   <see cref="EventTriggerCondition{TEvent}"/> over
///   <see cref="CombatDamageDealtEvent"/> gated on
///   (<see cref="CombatDamageDealtEvent.Source"/> ==
///   <see cref="Permanent.AttachedTo"/>) AND
///   (<see cref="DamageDealtEvent.TargetPlayer"/> != null). On
///   resolution the target player loses
///   <c>ceil(currentLife / 2)</c> life via
///   <see cref="Player.LoseLife"/>. Mirrors the trigger shape of
///   <see cref="SwordOfFireAndIceFactory"/> / Umezawa's Jitte (combat
///   damage to a player) but the payoff reads the live life total of
///   the targeted player at resolution time (CR 608.2b — half is
///   computed on resolution, not on trigger).
/// - <b>Equip {3}</b> — <see cref="EquipActivatedAbility"/> primitive
///   (CR 702.6). Sorcery-speed, "Attach to target creature you control."
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload omits trigger
/// wiring and produces a shape-only card. The
/// <see cref="Create(Player, TriggerManager?)"/> overload registers the
/// combat trigger so bus-driven firing works.
///
/// ## Deferred
///
/// - <b>Replacement / "loses life equal to" interactions</b>: the half-
///   life amount is computed at resolution and passed to
///   <see cref="Player.LoseLife"/>, so any "if a player would lose
///   life, instead …" replacement effect (Worship-family, Roiling
///   Vortex bottom-clause) routes through the existing life-loss
///   pipeline unchanged.
/// - <b>Negative-life rounding</b>: CR 107.1b — life totals can go
///   below zero. <c>Math.Ceiling(currentLife / 2.0)</c> with a negative
///   <c>currentLife</c> yields a smaller absolute value, which matches
///   the printed "half rounded up" reading. Tests cover the positive
///   path; the negative-life edge is exercised via the SBA path
///   elsewhere (CR 704.5a — life &lt;= 0 → loss).
/// </summary>
[CardName("Quietus Spike")]
public static class QuietusSpikeFactory
{
    public const string CardName = "Quietus Spike";
    public const string PrintedManaCost = "{3}";
    public const string EquipCost = "{3}";

    /// <summary>
    /// Shape-only constructor — the combat trigger is attached for shape
    /// but NOT registered with a <see cref="TriggerManager"/>. Suitable
    /// for factory-shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Quietus Spike. When <paramref name="triggers"/> is
    /// supplied the combat-damage trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from the equipped creature
    /// targeting a player automatically queues the ability.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage to a player,
        //    that player loses half their life, rounded up."
        //
        // Predicate gates on (Source == AttachedTo) AND
        // (TargetPlayer != null). Resolution reads the LIVE life total
        // of the triggering target player (CR 608.2b — half is computed
        // at resolution, not at trigger time).
        // ----------------------------------------------------------------
        Player? lastTargetPlayer = null;

        var damageEffect = new Effect(
            $"{CardName}: target player loses half their life, rounded up",
            () =>
            {
                var target = lastTargetPlayer;
                if (target == null) return;

                // CR 107.1 / printed "half rounded up". Math.Ceiling
                // over the live LifeTotal gives the printed semantics
                // for positive life totals (the rule's printed reading);
                // negative life totals are an SBA-loss boundary edge
                // documented in the class xmldoc.
                var amount = (int)Math.Ceiling(target.LifeTotal / 2.0);
                if (amount <= 0) return;
                target.LoseLife(amount);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                if (!ReferenceEquals(e.Source, equipped)) return false;

                lastTargetPlayer = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // Equip {3} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive.
        // ----------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }
}
