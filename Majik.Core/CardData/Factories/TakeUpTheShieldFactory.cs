using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Take Up the Shield (Theros, {1}{W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Put a +1/+1 counter on target creature. It gains lifelink and
///    indestructible until end of turn. (Damage and effects that say
///    "destroy" don't destroy it.)"
///
/// ## Implementation
/// The base card shape (name / Instant type / {1}{W} cost) is materialised
/// from the embedded JSON definition (<c>take-up-the-shield.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="LightOfHopeFactory"/> / <see cref="InspiringCallFactory"/>.
///
/// No new mechanic. The resolve-time <see cref="SpellDefinition"/> (via
/// <see cref="BuildSpellDefinition"/>) declares one 1..1 "target creature"
/// request (CR 601.2c). On resolution:
///   1. CR 122 — place a single <see cref="CounterType.PlusOnePlusOne"/>
///      counter on the target (same placement path as
///      <see cref="LightOfHopeFactory"/> / <see cref="HeliodSunCrownedFactory"/>).
///   2. CR 613.1f Layer 6 — grant the target "Lifelink" (CR 702.15) and
///      "Indestructible" (CR 702.12) until end of turn, each registered as a
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
///      <see cref="Creature.ActiveEffects"/>, expiring at cleanup (CR 514.2).
///      Same grant path as <see cref="BorosCharmFactory"/> (indestructible)
///      and <see cref="JeskaiCharmFactory"/> (lifelink).
///
/// CR 608.2b — at resolution the target must still be a creature on the
/// battlefield; otherwise the spell is a no-op (no counter, no grants). The
/// counter is placed even when no continuous-effects service is wired
/// (shape-only tests); the keyword grants are skipped in that case.
/// </summary>
[CardName("Take Up the Shield")]
public static class TakeUpTheShieldFactory
{
    public const string CardName = "Take Up the Shield";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "take-up-the-shield";

    /// <summary>Granted keyword — CR 702.15 Lifelink.</summary>
    public const string GrantedLifelink = "Lifelink";

    /// <summary>Granted keyword — CR 702.12 Indestructible.</summary>
    public const string GrantedIndestructible = "Indestructible";

    /// <summary>
    /// Construct Take Up the Shield as an Instant owned by
    /// <paramref name="owner"/>. The base shape (name / Instant / {1}{W}) is
    /// materialised from the embedded JSON definition.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature" request; on resolution the target gains a +1/+1
    /// counter plus lifelink and indestructible until end of turn.
    /// </summary>
    /// <param name="resolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>(), BotIntent.Protection),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: +1/+1 counter on target creature; it gains lifelink and indestructible until end of turn.",
                        () =>
                        {
                            // CR 608.2b — the target must still be a creature on
                            // the battlefield at resolution; otherwise no-op.
                            if (raw is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 122 — place a single +1/+1 counter (same path
                            // as Light of Hope / Heliod, Sun-Crowned).
                            target.Counters.Add(CounterType.PlusOnePlusOne, 1);

                            // CR 613.1f Layer 6 — grant Lifelink (CR 702.15) and
                            // Indestructible (CR 702.12) until end of turn
                            // (CR 514.2 cleanup expiry). Skipped when no live
                            // continuous-effects service is wired (shape-only).
                            var svc = target.ActiveEffects;
                            if (svc == null) return;

                            svc.Register(new GrantKeywordUntilEndOfTurnEffect(target, GrantedLifelink));
                            svc.Register(new GrantKeywordUntilEndOfTurnEffect(target, GrantedIndestructible));
                        }),
                };
            });
    }
}
