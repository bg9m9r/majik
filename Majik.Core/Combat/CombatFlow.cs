using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.Combat;

/// <summary>
/// Async combat orchestrator (CR 506-511) covering Phase 14 keyword set:
///
///   1. ask attacker's agent for attackers → tap each (Vigilance skip)
///   2. ask defender's agent for blockers + ordering
///   3. damage steps (CR 510.2/.3):
///       a. first-strike step — only first/double strike creatures deal damage
///       b. SBA cleanup
///       c. regular step — non-first-strike + double-strike again
///       d. SBA cleanup
///   4. publish <see cref="CombatDamageDealtEvent"/> per damage instance
///
/// Keywords honored: First strike, Double strike, Deathtouch, Lifelink,
/// Trample, Indestructible, Vigilance. Deathtouch effect:
/// any non-zero damage dealt to a creature is lethal regardless of toughness.
/// Trample: after lethal-each on blockers, overflow goes to defender.
/// Indestructible: SBA does not destroy (handled in StateBasedActions).
/// </summary>
public sealed class CombatFlow
{
    private readonly IEventBus _bus;
    private readonly StateBasedActions _sba;
    private readonly Majik.Core.Effects.ReplacementBus? _replacements;

    public CombatFlow(IEventBus bus, StateBasedActions sba,
        Majik.Core.Effects.ReplacementBus? replacements = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sba = sba ?? throw new ArgumentNullException(nameof(sba));
        _replacements = replacements;
    }

    public async Task RunCombatAsync(
        Player attacker,
        Player defender,
        IPlayerAgent attackerAgent,
        IPlayerAgent defenderAgent,
        IReadOnlyList<Creature> attackers,
        IReadOnlyList<Creature> blockers,
        GameContext ctx,
        CancellationToken ct = default)
    {
        var attackPlan = await attackerAgent.DeclareAttackersAsync(ctx, attackers, ct);

        foreach (var decl in attackPlan.Attackers)
        {
            if (!CombatAbilities.HasVigilance(decl.Attacker) && !decl.Attacker.IsTapped)
            {
                decl.Attacker.Tap();
            }
        }

        if (attackPlan.Attackers.Count == 0)
        {
            return;
        }

        var blockPlan = await defenderAgent.DeclareBlockersAsync(
            ctx, attackPlan.Attackers.Select(a => a.Attacker).ToList(), blockers, ct);

        var blockersByAttacker = blockPlan.Blockers
            .GroupBy(b => b.Attacker)
            .ToDictionary(g => g.Key, g => g.ToList());

        var hasFirstOrDoubleStrike = attackPlan.Attackers.Any(a =>
                CombatAbilities.HasFirstStrike(a.Attacker) || CombatAbilities.HasDoubleStrike(a.Attacker))
            || blockPlan.Blockers.Any(b =>
                CombatAbilities.HasFirstStrike(b.Blocker) || CombatAbilities.HasDoubleStrike(b.Blocker));

        if (hasFirstOrDoubleStrike)
        {
            AssignAndDealDamage(attackPlan, blockersByAttacker, defender, DamageStep.FirstStrike);
            CleanupAfterDamage(attacker, defender);
            AssignAndDealDamage(attackPlan, blockersByAttacker, defender, DamageStep.Regular);
            CleanupAfterDamage(attacker, defender);
        }
        else
        {
            AssignAndDealDamage(attackPlan, blockersByAttacker, defender, DamageStep.SingleStep);
            CleanupAfterDamage(attacker, defender);
        }
    }

    private enum DamageStep { SingleStep, FirstStrike, Regular }

    private void AssignAndDealDamage(
        CombatPlan attackPlan,
        Dictionary<Creature, List<Majik.Core.Players.Agents.BlockerDeclaration>> blockersByAttacker,
        Player defender,
        DamageStep step)
    {
        foreach (var decl in attackPlan.Attackers)
        {
            var attacker = decl.Attacker;
            if (!CreatureDealsDamageThisStep(attacker, step)) continue;
            if (attacker.Zone != ZoneType.Battlefield) continue;

            if (blockersByAttacker.TryGetValue(attacker, out var blocks) && blocks.Count > 0)
            {
                DealBlockedDamage(attacker, blocks, defender);
            }
            else if (decl.DefendingPlayerOrPlaneswalker is Player p)
            {
                DealDamageToPlayer(attacker, p, attacker.Power);
            }
            else if (decl.DefendingPlayerOrPlaneswalker is Planeswalker pw)
            {
                DealDamageToPlaneswalker(attacker, pw, attacker.Power);
            }
        }

        // Blockers deal damage back to their assigned attackers.
        foreach (var (attacker, blocks) in blockersByAttacker)
        {
            foreach (var b in blocks)
            {
                if (!CreatureDealsDamageThisStep(b.Blocker, step)) continue;
                if (b.Blocker.Zone != ZoneType.Battlefield) continue;
                if (attacker.Zone != ZoneType.Battlefield) continue;

                DealDamageToCreature(b.Blocker, attacker, b.Blocker.Power);
            }
        }
    }

