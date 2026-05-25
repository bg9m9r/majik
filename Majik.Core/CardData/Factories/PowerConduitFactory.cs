using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Power Conduit (Darksteel, {1}).
///
/// Artifact. v1 ships the simpler "move a counter source→target"
/// activated ability per the original factory brief:
///   "{T}: Remove a counter from a permanent you control and put a
///    counter of the same type on another permanent you control."
///
/// ## Oracle delta (v1)
///
/// The current Scryfall oracle (NCC reprint, 2022-04-29, oracle id
/// <c>2e358f7e-c282-4b92-8255-1436c99cda49</c>, mana cost <c>{2}</c>) is:
///   "{T}, Remove a counter from a permanent you control: Choose one —
///    • Put a charge counter on target artifact.
///    • Put a +1/+1 counter on target creature."
///
/// v1 ships the older printed mode (per the original factory brief):
/// cost <c>{1}</c>, no modal choice, target permanent receives a counter
/// of the SAME TYPE as the one removed. The current modal oracle is the
/// documented follow-up — see "Deferred (v1 gaps)" below.
///
/// ## Implemented (v1)
///
/// - <b>Artifact {1}</b> — printed mana cost, owner / controller wired.
/// - <b>Activated {T} (CR 605.1)</b> — a single
///   <see cref="ActivatedAbility"/> with a <see cref="AdditionalCost.Tap"/>
///   cost and TWO <see cref="TargetRequest"/> slots:
///     1. Source permanent (controller-side; predicate filter "any
///        permanent you control with at least one counter on it" lives
///        on the resolution-time guard, not in
///        <see cref="TargetRequest.LegalCandidates"/> which is empty in
///        v1 per the engine convention).
///     2. Target permanent (controller-side, distinct from the source).
///   On resolution:
///     a. Pick the first counter type present on the source (any kind —
///        +1/+1, -1/-1, charge, loyalty, …). v1 picks deterministically
///        (first key in dictionary iteration order); an agent-driven
///        pick is deferred.
///     b. Remove one counter of that type from the source via
///        <see cref="CounterCollection.Remove"/>.
///     c. Place one counter of that type on the target via
///        <see cref="CountersService.Add"/> so Hardened Scales /
///        Doubling Season replacements observe the placement AND a
///        post-commit <see cref="CounterAddedEvent"/> fires when an
///        <see cref="IEventBus"/> is supplied (CR 614 / CR 121.2 /
///        CR 603.6 — the placement triggers Animation-Module-shaped
///        counters-matter payoffs the same way every other CountersService
///        client does).
///
/// ## Resolution guards (CR 608.2b)
///
/// - Source must still be on the battlefield, controlled by the ability
///   controller, AND carry at least one counter.
/// - Target must still be on the battlefield, controlled by the ability
///   controller, AND NOT be the same permanent as the source
///   ("another permanent" — CR 109.3 / printed "another" identity check).
/// - Any check failing → silent no-op (CR 608.2b — illegal-on-resolution
///   targets fall away, no counters move).
///
/// ## Overloads
///
/// - <see cref="Create(Player)"/> — single-arg dispatcher path. Shape
///   only: counter placement on the target falls through to a direct
///   add (no replacement bus, no event publish). Suitable for shape /
///   <see cref="NamedCardFactory"/> dispatch tests.
/// - <see cref="Create(Player, ReplacementBus?, IEventBus?)"/> —
///   fully wired. Counter placement on the target routes through the
///   replacement bus + event bus.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Activated ability oracle</b>: the actual Scryfall oracle is
///   <c>{T}, Remove a counter from a permanent you control: Choose
///    one — • Put a charge counter on target artifact. • Put a +1/+1
///    counter on target creature.</c> with mana cost <c>{2}</c>.
///   Requires the modal-effect plumbing (CR 700.2) the rest of the
///   engine's modal cards already use (Cryptic Command shape) + the
///   "remove a counter" piece reused as an
///   <see cref="AdditionalCost"/>-shaped activation cost (parallels
///   Engineered Explosives' sacrifice cost). v1 ships the older
///   printed mode per the original factory brief.
/// - <b>Agent-driven counter type pick</b>: when the source permanent
///   carries multiple counter types (e.g. +1/+1 AND charge), v1 picks
///   the first key returned by <see cref="CounterCollection.All"/>'s
///   dictionary iteration. Choose-time prompt for which TYPE to move
///   is deferred — same posture as the rest of the "choose a counter
///   on …" family (Vampire Hexmage was the canonical example).
/// - <b>Choose-time target legality</b>:
///   <see cref="TargetRequest.LegalCandidates"/> is empty at activation
///   time (the engine-wide convention used by Heliod / Earthshaker
///   Khenra et al.) — the resolution-time recheck (CR 608.2b) is the
///   sole legality gate. The activator does NOT today consult the live
///   battlefield to filter candidates at activation.
/// </summary>
[CardName("Power Conduit")]
public static class PowerConduitFactory
{
    public const string CardName = "Power Conduit";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Power Conduit with no live replacement bus or event
    /// bus wiring. Counter placements on the target fall through to a
    /// direct add (Hardened Scales / Doubling Season won't bump, no
    /// CounterAddedEvent publish). Suitable for dispatcher / shape
    /// tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Power Conduit. When <paramref name="replacements"/> is
    /// supplied the placement on the target permanent routes through
    /// <see cref="CountersService.Add"/>; when <paramref name="eventBus"/>
    /// is supplied a post-commit <see cref="CounterAddedEvent"/> is
    /// published so counters-matter triggers (Animation Module) can fire.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Remove a counter from a permanent you control and put a
        // counter of the same type on another permanent you control.
        // (CR 605.1 / CR 121).
        //
        // Two TargetRequest slots: source + destination. Resolution
        // picks the first counter type present on the source (any kind
        // — +1/+1, -1/-1, charge, loyalty, …), removes one, and places
        // one of the same type on the destination via
        // CountersService.Add. Self-targeting is forbidden ("another
        // permanent" — CR 109.3 identity check).
        // ----------------------------------------------------------------
        ActivatedAbility? activated = null;

        var activatedEffect = new Effect(
            $"{CardName}: move a counter from one permanent you control to another",
            () =>
            {
                if (activated is null) return;
                if (activated.ChosenTargets.Count < 2) return;
                if (activated.ChosenTargets[0].Count == 0) return;
                if (activated.ChosenTargets[1].Count == 0) return;

                var rawSource = activated.ChosenTargets[0][0];
                var rawTarget = activated.ChosenTargets[1][0];
                if (rawSource is not Permanent source) return;
                if (rawTarget is not Permanent target) return;

                var controller = card.Controller ?? owner;

                // CR 608.2b — full resolution-time recheck.
                if (source.Zone != ZoneType.Battlefield) return;
                if (target.Zone != ZoneType.Battlefield) return;
                if (!ReferenceEquals(source.Controller, controller)) return;
                if (!ReferenceEquals(target.Controller, controller)) return;
                if (ReferenceEquals(source, target)) return; // "another"
                if (!source.Counters.HasAny) return;

                // v1: pick the first counter type present on the source.
                // Agent-driven pick is deferred (see factory xmldoc).
                var picked = source.Counters.All
                    .Where(kvp => kvp.Value > 0)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();
                if (picked == null) return;

                source.Counters.Remove(picked, 1);
                CountersService.Add(target, picked, 1, replacements, eventBus);
            });

        activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { activatedEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "permanent you control (source — remove a counter)",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
                new TargetRequest(
                    Description: "another permanent you control (destination)",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(activated);

        return card;
    }
}
