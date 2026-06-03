using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.135 — Boast. "Boast" is an activated-ability keyword from Kaldheim.
/// The printed form is <c>Boast — [cost]: [effect]</c>, and every Boast ability
/// carries the same built-in activation restriction (CR 702.135b/c):
///
///   "Activate only if this creature attacked this turn and only once each turn."
///
/// <para>
/// This helper builds the activated ability and wires the two-part gate onto the
/// existing <see cref="ActivatedAbility"/> <c>canActivateCheck</c> seam
/// (CR 602.5c / the gate added in PR #1683):
/// </para>
/// <list type="bullet">
///   <item><b>"attacked this turn"</b> (CR 702.135b) — a boxed cell flipped true
///   when this creature is among the attackers in an
///   <see cref="AttackersDeclaredEvent"/> whose attacking player controls it
///   (mirrors <see cref="Majik.Core.CardData.Factories.CombatCelebrantFactory"/>'s
///   once-per-turn exert cell). Reset to false on each
///   <see cref="TurnStartedEvent"/>.</item>
///   <item><b>"only once each turn"</b> (CR 702.135c) — a boxed per-turn
///   activation counter incremented when this ability is the subject of an
///   <see cref="AbilityActivatedEvent"/> (recorded at activation time, CR
///   602.2 — not at resolution, so two boasts can't slip past the cap). The
///   activation is allowed only while the counter is below the cap. Also reset
///   on <see cref="TurnStartedEvent"/>.</item>
/// </list>
///
/// <para>
/// <b>Per-turn cap override (CR 702.135c modifier).</b> The cap is a
/// <c>Func&lt;int&gt;</c> defaulting to 1. Birgi, God of Storytelling
/// ("Creatures you control can boast twice during each of your turns rather
/// than once") raises the cap to 2 for the creatures it controls — a Boast
/// factory wires <paramref name="perTurnCap"/> to a predicate that returns 2
/// while such a static is in force, 1 otherwise. The cap is re-read on every
/// activation check so a static entering/leaving mid-turn is honoured.
/// </para>
///
/// <para>
/// Wiring posture mirrors <see cref="AdaptFactory"/>: <see cref="Build"/>
/// returns the activated ability plus stamps a <see cref="KeywordAbility"/>
/// "Boast" marker so card inspectors / tooltips see the reminder text. The
/// caller mounts it on the source with
/// <see cref="Permanent.AddAbility(IAbility)"/> (Build does call AddAbility for
/// the marker but returns the activated ability so the caller can add it).
/// </para>
/// </summary>
public static class BoastAbility
{
    /// <summary>
    /// Build a Boast activated ability for <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The creature carrying Boast. Must be non-null with
    /// an owner or controller.</param>
    /// <param name="cost">Mana-cost portion of the Boast ability (e.g.
    /// <c>"{1}{W}"</c> for Usher of the Fallen). Non-null / non-empty.</param>
    /// <param name="effects">The effect(s) the ability resolves.</param>
    /// <param name="eventBus">When supplied, the "attacked this turn" cell is
    /// flipped on <see cref="AttackersDeclaredEvent"/>, the per-turn activation
    /// counter is incremented on <see cref="AbilityActivatedEvent"/>, and both
    /// reset on <see cref="TurnStartedEvent"/>. When null the gate stays closed
    /// (a creature that has provably never attacked can't boast) — suitable for
    /// shape tests.</param>
    /// <param name="perTurnCap">CR 702.135c cap. Re-read on each activation
    /// check. Null defaults to a constant 1 ("only once each turn"). Birgi's
    /// static supplies <c>() =&gt; 2</c>.</param>
    /// <returns>The configured activated ability (caller mounts it via
    /// <see cref="Permanent.AddAbility(IAbility)"/>).</returns>
    public static ActivatedAbility Build(
        Creature source,
        string cost,
        IReadOnlyList<IEffect> effects,
        IEventBus? eventBus = null,
        Func<int>? perTurnCap = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(cost))
            throw new ArgumentException("Boast cost must be non-empty.", nameof(cost));
        ArgumentNullException.ThrowIfNull(effects);

