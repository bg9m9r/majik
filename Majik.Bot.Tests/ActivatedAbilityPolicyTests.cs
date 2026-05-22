using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// Tests the activated-ability EV projection — both isolated
/// <see cref="ActivatedAbilityPolicy.ProjectActivateDelta"/> calls and the
/// argmax behaviour inside <see cref="PriorityPolicy"/>. Pump abilities
/// outside combat must score below Pass; removal vs threats must score
/// above Pass; sacrifice costs must not be auto-fired when the source is
/// our only creature.
/// </summary>
public class ActivatedAbilityPolicyTests
{
    // ----- ProjectActivateDelta — isolated unit tests -----

    [Fact]
    public void PumpAbility_OutsideCombat_ScoresZero()
    {
        // Setup: our turn, but all creatures are tapped (post-combat).
        // CombatRelevant → false → pump returns 0, so PriorityAction.Pass
        // (which ties the current eval) wins the strict-greater argmax.
        var s = new BotTestScenario();
        var crt = s.AddCreatureToBattlefield(s.Self, "Goblin", power: 1, toughness: 1);
        crt.ClearSummoningSickness();
        crt.Tap(); // tapped → no untapped creatures → CombatRelevant=false

        var ability = new ActivatedAbility(
            source: crt, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Target creature you control gets +1/+1 until end of turn",
                    () => { }),
            });

        var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
            ability, s.Context, s.Self, ArchetypeWeights.Prowess);

        delta.Should().Be(0);
    }

    [Fact]
    public void PumpAbility_WithUntappedCreature_ScoresFullPumpBonus()
    {
        // Untapped, sickness-cleared creature → combat relevant → full bonus.
        var s = new BotTestScenario();
        var crt = s.AddCreatureToBattlefield(s.Self, "Goblin", power: 1, toughness: 1);
        crt.ClearSummoningSickness();

        var source = new Artifact("Pump Source", manaCost: "{1}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);

        var ability = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Target creature you control gets +1/+1 until end of turn",
                    () => { }),
            });

        var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
            ability, s.Context, s.Self, ArchetypeWeights.Prowess);

        // Combat-relevant pump = BoardPower * 1.5 = 2.0 * 1.5 = 3.0
        delta.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void RemovalAbility_VsThreat_ScoresHighlyPositive()
    {
        var s = new BotTestScenario();
        s.AddCreatureToBattlefield(s.Opponent, "Big Threat", power: 5, toughness: 5);

        var source = new Artifact("Removal Source", manaCost: "{2}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);

        var ability = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Destroy target creature", () => { }),
            });

        var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
            ability, s.Context, s.Self, ArchetypeWeights.Prowess);

        // Big threat removal: OpponentThreats * -3 + Tempo * 2
        //                  = -2.0 * -3 + 1.5 * 2 = 6 + 3 = 9 (Prowess weights).
        delta.Should().BeGreaterThan(5.0);
    }

    [Fact]
    public void SacrificeSelfCreatureCost_DragsDeltaNegative()
    {
        var s = new BotTestScenario();
        var bigCrt = new Creature("Big Sac", manaCost: string.Empty, power: 5, toughness: 5);
        bigCrt.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(bigCrt);

        var ability = new ActivatedAbility(
            source: bigCrt, controller: s.Self,
            costs: new ICost[] { AdditionalCost.Sacrifice(bigCrt) },
            effects: new IEffect[]
            {
                new Effect("Draw a card", () => { }),
            });

        var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
            ability, s.Context, s.Self, ArchetypeWeights.Prowess);

        // Sac a 5/5: -BoardPower*5 - BoardToughness*5 = -10 - 2.5 = -12.5.
        // Draw effect: +HandSize*1 = +0.8. Net very negative.
        delta.Should().BeLessThan(-5.0);
    }

    [Fact]
    public void PayLifeCost_ScalesWithLifeDelta()
    {
        var s = new BotTestScenario();
        var source = new Artifact("Bauble", manaCost: "{0}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);

        var withLife = new ActivatedAbility(
            source: source, controller: s.Self,
            costs: new ICost[] { AdditionalCost.PayLife(3) },
            effects: new IEffect[] { new Effect("Draw a card", () => { }) });

        var withoutLife = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[] { new Effect("Draw a card", () => { }) });

        var dLife = ActivatedAbilityPolicy.ProjectActivateDelta(
            withLife, s.Context, s.Self, ArchetypeWeights.Burn);
        var dNo = ActivatedAbilityPolicy.ProjectActivateDelta(
            withoutLife, s.Context, s.Self, ArchetypeWeights.Burn);

        // Burn weights LifeDelta * 3.0 — paying 3 life costs 9 in eval.
        (dNo - dLife).Should().BeApproximately(3.0 * 3, 0.01);
    }

    [Fact]
    public void BurnEffect_ScoresPositiveOnBurnArchetype()
    {
        var s = new BotTestScenario();
        var source = new Artifact("Bolt Source", manaCost: "{1}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);

        var ability = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Deals 2 damage to any target", () => { }),
            });

        var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
            ability, s.Context, s.Self, ArchetypeWeights.Burn);

        // Burn LifeDelta=3.0; 2 dmg -> LifeDelta * 2 * 0.5 = 3.0; + Tempo*0.25=0.25.
        delta.Should().BeGreaterThan(2.0);
    }

    [Fact]
    public void DrawEffect_AddsHandSize()
    {
        var s = new BotTestScenario();
        var source = new Artifact("Drawer", manaCost: "{1}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);

        var ability = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Draw a card", () => { }),
            });

        var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
            ability, s.Context, s.Self, ArchetypeWeights.BorosEnergy);

        // BorosEnergy HandSize=2.0; draw 1 → +2.0.
        delta.Should().BeApproximately(2.0, 0.5);
    }

    // ----- PriorityPolicy integration — argmax behaviour -----

    [Fact]
    public void PriorityPolicy_DoesNotActivate_PumpWithNoCreatures()
    {
        // Bot has battlefield permanent that grants pump but ZERO creatures
        // to put it on → CombatRelevant returns false and ourCreatureCount=0,
        // so delta is BoardPower * 0 = tiny tempo. Outer argmax stays at Pass.
        var s = new BotTestScenario();
        var source = new Artifact("Pump Granter", manaCost: "{0}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);
        source.AddAbility(new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Target creature gets +1/+1 until end of turn", () => { }),
            }));

        var pol = new PriorityPolicy(ArchetypeWeights.Prowess);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.PassAction>(
            "no creatures to pump → activating is pure waste");
    }

    [Fact]
    public void PriorityPolicy_Activates_RemovalVsBigThreat()
    {
        var s = new BotTestScenario();
        s.AddCreatureToBattlefield(s.Opponent, "Threat", power: 6, toughness: 6);

        var source = new Artifact("Removal Engine", manaCost: "{2}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);
        var removalAbility = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Destroy target creature", () => { }),
            });
        source.AddAbility(removalAbility);

        var pol = new PriorityPolicy(ArchetypeWeights.Prowess);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.ActivateAbility>();
        ((PriorityAction.ActivateAbility)action).Ability.Should().BeSameAs(removalAbility);
    }

    [Fact]
    public void PriorityPolicy_DoesNotActivate_SacrificeSelfBigCreature()
    {
        // Sacrifice a 5/5 for a marginal effect (draw 1) — cost > benefit.
        var s = new BotTestScenario();
        var bigCrt = new Creature("Champion", manaCost: string.Empty, power: 5, toughness: 5);
        bigCrt.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(bigCrt);
        bigCrt.AddAbility(new ActivatedAbility(
            source: bigCrt, controller: s.Self,
            costs: new ICost[] { AdditionalCost.Sacrifice(bigCrt) },
            effects: new IEffect[]
            {
                new Effect("Draw a card", () => { }),
            }));

        var pol = new PriorityPolicy(ArchetypeWeights.Prowess);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.PassAction>(
            "sacrificing a 5/5 for one card should be passed on");
    }

    [Fact]
    public void PriorityPolicy_ActivatedAbilityMemo_DoesNotInfiniteLoop()
    {
        // After the first call surfaces an activation, the second call must
        // not re-propose the same ability (memo gate). Otherwise the priority
        // pump loops forever on a single permanent.
        var s = new BotTestScenario();
        s.AddCreatureToBattlefield(s.Opponent, "Threat", power: 5, toughness: 5);

        var source = new Artifact("Removal Engine", manaCost: "{2}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);
        source.AddAbility(new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Destroy target creature", () => { }),
            }));

        var pol = new PriorityPolicy(ArchetypeWeights.Prowess);
        var first = pol.Pick(s.Context, s.Self);
        first.Should().BeOfType<PriorityAction.ActivateAbility>();

        // Second pump on same turn — ability marked fired, no re-proposal.
        var second = pol.Pick(s.Context, s.Self);
        second.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void PriorityPolicy_OpponentsTurn_ActivatesRemovalAtInstantSpeed()
    {
        // We have a removal-style activation; opp's combat = a window where
        // we want to fire. Pass-action otherwise on opp's turn.
        var s = new BotTestScenario();
        s.AddCreatureToBattlefield(s.Opponent, "Attacker", power: 4, toughness: 4);

        var source = new Artifact("Removal Engine", manaCost: "{0}");
        source.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(source);
        var ability = new ActivatedAbility(
            source: source, controller: s.Self,
            effects: new IEffect[]
            {
                new Effect("Destroy target creature", () => { }),
            });
        source.AddAbility(ability);

        var oppCtx = new GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Opponent,
            turnNumber: 1, currentPhase: PhaseStateType.DeclareAttackers, stack: s.Stack);

        var pol = new PriorityPolicy(ArchetypeWeights.Prowess);
        var action = pol.Pick(oppCtx, s.Self);

        action.Should().BeOfType<PriorityAction.ActivateAbility>();
    }

    [Fact]
    public void PriorityPolicy_ManaAbility_NeverEnumerated()
    {
        // Mana abilities aren't priority actions — the mana-payment path
        // fires them. Even though they sit on a permanent and are payable,
        // EnumerateActivatedAbilities must skip them.
        var s = new BotTestScenario();
        var land = s.AddLandToBattlefield(s.Self, "Mountain");
        land.AddAbility(new Majik.Core.Abilities.ManaAbility(
            source: land, controller: s.Self,
            manaGenerated: Majik.Core.ValueObjects.ManaCost.Parse("R")));

        var pol = new PriorityPolicy(ArchetypeWeights.Burn);
        var action = pol.Pick(s.Context, s.Self);

        action.Should().NotBeOfType<PriorityAction.ActivateAbility>();
    }
}
