using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abrade (Hour of Devastation, {1}{R}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • Abrade deals 3 damage to target creature.
///     • Destroy target artifact."
///
/// CR 700.2d — modal "Choose one —" spell. Two <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so the unchosen mode doesn't gate the cast). Pattern mirrors
/// <see cref="IzzetCharmFactory"/> for the modal choose-one shape.
///
/// Mode 0 — "3 damage to target creature": delegates to
/// <see cref="OracleSpellBinder.DealDamage"/> (same as the Izzet Charm damage
/// mode / FellFactory creature gatherer).
///
/// Mode 1 — "destroy target artifact": cribs from <see cref="ShatterFactory"/>
/// — re-checks the resolved target is still a Permanent on the Battlefield
/// with type Artifact (CR 608.2b illegal-target gate), then destroys via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
/// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
/// (CR 702.12) / regeneration (CR 701.15) shields are honoured.
/// </summary>
[CardName("Abrade")]
public static class AbradeFactory
{
    public const string CardName = "Abrade";
    public const string PrintedManaCost = "{1}{R}";

    public const int ModeDamage  = 0;
    public const int ModeDestroy = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Abrade deals 3 damage to target creature.",
        "Destroy target artifact.",
    };

    /// <summary>Construct Abrade as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Abrade. Both modes are wired.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand engine
    /// objects directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes a
        // target. MinTargets=0 so the unchosen mode doesn't gate the cast
        // (mirrors IzzetCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — 3 damage to target creature. Live gatherer: every
            // creature on every battlefield (CR 301). Bot ranks opponent
            // creatures highest via Removal intent.
            new TargetRequest(
                "target creature",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .OfType<Creature>()
                    .Cast<object>()
                    .ToList()),
            // Mode 1 — destroy target artifact. Live gatherer: every artifact
            // on every battlefield (CR 301).
            new TargetRequest(
                "target artifact",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Removal,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Artifact))
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
                        case ModeDamage:
                            effectsOut.Add(BuildDamageEffect(p, targetResolver));
                            break;
                        case ModeDestroy:
                            effectsOut.Add(BuildDestroyEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — deals 3 damage to target creature", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check: the target must
            // still be a creature on the battlefield. (Same posture as
            // ShatterFactory's type re-check.)
            if (resolved is not Creature creature) return;
            if (creature.Zone != ZoneType.Battlefield) return;

            OracleSpellBinder.DealDamage(creature, 3);
        });

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target artifact", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Artifact)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) handled via the Destroy-reason gate in MoveToGraveyard.
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });
}
