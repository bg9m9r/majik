using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 702.90 — Infect damage replacement.
///
/// Damage dealt to a player by a source with infect causes that player to
/// get that many poison counters (CR 702.90b). Damage dealt to a creature
/// by a source with infect is dealt in the form of -1/-1 counters
/// (CR 702.90c).
///
/// Modelled as a single, always-on replacement registered globally on the
/// <see cref="ReplacementBus"/>. Predicate:
///
///   - <see cref="DamageIntent.Source"/> is a <see cref="Creature"/> (or
///     other <see cref="Permanent"/>) carrying the "Infect" keyword marker;
///   - source permanent's <see cref="Card.Zone"/> is
///     <see cref="ZoneType.Battlefield"/> (CR 702.90 — Infect only applies
///     while the source is on the battlefield);
///   - <see cref="DamageIntent.Amount"/> is positive.
///
/// When the predicate fires:
///
///   - target is a <see cref="Player"/> → add <c>Amount</c>
///     <see cref="Player.PoisonCounters"/>; the 10-poison loss check is
///     picked up by <see cref="Majik.Core.Rules.Sba.Checks.PlayerLifeCheck"/>
///     on the next SBA pass (CR 704.5c).
///   - target is a <see cref="Creature"/> → add <c>Amount</c>
///     <see cref="CounterType.MinusOneMinusOne"/> counters; the layer
///     system applies the P/T mod and the CR 704.5q +1/+1 ↔ -1/-1
///     cancellation SBA balances them.
///   - target is a <see cref="Planeswalker"/> → out of scope; CR 702.90
///     does not redirect planeswalker damage, so the intent passes through
///     unchanged.
///
/// Returns <c>null</c> in all replacing cases to cancel the original
/// damage — Infect <i>replaces</i> the damage with counters, not stacks
/// on top of it (CR 614 self-replacement rule).
///
/// Registration: one global registration per game, performed by
/// <see cref="RegisterGlobal"/>. Per-card Infect grants (Inkmoth Nexus
/// animate, Glistener Elf printed marker, Phyresis-style anthem grants)
/// require no per-card wiring — adding the "Infect" keyword marker to the
/// source permanent is sufficient.
///
/// Stack interaction with damage modifiers: per-effect dedup in
/// <see cref="ReplacementBus.Apply{TIntent}"/> (CR 616.1c) means Infect
/// fires at most once per intent. If a doubling replacement (Furnace of
/// Rath, Inquisitor's Flail) runs first the doubled amount is what gets
/// converted to poison / -1/-1 counters; if Infect runs first the damage
/// is cancelled and the doubler has nothing to double. CR 616 player-
/// choice ordering is deferred to a later pass (registration order wins
/// today).
/// </summary>
public sealed class InfectDamageReplacement : IReplacementEffect<DamageIntent>
{
    /// <summary>The keyword marker scanned on the damage source. Matches
    /// the literal stamped by <see cref="KeywordAbility"/> and the
    /// continuous-effect layer (<c>chars.Keywords.Add("Infect")</c>).</summary>
    public const string InfectKeyword = "Infect";

    public bool OneShot => false;
    public object? Tag => this;

    /// <summary>
    /// Register a single global Infect damage replacement on the supplied
    /// bus. Idempotent guard left to the caller — call once per game at
    /// bus construction. Returns the registered effect so callers can
    /// unregister it during teardown if they share buses across games.
    /// </summary>
    public static InfectDamageReplacement RegisterGlobal(ReplacementBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        var eff = new InfectDamageReplacement();
        bus.Register<DamageIntent>(eff);
        return eff;
    }

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Amount <= 0) return false;
        // Planeswalker damage falls through — CR 702.90 doesn't cover it.
        if (intent.TargetPlayer == null && intent.TargetCreature == null) return false;
        return SourceHasInfect(intent.Source);
    }

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
    {
        var amount = intent.Amount;
        if (amount <= 0) return intent;

        if (intent.TargetPlayer is { } player)
        {
            // CR 702.90b — poison counters instead of life loss. The 10-
            // counter loss is picked up by the PlayerLife SBA on the next
            // pass (CR 704.5c).
            player.AddPoisonCounters(amount);
            return null;
        }

        if (intent.TargetCreature is { } creature)
        {
            // CR 702.90c — -1/-1 counters instead of marked damage. Layer 7c
            // applies the P/T mod; CR 704.5q +1/+1 ↔ -1/-1 cancellation
            // SBA pairs them off.
            creature.Counters.Add(CounterType.MinusOneMinusOne, amount);
            return null;
        }

        // Defensive — Applies() rejects this shape.
        return intent;
    }

    /// <summary>
    /// Source carries the Infect keyword while on the battlefield. Reads
    /// the layer-system-computed keyword set when an
    /// <see cref="Creature.ActiveEffects"/> service is wired (so grants
    /// like Inkmoth Nexus's animate / Phyresis-style anthems light up),
    /// and falls back to printed <see cref="KeywordAbility"/> markers
    /// otherwise. Sources off the battlefield never satisfy the gate.
    /// </summary>
    internal static bool SourceHasInfect(object? source)
    {
        if (source is not Permanent permanent) return false;
        if (permanent.Zone != ZoneType.Battlefield) return false;

        // Layer system source-of-truth (CR 613) for granted keywords.
        if (permanent is Creature creature && creature.ActiveEffects != null)
        {
            var chars = creature.ActiveEffects.Compute(creature);
            if (chars.Keywords.Contains(InfectKeyword)) return true;
        }

        // Fallback / additive — printed KeywordAbility markers.
        foreach (var ability in permanent.Abilities)
        {
            if (ability is KeywordAbility kw &&
                string.Equals(kw.Keyword, InfectKeyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
