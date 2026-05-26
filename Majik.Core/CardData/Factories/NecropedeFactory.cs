using Majik.Core.Abilities;
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
/// Named-card factory for Necropede (Scars of Mirrodin, {2}).
///
/// Artifact Creature — Insect 1/1. Oracle text:
///   "Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)
///    When Necropede dies, you may put a -1/-1 counter on target
///    creature."
///
/// ## Implemented (v1)
///
/// - 1/1 <b>Artifact Creature</b> — Insect at {2}. The base
///   <see cref="Creature"/> constructor only registers
///   <see cref="CardType.Creature"/>; the Artifact type is additively
///   flagged via <c>AddCardType(CardType.Artifact)</c> (mirrors
///   <see cref="PlagueMyrFactory"/> / <see cref="MyrEnforcerFactory"/>'s
///   Artifact Creature shape).
/// - <b>Infect (CR 702.90)</b>: <see cref="KeywordAbility"/> marker
///   "Infect". The combat-damage replacement (-1/-1 counters to creatures
///   + poison counters to players) is deferred at the primitive level;
///   the marker surfaces the keyword so a downstream Infect primitive
///   picks Necropede up without re-touching the factory (same posture
///   as <see cref="PhyrexianCrusaderFactory"/> /
///   <see cref="BlightedAgentFactory"/> / <see cref="PlagueMyrFactory"/>).
/// - <b>Dies trigger (CR 603.6c)</b>: fires on the
///   Battlefield → Graveyard transition (via
///   <see cref="Triggers.OnDies"/>). Declares a 1..1
///   <see cref="TargetRequest"/> for "target creature" with a
///   <see cref="TargetRequest.CandidateGatherer"/> enumerating every
///   creature on every player's battlefield at resolution. On resolution
///   the "you may" is consulted via <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///   when an agent is supplied (auto-accepts otherwise — same legacy
///   posture as <see cref="ModularFactory"/>'s death-trigger). The
///   -1/-1 counter is routed through <see cref="CountersService.Add"/>
///   so any replacement-bus rewrites (Hardened Scales is +1/+1-only;
///   placeholder symmetry with Modular / Undying) apply.
/// - <c>activeZones</c> includes <see cref="ZoneType.Battlefield"/> +
///   <see cref="ZoneType.Graveyard"/> so the trigger still matches
///   after ZoneService stamps Zone = Graveyard before publishing
///   (mirrors <see cref="NihilSpellbombFactory"/> /
///   <see cref="PersistFactory"/>).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The dies trigger is
///   attached for inspection; not registered (no trigger manager).
///   Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, IPlayerAgent?)"/> —
///   fully wired. When <paramref name="triggers"/> is supplied the
///   trigger registers with the live trigger manager;
///   <paramref name="replacements"/> threads through
///   <see cref="CountersService.Add"/>; <paramref name="agent"/> drives
///   the "you may" prompt.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Infect damage-replacement</b>: poison counter tracking on
///   <see cref="Player"/> + the layered combat replacement land in a
///   follow-up infrastructure PR. Necropede's Infect marker becomes
///   live behaviour for free at that point.
/// - <b>Target legality at announcement</b>: the validator does not
///   filter the target to creatures-only at announcement; the resolution
///   guard handles CR 608.2b illegal-target cleanup.
/// </summary>
[CardName("Necropede")]
public static class NecropedeFactory
{
    public const string CardName = "Necropede";
    public const string PrintedManaCost = "{2}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Necropede with no live wiring. The dies trigger is
    /// attached for shape inspection; the "you may" auto-accepts and
    /// the counter is placed directly (no replacement-bus routing).
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, agent: null);

    /// <summary>
    /// Construct Necropede with optional runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager for the dies trigger.
    /// May be null — the trigger is still attached to the card shape.</param>
    /// <param name="replacements">ReplacementBus to route the -1/-1
    /// counter placement through (CR 614). May be null — the counter
    /// is placed directly via <see cref="Permanent.Counters"/>.</param>
    /// <param name="agent">Optional IPlayerAgent for the "you may"
    /// prompt (CR 117.x). When null, the may-rider auto-accepts (same
    /// posture as <see cref="ModularFactory"/> /
    /// <see cref="ReclamationSageFactory"/>).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Insect });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups see both types (mirrors
        // Plague Myr / Myr Enforcer).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.90 — Infect. Keyword marker; combat-damage replacement
        // is deferred (see class xmldoc).
        card.AddAbility(new KeywordAbility("Infect", card, owner));

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c.
        //   "When Necropede dies, you may put a -1/-1 counter on target
        //    creature."
        //
        // 1..1 TargetRequest for "target creature" with a live gatherer
        // that scans every player's battlefield at resolution (mirrors
        // ReclamationSageFactory's cross-player gatherer). v1 falls back
        // deterministically to the first opponent creature when no agent
        // is wired (BotIntent.Removal flips ownership priority).
        // ----------------------------------------------------------------
        TriggeredAbility? diesTrigger = null;

        var diesEffect = new Effect(
            $"{CardName}: may put a -1/-1 counter on target creature",
            () => ResolveDeathCounter(card, owner, diesTrigger, replacements, agent));

        diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            interveningIf: null,
            // activeZones: Battlefield + Graveyard so the trigger still
            // matches after ZoneService stamps Zone = Graveyard before
            // publishing (mirrors Nihil Spellbomb / Persist).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// Resolve the death trigger. Honours
    /// <see cref="TriggeredAbility.ChosenTargets"/> when set by an agent;
    /// otherwise picks the first creature controlled by an opponent on
    /// the controller's own battlefield (BotIntent.Removal posture).
    /// Validates the target is still a creature on the battlefield
    /// (CR 608.2b) before stamping the counter. Routes through
    /// <see cref="CountersService.Add"/> so replacement-bus rewrites
    /// apply.
    /// </summary>
    private static void ResolveDeathCounter(
        Creature necropede,
        Player owner,
        TriggeredAbility? trigger,
        ReplacementBus? replacements,
        IPlayerAgent? agent)
    {
        Creature? picked = null;

        // 1) Honour agent-set target (production path).
        if (trigger != null
            && trigger.ChosenTargets.Count > 0
            && trigger.ChosenTargets[0].Count > 0
            && trigger.ChosenTargets[0][0] is Creature chosen)
        {
            picked = chosen;
        }

        // 2) Deterministic fallback — first opponent creature on the
        //    controller's local view (no live GameContext here).
        picked ??= owner.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => !ReferenceEquals(c.Controller, owner));

        if (picked == null) return;

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!picked.HasType(CardType.Creature)) return;

        // "You may" — CR 117.x. Default posture is accept on
        // BotIntent.Removal; null-agent path auto-accepts (legacy).
        if (agent != null)
        {
            var yes = agent.ChooseYesNoAsync(
                "Put a -1/-1 counter on target creature?",
                BotIntent.Removal).GetAwaiter().GetResult();
            if (!yes) return;
        }

        // CR 122 / CR 614 — counter placement routed through
        // CountersService so any replacement-bus rewrites apply.
        CountersService.Add(picked, CounterType.MinusOneMinusOne, 1, replacements);
    }
}
