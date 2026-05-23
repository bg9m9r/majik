using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Damnation (Planar Chaos, {2}{B}{B}, Sorcery).
///
/// Oracle: "Destroy all creatures. They can't be regenerated."
/// Functional reprint of Wrath of God.
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Sweep destroys every creature on every supplied player's
///     battlefield → owner's graveyard (CR 701.7).
///   - Non-creature permanents survive the sweep.
/// </summary>
public class DamnationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Damnation_IsSorcery_At2BB()
    {
        var d = DamnationFactory.Create(_alice);

        d.Name.Should().Be("Damnation");
        d.ManaCost.Should().Be("{2}{B}{B}");
        d.HasType(CardType.Sorcery).Should().BeTrue();
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Damnation()
    {
        var card = NamedCardFactory.Create("Damnation", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Damnation");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysCreaturesOnBothBattlefields_ToOwnerGraveyards()
    {
        var aliceCreatures = new[] { SeedCreature(_alice, "Alice-Zombie"), SeedCreature(_alice, "Alice-Imp") };
        var bobCreatures = new[] { SeedCreature(_bob, "Bob-Zombie"), SeedCreature(_bob, "Bob-Imp") };

        var effects = DamnationFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceCreatures);
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobCreatures);

        foreach (var c in aliceCreatures) c.Zone.Should().Be(ZoneType.Graveyard);
        foreach (var c in bobCreatures) c.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_LeavesNonCreaturePermanentsAlone()
    {
        var aliceCreature = SeedCreature(_alice, "Alice-Zombie");
        var aliceLand = SeedLand(_alice, "Alice-Swamp");
        var aliceEnchantment = SeedEnchantment(_alice, "Alice-Curse");
        var aliceArtifact = SeedArtifact(_alice, "Alice-Mox");

        var effects = DamnationFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceLand, aliceEnchantment, aliceArtifact });
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new[] { aliceCreature });
        aliceCreature.Zone.Should().Be(ZoneType.Graveyard);
        aliceLand.Zone.Should().Be(ZoneType.Battlefield);
        aliceEnchantment.Zone.Should().Be(ZoneType.Battlefield);
        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_FunctionalReprintOfWrathOfGod()
    {
        // Seed identical board state and verify Damnation and Wrath of God
        // produce the same end state — the two factories must remain
        // observationally identical (functional reprint).
        var damAliceCreatures = new[] { SeedCreature(_alice, "Bear-1"), SeedCreature(_alice, "Bear-2") };
        var damBobCreatures = new[] { SeedCreature(_bob, "Wolf-1") };

        DamnationFactory.BuildResolveEffect(new[] { _alice, _bob })
            .ToList().ForEach(e => e.Execute());

        var damAliceGy = _alice.Zones.Graveyard.GetCards().ToList();
        var damBobGy = _bob.Zones.Graveyard.GetCards().ToList();

        // Reset to a parallel-board fresh pair of players for Wrath.
        var wAlice = new Player("Alice", 20);
        var wBob = new Player("Bob", 20);
        var wAliceCreatures = new[] { SeedCreature(wAlice, "Bear-1"), SeedCreature(wAlice, "Bear-2") };
        var wBobCreatures = new[] { SeedCreature(wBob, "Wolf-1") };

        WrathOfGodFactory.BuildResolveEffect(new[] { wAlice, wBob })
            .ToList().ForEach(e => e.Execute());

        wAlice.Zones.Graveyard.GetCards().Select(c => c.Name).Should()
            .BeEquivalentTo(damAliceGy.Select(c => c.Name));
        wBob.Zones.Graveyard.GetCards().Select(c => c.Name).Should()
            .BeEquivalentTo(damBobGy.Select(c => c.Name));
        wAlice.Zones.Battlefield.GetCards().Should().BeEmpty();
        wBob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Land SeedLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        owner.Zones.Battlefield.AddCard(l);
        l.SetZone(ZoneType.Battlefield);
        return l;
    }

    private static Enchantment SeedEnchantment(Player owner, string name)
    {
        var e = new Enchantment(name, "");
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
    }

    private static Artifact SeedArtifact(Player owner, string name)
    {
        var a = new Artifact(name, "");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
