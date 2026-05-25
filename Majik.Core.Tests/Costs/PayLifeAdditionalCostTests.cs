using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="PayLifeAdditionalCost"/> — both fixed and
/// variable-X flavours — covering the cast-time primitive in isolation
/// AND end-to-end through <see cref="SpellCastFlow"/>'s CR 601.2f
/// additional-cost loop.
///
/// CR references: 118.8 (life-payment cost), 119.4 (can't pay life you
/// don't have), 601.2f (additional cost at cast time), 601.2g (illegal
/// cast rewind).
/// </summary>
public class PayLifeAdditionalCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Fixed-amount flavour
    // -----------------------------------------------------------------------

    [Fact]
    public void Fixed_Description_RendersAmount()
    {
        new PayLifeAdditionalCost(amount: 2).Description.Should().Be("pay 2 life");
        new PayLifeAdditionalCost(amount: 0).Description.Should().Be("pay 0 life");
    }

    [Fact]
    public void Fixed_NegativeAmount_Throws()
    {
        var act = () => new PayLifeAdditionalCost(amount: -1);
        act.Should().Throw<ArgumentOutOfRangeException>(
            "CR 118.8 — pay-life amounts must be non-negative.");
    }

    [Fact]
    public void Fixed_CanPay_WhenLifeMeetsAmount()
    {
        var cost = new PayLifeAdditionalCost(amount: 5);
        cost.CanPay(_alice).Should().BeTrue("Alice has 20 life ≥ 5");
    }

    [Fact]
    public void Fixed_CanPay_RejectsWhenLifeShortOfAmount()
    {
        // CR 119.4 — "Players can't pay more life than they have."
        _alice.LoseLife(18); // Alice = 2 life
        var cost = new PayLifeAdditionalCost(amount: 5);
        cost.CanPay(_alice).Should().BeFalse("Alice has 2 life, can't pay 5");
    }

    [Fact]
    public void Fixed_Pay_DeductsLife_AndStampsPaidAmount()
    {
        var cost = new PayLifeAdditionalCost(amount: 3);
        cost.Pay(_alice).Should().BeTrue();
        _alice.LifeTotal.Should().Be(17, "20 - 3 = 17");
        cost.PaidAmount.Should().Be(3, "Pay stamps the paid magnitude");
    }

    [Fact]
    public void Fixed_Pay_ZeroAmount_IsLegalNoOp()
    {
        var cost = new PayLifeAdditionalCost(amount: 0);
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();
        _alice.LifeTotal.Should().Be(20);
        cost.PaidAmount.Should().Be(0);
    }

    [Fact]
    public void Fixed_Pay_ReturnsFalse_WhenCasterLacksLife()
    {
        // CR 601.2g — illegal cast, no partial payment. Pay returns false
        // and PaidAmount stays null so the cast pipeline catches it.
        _alice.LoseLife(19); // Alice = 1 life
        var cost = new PayLifeAdditionalCost(amount: 5);
        cost.Pay(_alice).Should().BeFalse();
        _alice.LifeTotal.Should().Be(1, "no partial payment");
        cost.PaidAmount.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Variable-X flavour
    // -----------------------------------------------------------------------

    [Fact]
    public void Variable_RequiresVariableXTrueFlag()
    {
        // The bool arg exists purely for readable call sites; passing
        // false is the wrong overload (caller meant the fixed flavour).
        var card = new Sorcery("Stub", "B") { Owner = _alice };
        var act = () => new PayLifeAdditionalCost(card, variableX: false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Variable_NullCard_Throws()
    {
        var act = () => new PayLifeAdditionalCost(card: null!, variableX: true);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Variable_GetCurrentAmount_ReadsPendingCastX()
    {
        var card = new Sorcery("Stub", "B") { Owner = _alice };
        var cost = new PayLifeAdditionalCost(card, variableX: true);

        // No stamp yet — amount reads as 0 (legal no-op).
        cost.GetCurrentAmount().Should().Be(0);
        cost.CanPay(_alice).Should().BeTrue("0-life payment is always legal");

        // Stamp X = 4 (simulating SpellCastFlow's prompt).
        card.SetPendingCastX(4);
        cost.GetCurrentAmount().Should().Be(4);
    }

    [Fact]
    public void Variable_Pay_ReadsPendingCastX_AndDeductsLife()
    {
        var card = new Sorcery("Stub", "B") { Owner = _alice };
        card.SetPendingCastX(7);

        var cost = new PayLifeAdditionalCost(card, variableX: true);
        cost.Pay(_alice).Should().BeTrue();
        _alice.LifeTotal.Should().Be(13, "20 - 7 = 13");
        cost.PaidAmount.Should().Be(7);
    }

    [Fact]
    public void Variable_CanPay_RejectsWhenLifeShortOfPendingX()
    {
        var card = new Sorcery("Stub", "B") { Owner = _alice };
        card.SetPendingCastX(25);

        var cost = new PayLifeAdditionalCost(card, variableX: true);
        cost.CanPay(_alice).Should().BeFalse(
            "CR 119.4 — Alice has 20 life, can't pay X=25");
        cost.Pay(_alice).Should().BeFalse("no partial payment");
        _alice.LifeTotal.Should().Be(20, "no deduction on failed pay");
    }

    [Fact]
    public void Variable_Description()
    {
        var card = new Sorcery("Stub", "B") { Owner = _alice };
        new PayLifeAdditionalCost(card, variableX: true).Description
            .Should().Be("pay X life");
    }

    // -----------------------------------------------------------------------
    // End-to-end through SpellCastFlow — the real cast pipeline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_FixedPayLife_DeductsLifeBeforeStackPush()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var spell = new Sorcery("Stub Spell", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);

        await flow.CastAsync(
            _alice, spell,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            additionalCosts: new IAdditionalCost[] { new PayLifeAdditionalCost(amount: 4) });

        _alice.LifeTotal.Should().Be(16, "20 - 4 = 16, paid at cast time");
        stack.Count.Should().Be(1, "spell is on the stack");
    }

    [Fact]
    public async Task SpellCastFlow_FixedPayLife_RejectsCast_WhenCasterShortOfLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        _alice.LoseLife(18); // Alice = 2 life

        var spell = new Sorcery("Stub Spell", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);

        var act = async () => await flow.CastAsync(
            _alice, spell,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            additionalCosts: new IAdditionalCost[] { new PayLifeAdditionalCost(amount: 5) });

        await act.Should().ThrowAsync<InvalidOperationException>(
            "CR 119.4 — caster lacks the life total to pay the additional cost");
        _alice.LifeTotal.Should().Be(2, "no partial payment, card stays in hand");
        spell.Zone.Should().Be(ZoneType.Hand, "rewound to hand, no zone mutation");
        stack.Count.Should().Be(0);
    }

    [Fact]
    public async Task SpellCastFlow_VariableX_StampsPendingCastX_ThenPaysLife()
    {
        // The reorder in SpellCastFlow stamps PendingCastX from the
        // agent's ChooseXAsync response BEFORE the additional-cost loop
        // runs, so PayLifeAdditionalCost(variableX: true) can read it.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var spell = new Sorcery("Stub X Spell", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueX(6);                       // agent picks X = 6
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>(),
            ModeIntents: null,
            AdditionalCosts: new IAdditionalCost[]
            {
                new PayLifeAdditionalCost(spell, variableX: true),
            });

        await flow.CastAsync(_alice, spell, def, agent, ctx);

        _alice.LifeTotal.Should().Be(14, "20 - 6 = 14, paid at cast time");
        spell.PendingCastX.Should().Be(6, "X stamped on the card pre-pay");
        stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task SpellCastFlow_VariableX_RejectsCast_WhenCasterShortOfLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        _alice.LoseLife(17); // Alice = 3 life

        var spell = new Sorcery("Stub X Spell", "B") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spell);

        var agent = new ScriptedAgent();
        agent.QueueX(10); // agent picks X = 10 — caster only has 3 life
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, stack);

        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>(),
            ModeIntents: null,
            AdditionalCosts: new IAdditionalCost[]
            {
                new PayLifeAdditionalCost(spell, variableX: true),
            });

        var act = async () => await flow.CastAsync(_alice, spell, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "CR 119.4 — caster has 3 life, can't pay X = 10");
        _alice.LifeTotal.Should().Be(3, "no partial payment");
        stack.Count.Should().Be(0);
    }
}
