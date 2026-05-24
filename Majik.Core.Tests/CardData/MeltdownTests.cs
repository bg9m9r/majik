using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Meltdown (Urza's Destiny, {X}{R}, Sorcery).
///
/// Oracle: "Destroy each artifact with mana value X or less."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - X = 2 sweep destroys mv-0, mv-1, mv-2 artifacts; leaves mv-3+
///     artifacts alone (CR 202.3b mv compare).
///   - Non-artifacts (creatures / enchantments / lands) are unaffected
///     regardless of mv (CR 701.7 + printed predicate is Artifact-only).
///   - Multi-player iteration: artifacts on every supplied battlefield
///     are swept.
///   - Indestructible rider deferred — same lossy MoveToGraveyard path
///     as Slaughter Pact and the rest of the destroy family.
///   - Dispatcher entry produces a Sorcery shape.
/// </summary>
public class MeltdownTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Meltdown_IsSorcery_AtXR()
    {
        var m = MeltdownFactory.Create(_alice);

        m.Name.Should().Be("Meltdown");
        m.ManaCost.Should().Be("{X}{R}");
        m.HasType(CardType.Sorcery).Should().BeTrue();
        m.Owner.Should().BeSameAs(_alice);
        m.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Meltdown()
    {
        var card = NamedCardFactory.Create("Meltdown", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Meltdown");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{X}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_X2_DestroysArtifactsWithMvLeq2()
    {
        // Alice: mv-0 bauble, mv-1 artifact, mv-2 artifact (all destroyed).
        var bauble = SeedArtifact(_alice, "Mishra's Bauble", "0");
        var moxlike = SeedArtifact(_alice, "1-Artifact", "1");
        var twoArt = SeedArtifact(_alice, "2-Artifact", "2");

        var effects = MeltdownFactory.BuildResolveEffect(
            _alice,
            new[] { _alice, _bob },
            x: 2);
        foreach (var e in effects) e.Execute();

        bauble.Zone.Should().Be(ZoneType.Graveyard, "mv-0 artifact destroyed at X=2");
        moxlike.Zone.Should().Be(ZoneType.Graveyard, "mv-1 artifact destroyed at X=2");
        twoArt.Zone.Should().Be(ZoneType.Graveyard, "mv-2 artifact destroyed at X=2");

        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new ICard[] { bauble, moxlike, twoArt });
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_X2_LeavesMv3PlusArtifactsAlone()
    {
        var bauble = SeedArtifact(_alice, "Mishra's Bauble", "0");
        var threeArt = SeedArtifact(_alice, "3-Artifact", "3");
        var fourArt = SeedArtifact(_alice, "4-Artifact", "4");

        var effects = MeltdownFactory.BuildResolveEffect(
            _alice,
            new[] { _alice, _bob },
            x: 2);
        foreach (var e in effects) e.Execute();

        bauble.Zone.Should().Be(ZoneType.Graveyard, "mv-0 ≤ 2, destroyed");
        threeArt.Zone.Should().Be(ZoneType.Battlefield, "mv-3 > 2, survives");
        fourArt.Zone.Should().Be(ZoneType.Battlefield, "mv-4 > 2, survives");

        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(new ICard[] { threeArt, fourArt });
    }

    [Fact]
    public void Resolve_X2_DoesNotTouchNonArtifacts()
    {
        // Non-artifact permanents (creature, enchantment, land) — all
        // mv-≤-2 to confirm the type filter rejects them, not the mv.
        var bear = SeedCreature(_alice, "Grizzly Bears", "1G");      // mv-2
        var aura = SeedEnchantment(_alice, "Some Aura", "1B");       // mv-2
        var mountain = SeedLand(_alice, "Mountain");                  // mv-0
        var aliceArt = SeedArtifact(_alice, "Mishra's Bauble", "0");  // mv-0 sanity

        var effects = MeltdownFactory.BuildResolveEffect(
            _alice,
            new[] { _alice, _bob },
            x: 2);
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield, "creature is not Artifact");
        aura.Zone.Should().Be(ZoneType.Battlefield, "enchantment is not Artifact");
        mountain.Zone.Should().Be(ZoneType.Battlefield, "land is not Artifact");
        aliceArt.Zone.Should().Be(ZoneType.Graveyard, "sanity — artifact still swept");

        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { bear, aura, mountain });
    }

    [Fact]
    public void Resolve_MultiPlayer_SweepsArtifactsOnEveryBattlefield()
    {
        // Alice + Bob each carry artifacts; both should be swept.
        var aliceArt = SeedArtifact(_alice, "Alice-Bauble", "0");
        var bobArt = SeedArtifact(_bob, "Bob-Bauble", "1");

        var effects = MeltdownFactory.BuildResolveEffect(
            _alice,
            new[] { _alice, _bob },
            x: 2);
        foreach (var e in effects) e.Execute();

        aliceArt.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceArt);

        bobArt.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobArt);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Artifact SeedArtifact(Player owner, string name, string cost)
    {
        var a = new Artifact(name, cost);
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    private static Creature SeedCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Enchantment SeedEnchantment(Player owner, string name, string cost)
    {
        var e = new Enchantment(name, cost);
        e.SetOwner(owner);
        e.SetController(owner);
        owner.Zones.Battlefield.AddCard(e);
        e.SetZone(ZoneType.Battlefield);
        return e;
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
}