        var controller = source.Controller
            ?? source.Owner
            ?? throw new InvalidOperationException(
                "Boast source must have a controller or owner.");

        // CR 702.135 — keyword marker so inspectors / tooltips see "Boast".
        source.AddAbility(new KeywordAbility("Boast", source, controller));

        // CR 702.135b — "attacked this turn"; CR 702.135c — per-turn activation
        // count. Boxed cells shared by the event subscriptions + the activation
        // gate. Default cap of 1 unless a static raises it.
        var attackedThisTurn = new bool[] { false };
        var activatedThisTurn = new int[] { 0 };
        var cap = perTurnCap ?? (() => 1);

        // The ability whose canActivateCheck consults the two cells. Built first
        // so the AbilityActivatedEvent subscription can compare by reference.
        ActivatedAbility ability = null!;

        ability = new ActivatedAbility(
            source: source,
            controller: controller,
            costs: new ICost[] { new ManaCostCost(cost) },
            effects: effects,
            // CR 602.5c — "Activate only if this creature attacked this turn
            // and only once each turn." Re-evaluated on every check.
            canActivateCheck: () =>
                source.Zone == ZoneType.Battlefield
                && attackedThisTurn[0]
                && activatedThisTurn[0] < Math.Max(1, cap()));

        if (eventBus != null)
        {
            // CR 702.135b — this creature attacked this turn: flip the cell when
            // it is among the declared attackers under its own controller.
            eventBus.Subscribe<AttackersDeclaredEvent>(e =>
            {
                var ctrl = source.Controller ?? controller;
                if (!ReferenceEquals(e.Combat.AttackingPlayer, ctrl)) return;
                if (e.Combat.Attackers.Any(a => ReferenceEquals(a?.Creature, source)))
                {
                    attackedThisTurn[0] = true;
                }
            });

            // CR 702.135c — record this ability's activation against the
            // per-turn cap (counted at activation, not resolution).
            eventBus.Subscribe<AbilityActivatedEvent>(e =>
            {
                if (ReferenceEquals(e.Ability, ability))
                {
                    activatedThisTurn[0]++;
                }
            });

            // CR 500.1 / 702.135b-c — reset both rails at the start of each turn.
            eventBus.Subscribe<TurnStartedEvent>(_ =>
            {
                attackedThisTurn[0] = false;
                activatedThisTurn[0] = 0;
            });
        }

        return ability;
    }

    /// <summary>
    /// Keyword marker stamped on a permanent whose static raises the Boast
    /// per-turn cap for the creatures its controller controls (Birgi, God of
    /// Storytelling — "Creatures you control can boast twice during each of
    /// your turns rather than once"). <see cref="ControllerCapResolver"/>
    /// scans for this marker.
    /// </summary>
    public const string BoastTwiceMarker = "Boast Twice";

    /// <summary>
    /// CR 702.135c cap resolver for a Boast on <paramref name="source"/>: returns
    /// a <c>Func&lt;int&gt;</c> that yields 2 while <paramref name="source"/>'s
    /// controller controls a permanent carrying the
    /// <see cref="BoastTwiceMarker"/> keyword (Birgi's "boast twice" static),
    /// otherwise 1. Re-evaluated on every activation check, so a Birgi entering
    /// or leaving mid-turn is honoured (CR 613 continuous effect).
    /// </summary>
    public static Func<int> ControllerCapResolver(Creature source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return () =>
        {
            var ctrl = source.Controller ?? source.Owner;
            if (ctrl == null) return 1;
            var controlsBirgi = ctrl.Zones.Battlefield.GetCards()
                .OfType<Permanent>()
                .Any(p => ReferenceEquals(p.Controller, ctrl)
                          && p.Abilities.OfType<KeywordAbility>()
                              .Any(k => k.Keyword == BoastTwiceMarker));
            return controlsBirgi ? 2 : 1;
        };
    }
}
