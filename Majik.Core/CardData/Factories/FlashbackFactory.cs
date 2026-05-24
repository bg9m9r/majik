using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the card literally named "Flashback" (Secrets of
/// Strixhaven, {R} Instant, Lorehold watermark). NOT to be confused with the
/// Flashback keyword (CR 702.34) or its parser
/// <see cref="Majik.Core.CardData.FlashbackOracleParser"/> / alt-cost
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> — those handle
/// cards which themselves carry the Flashback keyword. This card GRANTS the
/// keyword (CR 702.34) to a chosen graveyard target.
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Scryfall ID: <c>1b832fda-d7c4-4566-884c-2a8b6da15488</c></item>
///   <item>Oracle ID: <c>02070488-9203-4304-9392-a111d20218c5</c></item>
///   <item>Set: Secrets of Strixhaven (sos), #115, rare</item>
///   <item>Mana cost: <c>{R}</c></item>
///   <item>Type line: Instant</item>
///   <item>Colors: R; color identity: R</item>
/// </list>
///
/// ## Oracle text (Scryfall, verbatim)
/// <code>
/// Target instant or sorcery card in your graveyard gains flashback until
/// end of turn. The flashback cost is equal to its mana cost. (You may cast
/// that card from your graveyard for its flashback cost. Then exile it.)
/// </code>
///
/// ## Implementation (v1)
/// Mirrors <see cref="SnapcasterMageFactory"/>'s resolved ETB body precisely
/// — Snapcaster Mage's whole reason for existing is "be a 2/1 Flash creature
/// that, on ETB, does what this instant does". The mechanic and the rules
/// citations are identical; only the carrier (creature ETB vs. {R} instant)
/// differs.
///
/// - Instant shape, mana cost <c>{R}</c>, red.
/// - <see cref="BuildSpellDefinition"/> exposes a single 1..1
///   <see cref="TargetRequest"/> for an instant or sorcery card in the
///   caster's graveyard. Intent: <see cref="BotIntent.Reanimate"/> — the
///   closest fit on the existing flag set (the effect makes a buried spell
///   re-castable, which is the same value pattern as reanimation).
/// - On resolution, the chosen target is rechecked per CR 608.2b (must
///   still be in caster's graveyard AND still be an instant or sorcery
///   card); on success, the target gets a runtime flashback grant via
///   <see cref="Card.GrantRuntimeFlashback"/> stamped with the target's
///   own printed mana cost (CR 702.34's "flashback cost is equal to its
///   mana cost" language — same wiring as Snapcaster).
/// - When an <see cref="IEventBus"/> is supplied, a one-shot
///   <see cref="StepStartedEvent"/> subscription clears the grant on the
///   first <see cref="PhaseStateType.Cleanup"/> step (CR 514.2 — "until
///   end of turn"). No bus → grant persists until callers clear it
///   manually (shape-only / test path).
///
/// ## How the granted card gets cast
/// The graveyard-target keeps its Flashback grant in
/// <see cref="Card.RuntimeFlashbackCost"/>; callers build a
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> from that cost
/// and feed it to <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>.
/// The existing alt-cost path handles graveyard-zone gating, alt-cost-
/// replaces-printed semantics, and the exile-on-resolution side effect
/// (CR 702.34b). No new spell-cast plumbing is introduced here.
///
/// ## Bot-side discovery
/// <see cref="Majik.Core.Players.Agents.RuntimeFlashbackAltCostProbe"/>
/// already surfaces <see cref="Card.RuntimeFlashbackCost"/> as a
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/>; a bot wired
/// with that probe automatically bids the grant the same way it does
/// Snapcaster's.
///
/// ## Deferred (v1 gaps)
/// - Agent-driven target prompt is honoured via <see cref="ChosenSpellParams.Targets"/>
///   (same posture as Snapcaster's ETB trigger and every other "target X in
///   your graveyard" factory). No new prompt plumbing.
/// - "You may cast that card …" reminder text in parens is descriptive of
///   the Flashback keyword itself (CR 702.34) and needs no extra wiring —
///   it's already serviced by <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/>.
/// </summary>
[CardName("Flashback")]
public static class FlashbackFactory
{
    public const string CardName = "Flashback";
    public const string PrintedManaCost = "{R}";

    /// <summary>
    /// Construct the Flashback instant shape. Card-only (no resolve
    /// effect bound); see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time spell.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Flashback.
    /// Single 1..1 "target instant or sorcery card in your graveyard"
    /// request; on resolution stamps a runtime flashback grant
    /// (cost = target's printed mana cost) and — when
    /// <paramref name="eventBus"/> is non-null — schedules EOT cleanup.
    /// </summary>
    /// <param name="caster">Spell controller. Target must be in this
    /// player's graveyard at choose-time AND resolution-time.</param>
    /// <param name="resolver">Resolves raw chosen targets to live engine
    /// objects (same shape as the rest of the targeting subsystem).</param>
    /// <param name="eventBus">Optional event bus; when supplied the granted
    /// flashback is cleared on the first <see cref="PhaseStateType.Cleanup"/>
    /// step via a one-shot subscription (CR 514.2).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate),
            },
            EffectFactory: p => new IEffect[]
            {
                new Effect(
                    "Flashback — target instant or sorcery card in your graveyard gains flashback until end of turn (cost = its mana cost)",
                    () => ApplyGrant(caster, p, resolver, eventBus)),
            });
    }

    private static void ApplyGrant(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver,
        IEventBus? eventBus)
    {
        // CR 608.2b — illegal-on-resolution checks.
        if (p.Targets.Count == 0 || p.Targets[0].Count == 0) return;

        var raw = p.Targets[0][0];
        var resolved = resolver(raw);
        if (resolved is not Card target) return;

        // Target must still be (a) in the caster's graveyard, (b) owned by
        // caster (the printed wording is "your graveyard"), (c) an instant
        // or sorcery card. Any failure → clean no-op (CR 608.2b).
        if (target.Zone != ZoneType.Graveyard) return;
        if (!ReferenceEquals(target.Owner, caster)) return;
        if (!target.HasType(CardType.Instant) && !target.HasType(CardType.Sorcery)) return;

        // Stamp the runtime flashback grant. CR 702.34 — "The flashback
        // cost is equal to its mana cost" so we read ManaCostValue
        // directly off the target (matches Snapcaster Mage's wiring).
        target.GrantRuntimeFlashback(target.ManaCostValue);

        // CR 514.2 — schedule EOT cleanup. Subscribes to StepStartedEvent
        // and clears the grant on the first Cleanup step it sees.
        // No bus → no auto-cleanup (shape-only / test-driven path); same
        // posture as Snapcaster.
        if (eventBus == null) return;

        Action<StepStartedEvent>? handler = null;
        handler = e =>
        {
            if (e.StepType != PhaseStateType.Cleanup) return;
            target.ClearRuntimeFlashback();
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }
}
