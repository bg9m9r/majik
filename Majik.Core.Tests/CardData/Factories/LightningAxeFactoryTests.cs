using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LightningAxeFactory"/> — Instant {R}
/// (Time Spiral / many reprints).
///
/// "As an additional cost to cast this spell, discard a card or pay {5}.
///  Lightning Axe deals 5 damage to target creature."
///
/// Covers:
///   - Identity ({R} Instant, red, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape:
///     <see cref="DiscardACardOrPayManaAdditionalCost"/> additional cost +
///     one 1..1 "target creature" target request.
///   - Resolve: deals 5 damage to target creature (CR 608.2b path).
///   - Resolve: no-op when the resolved target is not a creature (CR 608.2b).
///   - Cost picks discard mode when a card is in hand, pay-{5} mode otherwise
///     (CR 601.2f — disjunctive additional cost).
///   - Cost CanPay reflects the OR of the two modes.
///   - SpellCastFlow rejects cast when caster has no card in hand AND cannot
///     produce {5} (CR 601.2g — additional cost can't be paid).
/// </summary>
[Trait("Color", "R")]
public class LightningAxeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningAxe_Identity_InstantAtR()
    {
        var axe = LightningAxeFactory.Create(_alice);

        axe.Name.Should().Be("Lightning Axe");
        axe.HasType(CardType.Instant).Should().BeTrue();
        axe.ManaCost.ToString().Should().Be("{R}");
        axe.Owner.Should().BeSameAs(_alice);
        axe.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningAxe_SpellDefinition_DeclaresDiscardOrPayManaCost_AndTargetCreature()
    {
        var def = LightningAxeFactory.BuildSpellDefinition(resolver: x => x);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<DiscardACardOrPayManaAdditionalCost>(
                "Lightning Axe prints 'As an additional cost to cast this spell, discard a card or pay {5}.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target creature");
    }

    // -----------------------------------------------------------------------
    // Resolve — 5 damage to target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void LightningAxe_Resolve_DealsFiveDamageToCreature()
    {
        // 0/6 so 5 damage is not lethal — verifies the damage marker is
        // applied without SBA wipe interfering.
        var bear = new Creature("Wall of Stone", "{R}{R}", 0, 6,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = LightningAxeFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bear.Damage.Should().Be(5, "Lightning Axe deals 5 damage to target creature");
    }

    [Fact]
    public void LightningAxe_Resolve_NoOp_WhenTargetIsNotCreature()
    {
        // CR 608.2b — "target creature" excludes players; if a player ends up
        // as the resolved target the effect does nothing.
        var def = LightningAxeFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        _bob.LifeTotal.Should().Be(20, "Lightning Axe only damages creatures (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Cost: prefers discard, falls back to pay {5}
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_PrefersDiscardWhenCardAvailable()
    {
        var spareCard = new Sorcery("Bogus Spell", "{B}");
        spareCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);
        // Plenty of mana floating — discard mode should still win (v1 picks
        // discard-first).
        _alice.AddManaToPool(ManaCost.Parse("{5}"));

        var cost = new DiscardACardOrPayManaAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Discarded.Should().Be(spareCard, "a card is in hand — discard mode wins (v1 deterministic)");
        cost.PaidMana.Should().BeFalse();
        spareCard.Zone.Should().Be(ZoneType.Graveyard);
        _alice.ManaPool.Pay(ManaCost.Parse("{5}")).Success.Should().BeTrue(
            "the floating {5} was NOT spent — discard mode was used");
    }

    [Fact]
    public void Cost_FallsBackToPayManaWhenEmptyHand()
    {
        // No card in hand; pay {5} from the pool instead.
        _alice.AddManaToPool(ManaCost.Parse("{5}"));

        var cost = new DiscardACardOrPayManaAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Discarded.Should().BeNull();
        cost.PaidMana.Should().BeTrue(
            "no card to discard — pay-{5} mode is the only payable mode");
        _alice.ManaPool.Pay(ManaCost.Parse("{1}")).Success.Should().BeFalse(
            "the floating {5} was consumed paying the additional cost");
    }

    [Fact]
    public void Cost_CanPay_FalseWhenEmptyHandAndNoMana()
    {
        var cost = new DiscardACardOrPayManaAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "neither mode can be paid: empty hand + empty mana pool (CR 117.1)");
    }

    // -----------------------------------------------------------------------
    // Cast-time: neither discard nor pay-{5} payable → cast rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNeitherDiscardNorPayManaPossible()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var card = LightningAxeFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        // The cast flow drains the spell off the hand before pre-checking
        // costs; remove it up front so Alice's hand is empty (no card to
        // discard). Alice also has no mana floating (no pay-{5} mode).
        _alice.Zones.Hand.RemoveCard(card);

        // Bob has a creature to target — targeting is fine; the cost is the
        // illegality.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = LightningAxeFactory.BuildSpellDefinition(resolver: t => t);

        var act = async () => await flow.CastAsync(_alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*discard*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        bear.Damage.Should().Be(0);
    }
}
