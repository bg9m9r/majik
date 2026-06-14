using Majik.Core.Cards;
using Majik.Core.Costs;
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
    private readonly AttackRestrictionRegistry? _attackRestrictions;

    public CombatFlow(IEventBus bus, StateBasedActions sba,
        Majik.Core.Effects.ReplacementBus? replacements = null,
        AttackRestrictionRegistry? attackRestrictions = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _sba = sba ?? throw new ArgumentNullException(nameof(sba));
        _replacements = replacements;
        _attackRestrictions = attackRestrictions;
    }

    /// <param name="grantStepPriority">
    /// CR 508.4 / 509.4 — invoked at the end of the declare-attackers step
    /// (after attackers are declared and each <see cref="CreatureAttacksEvent"/>
    /// has published) and again at the end of the declare-blockers step (after
    /// blockers are declared and each <see cref="CreatureBlocksEvent"/> has
    /// published). Players get priority in each of these steps, so the engine
    /// runs a full priority round here: pending "attacks"/"blocks" triggers
    /// (CR 508.1f / 509.1h) are put on the stack (CR 603.3), state-based actions
    /// are checked (CR 704), the stack resolves, and players may respond — all
    /// BEFORE combat proceeds to the next step. <see cref="TurnDriver"/> supplies
    /// its real priority round here. Null (direct unit-test callers with no
    /// priority/stack infrastructure) skips the windows, preserving the legacy
    /// "declare → declare → damage" shape those tests assert against.
    /// </param>
    public async Task RunCombatAsync(
        Player attacker,
        Player defender,
        IPlayerAgent attackerAgent,
        IPlayerAgent defenderAgent,
        IReadOnlyList<Permanent> attackers,
        IReadOnlyList<Permanent> blockers,
        GameContext ctx,
        CancellationToken ct = default,
        Func<Majik.Core.StateMachine.StepStateType, CancellationToken, Task>? grantStepPriority = null)
    {
        var attackPlan = await attackerAgent.DeclareAttackersAsync(ctx, attackers, ct);

        // CR 508.1g — "can't attack [defender] unless its controller pays
        // {cost}" (Ghostly Prison / Propaganda / Sphere of Safety). The cost
        // is part of declaring the attacker; a creature whose tax goes unpaid
        // was never legally declared, so it is removed from the attack before
        // it taps or fires its "attacks" trigger.
        attackPlan = await ChargeAttackTaxesAsync(attacker, attackerAgent, attackPlan, ctx, ct);

        foreach (var decl in attackPlan.Attackers)
        {
            if (!CombatAbilities.HasVigilance(decl.Attacker) && !decl.Attacker.IsTapped)
            {
                decl.Attacker.Tap();
            }
            // CR 508.1f — per-attacker "attacks" event so triggered abilities
            // ("Whenever ~ attacks, …") can fire on declaration.
            _bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(
                decl.Attacker, decl.DefendingPlayerOrPlaneswalker));
        }

        if (attackPlan.Attackers.Count == 0)
        {
            return;
        }

        // CR 508.4 — the declare-attackers step grants priority AFTER attackers
        // are declared and their "whenever ~ attacks" triggers (CR 508.1f) have
        // fired. Run a full priority round here so those pending triggers are put
        // on the stack (CR 603.3), state-based actions are checked (CR 704), the
        // stack resolves, and both players may respond — all BEFORE blockers are
        // declared. Without this, attack triggers (Goblin Guide, etc.) sat
        // pending and only drained after combat damage, far too late.
        if (grantStepPriority != null)
        {
            await grantStepPriority(Majik.Core.StateMachine.StepStateType.DeclareAttackers, ct);
        }

        // CR 508.1c — an attacker that left the battlefield (or stopped being a
        // creature / got removed from combat) during the declare-attackers
        // priority round no longer deals or receives combat damage. Re-narrow
        // the plan to attackers still on the battlefield so the resolved stack
        // can't make a dead attacker swing.
        if (grantStepPriority != null
            && attackPlan.Attackers.Any(a => a.Attacker.Zone != ZoneType.Battlefield))
        {
            var stillOnBattlefield = attackPlan.Attackers
                .Where(a => a.Attacker.Zone == ZoneType.Battlefield)
                .ToList();
            attackPlan = new CombatPlan(stillOnBattlefield);
            if (attackPlan.Attackers.Count == 0)
            {
                return;
            }
        }

        // CR 508 — record the finalized attacker set (after tax pruning and
        // left-battlefield narrowing) into the live combat-membership registry
        // BEFORE the declare-blockers half runs, so an ability activated in a
        // combat priority window reads a faithful "is attacking" membership
        // (Eiganjo's "target attacking or blocking creature" gate). Recorded
        // here rather than in the declare loop above so an un-declared (unpaid
        // tax) or removed attacker is never counted.
        var membership = CombatMembershipRegistryProvider.Current;
        foreach (var decl in attackPlan.Attackers)
        {
            membership.RecordAttacker(decl.Attacker);
        }

        await RunCombatFromBlocksAsync(
            attacker, defender, defenderAgent, attackPlan, blockers, ctx, ct, grantStepPriority);
    }

    /// <summary>
    /// CR 509+ — the post-declaration half of combat: blocker declaration,
    /// block triggers (CR 509.1h), the declare-blockers priority window
    /// (CR 509.4), and the damage steps (CR 510). Called by
    /// <see cref="RunCombatAsync"/> on the normal path and directly by the
    /// sim combat-state resume (root block search) with a rebound attack plan
    /// whose declaration ALREADY happened live (attackers tapped, attack
    /// triggers fired) — re-running the declaration half there would
    /// double-fire CR 508.1f triggers.
    /// </summary>
    public async Task RunCombatFromBlocksAsync(
        Player attacker,
        Player defender,
        IPlayerAgent defenderAgent,
        CombatPlan attackPlan,
        IReadOnlyList<Permanent> blockers,
        GameContext ctx,
        CancellationToken ct = default,
        Func<Majik.Core.StateMachine.StepStateType, CancellationToken, Task>? grantStepPriority = null)
    {
        // CR 508 — the sim-resume entry calls this method directly with an
        // attack plan whose declaration already happened live; (re)record the
        // attackers so the membership registry is populated on BOTH entry paths
        // (idempotent — the registry is a set).
        var membership = CombatMembershipRegistryProvider.Current;
        foreach (var decl in attackPlan.Attackers)
        {
            membership.RecordAttacker(decl.Attacker);
        }

        var blockPlan = await defenderAgent.DeclareBlockersAsync(
            ctx, attackPlan.Attackers.Select(a => a.Attacker).ToList(), blockers, ct);

        // CR 509 — record declared blockers into the live combat-membership
        // registry BEFORE the declare-blockers priority window runs, so an
        // ability activated in response reads a faithful "is blocking"
        // membership (Eiganjo's "target attacking or blocking creature" gate).
        foreach (var b in blockPlan.Blockers)
        {
            membership.RecordBlocker(b.Blocker);
        }

        // CR 509.1h — per-blocker "blocks" event so "Whenever ~ blocks a
        // creature, …" triggers (Brimaz, King of Oreskos) can fire on
        // declaration. One event per blocker→attacker pairing, naming the
        // blocked attacker so the trigger can act on that specific creature.
        foreach (var b in blockPlan.Blockers)
        {
            _bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureBlocksEvent(
                b.Blocker, b.Attacker));
        }

        // CR 509.4 — the declare-blockers step grants priority AFTER blockers
        // are declared and their "whenever ~ blocks / becomes blocked" triggers
        // (CR 509.1h) have fired. Same priority round as above: pending triggers
        // go on the stack, SBAs run, the stack resolves, players respond — all
        // BEFORE combat damage is dealt.
        if (grantStepPriority != null)
        {
            await grantStepPriority(Majik.Core.StateMachine.StepStateType.DeclareBlockers, ct);
        }

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

        // CR 511.3 — combat is over: drop the combat-state membership so a
        // creature that was attacking / blocking this combat is no longer
        // reported as such in the post-combat main phase (or a later combat).
        membership.Clear();
    }

    /// <summary>
    /// CR 508.1g — charge each declared attacker's "unless its controller
    /// pays {cost}" tax (Ghostly Prison / Propaganda / Sphere of Safety),
    /// returning a pruned <see cref="CombatPlan"/> with the unpaid attackers
    /// removed. An attacker whose declared target is protected by an active
    /// <see cref="PayPerAttackerRestriction"/> must have its per-attacker cost
    /// paid (the controller both CAN pay and CHOOSES to, mirroring
    /// <see cref="Majik.Core.Keywords.WardEffect.Resolve"/>); a creature with
    /// no active tax on its target is untouched.
    ///
    /// The cost is a real mana payment (<see cref="ManaCostCost"/>) charged
    /// against the attacking player's pool via <see cref="ICost.Pay"/>, so the
    /// {2}/{X} comes out of floated mana exactly as a manual declare-attackers
    /// payment would. A creature whose controller can't or won't pay is
    /// "un-declared" (CR 508.1g — the declaration was illegal): it is dropped
    /// from the plan so it never taps, never fires its "attacks" trigger, and
    /// deals no damage. Multiple paywalls on the same defender stack additively
    /// (two Ghostly Prisons → {4} per attacker).
    /// </summary>
    private async Task<CombatPlan> ChargeAttackTaxesAsync(
        Player attackingPlayer,
        IPlayerAgent attackerAgent,
        CombatPlan plan,
        GameContext ctx,
        CancellationToken ct)
    {
        // Prefer the explicitly-injected registry (tests); otherwise consult
        // the per-game ambient registry that Ghostly-Prison-class enchantments
        // register their paywalls onto (production via GameRegistryScope).
        var registry = _attackRestrictions ?? AttackRestrictionRegistryProvider.Current;
        if (plan.Attackers.Count == 0)
        {
            return plan;
        }

        // Reset every paywall's paid-marks before this combat's declaration so a
        // creature paid-for last combat must pay again this combat (the tax is
        // per declare-attackers — CR 508.1g — not a permanent unlock).
        foreach (var r in registry.Active.OfType<PayPerAttackerRestriction>())
        {
            r.ClearForTurn();
        }

        var kept = new List<Majik.Core.Players.Agents.AttackerDeclaration>(plan.Attackers.Count);
        var anyDropped = false;

        foreach (var decl in plan.Attackers)
        {
            // Only attackers whose declared defender is protected by a paywall
            // owe a tax; everything else attacks for free.
            if (registry.MayAttack(decl.Attacker, decl.DefendingPlayerOrPlaneswalker))
            {
                kept.Add(decl);
                continue;
            }

            // Sum the per-attacker cost across every paywall protecting this
            // defender (attack taxes are all generic mana, so total the values
            // and rebuild a single generic cost — CR 508.1g checked per
            // restriction).
            var owed = registry.Active
                .OfType<PayPerAttackerRestriction>()
                .Where(r => r.Protects(decl.DefendingPlayerOrPlaneswalker))
                .ToList();

            if (owed.Count == 0)
            {
                // A non-payment restriction blocks this attacker outright.
                anyDropped = true;
                continue;
            }

            var totalGeneric = owed.Sum(r => r.CostPerAttacker.TotalValue);
            var cost = new ManaCostCost(
                Majik.Core.ValueObjects.ManaCost.Zero.AddGenericCost(totalGeneric));

            // CR 508.1g — the controller pays only if they both CAN and CHOOSE
            // to. Ask the agent (declarative Yes/No) before charging; a
            // decline or an unaffordable cost un-declares the attacker.
            var paid = false;
            if (cost.CanPay(attackingPlayer))
            {
                var question = totalGeneric > 0
                    ? $"Pay {{{totalGeneric}}} for {decl.Attacker.Name} to attack?"
                    : $"Declare {decl.Attacker.Name} as an attacker?";
                var wantsToPay = await attackerAgent.ChooseYesNoAsync(
                    ctx, question, decl.Attacker.Name, ct).ConfigureAwait(false);
                if (wantsToPay && cost.CanPay(attackingPlayer))
                {
                    cost.Pay(attackingPlayer);
                    paid = true;
                }
            }

            if (paid)
            {
                foreach (var r in owed) r.MarkPaid(decl.Attacker);
                kept.Add(decl);
            }
            else
            {
                anyDropped = true;
            }
        }

        return anyDropped ? new CombatPlan(kept) : plan;
    }

    private enum DamageStep { SingleStep, FirstStrike, Regular }

    private void AssignAndDealDamage(
        CombatPlan attackPlan,
        Dictionary<Permanent, List<Majik.Core.Players.Agents.BlockerDeclaration>> blockersByAttacker,
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
                DealDamageToPlayer(attacker, p, attacker.GetEffectivePower());
            }
            else if (decl.DefendingPlayerOrPlaneswalker is Permanent pw && pw.IsEffectivePlaneswalker())
            {
                DealDamageToPlaneswalker(attacker, pw, attacker.GetEffectivePower());
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

                DealDamageToCreature(b.Blocker, attacker, b.Blocker.GetEffectivePower());
            }
        }
    }

    private static bool CreatureDealsDamageThisStep(Permanent c, DamageStep step) => step switch
    {
        DamageStep.SingleStep => true,
        DamageStep.FirstStrike => CombatAbilities.HasFirstStrike(c) || CombatAbilities.HasDoubleStrike(c),
        DamageStep.Regular => !CombatAbilities.HasFirstStrike(c) || CombatAbilities.HasDoubleStrike(c),
        _ => true,
    };

    private void DealBlockedDamage(Permanent attacker, List<Majik.Core.Players.Agents.BlockerDeclaration> blocks, Player defender)
    {
        var remaining = attacker.GetEffectivePower();
        var deathtouch = CombatAbilities.HasDeathtouch(attacker);
        var trample = CombatAbilities.HasTrample(attacker);

        for (var idx = 0; idx < blocks.Count; idx++)
        {
            var b = blocks[idx];
            if (remaining <= 0) break;
            if (b.Blocker.Zone != ZoneType.Battlefield) continue;

            var lethal = deathtouch
                ? 1
                : Math.Max(1, b.Blocker.GetEffectiveToughness() - b.Blocker.MarkedDamage);

            if (remaining < lethal)
            {
                // CR 510.1c — the attacking player must assign at least lethal
                // damage to a blocker before assigning damage to the NEXT one,
                // but is never forced to "waste" the remainder on a later
                // blocker. The auto-assigner keeps the "rest is lost" semantics
                // once it has already assigned lethal to a PRIOR blocker (the
                // attacker is allowed to stop). But a creature whose power is
                // less than its lone blocker's toughness still deals all its
                // damage to that blocker (a 3-power attacker vs a lone 4/4
                // deals 3 — marked, or as -1/-1 counters under wither). Detect
                // that case: nothing has been assigned yet (remaining ==
                // attacker.Power) and there is no later battlefield blocker.
                var nothingAssignedYet = remaining == attacker.GetEffectivePower();
                var hasLaterBlocker = false;
                for (var j = idx + 1; j < blocks.Count; j++)
                {
                    if (blocks[j].Blocker.Zone == ZoneType.Battlefield) { hasLaterBlocker = true; break; }
                }

                if (trample)
                {
                    DealDamageToPlayer(attacker, defender, remaining);
                    remaining = 0;
                }
                else if (nothingAssignedYet && !hasLaterBlocker)
                {
                    DealDamageToCreature(attacker, b.Blocker, remaining);
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

    private void DealDamageToCreature(Permanent source, Permanent target, int amount)
    {
        // CR 702.16e — protection-from-X prevents damage from any source
        // matching the quality. Check colour-quality before mutating state.
        if (HasProtectionFromSource(target, source)) return;

        // CR 614/615 — the replacement pipeline keys creature-damage replacements
        // off a real Creature target. An animated NON-creature combatant (a
        // manland) carries no Creature instance, so it is passed null here and
        // takes the raw amount (creature-targeted replacements — e.g. "prevent
        // all combat damage to creatures you control" — are a rare edge over an
        // animated land in v1; the deathtouch / lethal SBA below still apply
        // through the lifted Permanent surface).
        var intent = new Majik.Core.Effects.DamageIntent(
            source, amount, TargetCreature: target as Creature)
        { IsCombatDamage = true };
        intent = _replacements?.Apply(intent) ?? intent;
        if (intent == null || intent.Amount <= 0) return;

        // CR 702.90b — a source with wither (or infect) deals its damage to a
        // CREATURE in the form of that many -1/-1 counters instead of marked
        // damage. Mirrors the deathtouch branch below: wither changes the FORM
        // of the damage, not the timing — first-strike ordering, lifelink, and
        // the combat-damage event are unchanged. Lethal-via-counters is left to
        // the Layer 7c P/T mod + CR 704.5g state-based action.
        if (CombatAbilities.DealsCreatureDamageAsMinusCounters(source))
        {
            // CR 702.90b — wither changes the FORM (counters, not marked
            // damage), but the creature WAS still dealt damage this turn
            // (CR 120.3). MarkDamage's stamp is bypassed here, so record it.
            target.RecordDamageDealt(intent.Amount);
            target.Counters.Add(Majik.Core.Counters.CounterType.MinusOneMinusOne, intent.Amount);
        }
        else
        {
            // CR 119.3 — mark damage through the Permanent-level surface so an
            // animated land (no Creature.Damage field) accumulates marked damage;
            // a real Creature's MarkDamage override routes to its authoritative
            // Damage field (single source of truth).
            target.MarkDamage(intent.Amount);
        }
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

    private void DealDamageToPlaneswalker(Permanent source, Permanent target, int amount)
    {
        var intent = new Majik.Core.Effects.DamageIntent(
            source, amount, TargetPlaneswalker: target)
        { IsCombatDamage = true };
        intent = _replacements?.Apply(intent) ?? intent;
        if (intent == null || intent.Amount <= 0) return;

        // CR 120.3 — a planeswalker dealt damage (loyalty removal) "was dealt
        // damage this turn" too. RemoveTransientLoyalty is shared with
        // loyalty-ability costs (NOT damage), so the flag is stamped here at
        // the damage seam. The removal routes to a real Planeswalker's own
        // loyalty field OR the transient body of a flipped creature-front DFC
        // (CR 711) — both via the Permanent-level surface.
        target.RecordDamageDealt(intent.Amount);
        target.RemoveTransientLoyalty(intent.Amount);
        if (CombatAbilities.HasLifelink(source) && source.Controller != null)
        {
            source.Controller.GainLife(intent.Amount);
        }
        _bus.Publish(new CombatDamageDealtEvent(source, target, intent.Amount));
    }

    private void DealDamageToPlayer(Permanent source, Player target, int amount)
    {
        if (target.HasLost) return; // CR 104.2 — game over for this player

        var intent = new Majik.Core.Effects.DamageIntent(
            source, amount, TargetPlayer: target)
        { IsCombatDamage = true };
        intent = _replacements?.Apply(intent) ?? intent;
        if (intent == null || intent.Amount <= 0) return;

        // CR 120.3 — record damage (Bloodthirst etc.) before applying it.
        target.RecordDamageDealt(intent.Amount);

        // CR 702.90c — a source with infect deals its damage to a PLAYER as
        // that many poison counters instead of life loss. The 10-poison loss
        // is a state-based action (CR 704.5c) picked up on the next SBA pass.
        // Lifelink (below) still gains life equal to the damage dealt, because
        // damage was still dealt — only its life-loss FORM is replaced
        // (CR 702.15g / 119.3).
        if (CombatAbilities.DealsPlayerDamageAsPoison(source))
        {
            target.AddPoisonCounters(intent.Amount);
        }
        else
        {
            target.LoseLife(intent.Amount);
        }

        // CR 702.180b — Toxic N. A creature with toxic N that deals combat
        // damage to a player causes that player to ALSO get N poison counters,
        // in addition to (not instead of) the normal effect of the damage. This
        // is independent of infect: an infect+toxic source deals its damage as
        // poison (infect form) AND adds the toxic N poison on top. The toxic
        // value is the cumulative sum of every toxic marker (CR 702.180c). The
        // 10-poison loss is picked up by the next SBA pass (CR 704.5c).
        var toxic = CombatAbilities.GetToxic(source);
        if (toxic > 0)
        {
            target.AddPoisonCounters(toxic);
        }

        if (CombatAbilities.HasLifelink(source) && source.Controller != null)
        {
            source.Controller.GainLife(intent.Amount);
        }

        // CR 903.10a — track commander damage per-attacker on the defender.
        // The loss itself is NOT flipped here: it is a DEFERRED state-based
        // action (CR 704.5j) handled by CommanderDamageCheck, consistent with
        // how CR 704.5a life-loss is a deferred SBA rather than an eager flip
        // at the damage site. Eagerly setting HasLost here was inconsistent
        // with that deferred model (the accumulated total is converted to the
        // loss on the next SBA sweep).
        if (source is Creature { IsCommander: true } && target.Commander != null)
        {
            target.Commander.TakeCommanderDamage(source, intent.Amount);
        }

        _bus.Publish(new CombatDamageDealtEvent(source, target, intent.Amount));
    }

    private static bool HasProtectionFromSource(ICard target, ICard source)
    {
        // CR 105.3 / 702.16e — a battlefield source's colour can be changed
        // by a Layer-5 effect; use its effective colour. Non-permanent
        // sources (instants/sorceries) have no Layer-5 colour effect, so the
        // printed/static colour applies.
        var sourceColors = source is Permanent perm
            ? perm.GetEffectiveColors()
            : Majik.Core.Cards.CardColors.GetColors(source);
        foreach (var c in sourceColors)
        {
            if (Majik.Core.Rules.Protection.HasProtectionFromColor(target, c))
                return true;
        }

        // CR 702.16e / 205.3 — protection from a creature SUBTYPE prevents
        // combat damage from a matching source (Baneslayer Angel takes no
        // damage from a Demon or Dragon). Reads the source's effective
        // subtypes (Layer-4).
        if (Majik.Core.Rules.Protection.HasProtectionFromSubtype(target, source))
            return true;

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
