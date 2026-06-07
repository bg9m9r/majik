using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Simulation;

public sealed class CloneFidelityTests
{
    [Fact]
    public void Clone_CopiesCardsIntoZones_PreservingInstanceIdAndOrder()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", manaCost: "{1}{G}", power: 2, toughness: 2);
        bear.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(bear);

        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);

        var cBear = cAlice.Zones.Battlefield.GetCards().Single();   // real zone-read accessor
        cBear.Should().NotBeSameAs(bear);
        cBear.InstanceId.Should().Be(bear.InstanceId);
        cBear.Name.Should().Be("Grizzly Bears");
        cloned.CardMap[bear.InstanceId].Should().BeSameAs(cBear);
    }
    [Fact]
    public void Clone_CopiesLife_AndIsIndependent()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 17);

        var cloned = GameStateCloner.Clone(new[] { alice, bob });

        var cAlice = cloned.PlayerFor(alice);
        var cBob = cloned.PlayerFor(bob);
        cAlice.LifeTotal.Should().Be(20);
        cBob.LifeTotal.Should().Be(17);

        // Independence: mutating the clone must not touch the original.
        cAlice.SetLifeTotal(5);
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Clone_CopiesPlayerScalarState()
    {
        var alice = new Player("Alice", 20);
        alice.GainEnergy(3);                              // EnergyCounters
        alice.AddPoisonCounters(2);                       // PoisonCounters
        alice.AddManaToPool(ManaCost.Parse("{R}{R}"));    // _manaPool (Red = 2)

        var cloned = GameStateCloner.Clone(new[] { alice });
        var c = cloned.PlayerFor(alice);

        c.EnergyCounters.Should().Be(3);
        c.PoisonCounters.Should().Be(2);
        c.ManaPool.Red.Should().Be(2);

        // Independence: mutating the clone must not touch the original.
        c.GainEnergy(10);
        alice.EnergyCounters.Should().Be(3);
    }

    [Fact]
    public void Clone_CopiesPermanentBoardState()
    {
        // Arrange: a creature with tap state, damage, a counter, and no summoning sickness.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", manaCost: "{1}{G}", power: 2, toughness: 2);
        bear.ChangeOwner(alice);
        bear.ClearSummoningSickness();          // clear the default sick flag
        alice.Zones.Battlefield.AddCard(bear);
        bear.Tap();                             // IsTapped = true
        bear.TakeDamage(1);                     // Damage = 1
        bear.Counters.Add(CounterType.PlusOnePlusOne, 1); // one +1/+1 counter

        // Act: clone
        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);
        var cBear = (Creature)cAlice.Zones.Battlefield.GetCards().Single();

        // Assert: clone carries the board state
        cBear.IsTapped.Should().BeTrue();
        cBear.Damage.Should().Be(1);
        cBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        cBear.HasSummoningSickness.Should().BeFalse();

        // Independence: mutating the clone must not touch the original
        cBear.Untap();
        bear.IsTapped.Should().BeTrue("original must remain tapped after cloning");
    }

    [Fact]
    public void Clone_RelinksControllerAndAttachments_ToClones()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var aura = new Enchantment("Holy Strength", "{W}");
        bear.ChangeOwner(alice); aura.ChangeOwner(alice);
        bear.ChangeController(alice); aura.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        alice.Zones.Battlefield.AddCard(aura);
        aura.AttachTo(bear);

        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);
        var cBear = (Creature)cloned.CardMap[bear.InstanceId];
        var cAura = (Permanent)cloned.CardMap[aura.InstanceId];

        cBear.Controller.Should().BeSameAs(cAlice);           // points at CLONE player
        cBear.Owner.Should().BeSameAs(cAlice);
        cAura.AttachedTo.Should().BeSameAs(cBear);            // attachment remapped to clone
        cBear.Attachments.Should().ContainSingle().Which.Should().BeSameAs(cAura);
    }

    [Fact]
    public void Clone_PreservesRuntimeTypeForEachCardType()
    {
        // Arrange: one of each concrete card type in a zone.
        var alice = new Player("Alice", 20);

        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(creature);

        var land = new Land("Forest");
        land.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(land);

        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(artifact);

        var enchantment = new Enchantment("Glorious Anthem", "{1}{W}{W}");
        enchantment.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(enchantment);

        var planeswalker = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", startingLoyalty: 3);
        planeswalker.ChangeOwner(alice);
        alice.Zones.Battlefield.AddCard(planeswalker);

        var instant = new Instant("Lightning Bolt", "{R}");
        instant.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(instant);

        var sorcery = new Sorcery("Cultivate", "{2}{G}");
        sorcery.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(sorcery);

        // Act
        var cloned = GameStateCloner.Clone(new[] { alice });
        var cAlice = cloned.PlayerFor(alice);

        var bf = cAlice.Zones.Battlefield.GetCards().ToList();
        var gy = cAlice.Zones.Graveyard.GetCards().ToList();

        // Assert: type preservation
        bf.Should().ContainSingle(c => c.InstanceId == creature.InstanceId)
            .Which.Should().BeOfType<Creature>();
        bf.Should().ContainSingle(c => c.InstanceId == land.InstanceId)
            .Which.Should().BeOfType<Land>();
        bf.Should().ContainSingle(c => c.InstanceId == artifact.InstanceId)
            .Which.Should().BeOfType<Artifact>();
        bf.Should().ContainSingle(c => c.InstanceId == enchantment.InstanceId)
            .Which.Should().BeOfType<Enchantment>();
        bf.Should().ContainSingle(c => c.InstanceId == planeswalker.InstanceId)
            .Which.Should().BeOfType<Planeswalker>();
        gy.Should().ContainSingle(c => c.InstanceId == instant.InstanceId)
            .Which.Should().BeOfType<Instant>();
        gy.Should().ContainSingle(c => c.InstanceId == sorcery.InstanceId)
            .Which.Should().BeOfType<Sorcery>();
    }
}
