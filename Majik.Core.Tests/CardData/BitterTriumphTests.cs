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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="BitterTriumphFactory"/> — Instant {1}{B}
/// (The Lost Caverns of Ixalan).
///
/// Oracle text:
///   "As an additional cost to cast this spell, discard a card or pay 3 life.
///    Destroy target creature or planeswalker."
///
/// Covers:
///   - Identity (Instant, {1}{B}, CMC 2, black) + NamedCardFactory dispatch.
///   - SpellDefinition shape:
///     <see cref="DiscardACardOrPayLifeAdditionalCost"/> additional cost +
///     one 1..1 "target creature or planeswalker" target request, BotIntent.Removal.
///   - Resolve: destroys target creature (CR 701.7).
///   - Resolve: destroys target planeswalker (CR 701.7).
///   - Resolve: target left battlefield → no-op (CR 608.2b).
///   - Cost: discard mode removes a card from hand and sets Discarded.
///   - Cost: pay-life mode deducts 3 life when hand is empty and sets PaidLife.
///   - Cost.CanPay: false when hand is empty AND life &lt; 3.
///   - Cost.CanPay: true when hand has a card (discard mode available).
///   - Cost.CanPay: true when hand is empty but life ≥ 3 (pay-life mode available).
///   - SpellCastFlow rejects cast when neither mode payable (CR 601.2g).
/// </summary>
public class BitterTriumphTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeManaCostManaValue()
    {
        var card = BitterTriumphFactory.Create(_alice);

        card.Name.Should().Be("Bitter Triumph");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BitterTriumph()
    {
        var card = NamedCardFactory.Create("Bitter Triumph", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Bitter Triumph");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresDiscardOrPayLifeCost_AndCreatureOrPlaneswalkerTarget()
    {
        var def = BitterTriumphFactory.BuildDefinition(t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<DiscardACardOrPayLifeAdditionalCost>(
                "Bitter Triumph prints 'As an additional cost, discard a card or pay 3 life.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys creature / planeswalker
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysTargetCreature()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Bitter Triumph destroys the target creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_DestroysTargetPlaneswalker()
    {
        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        liliana.SetOwner(_bob);
        liliana.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        Resolve(liliana);

        liliana.Zone.Should().Be(ZoneType.Graveyard,
            "Bitter Triumph destroys the target planeswalker (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(liliana);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(liliana);
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_DoesNothing()
    {
        // Target already left the battlefield before resolution (CR 608.2b).
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 1, 1);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        Resolve(goyf);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "Bitter Triumph is a no-op when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Cost: discard mode
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_DiscardsACard_WhenHandIsNonEmpty()
    {
        var alice = new Player("Alice", 20);
        var spareCard = new Sorcery("Bogus Spell", "{B}");
        spareCard.SetOwner(alice);
        alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new DiscardACardOrPayLifeAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Discarded.Should().Be(spareCard,
            "discard mode is preferred when a card is available (v1 deterministic)");
        cost.PaidLife.Should().BeFalse();
        spareCard.Zone.Should().Be(ZoneType.Graveyard,
            "discarded card moves to graveyard (CR 701.16a)");
        alice.Zones.Hand.GetCards().Should().NotContain(spareCard);
        alice.LifeTotal.Should().Be(20, "no life was paid in discard mode");
    }

    [Fact]
    public void Cost_PreferDiscardOverLife_WhenBothAvailable()
    {
        var alice = new Player("Alice", 20);
        var spareCard = new Sorcery("Bogus Spell", "{B}");
        spareCard.SetOwner(alice);
        alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);
        // Alice has plenty of life too — discard should still win.

        var cost = new DiscardACardOrPayLifeAdditionalCost();
        cost.Pay(alice);

        cost.Discarded.Should().Be(spareCard, "discard mode is preferred (v1)");
        cost.PaidLife.Should().BeFalse();
        alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Cost: pay-life mode
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_PaysLife_WhenHandIsEmpty()
    {
        var alice = new Player("Alice", 20);
        // Hand is empty; life is 20 — pay-life mode must fire.

        var cost = new DiscardACardOrPayLifeAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.PaidLife.Should().BeTrue(
            "pay-life mode is used when hand is empty (v1 fallback)");
        cost.Discarded.Should().BeNull();
        alice.LifeTotal.Should().Be(17,
            "3 life deducted (CR 118.8 / 119.4)");
    }

    // -----------------------------------------------------------------------
    // Cost.CanPay edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_CanPay_TrueWhenHandHasCard()
    {
        var alice = new Player("Alice", 1); // only 1 life — pay-life not available alone
        var card = new Sorcery("Bogus Spell", "{B}");
        card.SetOwner(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var cost = new DiscardACardOrPayLifeAdditionalCost();
        cost.CanPay(alice).Should().BeTrue(
            "discard mode is available (CR 117.1 OR gate)");
    }

    [Fact]
    public void Cost_CanPay_TrueWhenLifeIsAtLeast3AndHandEmpty()
    {
        var alice = new Player("Alice", 3); // exactly 3 life, empty hand

        var cost = new DiscardACardOrPayLifeAdditionalCost();
        cost.CanPay(alice).Should().BeTrue(
            "pay-life mode is available when LifeTotal >= 3 (CR 119.4)");
    }

    [Fact]
    public void Cost_CanPay_FalseWhenHandEmptyAndLifeBelow3()
    {
        var alice = new Player("Alice", 2); // 2 life, empty hand

        var cost = new DiscardACardOrPayLifeAdditionalCost();
        cost.CanPay(alice).Should().BeFalse(
            "neither discard (no hand) nor pay-life (life < 3) is payable (CR 117.1)");
    }

    // -----------------------------------------------------------------------
    // SpellCastFlow: cast rejected when neither mode is payable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNeitherModePayable()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 2); // 2 life, hand will be empty
        var bob = new Player("Bob", 20);

        var card = BitterTriumphFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        // Remove the spell from Alice's hand so she has no cards to discard
        // (SpellCastFlow drains it off hand before pre-checking costs).
        alice.Zones.Hand.RemoveCard(card);

        // Bob has a creature to target, but Alice has no hand cards AND < 3 life.
        var bear = NewControlledCreature(bob, "Grizzly Bears", "{1}{G}");

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = BitterTriumphFactory.BuildDefinition(t => t);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = BitterTriumphFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
