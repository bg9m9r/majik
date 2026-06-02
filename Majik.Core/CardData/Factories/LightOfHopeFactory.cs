using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Light of Hope (Aether Revolt, {W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Choose one —
///     • You gain 4 life.
///     • Destroy target enchantment.
///     • Put a +1/+1 counter on target creature."
///
/// CR 700.2d — modal "Choose one —" spell. Three <see cref="TargetRequest"/>s
/// (one slot per mode) so the chosen-mode index lines up with its target slot,
/// with MinTargets=0 on every slot so unchosen modes don't gate the cast
/// (mirrors <see cref="WitherbloomCharmFactory"/> /
/// <see cref="IzzetCharmFactory"/>).
///
/// The base card shape (name / Instant type / {W} cost) is materialised from
/// the embedded JSON definition (<c>light-of-hope.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the modal spell logic is wired in
/// <see cref="BuildDefinition"/> because the JSON schema does not express
/// "Choose one —" modes (same posture as the charm cycle).
///
/// Mode 0 — "You gain 4 life": CR 119.3 — <see cref="Player.GainLife"/>. Same
/// shape as <see cref="WitherbloomCharmFactory"/>'s gain-life mode.
///
/// Mode 1 — "Destroy target enchantment": mirrors
/// <see cref="WitherbloomCharmFactory"/>'s destroy mode restricted to
/// enchantments. On resolution the target is destroyed (CR 701.7) iff it is
/// still an enchantment on the battlefield (CR 608.2b). Indestructible
/// (CR 702.12) and active regeneration shields (CR 701.15) are honoured via
/// the Destroy reason — Light of Hope does not print "can't be regenerated".
///
/// Mode 2 — "Put a +1/+1 counter on target creature": CR 122 — places a
/// <see cref="CounterType.PlusOnePlusOne"/> counter via
/// <see cref="CounterCollection.Add"/>, the same placement path as
/// <see cref="HeliodSunCrownedFactory"/>. The target must still be a creature
/// on the battlefield at resolution (CR 608.2b).
/// </summary>
[CardName("Light of Hope")]
public static class LightOfHopeFactory
{
    public const string CardName = "Light of Hope";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "light-of-hope";

    public const int ModeGainLife = 0;
    public const int ModeDestroyEnchantment = 1;
    public const int ModeCounter = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Mode 0 — life gained (CR 119.3).</summary>
    public const int LifeGain = 4;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "You gain 4 life.",
        "Destroy target enchantment.",
        "Put a +1/+1 counter on target creature.",
    };

    /// <summary>
    /// Construct Light of Hope as an Instant owned by <paramref name="owner"/>.
    /// The base shape (name / Instant / {W}) is materialised from the embedded
    /// JSON definition.
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
    /// Build the SpellDefinition for Light of Hope. All three modes are wired.
    /// </summary>
    /// <param name="caster">The spell's controller (gains life in mode 0).</param>
    /// <param name="targetResolver">Resolves the raw mode target token to a
    /// live object (from the caller's GameContext).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index lines
        // up with its target slot. MinTargets=0 so unchosen modes don't gate
        // the cast (mirrors WitherbloomCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — you gain 4 life (no target).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Heal),
            // Mode 1 — destroy target enchantment.
            new TargetRequest("target enchantment", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 2 — put a +1/+1 counter on target creature.
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.CombatTrick),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Heal,
                BotIntent.Removal,
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
                        case ModeGainLife:
                            effectsOut.Add(BuildGainLifeEffect(caster));
                            break;
                        case ModeDestroyEnchantment:
                            effectsOut.Add(BuildDestroyEnchantmentEffect(p, targetResolver));
                            break;
                        case ModeCounter:
                            effectsOut.Add(BuildCounterEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: you gain 4 life
    // -----------------------------------------------------------------------

    private static IEffect BuildGainLifeEffect(Player caster) =>
        new Effect($"{CardName} — you gain {LifeGain} life", () =>
        {
            // CR 119.3 — gaining life.
            caster.GainLife(LifeGain);
        });

    // -----------------------------------------------------------------------
    // Mode 1: destroy target enchantment
    // -----------------------------------------------------------------------

    private static IEffect BuildDestroyEnchantmentEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target enchantment", () =>
        {
            if (p.Targets.Count <= ModeDestroyEnchantment) return;
            var slot = p.Targets[ModeDestroyEnchantment];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not Permanent target) return;

            // CR 608.2b — resolution-time legality check: still an enchantment
            // on the battlefield.
            if (target.Zone != ZoneType.Battlefield) return;
            if (!target.HasType(CardType.Enchantment)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) are honoured via the Destroy reason; Light of Hope
            // does not print "can't be regenerated".
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });

    // -----------------------------------------------------------------------
    // Mode 2: put a +1/+1 counter on target creature
    // -----------------------------------------------------------------------

    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — put a +1/+1 counter on target creature", () =>
        {
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be a creature on the battlefield.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // CR 122 — place a +1/+1 counter (same path as Heliod, Sun-Crowned).
            target.Counters.Add(CounterType.PlusOnePlusOne, 1);
        });
}
