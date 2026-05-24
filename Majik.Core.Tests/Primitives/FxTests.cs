using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Primitives;

/// <summary>
/// Unit tests for the <see cref="Fx"/> effects-primitive facade. Each
/// verb gets one or more shape tests so the facade-level contracts
/// (null-arg guards, no-op on non-positive amounts, ZoneService routing
/// vs raw-zone fallback) stay locked in.
/// </summary>
public class FxTests
{
    // ------------------------------------------------------------------
    // DealDamage / DealDamageAny
    // ------------------------------------------------------------------

    [Fact]
    public void DealDamage_ToPlayer_LosesLife()
    {
        var p = new Player("Alice", 20);
        Fx.DealDamage(p, 3);
        p.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void DealDamage_ToCreature_StampsDamage()
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        Fx.DealDamage(c, 1);
        c.Damage.Should().Be(1);
    }

    [Fact]
    public void DealDamage_PlaneswalkerNotRouted_NoLoyaltyLoss()
    {
        // CR 119 — bare DealDamage does NOT cover Planeswalker; that's
        // DealDamageAny's job. This locks in the split.
        var pw = new Planeswalker("Liliana", "{1}{B}{B}", startingLoyalty: 3);
        Fx.DealDamage(pw, 2);
        pw.Loyalty.Should().Be(3);
    }

    [Fact]
    public void DealDamageAny_ToPlaneswalker_RemovesLoyalty()
    {
        var pw = new Planeswalker("Liliana", "{1}{B}{B}", startingLoyalty: 3);
        Fx.DealDamageAny(pw, 2);
        pw.Loyalty.Should().Be(1);
    }

    [Fact]
    public void DealDamageAny_ToPlayer_LosesLife()
    {
        var p = new Player("Alice", 20);
        Fx.DealDamageAny(p, 5);
        p.LifeTotal.Should().Be(15);
    }

    [Fact]
    public void DealDamage_NegativeAmount_IsNoOp()
    {
        var p = new Player("Alice", 20);
        Fx.DealDamage(p, -3);
        Fx.DealDamage(p, 0);
        p.LifeTotal.Should().Be(20);
    }

    // ------------------------------------------------------------------
    // Life
    // ------------------------------------------------------------------

    [Fact]
    public void GainLife_AddsToLifeTotal()
    {
        var p = new Player("Alice", 20);
        Fx.GainLife(p, 5);
        p.LifeTotal.Should().Be(25);
    }

    [Fact]
    public void LoseLife_SubtractsFromLifeTotal()
    {
        var p = new Player("Alice", 20);
        Fx.LoseLife(p, 7);
        p.LifeTotal.Should().Be(13);
    }

