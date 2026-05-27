using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boros Charm (Gatecrash, {R}{W}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • Boros Charm deals 4 damage to target player or planeswalker.
///     • Permanents you control gain indestructible until end of turn.
///     • Target creature gains double strike until end of turn."
///
/// CR 700.2d — modal "Choose one —" spell. Three <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast per CR 601.2c).
///
/// Mode 0 — "4 damage to target player or planeswalker":
///   Uses <see cref="Fx.DealDamageAny"/> (same pattern as
///   <see cref="SkullcrackFactory"/>). The target must be a
///   <see cref="Player"/> or <see cref="Planeswalker"/>; non-legal targets
///   no-op per CR 608.2b.
///
/// Mode 1 — "permanents you control gain indestructible until end of turn":
///   Enumerates the caster's battlefield. For each <see cref="Creature"/>
///   with a live <see cref="ContinuousEffectsService"/> wired, registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Indestructible"
///   (CR 613.1f Layer 6, expiring at cleanup CR 514.2). Non-creature
///   permanents (enchantments, lands, artifacts, planeswalkers) without a
///   live effects service are passed through — same limitation noted by
///   <see cref="SelflessSpiritFactory"/> which also uses
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>.
///
/// Mode 2 — "target creature gains double strike until end of turn":
///   Mirrors <see cref="TemurBattleRageFactory"/>: resolves the target,
///   guards for a live <see cref="ContinuousEffect"/> service, and registers
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Double strike"
///   (CR 702.4 / CR 514.2).
///
/// Pattern mirrors <see cref="IzzetCharmFactory"/> for the modal
/// choose-one shape.
/// </summary>
[CardName("Boros Charm")]
public static class BorosCharmFactory
{
    public const string CardName = "Boros Charm";
    public const string PrintedManaCost = "{R}{W}";

    public const int ModeDamage        = 0;
    public const int ModeIndestructible = 1;
    public const int ModeDoubleStrike  = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Damage dealt by mode 0.</summary>
    public const int DamageAmount = 4;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        $"Boros Charm deals {DamageAmount} damage to target player or planeswalker.",
        "Permanents you control gain indestructible until end of turn.",
        "Target creature gains double strike until end of turn.",
    };

    /// <summary>Construct Boros Charm as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Boros Charm.
    /// All three modes are wired.
    /// </summary>
    /// <param name="caster">The player casting the spell.</param>
    /// <param name="targetResolver">Resolver from the caller's GameContext.</param>
    /// <param name="allPlayers">All players in the game.</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service. Required for mode 1 (indestructible) and mode 2 (double
    /// strike) to register layer-6 grants. When null those modes perform no
    /// layer registration (shape-only path).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — target requests for every mode that takes a target.
        // MinTargets=0 so unchosen modes don't gate the cast
        // (mirrors IzzetCharmFactory / ArchmagesCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — 4 damage to target player or planeswalker.
            new TargetRequest("target player or planeswalker", 0, 1, Array.Empty<object>(), BotIntent.Burn),
            // Mode 1 — no target (permanents you control).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Protection),
            // Mode 2 — target creature gains double strike.
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.CombatTrick),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Burn,
                BotIntent.Protection,
                BotIntent.CombatTrick,
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
                        case ModeIndestructible:
                            effectsOut.Add(BuildIndestructibleEffect(caster, continuousEffects));
                            break;
                        case ModeDoubleStrike:
                            effectsOut.Add(BuildDoubleStrikeEffect(p, targetResolver, continuousEffects));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: 4 damage to target player or planeswalker
    // -----------------------------------------------------------------------

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"Boros Charm — deals {DamageAmount} damage to target player or planeswalker", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — only Player and Planeswalker are legal targets for
            // this mode; other types are a no-op.
            if (resolved is Player || resolved is Planeswalker)
            {
                Fx.DealDamageAny(resolved, DamageAmount);
            }
        });

    // -----------------------------------------------------------------------
    // Mode 1: permanents you control gain indestructible until end of turn
    // -----------------------------------------------------------------------

    private static IEffect BuildIndestructibleEffect(
        Player caster,
        ContinuousEffectsService? continuousEffects) =>
        new Effect("Boros Charm — permanents you control gain indestructible until end of turn", () =>
        {
            if (continuousEffects == null) return;

            // CR 613.1f / 702.12 — grant indestructible until end of turn to
            // every creature the caster controls on the battlefield.
            // GrantKeywordUntilEndOfTurnEffect targets Creature objects; for
            // non-creature permanents (lands, artifacts, enchantments) the
            // EOT keyword grant path is not yet wired — same limitation as
            // SelflessSpiritFactory (creatures-only scope in v1).
            foreach (var creature in caster.Zones.Battlefield
                .GetCards()
                .OfType<Creature>()
                .ToList())
            {
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, "Indestructible"));
            }
        });

    // -----------------------------------------------------------------------
    // Mode 2: target creature gains double strike until end of turn
    // -----------------------------------------------------------------------

    private static IEffect BuildDoubleStrikeEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        ContinuousEffectsService? continuousEffects) =>
        new Effect("Boros Charm — target creature gains double strike until end of turn", () =>
        {
            if (p.Targets.Count <= ModeDoubleStrike) return;
            var slot = p.Targets[ModeDoubleStrike];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — if the target is no longer a Creature or has no
            // live continuous-effects service wired, the spell is a no-op
            // (same guard as TemurBattleRageFactory).
            if (resolved is not Creature target) return;

            // Use the target's own ActiveEffects service if continuousEffects
            // was not supplied (mirrors TemurBattleRageFactory pattern).
            var svc = continuousEffects ?? target.ActiveEffects;
            if (svc == null) return;

            // CR 613.1c Layer 6 — keyword grant: Double strike.
            svc.Register(
                new GrantKeywordUntilEndOfTurnEffect(target, "Double strike"));
        });
}
