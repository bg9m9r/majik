using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snakeskin Veil (Kaldheim, {G}).
///
/// Instant. Oracle text (verified against the embedded Modern seed):
///   "Put a +1/+1 counter on target creature you control. It gains hexproof
///    until end of turn. (It can't be the target of spells or abilities your
///    opponents control.)"
///
/// ## Implementation
///
/// A targeted "protect" instant in the Veil-of-Summer / Felonious-Rage
/// family: a single 1..1 "target creature you control" request, then a
/// resolve-time body that (1) places one +1/+1 counter and (2) grants
/// Hexproof until end of turn. Card shape comes from the embedded JSON
/// (<c>snakeskin-veil.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>; the resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because it needs a controller-scoped
/// target gatherer + a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// On resolution:
///   1. CR 608.2b / CR 109.5 — re-check the target is still a Creature the
///      caster controls on the battlefield; otherwise no-op.
///   2. CR 122 — place one +1/+1 counter via
///      <see cref="CountersService.Add"/> so replacement effects (Hardened
///      Scales, Doubling Season) can rewrite the amount and "whenever one or
///      more counters are put on …" triggers (Conclave Mentor) can fire. The
///      optional <c>replacements</c> / <c>eventBus</c> are threaded through;
///      both null = a plain direct add (shape-only tests).
///   3. CR 702.11 — grant Hexproof until end of turn, registered as a
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
///      <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at cleanup).
///      Skipped when no continuous-effects service is wired (shape tests).
/// </summary>
[CardName("Snakeskin Veil")]
public static class SnakeskinVeilFactory
{
    public const string CardName = "Snakeskin Veil";
    public const string Slug = "snakeskin-veil";

    /// <summary>Granted keyword — CR 702.11 Hexproof.</summary>
    public const string GrantedHexproof = "Hexproof";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time SpellDefinition. Single 1..1 "target creature
    /// you control" request. On resolution: one +1/+1 counter + Hexproof
    /// until end of turn.
    /// </summary>
    /// <param name="caster">Spell controller — the "you control" filter on the
    /// target gatherer + the resolution-time legality re-check.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="replacements">Optional replacement bus so counter-placement
    /// replacements (Hardened Scales, Doubling Season) can rewrite the amount.
    /// Null = direct add.</param>
    /// <param name="eventBus">Optional event bus so a post-commit
    /// <c>CounterAddedEvent</c> publishes for "whenever one or more counters
    /// are put on …" triggers. Null = no event.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ReplacementBus? replacements = null,
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
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff,
                    // CR 109.5 / CR 608.2b — "you control" reads
                    // Permanent.Controller at choose-time (controller-scoped).
                    CandidateGatherer: ctx => caster.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Controller, caster))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    return Array.Empty<IEffect>();
                }

                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: +1/+1 counter + hexproof until end of turn",
                        () => Resolve(raw, caster, replacements, eventBus)),
                };
            });
    }

    private static void Resolve(
        object raw,
        Player caster,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        // CR 608.2b — illegal target / non-Creature resolver → no-op.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        // CR 109.5 — "you control" re-checked at resolution.
        if (!ReferenceEquals(target.Controller, caster)) return;

        // CR 122 — place one +1/+1 counter. Route through CountersService so
        // counter-replacement effects (Hardened Scales, Doubling Season) and
        // "whenever one or more counters are put on …" triggers observe it.
        CountersService.Add(target, CounterType.PlusOnePlusOne, 1, replacements, eventBus);

        // CR 702.11 — grant Hexproof until end of turn (CR 514.2 cleanup).
        // Skipped when no continuous-effects service is wired (shape tests).
        if (target.ActiveEffects != null)
        {
            target.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(target, GrantedHexproof));
        }
    }
}
