using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rip Apart (Strixhaven: School of Mages, {R}{W}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Rip Apart deals 3 damage to target creature or planeswalker.
///     • Destroy target artifact or enchantment."
///
/// CR 700.2d — modal "Choose one —" spell. Each mode takes its own target,
/// so the bound <see cref="SpellDefinition"/> exposes one
/// <see cref="TargetRequest"/> slot per mode (the chosen-mode index lines up
/// with its target slot), each with MinTargets=0 so the unchosen mode does
/// not gate the cast (mirrors <see cref="WitherbloomCharmFactory"/> /
/// <see cref="ArchmagesCharmFactory"/>).
///
/// Mode 0 — "Rip Apart deals 3 damage to target creature or planeswalker":
/// mirrors <see cref="FlameSlashFactory"/>'s 3-to-creature shape extended to
/// planeswalkers (same target shape as <see cref="BoneShardsFactory"/>'s
/// destroy clause). On resolution deals <see cref="Damage"/> (3) damage via
/// <see cref="Fx.DealDamageAny(object, int)"/> (CR 119 / CR 306.7 — damage to
/// a planeswalker removes that much loyalty). CR 608.2b — if the resolved
/// object is neither a creature nor a planeswalker (illegal target due to a
/// zone/type change after targeting), the effect is a no-op rather than
/// redirecting damage.
///
/// Mode 1 — "Destroy target artifact or enchantment": mirrors
/// <see cref="NaturalizeFactory"/>. On resolution the target is destroyed
/// (CR 701.7) iff it is still a Permanent on the battlefield with type
/// Artifact or Enchantment at resolution (CR 608.2b / CR 301–303).
/// Indestructible (CR 702.12) and active regeneration shields (CR 701.15) are
/// honoured via the Destroy reason — Rip Apart does not print "can't be
/// regenerated".
///
/// Card shape comes from the embedded JSON (<c>rip-apart.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/> needs
/// a target resolver supplied by the caller's <see cref="GameContext"/> (not
/// expressible in the data-only JSON schema).
/// </summary>
[CardName("Rip Apart")]
public static class RipApartFactory
{
    public const string CardName = "Rip Apart";
    public const string Slug = "rip-apart";
    public const string PrintedManaCost = "{R}{W}";

    public const int ModeDamage = 0;
    public const int ModeDestroy = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Mode 0 — damage dealt to the chosen creature or planeswalker.</summary>
    public const int Damage = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Rip Apart deals 3 damage to target creature or planeswalker.",
        "Destroy target artifact or enchantment.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Rip Apart. One target slot
    /// per mode (CR 601.2c) with MinTargets=0 so the unchosen mode does not
    /// gate the cast; on resolution the chosen mode either deals 3 damage to a
    /// creature/planeswalker (mode 0) or destroys an artifact/enchantment
    /// (mode 1).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index lines
        // up with its target slot. MinTargets=0 so the unchosen mode doesn't
        // gate the cast (mirrors WitherbloomCharmFactory / ArchmagesCharm).
        var targetRequests = new[]
        {
            // Mode 0 — 3 damage to target creature or planeswalker.
            new TargetRequest(
                "target creature or planeswalker",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Removal,
                // Agent-prompt: every creature + planeswalker on the
                // battlefield across all players (CR 302 / CR 306).
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature)
                             || c.HasType(CardType.Planeswalker))
                    .Cast<object>()
                    .ToList()),
            // Mode 1 — destroy target artifact or enchantment.
            new TargetRequest(
                "target artifact or enchantment",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Removal,
                // Agent-prompt: every artifact + enchantment on the
                // battlefield across all players (CR 301 / CR 303).
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Artifact)
                             || c.HasType(CardType.Enchantment))
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
                // Honour either the multi-pick list (first entry wins for a
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
        new Effect($"{CardName} — 3 damage to target creature or planeswalker", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — only creatures and planeswalkers are legal targets;
            // anything else (e.g. the target left the battlefield) is a no-op.
            if (resolved is not (Creature or Planeswalker)) return;

            // CR 119 / CR 306.7 — deal 3 damage; a planeswalker target loses
            // that much loyalty via Fx.DealDamageAny.
            Fx.DealDamageAny(resolved, Damage);
        });

    private static IEffect BuildDestroyEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — destroy target artifact or enchantment", () =>
        {
            if (p.Targets.Count <= ModeDestroy) return;
            var slot = p.Targets[ModeDestroy];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Permanent target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // Oracle constraint: target must be an artifact or enchantment at
            // resolution (CR 608.2b / CR 301–303).
            if (!target.HasType(CardType.Artifact)
                && !target.HasType(CardType.Enchantment)) return;

            // CR 701.7 — Destroy. Indestructible (CR 702.12) and regeneration
            // (CR 701.15) are honoured via the Destroy-reason gate in
            // MoveToGraveyard; Rip Apart does not print "can't be regenerated".
            OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
        });
}