    private static bool CreatureDealsDamageThisStep(Creature c, DamageStep step) => step switch
    {
        DamageStep.SingleStep => true,
        DamageStep.FirstStrike => CombatAbilities.HasFirstStrike(c) || CombatAbilities.HasDoubleStrike(c),
        DamageStep.Regular => !CombatAbilities.HasFirstStrike(c) || CombatAbilities.HasDoubleStrike(c),
        _ => true,
    };

    private void DealBlockedDamage(Creature attacker, List<Majik.Core.Players.Agents.BlockerDeclaration> blocks, Player defender)
    {
        var remaining = attacker.Power;
        var deathtouch = CombatAbilities.HasDeathtouch(attacker);
        var trample = CombatAbilities.HasTrample(attacker);

        foreach (var b in blocks)
        {
            if (remaining <= 0) break;
            if (b.Blocker.Zone != ZoneType.Battlefield) continue;

            var lethal = deathtouch
                ? 1
                : Math.Max(1, b.Blocker.Toughness - b.Blocker.Damage);

            if (remaining < lethal)
            {
                // CR 510.1c — must assign lethal before moving on. Couldn't,
                // so leftover (and any subsequent blockers) goes unassigned.
                if (trample)
                {
                    DealDamageToPlayer(attacker, defender, remaining);
                    remaining = 0;
                }
                break;
            }

            DealDamageToCreature(attacker, b.Blocker, lethal);
            remaining -= lethal;
        }

        if (trample && remaining > 0)
        {
            DealDamageToPlayer(attacker, defender, remaining);
        }
    }

    private void DealDamageToCreature(Creature source, Creature target, int amount)
    {
        // CR 702.16e — protection-from-X prevents damage from any source
        // matching the quality. Check colour-quality before mutating state.
        if (HasProtectionFromSource(target, source)) return;

        var intent = new Majik.Core.Effects.DamageIntent(source, amount, TargetCreature: target);
        intent = _replacements?.Apply(intent) ?? intent;
        if (intent == null || intent.Amount <= 0) return;

        target.TakeDamage(intent.Amount);
        if (CombatAbilities.HasDeathtouch(source))
        {
            target.MarkedForDestructionByDeathtouch = true;
        }
        if (CombatAbilities.HasLifelink(source) && source.Controller != null)
        {
            source.Controller.GainLife(intent.Amount);
        }
        _bus.Publish(new CombatDamageDealtEvent(source, target, intent.Amount));
    }

    private void DealDamageToPlaneswalker(Creature source, Planeswalker target, int amount)
    {
        var intent = new Majik.Core.Effects.DamageIntent(source, amount, TargetPlaneswalker: target);
        intent = _replacements?.Apply(intent) ?? intent;
        if (intent == null || intent.Amount <= 0) return;

        target.RemoveLoyalty(intent.Amount);
        if (CombatAbilities.HasLifelink(source) && source.Controller != null)
        {
            source.Controller.GainLife(intent.Amount);
        }
        _bus.Publish(new CombatDamageDealtEvent(source, target, intent.Amount));
    }

    private void DealDamageToPlayer(Creature source, Player target, int amount)
    {
        if (target.HasLost) return; // CR 104.2 — game over for this player

        var intent = new Majik.Core.Effects.DamageIntent(source, amount, TargetPlayer: target);
        intent = _replacements?.Apply(intent) ?? intent;
        if (intent == null || intent.Amount <= 0) return;

        target.LoseLife(intent.Amount);
        if (CombatAbilities.HasLifelink(source) && source.Controller != null)
        {
            source.Controller.GainLife(intent.Amount);
        }

        // CR 903.10a — track commander damage per-attacker on the defender.
        if (source.IsCommander && target.Commander != null)
        {
            target.Commander.TakeCommanderDamage(source, intent.Amount);
            if (target.Commander.HasLostToCommanderDamage())
            {
                target.HasLost = true;
            }
        }

        _bus.Publish(new CombatDamageDealtEvent(source, target, intent.Amount));
    }

    private static bool HasProtectionFromSource(ICard target, ICard source)
    {
        var sourceColors = Majik.Core.Cards.CardColors.GetColors(source);
        foreach (var c in sourceColors)
        {
            if (Majik.Core.Rules.Protection.HasProtectionFromColor(target, c))
                return true;
        }
        return false;
    }

    private void CleanupAfterDamage(Player attacker, Player defender)
    {
        _sba.CheckStateBasedActions(
            new[] { attacker, defender },
            attacker.Zones.Battlefield.GetCards()
                .Concat(defender.Zones.Battlefield.GetCards())
                .ToList());
    }
}
