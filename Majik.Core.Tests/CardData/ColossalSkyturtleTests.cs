using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ColossalSkyturtleFactory"/>.
///
/// Colossal Skyturtle (The Lost Caverns of Ixalan, {4}{G}{G}{U}):
///   Enchantment Creature — Turtle 6/5. (v1: modelled as plain Creature)
///   Flying, ward {2}.
///   Channel — {2}{G}, Discard this card: Return target card from your
///   graveyard to your hand.
///   Channel — {1}{U}, Discard this card: Return target creature to its
///   owner's hand.
///
/// Covers:
///   - Card identity: Turtle 6/5, {4}{G}{G}{U}, MV 7, owner / controller.
///   - NamedCardFactory dispatch.
///   - Flying keyword marker attached (CR 702.9).
///   - Ward {2} keyword marker attached; BuildWardEffect exposes printed {2}
///     cost (CR 702.21).
///   - Channel 1 cost shape: {2}{G} + DiscardSelfCost (CR 702.74a).
///   - Channel 1 DiscardSelfCost: payable in hand, rejected outside hand.
///   - Channel 1 resolve: returns target card from controller's graveyard to
///     hand (any card type).
///   - Channel 1 resolve: fizzles when target card has left the graveyard
///     (CR 608.2b).
///   - Channel 2 cost shape: {1}{U} + DiscardSelfCost (CR 702.74a).
///   - Channel 2 DiscardSelfCost: payable in hand, rejected outside hand.
///   - Channel 2 resolve: returns controller's creature to owner's hand.
///   - Channel 2 resolve: returns opponent's creature to that opponent's hand.
///   - Channel 2 resolve: fizzles when target creature left the battlefield
///     (CR 608.2b).
/// </summary>
public class ColossalSkyturtleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossalSkyturtle_Identity()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);

        card.Name.Should().Be("Colossal Skyturtle");
        card.ManaCost.Should().Be("{4}{G}{G}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Turtle).Should().BeTrue();
        card.BasePower.Should().Be(6);
        card.BaseToughness.Should().Be(5);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ColossalSkyturtle_ManaValue_Is_7()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);

        // {4}{G}{G}{U} = 4 generic + 2 green + 1 blue = MV 7 (CR 202.3).
        card.ManaCostValue.TotalValue.Should().Be(7,
            "{4}{G}{G}{U} has mana value 7");
    }

    [Fact]
    public void ColossalSkyturtle_Colors_GreenAndBlue()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);

        card.ManaCostValue.Green.Should().Be(2, "two {G} pips");
        card.ManaCostValue.Blue.Should().Be(1, "one {U} pip");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossalSkyturtle_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Colossal Skyturtle", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Colossal Skyturtle");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Turtle).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(6);
        ((Creature)card).BaseToughness.Should().Be(5);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Flying")
            .And.Contain("Ward");

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "Channel 1 ({2}{G}) and Channel 2 ({1}{U})");
    }

    // -----------------------------------------------------------------------
    // Flying (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossalSkyturtle_HasFlyingKeyword()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Flying",
                "CR 702.9 — Flying is an evasion keyword");
    }

    // -----------------------------------------------------------------------
    // Ward {2} (CR 702.21)
    // -----------------------------------------------------------------------

    [Fact]
    public void ColossalSkyturtle_HasWardKeyword()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Ward",
                "CR 702.21 — Ward {2} marker");
    }

    [Fact]
    public void ColossalSkyturtle_BuildWardEffect_ExposesWardCost2()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ward = ColossalSkyturtleFactory.BuildWardEffect(card);

        ward.Source.Should().BeSameAs(card);
        ward.Cost.Generic.Should().Be(2,
            "Ward {2} — 2 generic mana (CR 702.21)");
    }

    // -----------------------------------------------------------------------
    // Channel 1 — {2}{G}, Discard this card: return graveyard card to hand
    // -----------------------------------------------------------------------

    private static ActivatedAbility Channel1(Creature card) =>
        card.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<ManaCostCost>()
                .Any(m => m.Cost.Green >= 1 && m.Cost.Generic == 2));

    private static ActivatedAbility Channel2(Creature card) =>
        card.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<ManaCostCost>()
                .Any(m => m.Cost.Blue >= 1 && m.Cost.Generic == 1));

    [Fact]
    public void Channel1_CostShape_Is2GAndDiscardSelf()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ch1 = Channel1(card);

        ch1.Costs.Should().HaveCount(2);
        ch1.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = ch1.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Channel 1 costs {2}{G}: 2 generic");
        manaCost.Green.Should().Be(1, "Channel 1 costs {2}{G}: 1 green");
    }

    [Fact]
    public void Channel1_DiscardSelfCost_PayableWhenInHand_RejectedElsewhere()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ch1 = Channel1(card);
        var discardCost = ch1.Costs.OfType<DiscardSelfCost>().Single();

        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        discardCost.CanPay(_alice).Should().BeTrue("Channel active from hand — CR 702.74a");

        _alice.Zones.Hand.RemoveCard(card);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        discardCost.CanPay(_alice).Should().BeFalse(
            "Channel cannot activate from outside the hand (CR 702.74a)");
    }

    [Fact]
    public void Channel1_TargetRequest_HasGraveyardGatherIntent()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ch1 = Channel1(card);

        ch1.TargetRequests.Should().HaveCount(1);
        var tr = ch1.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("graveyard",
            "Channel 1 targets a card in the graveyard");
        tr.Intent.Should().Be(BotIntent.Tutor);
    }

    [Fact]
    public void Channel1_Resolve_ReturnsCreatureCardFromGraveyard()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var graveBear = NewGraveyardCard(_alice, "Bear", "{1}{G}");

        var ch1 = Channel1(card);
        ch1.SetChosenTargets(new[] { new object[] { graveBear } });
        ch1.Resolve();

        graveBear.Zone.Should().Be(ZoneType.Hand,
            "Channel 1 moves the target card from graveyard to hand");
        _alice.Zones.Hand.GetCards().Should().Contain(graveBear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(graveBear);
    }

    [Fact]
    public void Channel1_Resolve_ReturnsInstantFromGraveyard()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var inst = new Instant("Lightning Bolt", "{R}");
        inst.SetOwner(_alice);
        inst.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(inst);

        var ch1 = Channel1(card);
        ch1.SetChosenTargets(new[] { new object[] { inst } });
        ch1.Resolve();

        inst.Zone.Should().Be(ZoneType.Hand,
            "Channel 1 can return any card type — not just creatures");
        _alice.Zones.Hand.GetCards().Should().Contain(inst);
    }

    [Fact]
    public void Channel1_Resolve_FizzlesWhenTargetHasLeftGraveyard_CR608()
    {
        // CR 608.2b — target no longer in graveyard at resolution → no-op.
        var card = ColossalSkyturtleFactory.Create(_alice);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Exile); // moved out of graveyard before resolve

        var ch1 = Channel1(card);
        ch1.SetChosenTargets(new[] { new object[] { bear } });
        ch1.Resolve();

        bear.Zone.Should().Be(ZoneType.Exile,
            "CR 608.2b — card left the graveyard; Channel 1 effect fizzles");
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Channel1_Resolve_FallbackPick_FirstGraveyardCard_WhenNoTargetSet()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var graveBear = NewGraveyardCard(_alice, "Fallback Bear", "{1}{G}");

        var ch1 = Channel1(card);
        // No SetChosenTargets call — the fallback should pick the first card.
        ch1.Resolve();

        graveBear.Zone.Should().Be(ZoneType.Hand,
            "deterministic fallback picks first card in graveyard");
    }

    // -----------------------------------------------------------------------
    // Channel 2 — {1}{U}, Discard this card: return target creature to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Channel2_CostShape_Is1UAndDiscardSelf()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ch2 = Channel2(card);

        ch2.Costs.Should().HaveCount(2);
        ch2.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = ch2.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "Channel 2 costs {1}{U}: 1 generic");
        manaCost.Blue.Should().Be(1, "Channel 2 costs {1}{U}: 1 blue");
    }

    [Fact]
    public void Channel2_DiscardSelfCost_PayableWhenInHand_RejectedElsewhere()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ch2 = Channel2(card);
        var discardCost = ch2.Costs.OfType<DiscardSelfCost>().Single();

        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        discardCost.CanPay(_alice).Should().BeTrue("Channel active from hand — CR 702.74a");

        _alice.Zones.Hand.RemoveCard(card);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        discardCost.CanPay(_alice).Should().BeFalse(
            "Channel cannot activate from outside the hand (CR 702.74a)");
    }

    [Fact]
    public void Channel2_TargetRequest_HasBounceIntentAndCreatureGather()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var ch2 = Channel2(card);

        ch2.TargetRequests.Should().HaveCount(1);
        var tr = ch2.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature");
        tr.Intent.Should().Be(BotIntent.Bounce);
    }

    [Fact]
    public void Channel2_Resolve_ReturnsControllerCreatureToOwnersHand()
    {
        var card = ColossalSkyturtleFactory.Create(_alice);
        var bear = NewBattlefieldCreature(_alice, "Bear", "{1}{G}");

        var ch2 = Channel2(card);
        ch2.SetChosenTargets(new[] { new object[] { bear } });
        ch2.Resolve();

        bear.Zone.Should().Be(ZoneType.Hand,
            "Channel 2 returns the creature to its owner's hand (CR 701.10)");
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Channel2_Resolve_ReturnsOpponentCreatureToOpponentsHand()
    {
        // Oracle reads "target creature" — not controller-restricted.
        // The creature goes to its OWNER's hand (CR 701.10).
        var card = ColossalSkyturtleFactory.Create(_alice);
        var bobCreature = NewBattlefieldCreature(_bob, "Goblin Guide", "{R}");

        var ch2 = Channel2(card);
        ch2.SetChosenTargets(new[] { new object[] { bobCreature } });
        ch2.Resolve();

        bobCreature.Zone.Should().Be(ZoneType.Hand,
            "Channel 2 can target any creature on any battlefield");
        _bob.Zones.Hand.GetCards().Should().Contain(bobCreature,
            "returned to the creature's owner's hand (CR 701.10)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobCreature);
    }

    [Fact]
    public void Channel2_Resolve_FizzlesWhenCreatureLeftBattlefield_CR608()
    {
        // CR 608.2b — target no longer on battlefield at resolution → no-op.
        var card = ColossalSkyturtleFactory.Create(_alice);
        var bear = new Creature("Vanished Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Graveyard); // moved off battlefield before resolve
        _alice.Zones.Graveyard.AddCard(bear);

        var ch2 = Channel2(card);
        ch2.SetChosenTargets(new[] { new object[] { bear } });
        ch2.Resolve();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — creature left the battlefield; Channel 2 effect fizzles");
        _alice.Zones.Hand.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewGraveyardCard(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }

    private static Creature NewBattlefieldCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
