using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Harbinger of the Tides (Magic Origins, {U}{U}) and the
/// flash-alt-cost-permission primitive (<see cref="FlashAlternativeCost"/>,
/// CR 601.2b / 702.8).
///
/// Card: Creature — Merfolk Wizard 2/2.
///   "You may cast this spell as though it had flash if you pay {2} more to
///    cast it. (You may cast it any time you could cast an instant.)
///    When this creature enters, you may return target tapped creature an
///    opponent controls to its owner's hand."
///
/// Covers:
///   - Identity / dispatch (Merfolk Wizard, {U}{U}, 2/2, blue).
///   - ETB trigger shape: optional (Min 0) single "tapped creature an opponent
///     controls" target.
///   - ETB resolution: bounces a tapped opponent creature; declines cleanly;
///     untapped / own creatures are not legal candidates.
///   - Flash alt-cost: {2}{U}{U} surcharge cost, hand-zone gate, and an
///     instant-speed cast on the opponent's turn lands on the stack.
/// </summary>
[Trait("Color", "U")]
public class HarbingerOfTheTidesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Harbinger_Identity()
    {
        var c = HarbingerOfTheTidesFactory.Create(_alice);

        c.Name.Should().Be("Harbinger of the Tides");
        c.ManaCost.Should().Be("{U}{U}");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Harbinger_IsBlueOnly()
    {
        var c = HarbingerOfTheTidesFactory.Create(_alice);
        var colors = CardColors.GetColors(c);
        colors.Should().ContainSingle().Which.Should().Be(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Harbinger()
    {
        var card = NamedCardFactory.Create("Harbinger of the Tides", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Harbinger of the Tides");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Harbinger_HasExactlyOneEtbTrigger()
    {
        var c = HarbingerOfTheTidesFactory.Create(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Harbinger_EtbTrigger_IsOptionalTappedOpponentCreature()
    {
        var c = HarbingerOfTheTidesFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0, "the ETB is a 'you may return' optional (CR 603.3d)");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("tapped");
        req.Intent.Should().Be(BotIntent.Bounce);
    }

    // -----------------------------------------------------------------------
    // ETB resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Harbinger_Etb_BouncesTappedOpponentCreature()
    {
        var target = new Creature("Grizzly Bears", "1G", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);
        target.Tap();

        var harbinger = HarbingerOfTheTidesFactory.Create(_alice);
        var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        etb.Resolve();

        target.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(target);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void Harbinger_Etb_DeclineNoTarget_IsNoOp()
    {
        var harbinger = HarbingerOfTheTidesFactory.Create(_alice);
        var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();
        // No chosen targets — the "you may" was declined.

        var act = () => etb.Resolve();
        act.Should().NotThrow("declining the optional bounce is a clean no-op");
    }

    [Fact]
    public void Harbinger_TargetGatherer_OnlyOffersTappedOpponentCreatures()
    {
        // An untapped opponent creature, a tapped OWN creature, and a tapped
        // opponent creature: only the last is a legal candidate.
        var untappedOpp = new Creature("Untapped", "1G", 2, 2);
        untappedOpp.SetOwner(_bob); untappedOpp.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(untappedOpp); untappedOpp.SetZone(ZoneType.Battlefield);

        var tappedOwn = new Creature("Mine", "1G", 2, 2);
        tappedOwn.SetOwner(_alice); tappedOwn.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(tappedOwn); tappedOwn.SetZone(ZoneType.Battlefield);
        tappedOwn.Tap();

        var tappedOpp = new Creature("Bounce Me", "1G", 2, 2);
        tappedOpp.SetOwner(_bob); tappedOpp.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(tappedOpp); tappedOpp.SetZone(ZoneType.Battlefield);
        tappedOpp.Tap();

        var harbinger = HarbingerOfTheTidesFactory.Create(_alice);
        var etb = harbinger.Abilities.OfType<TriggeredAbility>().Single();
        var req = etb.TargetRequests[0];

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(new EventBus()));
        var candidates = req.CandidateGatherer!(ctx);

        candidates.Should().Contain(tappedOpp);
        candidates.Should().NotContain(untappedOpp, "untapped creatures aren't legal");
        candidates.Should().NotContain(tappedOwn, "your own creatures aren't legal");
    }

    // -----------------------------------------------------------------------
    // Flash alt-cost permission (CR 601.2b / 702.8)
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashAltCost_Is_PrintedPlusTwo()
    {
        var alt = HarbingerOfTheTidesFactory.BuildFlashAlternativeCost();
        alt.AlternativeManaCost.Should().Be(ManaCost.Parse("{2}{U}{U}"),
            "{U}{U} printed + {2} flash surcharge");
        alt.SurchargeGeneric.Should().Be(2);
    }

    [Fact]
    public void FlashAltCost_CanCastFor_OnlyFromOwnersHand()
    {
        var alt = HarbingerOfTheTidesFactory.BuildFlashAlternativeCost();
        var harbinger = HarbingerOfTheTidesFactory.Create(_alice);

        harbinger.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(harbinger);
        alt.CanCastFor(harbinger, _alice).Should().BeTrue();

        // Not the owner.
        alt.CanCastFor(harbinger, _bob).Should().BeFalse();

        // Not in hand.
        harbinger.SetZone(ZoneType.Battlefield);
        alt.CanCastFor(harbinger, _alice).Should().BeFalse();
    }

    [Fact]
    public async Task FlashAltCost_PermitsInstantSpeedCast_OnOpponentTurn()
    {
        // CR 601.2b — casting Harbinger for its flash alternative cost is legal
        // at instant speed (here: the opponent's End step). SpellCastFlow skips
        // the sorcery-speed gate whenever an alternative cost is supplied, so
        // the spell lands on the stack instead of throwing.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var flow = new SpellCastFlow(stack, zones, bus);

        var harbinger = HarbingerOfTheTidesFactory.Create(_alice);
        harbinger.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(harbinger);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        // Bob's turn, End step — sorcery speed is NOT available to Alice.
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _bob, 1, StepStateType.End, stack);

        await flow.CastAsync(
            _alice, harbinger,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx,
            alternativeCost: HarbingerOfTheTidesFactory.BuildFlashAlternativeCost());

        stack.Count.Should().Be(1, "the flash alt-cost permits the instant-speed cast");
    }
}