    [Fact]
    public void GainLife_NullPlayer_Throws()
    {
        Action act = () => Fx.GainLife(null!, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // DrawCards
    // ------------------------------------------------------------------

    [Fact]
    public void DrawCards_MovesTopNToHand()
    {
        var p = new Player("Alice", 20);
        var a = MakeCard("A"); var b = MakeCard("B"); var c = MakeCard("C");
        foreach (var card in new[] { a, b, c })
        {
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var drawn = Fx.DrawCards(p, 2);

        drawn.Select(x => x.Name).Should().Equal("A", "B");
        p.Zones.Hand.GetCards().Select(x => x.Name).Should().Equal("A", "B");
        p.Zones.Library.GetCards().Select(x => x.Name).Should().Equal("C");
    }

    [Fact]
    public void DrawCards_EmptyLibrary_MarksLossCondition()
    {
        var p = new Player("Alice", 20);
        var drawn = Fx.DrawCards(p, 1);
        drawn.Should().BeEmpty();
        p.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    [Fact]
    public void DrawCards_NegativeCount_IsNoOp()
    {
        var p = new Player("Alice", 20);
        Fx.DrawCards(p, 0).Should().BeEmpty();
        Fx.DrawCards(p, -1).Should().BeEmpty();
        p.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Discard
    // ------------------------------------------------------------------

    [Fact]
    public void Discard_MovesFirstNFromHandToGraveyard()
    {
        var p = new Player("Alice", 20);
        var a = MakeCard("A"); var b = MakeCard("B");
        p.Zones.Hand.AddCard(a); a.SetZone(ZoneType.Hand);
        p.Zones.Hand.AddCard(b); b.SetZone(ZoneType.Hand);

        var discarded = Fx.Discard(p, 1);

        discarded.Select(c => c.Name).Should().Equal("A");
        p.Zones.Hand.GetCards().Select(c => c.Name).Should().Equal("B");
        p.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("A");
    }

    [Fact]
    public void Discard_EmptyHand_HaltsCleanly()
    {
        var p = new Player("Alice", 20);
        var discarded = Fx.Discard(p, 3);
        discarded.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Mill / LookAtTopN
    // ------------------------------------------------------------------

    [Fact]
    public void Mill_DelegatesToMillAction()
    {
        var p = new Player("Alice", 20);
        var a = MakeCard("A"); var b = MakeCard("B");
        p.Zones.Library.AddCard(a); a.SetZone(ZoneType.Library);
        p.Zones.Library.AddCard(b); b.SetZone(ZoneType.Library);

        var milled = Fx.Mill(p, 1);

        milled.Select(c => c.Name).Should().Equal("A");
        p.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("A");
    }

    [Fact]
    public void LookAtTopN_Peeks_DoesNotMutate()
    {
        var p = new Player("Alice", 20);
        var a = MakeCard("A"); var b = MakeCard("B");
        p.Zones.Library.AddCard(a); a.SetZone(ZoneType.Library);
        p.Zones.Library.AddCard(b); b.SetZone(ZoneType.Library);

        var peek = Fx.LookAtTopN(p, 2);

        peek.Select(c => c.Name).Should().Equal("A", "B");
        p.Zones.Library.GetCards().Select(c => c.Name).Should().Equal("A", "B");
    }

    // ------------------------------------------------------------------
    // Zone moves
    // ------------------------------------------------------------------

    [Fact]
    public void MoveToGraveyard_BattlefieldToGraveyard()
    {
        var p = new Player("Alice", 20);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(p);
        p.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        Fx.MoveToGraveyard(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        p.Zones.Graveyard.GetCards().Should().Contain(bear);
        p.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void BounceToHand_RawZoneFallback_MovesToOwnersHand()
    {
        var p = new Player("Alice", 20);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(p);
        p.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        Fx.BounceToHand(bear);

        bear.Zone.Should().Be(ZoneType.Hand);
        p.Zones.Hand.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void ReturnFromGraveyardToBattlefield_RawZoneFallback_AssignsController()
    {
        var owner = new Player("Alice", 20);
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(owner);
        owner.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var newCtrl = new Player("Bob", 20);
        Fx.ReturnFromGraveyardToBattlefield(bear, newCtrl);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        bear.Controller.Should().Be(newCtrl);
        newCtrl.Zones.Battlefield.GetCards().Should().Contain(bear);
        owner.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    // ------------------------------------------------------------------
    // Counter (stack)
    // ------------------------------------------------------------------

    [Fact]
    public void Counter_RemovesSpellAndPlacesCardInGraveyard()
    {
        var caster = new Player("Alice", 20);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(caster);
        bolt.SetController(caster);
        var spell = new Majik.Core.Spells.Spell(bolt, caster);

        var stack = new Majik.Core.Stack.Stack();
        stack.Push(spell);

        Fx.Counter(stack, spell);

        stack.IsEmpty.Should().BeTrue();
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ------------------------------------------------------------------
    // Counters (P/T)
    // ------------------------------------------------------------------

    [Fact]
    public void PlaceCounter_AddsCounters()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        Fx.PlaceCounter(bear, CounterType.PlusOnePlusOne, 3);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public void RemoveCounter_RemovesCounters()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.Counters.Add(CounterType.PlusOnePlusOne, 4);
        Fx.RemoveCounter(bear, CounterType.PlusOnePlusOne, 1);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    [Fact]
    public void PlaceCounter_NegativeAmount_IsNoOp()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        Fx.PlaceCounter(bear, CounterType.PlusOnePlusOne, -2);
        Fx.PlaceCounter(bear, CounterType.PlusOnePlusOne, 0);
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Tap / Untap
    // ------------------------------------------------------------------

    [Fact]
    public void Tap_Untap_RoundTrips()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        Fx.Tap(bear);
        bear.IsTapped.Should().BeTrue();
        Fx.Untap(bear);
        bear.IsTapped.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // Inline effect wrapper
    // ------------------------------------------------------------------

    [Fact]
    public void Inline_ProducesIEffect_ThatExecutesBody()
    {
        var ran = false;
        var e = Fx.Inline("test effect", () => ran = true);
        e.Description.Should().Be("test effect");
        e.Execute();
        ran.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Tokens — sanity check that token primitives route through
    // TokenFactory.
    // ------------------------------------------------------------------

    [Fact]
    public void Investigate_CreatesClueOnBattlefield()
    {
        var p = new Player("Alice", 20);
        var clue = Fx.Investigate(p);

        clue.Name.Should().Contain("Clue");
        p.Zones.Battlefield.GetCards().Should().Contain(clue);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Instant MakeCard(string name)
    {
        var i = new Instant(name, "{R}");
        return i;
    }
}
