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
/// Ability-path coverage for the OPTIONAL REFLEXIVE mana-payment rider on the
/// declarative <c>gain_control</c> verb — the Eldrazi Obligator shape: "When you
/// cast this spell, you may pay {1}{C}. If you do, gain control of target
/// creature until end of turn, untap that creature, and it gains haste until end
/// of turn." (CR 603.2.1 cast trigger + CR 601.2b/603.4 "you may pay … if you
/// do" reflexive payment, CR 613.2 control change).
///
/// <para>
/// PR #2203 built the engine <see cref="TemporaryControlChangeEffect"/> + the
/// <c>gain_control</c> verb (spell path); the Zealous Conscripts PR threaded the
/// ability-path continuous-effects service. The residual this pins is the
/// optional reflexive <c>{1}{C}</c> mana payment: a yes/no agent confirm, then a
/// <see cref="Player.PayMana"/> charge, then the gated control swap — declined or
/// unpayable → the whole "if you do" rider is skipped. The trigger is the NEW
/// <c>cast_self</c> (<see cref="CastSelfTriggerDef"/>) cast
/// trigger that fires on the card's own <see cref="SpellCastEvent"/> while it is
/// on the stack.
/// </para>
/// </summary>
public class JsonOptionalReflexiveGainControlTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext(IPlayerAgent? agent = null) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(_bus));

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    /// <summary>
    /// Build a card from a CardDef carrying one cast-self triggered gain_control
    /// effect with an optional reflexive {1}{C} payment, routed through the
    /// effects-aware overload so the verb reaches <paramref name="continuous"/>.
    /// </summary>
    private TriggeredAbility BuildObligator(ContinuousEffectsService continuous)
    {
        var def = new CardDefinition
        {
            Name = "Eldrazi Obligator",
            Types = new() { "Creature" },
            Subtypes = new() { "Eldrazi" },
            ManaCost = "{2}{R}",
            Power = 3,
            Toughness = 1,
            Abilities = new()
            {
                new TriggeredAbilityDefinition
                {
                    Trigger = new CastSelfTriggerDef(),
                    Effects = new()
                    {
                        new GainControlEffectDef
                        {
                            TargetFilter = "creature",
                            Duration = "end_of_turn",
                            Untap = true,
                            GainsHaste = true,
                            OptionalManaCost = "{1}{C}",
                        },
                    },
                },
            },
        };

        var card = CardDefinitionFactory.Build(
            def, _alice, replacements: null, continuous: continuous);
        return card.Abilities.OfType<TriggeredAbility>().Single();
    }

    private async Task ResolveWith(
        TriggeredAbility ability, object target, IPlayerAgent agent)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new[] { target } });
        await ability.ResolveAsync(agent, game: NewContext(agent));
    }

    private ScriptedAgent YesNo(bool answer)
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(answer);
        return agent;
    }

    [Fact]
    public void CastSelfTrigger_FiresOnOwnSpellCast_OnStack()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildObligator(continuous);

        // CR 603.2.1 — the cast trigger lives in the Stack zone (the spell is
        // on the stack when it triggers), mirroring Emrakul's on-cast trigger.
        ability.ActiveZones.Should().Contain(ZoneType.Stack);
        ability.TargetRequests.Should().HaveCount(1);
    }

    [Fact]
    public async Task OptionalPay_AgentDeclines_NoControlChange_NoManaSpent()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildObligator(continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        // Float {1}{C} (== 2 generic in v1's pool model) so payment WOULD be
        // possible — but the agent declines.
        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));

        await ResolveWith(ability, bear, YesNo(false));

        bear.Controller.Should().BeSameAs(_bob,
            "declining the optional {1}{C} payment skips the 'if you do' rider (CR 601.2b)");
        _alice.ManaPool.Total.Should().Be(2, "no mana is spent when the payment is declined");
        bear.IsTapped.Should().BeTrue("the untap rider is gated behind the payment too");
    }

    [Fact]
    public async Task OptionalPay_AgentAcceptsAndPays_StealsUntapsGrantsHaste()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildObligator(continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));

        await ResolveWith(ability, bear, YesNo(true));

        _alice.ManaPool.Total.Should().Be(0, "the {1}{C} reflexive cost is paid");
        bear.Controller.Should().BeSameAs(_alice,
            "paying gains control of the target creature until end of turn (CR 613.2)");
        bear.IsTapped.Should().BeFalse("untap that creature (CR 701.21)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should()
            .BeTrue("it gains haste until end of turn (CR 302.6)");

        continuous.ExpireEndOfTurn();
        bear.Controller.Should().BeSameAs(_bob, "control reverts at cleanup (CR 514.2)");
    }

    [Fact]
    public async Task OptionalPay_AcceptsButCannotAfford_NoControlChange()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildObligator(continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;

        // Empty pool — the agent says yes but cannot actually pay {1}{C}.
        await ResolveWith(ability, bear, YesNo(true));

        bear.Controller.Should().BeSameAs(_bob,
            "an unpayable optional cost cannot be paid → the rider is skipped (CR 601.2b)");
        _alice.ManaPool.Total.Should().Be(0);
    }

    [Fact]
    public async Task OptionalPay_TwoGenericNoColorless_CannotPayTheColorlessPip()
    {
        // colorless-pip pay-down: the {C} in {1}{C} now demands a colorless
        // mana source (CR 107.4c). Two PLAIN generic mana cover the {1} but not
        // the {C}, so the optional payment fails and the steal is skipped — even
        // though the agent said yes and the total mana value is "enough".
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildObligator(continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        // Float two RED mana (a colored source) — covers {1} but the {C} pip
        // cannot be paid from colored/generic mana.
        _alice.AddManaToPool(ManaCost.Parse("RR"));

        await ResolveWith(ability, bear, YesNo(true));

        bear.Controller.Should().BeSameAs(_bob,
            "the {C} pip requires colorless mana; red can't pay it (CR 107.4c)");
        bear.IsTapped.Should().BeTrue("the steal/untap rider is gated behind the unpaid {C}");
        _alice.ManaPool.Total.Should().Be(2, "no mana is spent when the {C} pip is unpayable");
    }

    [Fact]
    public async Task OptionalPay_ColorlessSource_PaysTheColorlessPip()
    {
        // The complement: a real colorless source ({1} + {C}) DOES pay {1}{C}.
        var continuous = new ContinuousEffectsService(_bus);
        var ability = BuildObligator(continuous);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();

        // One generic + one colorless — exactly {1}{C}.
        _alice.AddManaToPool(ManaCost.Parse("1"));
        _alice.AddManaToPool(ManaCost.Parse("C"));

        await ResolveWith(ability, bear, YesNo(true));

        _alice.ManaPool.Total.Should().Be(0, "{1}{C} is paid with one generic + one colorless");
        bear.Controller.Should().BeSameAs(_alice,
            "a colorless source satisfies the {C} pip and the steal resolves (CR 613.2)");
        bear.IsTapped.Should().BeFalse("untap that creature (CR 701.21)");
    }

    [Fact]
    public async Task EldraziObligator_Factory_BuildsCastTriggerOptionalSteal()
    {
        var continuous = new ContinuousEffectsService(_bus);
        var obligator = Majik.Core.CardData.Factories.EldraziObligatorFactory
            .Create(_alice, continuous);

        obligator.Name.Should().Be("Eldrazi Obligator");
        obligator.Power.Should().Be(3);
        obligator.Toughness.Should().Be(1);

        var ability = obligator.Abilities.OfType<TriggeredAbility>().Single();
        ability.ActiveZones.Should().Contain(ZoneType.Stack);
        ability.TargetRequests.Should().HaveCount(1);

        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        bear.ActiveEffects = continuous;
        bear.Tap();
        _alice.AddManaToPool(ManaCost.Parse("{1}{C}"));

        await ResolveWith(ability, bear, YesNo(true));

        bear.Controller.Should().BeSameAs(_alice,
            "Eldrazi Obligator steals the chosen creature when {1}{C} is paid (CR 613.2)");
        bear.IsTapped.Should().BeFalse("untap that creature (CR 701.21)");
        Majik.Core.Combat.CombatAbilities.HasHaste(bear).Should().BeTrue();
    }

    [Fact]
    public void EldraziObligator_Factory_PureShape_NoServiceNoThrow()
    {
        var obligator = Majik.Core.CardData.Factories.EldraziObligatorFactory.Create(_alice);
        obligator.Should().NotBeNull();
        obligator.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }
}
