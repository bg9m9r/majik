using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Coverage for the GENERALIZED optional/reflexive "you may pay {cost}. If you
/// do, …" mana-payment rider on a TRIGGERED ability (CR 601.2b / 603.4) — the
/// declarative <see cref="TriggeredAbilityDefinition.OptionalManaCost"/> wrapper.
///
/// <para>
/// Where <see cref="JsonOptionalReflexiveGainControlTests"/> pins the
/// Obligator-SPECIFIC optional payment baked into the <c>gain_control</c> verb,
/// this pins the GENERAL wrapper that gates an ARBITRARY effect list behind the
/// optional payment: the whole "if you do" effect block runs only if the
/// controller's agent says yes AND the mana is actually paid. Declined or
/// unpayable → none of the gated effects run, no mana is spent. The trigger's
/// targets are still chosen as the ability goes on the stack (CR 603.3d),
/// independent of the later payment, so a wrapped targeted effect still declares
/// its <see cref="TargetRequest"/>.
/// </para>
///
/// <para>
/// {C} (CR 107.4c colorless pip) folds into a generic pip in v1's pool model,
/// so {1}{C} is charged as two generic mana — exercised below alongside a pure
/// {2}{C} cost.
/// </para>
/// </summary>
public class JsonOptionalManaRiderOnTriggerTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext(IPlayerAgent? agent = null) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private static ScriptedAgent YesNo(bool answer)
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(answer);
        return agent;
    }

    /// <summary>
    /// A cast_self trigger whose UNTARGETED draw-two effect sits behind an
    /// optional {1}{C} payment — proves the wrapper is generic (not tied to
    /// gain_control). Library is pre-stocked so the draw is observable.
    /// </summary>
    private TriggeredAbility BuildOptionalDrawTrigger(string optionalCost)
    {
        var def = new CardDefinition
        {
            Name = "Reflexive Draw Tester",
            Types = new() { "Creature" },
            ManaCost = "{2}",
            Power = 1,
            Toughness = 1,
            Abilities = new()
            {
                new TriggeredAbilityDefinition
                {
                    Trigger = new CastSelfTriggerDef(),
                    OptionalManaCost = optionalCost,
                    Effects = new()
                    {
                        new DrawCardEffectDef { Amount = 2 },
                    },
                },
            },
        };

        var card = CardDefinitionFactory.Build(def, _alice, replacements: null, continuous: null);
        return card.Abilities.OfType<TriggeredAbility>().Single();
    }

    private void StockLibrary(int n)
    {
        for (var i = 0; i < n; i++)
        {
            _alice.Zones.Library.AddCard(new Creature($"Filler {i}", "{G}", 1, 1));
        }
    }

    private async Task Resolve(TriggeredAbility ability, IPlayerAgent agent)
    {
        await ability.ResolveAsync(agent, game: NewContext(agent));
    }

    [Fact]
    public void OptionalManaRider_DeclaresTrigger_NoTargetForUntargetedBody()
    {
        var ability = BuildOptionalDrawTrigger("{1}{C}");
        ability.ActiveZones.Should().Contain(ZoneType.Stack);
        ability.TargetRequests.Should().BeEmpty("the gated draw effect is untargeted");
    }

    [Fact]
    public async Task OptionalManaRider_Declines_NoEffectRuns_NoManaSpent()
    {
        var ability = BuildOptionalDrawTrigger("{1}{C}");
        StockLibrary(5);
        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));
        var handBefore = _alice.Zones.Hand.Count;

        await Resolve(ability, YesNo(false));

        _alice.Zones.Hand.Count.Should().Be(handBefore,
            "declining the optional payment skips the entire 'if you do' effect list (CR 601.2b)");
        _alice.ManaPool.Total.Should().Be(2, "no mana is spent when the payment is declined");
    }

    [Fact]
    public async Task OptionalManaRider_AcceptsAndPays_RunsGatedEffect()
    {
        var ability = BuildOptionalDrawTrigger("{1}{C}");
        StockLibrary(5);
        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));
        var handBefore = _alice.Zones.Hand.Count;

        await Resolve(ability, YesNo(true));

        _alice.ManaPool.Total.Should().Be(0, "the {1}{C} reflexive cost is paid");
        _alice.Zones.Hand.Count.Should().Be(handBefore + 2,
            "paying runs the gated draw-two effect (CR 601.2b — 'if you do')");
    }

    [Fact]
    public async Task OptionalManaRider_AcceptsButUnpayable_NoEffectRuns()
    {
        var ability = BuildOptionalDrawTrigger("{1}{C}");
        StockLibrary(5);
        var handBefore = _alice.Zones.Hand.Count;

        // Empty pool — agent says yes but cannot pay {1}{C}.
        await Resolve(ability, YesNo(true));

        _alice.Zones.Hand.Count.Should().Be(handBefore,
            "an unpayable optional cost cannot be paid → the rider is skipped (CR 601.2b)");
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public async Task OptionalManaRider_PureColorlessCost_FoldsToGenericAndPays()
    {
        // {2}{C} == 3 generic in v1's pool model — proves the {C} colorless pip
        // is handled by the same ManaCost.Parse fold the gain_control path uses.
        var ability = BuildOptionalDrawTrigger("{2}{C}");
        StockLibrary(5);
        _alice.AddManaToPool(ManaCost.Parse("{2}{C}"));
        var handBefore = _alice.Zones.Hand.Count;

        await Resolve(ability, YesNo(true));

        _alice.ManaPool.Total.Should().Be(0, "{2}{C} folds to 3 generic and is paid in full");
        _alice.Zones.Hand.Count.Should().Be(handBefore + 2);
    }

    /// <summary>
    /// The wrapper also gates a TARGETED effect list — here gain_control behind
    /// the trigger-level optional payment (the Obligator shape, but with the
    /// rider at the ABILITY level rather than baked into the verb). The target is
    /// still declared + chosen as the trigger goes on the stack, regardless of
    /// the later payment.
    /// </summary>
    private TriggeredAbility BuildOptionalSteal(ContinuousEffectsService continuous)
    {
        var def = new CardDefinition
        {
            Name = "Reflexive Steal Tester",
            Types = new() { "Creature" },
            ManaCost = "{2}{R}",
            Power = 3,
            Toughness = 1,
            Abilities = new()
            {
                new TriggeredAbilityDefinition
                {
                    Trigger = new CastSelfTriggerDef(),
                    OptionalManaCost = "{1}{C}",
                    Effects = new()
                    {
                        new GainControlEffectDef
                        {
                            TargetFilter = "creature",
                            Duration = "end_of_turn",
                            Untap = true,
                            GainsHaste = true,
                        },
                    },
                },
            },
        };

        var card = CardDefinitionFactory.Build(def, _alice, replacements: null, continuous: continuous);
        return card.Abilities.OfType<TriggeredAbility>().Single();
    }

    [Fact]
    public async Task OptionalManaRider_GatesTargetedEffect_TargetChosenRegardlessOfPayment()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildOptionalSteal(continuous);

        ability.TargetRequests.Should().HaveCount(1,
            "the gated gain_control still declares its target (chosen as the trigger goes on the stack, CR 603.3d)");

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();
        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));

        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        var agent = YesNo(true);
        await ability.ResolveAsync(agent, game: NewContext(agent));

        bear.Controller.Should().BeSameAs(_alice, "paying runs the gated steal (CR 613.2)");
        bear.IsTapped.Should().BeFalse("untap that creature (CR 701.21)");
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public async Task OptionalManaRider_GatesTargetedEffect_DeclineSkipsSteal()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildOptionalSteal(continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));

        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        var agent = YesNo(false);
        await ability.ResolveAsync(agent, game: NewContext(agent));

        bear.Controller.Should().BeSameAs(_bob, "declining skips the gated steal");
        _alice.ManaPool.Total.Should().Be(2, "no mana spent on decline");
    }
}
