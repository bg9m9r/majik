using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heartless Act (Ikoria: Lair of Behemoths, {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Destroy target creature with no counters on it.
///     • Remove up to three counters from target creature."
///
/// CR 700.2d — modal "Choose one —" spell. Each of the two modes takes a
/// creature target. The bound <see cref="SpellDefinition"/> exposes two
/// <see cref="TargetRequest"/>s (one per mode); only the chosen mode's slot
/// is filled at cast time (MinTargets=0 so the unchosen mode doesn't gate the
/// cast — mirrors <see cref="BantCharmFactory"/> / <see cref="IzzetCharmFactory"/>).
///
/// The card's base shape (name, single Instant card type, {1}{B}) is
/// materialised from the embedded JSON (<c>heartless-act.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
/// <see cref="BantCharmFactory"/>. The resolve-time behaviour lives in
/// <see cref="BuildDefinition"/> because a modal <see cref="SpellDefinition"/>
/// (target resolver) isn't expressible in the JSON schema.
///
/// Mode 0 — "Destroy target creature with no counters on it": re-checks the
/// resolved target is still a battlefield <see cref="Creature"/> (CR 608.2b)
/// AND has zero counters on it (CR 122 / CR 702 — the "with no counters"
/// clause is part of the target restriction, re-evaluated at resolution via
/// <see cref="CounterCollection.HasAny"/>). If the creature has gained any
/// counter since targeting, the mode no-ops (illegal target). Otherwise it is
/// destroyed via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
/// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
/// (CR 702.12) / regeneration (CR 701.15) shields are honoured. Identical
/// destroy shape to <see cref="BantCharmFactory"/> mode 0.
///
/// Mode 1 — "Remove up to three counters from target creature": removes up to
/// <see cref="MaxCountersRemoved"/> counters from the target, draining counter
/// types in the order they appear in <see cref="CounterCollection.All"/>
/// (CR 122.5 — "up to three" means a target with fewer than three counters
/// simply loses all of them; you can't remove counters that aren't there).
/// Same deterministic v1 drain as <see cref="GlissaSunslayerFactory"/>'s
/// remove-counters mode — an agent-driven "choose which counters to remove"
/// prompt is deferred (same queue as Glissa).
/// </summary>
[CardName("Heartless Act")]
public static class HeartlessActFactory
{
    public const string CardName = "Heartless Act";
    public const string Slug = "heartless-act";

    public const int ModeDestroyNoCounters = 0;
    public const int ModeRemoveCounters    = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>CR 122.5 — "Remove up to three counters".</summary>
    private const int MaxCountersRemoved = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Destroy target creature with no counters on it.",
        "Remove up to three counters from target creature.",
    };

    /// <summary>Construct Heartless Act's base shape from the embedded JSON.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for
    /// Heartless Act.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand objects
    /// directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes a
        // target. MinTargets=0 so the unchosen mode doesn't gate the cast
        // (mirrors BantCharmFactory / IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — destroy target creature with no counters on it.
            // Candidate gather restricts to counter-free creatures (CR 608.2b
            // target restriction); re-checked at resolution too.
            new TargetRequest(
                Description: "target creature with no counters on it",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(pl => pl.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature))
                    .Where(c => c is Permanent perm && !perm.Counters.HasAny)
                    .Cast<object>()
                    .ToList()),

            // Mode 1 — remove up to three counters from target creature.
            new TargetRequest(
                Description: "target creature",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(pl => pl.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature))
                    .Cast<object>()
                    .ToList()),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Removal,
                BotIntent.Removal,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeDestroyNoCounters:
                            effectsOut.Add(BuildDestroyEffect(p, targetResolver));
                            break;
                        case ModeRemoveCounters:
                            effectsOut.Add(BuildRemoveCountersEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target creature with no counters on it", () =>
        {
            if (p.Targets.Count <= ModeDestroyNoCounters) return;
            var slot = p.Targets[ModeDestroyNoCounters];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // CR 608.2b / "with no counters on it" — the counter restriction is
            // part of the target's legality and is re-evaluated on resolution.
            // If the creature gained any counter after being targeted, it is no
            // longer a legal target and the spell does nothing to it.
            if (target.Counters.HasAny) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) / regeneration
            // (CR 701.15) handled via the Destroy-reason gate.
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    private static IEffect BuildRemoveCountersEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — remove up to three counters from target creature", () =>
        {
            if (p.Targets.Count <= ModeRemoveCounters) return;
            var slot = p.Targets[ModeRemoveCounters];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be a creature on the battlefield.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // CR 122.5 — "Remove up to three counters". Deterministic v1 drain
            // across counter types in CounterCollection.All order; "up to three"
            // tolerates a target with fewer than three counters (mirrors
            // GlissaSunslayerFactory). Agent-choice of which counters to remove
            // is deferred (same queue as Glissa).
            var remaining = MaxCountersRemoved;

            // Snapshot the counter types before mutating (mutating the bag while
            // enumerating its backing dictionary would throw).
            var present = target.Counters.All
                .Where(kvp => kvp.Value > 0)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var type in present)
            {
                if (remaining <= 0) break;
                var have = target.Counters.Count(type);
                var take = Math.Min(have, remaining);
                target.Counters.Remove(type, take);
                remaining -= take;
            }
        });
}
