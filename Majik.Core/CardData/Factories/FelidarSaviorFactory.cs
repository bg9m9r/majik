using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Felidar Savior (Aether Revolt, {3}{W}).
/// Creature — Cat Beast 2/3.
///
/// ## Oracle text (Scryfall verified 2026-06)
///   "Lifelink (Damage dealt by this creature also causes you to gain
///    that much life.)
///    When this creature enters, put a +1/+1 counter on each of up to
///    two other target creatures you control."
///
/// ## Base shape
/// Name / Creature / Cat Beast / {3}{W} / 2/3 are materialised from the
/// embedded JSON definition (<c>felidar-savior.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="TourachDreadCantorFactory"/> / <see cref="LyraDawnbringerFactory"/>.
/// The JSON carries no abilities; the two riders below are layered on here.
///
/// ## Implemented (v1)
/// - <b>Lifelink (CR 702.15)</b> — a single <see cref="KeywordAbility"/>
///   marker, consumed by the standard combat-damage life-gain path (same
///   shape as <see cref="LyraDawnbringerFactory"/>'s Lifelink marker).
/// - <b>ETB up-to-two-target +1/+1 counters trigger (CR 603.1 / CR 603.6a
///   / CR 115.1)</b> — "When this creature enters, put a +1/+1 counter on
///   each of up to two other target creatures you control." Keyed on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. A single
///   <see cref="TargetRequest"/> models "up to two ... target creatures"
///   (CR 115.1b — "up to two" = 0..2 targets): <c>MinTargets = 0</c>,
///   <c>MaxTargets = 2</c>. The legal-candidate pool is gathered at
///   prompt time from the controller's battlefield (CR 109.5 — "you
///   control"), dropping Felidar Savior itself for the printed "other"
///   rider (CR 109.5). On resolution one
///   <see cref="CounterType.PlusOnePlusOne"/> counter is placed on each
///   chosen target via <see cref="CountersService.Add"/> so Hardened
///   Scales / Doubling Season replacements (CR 614) can rewrite the count,
///   with a resolution-time legality re-check (CR 608.2b — a target that
///   has left the battlefield or changed control is skipped).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape + Lifelink marker only; the
///   ETB trigger is attached for shape / dispatch tests but not registered
///   with a <see cref="TriggerManager"/>. This is the overload the
///   dispatcher uses.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IEventBus?)"/>
///   — fully wired: the ETB trigger registers so a battlefield entry
///   auto-queues it, and each counter placement routes through the
///   replacement bus + publishes <see cref="CounterAddedEvent"/>.
/// </summary>
[CardName("Felidar Savior")]
public static class FelidarSaviorFactory
{
    public const string CardName = "Felidar Savior";
    public const string Slug = "felidar-savior";
    public const int MaxCounterTargets = 2;

    /// <summary>
    /// Construct Felidar Savior with no live <see cref="TriggerManager"/>
    /// wiring. The Lifelink marker + the ETB trigger are attached to the card
    /// shape for structural / dispatch tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Felidar Savior with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB counter-placement
    /// trigger registers so a battlefield entry auto-queues it. When
    /// <paramref name="replacements"/> / <paramref name="eventBus"/> are
    /// supplied each +1/+1 counter placement routes through the replacement
    /// bus (CR 614) and publishes <see cref="CounterAddedEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Creature — Cat Beast,
        // {3}{W}, 2/3). The JSON carries no abilities.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Lifelink — CR 702.15. KeywordAbility marker consumed by the
        // standard combat-damage life-gain path (same shape as Lyra's
        // Lifelink marker).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));

        // ----------------------------------------------------------------
        // ETB up-to-two-target +1/+1 counters trigger — CR 603.1 /
        // CR 603.6a / CR 115.1.
        //   "When this creature enters, put a +1/+1 counter on each of up to
        //    two other target creatures you control."
        // One TargetRequest models "up to two ... target creatures"
        // (CR 115.1b — MinTargets = 0, MaxTargets = 2). Candidates are
        // gathered at prompt time from the controller's battlefield
        // (CR 109.5 — "you control"), dropping Felidar Savior itself for the
        // printed "other" rider. On resolution one +1/+1 counter is placed
        // on each chosen target via CountersService.Add (CR 614 — replacement
        // bus rewrites apply), with a resolution-time legality re-check
        // (CR 608.2b).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var targetRequest = new TargetRequest(
            Description: "up to two other target creatures you control",
            MinTargets: 0,
            MaxTargets: MaxCounterTargets,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Buff,
            // CR 109.5 — controller-scoped gather; the "other" rider drops
            // Felidar Savior itself.
            CandidateGatherer: ctx => owner.Zones.Battlefield.GetCards()
                .OfType<Creature>()
                .Where(c => !ReferenceEquals(c, card))
                .Where(c => ReferenceEquals(c.Controller, owner))
                .Cast<object>()
                .ToList());

        var etbEffect = new Effect(
            $"{CardName} — put a +1/+1 counter on each of up to two other target creatures you control",
            () => ResolveCounterTrigger(etbTrigger, card, replacements, eventBus));

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { targetRequest });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // --- ETB counter resolution (CR 608.2b / CR 614) ---------------------

    /// <summary>
    /// Resolve the ETB trigger: place one +1/+1 counter on each chosen target
    /// creature. CR 608.2b — each target is re-checked for legality at
    /// resolution (still a creature on the battlefield, still controlled by
    /// the source's controller, still "another"); illegal targets are skipped
    /// without affecting the rest. CR 614 — placement routes through the
    /// replacement bus so Hardened Scales / Doubling Season rewrite the count.
    /// </summary>
    private static void ResolveCounterTrigger(
        TriggeredAbility? etbTrigger,
        Creature card,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        if (etbTrigger == null) return;
        var chosen = etbTrigger.ChosenTargets;
        if (chosen.Count == 0) return;

        var controller = card.Controller ?? etbTrigger.Controller;

        foreach (var target in chosen[0])
        {
            if (target is not Creature creature) continue;
            // CR 608.2b — resolution-time legality re-check.
            if (creature.Zone != ZoneType.Battlefield) continue;
            if (ReferenceEquals(creature, card)) continue; // "other"
            if (!ReferenceEquals(creature.Controller, controller)) continue;

            CountersService.Add(creature, CounterType.PlusOnePlusOne, 1, replacements, eventBus);
        }
    }
}
