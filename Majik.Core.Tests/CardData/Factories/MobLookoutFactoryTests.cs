using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MobLookoutFactory"/>.
///
/// Mob Lookout — Creature — Human Rogue Villain {1}{U/B} 0/3.
/// Oracle text: "When this creature enters, target creature you control
///               connives. (Draw a card, then discard a card. If you discarded a
///               nonland card, put a +1/+1 counter on that creature.)"
///
/// Mob Lookout is the canonical fixed-X <c>connive_target</c> card — the
/// declarative pay-down of the connive/surveil library-manipulation verbs
/// deferral. The ETB connives a chosen OTHER creature (the <c>connive_self</c>
/// verb only applies to the source).
///
/// Covers:
/// - Identity (name, type, P/T 0/3, Human/Rogue/Villain subtypes, mana cost,
///   owner/controller).
/// - Exactly one ETB triggered ability with one 1..1 "creature you control"
///   TargetRequest.
/// - ETB resolution: the chosen creature connives — nonland discard → +1/+1
///   counter on it.
/// - ETB resolution: a land discard → no counter.
/// - ETB resolution: an illegal target at resolution (CR 608.2b) → no-op.
/// </summary>
public class MobLookoutFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MobLookout_Identity()
    {
        var c = MobLookoutFactory.Create(_alice);

        c.Name.Should().Be("Mob Lookout");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(3);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.HasSubtype(CardSubtype.Villain).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U/B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MobLookout_HasExactlyOneEtbTrigger_WithCreatureYouControlTarget()
    {
        var c = MobLookoutFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature you control");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void MobLookout_EtbConnive_NonlandDiscarded_PutsCounterOnTarget()
    {
        var alice = new Player("Alice", 20);

        var drawn = new Creature("Spider-Bot", "{R}", 2, 2);
        drawn.SetOwner(alice);
        alice.Zones.Library.AddCard(drawn);
        drawn.SetZone(ZoneType.Library);

        var target = new Creature("Goon", "{B}", 1, 1);
        target.SetOwner(alice);
        target.SetController(alice);
        alice.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var mob = MobLookoutFactory.Create(alice);
        var etb = mob.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        etb.Resolve();

        alice.Zones.Graveyard.GetCards().Should().Contain(drawn,
            "the drawn nonland card is discarded by the connive routine");
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.50a — a +1/+1 counter lands on the connived target for the nonland discard");
    }

    [Fact]
    public void MobLookout_EtbConnive_LandDiscarded_NoCounter()
    {
        var alice = new Player("Alice", 20);

        var land = new Land("Swamp");
        land.SetOwner(alice);
        alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        var target = new Creature("Goon", "{B}", 1, 1);
        target.SetOwner(alice);
        target.SetController(alice);
        alice.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var mob = MobLookoutFactory.Create(alice);
        var etb = mob.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        etb.Resolve();

        alice.Zones.Graveyard.GetCards().Should().Contain(land);
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 701.50a — no counter when a LAND was discarded");
    }

    [Fact]
    public void MobLookout_EtbConnive_TargetAlreadyLeft_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var drawn = new Creature("Spider-Bot", "{R}", 2, 2);
        drawn.SetOwner(alice);
        alice.Zones.Library.AddCard(drawn);
        drawn.SetZone(ZoneType.Library);

        var target = new Creature("Goon", "{B}", 1, 1);
        target.SetOwner(alice);
        target.SetController(alice);
        alice.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard); // already gone at resolution

        var mob = MobLookoutFactory.Create(alice);
        var etb = mob.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => etb.Resolve();

        act.Should().NotThrow("CR 608.2b — illegal target at resolution is a no-op");
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        alice.Zones.Graveyard.GetCards().Should().NotContain(drawn,
            "the connive never runs, so nothing is drawn or discarded");
    }
}
