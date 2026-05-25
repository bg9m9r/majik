using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.44 — Sunburst. Shared primitive for the Fifth Dawn keyword:
///
///   "If this object is entering as a creature, ignoring any type-changing
///    effects that would affect it, it enters with a +1/+1 counter on it
///    for each color of mana spent to cast it. Otherwise, it enters with
///    a charge counter on it for each color of mana spent to cast it."
///
/// <para>
/// CR 702.44b — Sunburst only adds counters when the object is entering
/// the battlefield from the stack as a resolving spell, and only when one
/// or more colored mana was spent on its costs (including additional or
/// alternative costs). Non-cast battlefield entries (blink, reanimation,
/// Show and Tell, token copy) leave the permanent with zero Sunburst
/// counters, matching the printed behaviour.
/// </para>
///
/// <para>
/// Three pieces of wiring are produced by <see cref="Build"/>:
/// </para>
///
/// <para>
/// 1. A <see cref="KeywordAbility"/> marker (<c>"Sunburst"</c>) so card
///    inspectors / tooltips / future Layer-system scanners can see the
///    keyword. The marker is value-only; the counter wiring lives in the
///    ETB trigger below.
/// </para>
///
/// <para>
/// 2. CR 702.44a / CR 614.1d — "enters with N counters" ETB effect. v1
///    folds CR 122.1g "as it enters with N counters" into an ETB
///    triggered ability (same posture Chalice of the Void uses for its
///    X-counter ETB; the engine's <see cref="EntersWithCountersReplacement"/>
///    surface is +1/+1-only and Sunburst needs to branch
///    creature → +1/+1 vs non-creature → charge). On resolution the
///    effect reads <see cref="Card.PendingCastColors"/> (stamped by
///    <see cref="Majik.Core.Game.TurnDriver"/> right after mana payment
///    commits, computed by diffing the player's pool across the spend in
///    <see cref="Majik.Core.Costs.ManaPaymentResolver.Pay(Players.Player, ValueObjects.ManaCost, Players.Agents.ManaPayment, out IReadOnlyList{ValueObjects.ManaColor})"/>),
///    counts the distinct colors, and routes the placement through
///    <see cref="CountersService.Add"/> so Hardened Scales / Doubling
///    Season bumps apply. The stamp is consumed (cleared) so a later
///    non-cast battlefield entry (blink, copy) doesn't reuse the prior
///    cast's colors — such an entry leaves the permanent with zero
///    Sunburst counters, matching CR 702.44b.
/// </para>
///
/// <para>
/// 3. Creature-vs-noncreature branch (CR 702.44a). At ETB-effect resolve
///    time, if <see cref="Card.HasType(CardType)"/> reports Creature, the
///    counters are <see cref="CounterType.PlusOnePlusOne"/>; otherwise
///    they're <see cref="CounterType.Charge"/>. The "ignoring any
///    type-changing effects" rider (CR 702.44a) is approximated by the
///    printed type at construction time — the engine's Layer-7 type-
///    change machinery isn't yet plumbed through ETB-effect introspection,
///    and every printed Sunburst card from Fifth Dawn is either a
///    permanently-typed creature or a permanently-typed noncreature
///    artifact, so this v1 simplification has no observable effect on
///    the printed card pool.
/// </para>
/// </summary>
public static class SunburstFactory
{
    /// <summary>
    /// Wire Sunburst on <paramref name="source"/>: attach the keyword
    /// marker and an ETB triggered ability that reads
    /// <see cref="Card.PendingCastColors"/> at resolution time and places
    /// the matching number of counters (+1/+1 for creatures, charge for
    /// non-creatures per CR 702.44a). Returns the created
    /// <see cref="TriggeredAbility"/> so callers can introspect or
    /// further configure it (e.g. attach intervening-if checks). Counter
    /// placement is routed through <see cref="CountersService.Add"/>
    /// with the supplied <paramref name="replacements"/> bus so
    /// Hardened-Scales-style replacements apply; null bus → direct add.
    /// </summary>
    /// <param name="source">The Sunburst permanent. Per CR 702.44a, the
    /// counter-type branch reads the printed card type at resolve time
    /// — Artifact Creature with both Creature + Artifact types reads as
    /// Creature and lands +1/+1 counters; non-creature Artifact lands
    /// charge counters.</param>
    /// <param name="replacements">ReplacementBus to route counter
    /// placement through (CR 614.1d / Hardened Scales / Doubling
    /// Season). May be null — placement falls back to a direct add via
    /// <see cref="Permanent.Counters"/>.</param>
    public static TriggeredAbility Build(
        Permanent source,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(source);

        var owner = source.Owner
            ?? throw new InvalidOperationException("Sunburst source must have an owner.");
        var controller = source.Controller ?? owner;

        // ----------------------------------------------------------------
        // 1. Keyword marker — reminder-text shape ("Sunburst") so card
        //    inspectors / tooltips / future Layer scanners can see it.
        //    Value-only; counters are wired below.
        // ----------------------------------------------------------------
        source.AddAbility(new KeywordAbility("Sunburst", source, controller));

        // ----------------------------------------------------------------
        // 2. ETB trigger — CR 702.44a / CR 122.1g.
        //    Read PendingCastColors (stamped by TurnDriver right after the
        //    mana resolver commits payment), count distinct colors, route
        //    the matching counter type through CountersService.Add so
        //    Hardened Scales bumps apply. Clear the stamp so re-entries
        //    (blink, copy) don't reuse the prior cast's colors.
        //    CR 702.44b — zero colored mana spent (null OR empty list)
        //    yields zero counters.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Sunburst — enters with N counters (CR 702.44)",
            () =>
            {
                var colors = source.PendingCastColors;
                var n = colors?.Count ?? 0;
                if (n > 0)
                {
                    // CR 702.44a — branch by current card type. Artifact
                    // Creatures (Etched Champion / Etched Oracle) are
                    // Creature ⇒ +1/+1; non-creature artifacts (Sunburst
                    // Chimera variants if they ever ship as non-creature)
                    // ⇒ charge.
                    var counter = source.HasType(CardType.Creature)
                        ? CounterType.PlusOnePlusOne
                        : CounterType.Charge;
                    CountersService.Add(source, counter, n, replacements);
                }
                source.ClearPendingCastColors();
            });

        var etbTrigger = new TriggeredAbility(
            source: source,
            controller: controller,
            condition: Triggers.OnEnterBattlefieldSelf(source),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        source.AddAbility(etbTrigger);

        return etbTrigger;
    }
}
