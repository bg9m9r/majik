using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Terminus (Avacyn Restored, {4}{W}{W}, Sorcery).
///
/// Oracle: "Put all creatures on the bottom of their owners' libraries.
///          Miracle {W} (You may cast this card for its miracle cost when
///          you draw it if it's the first card you drew this turn.)"
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Miracle keyword marker present (CR 702.94) — alternative-cost
///     wiring is deferred, same posture as Reforge the Soul / Bonfire.
///   - Resolve tucks every creature on every supplied battlefield to the
///     BOTTOM of its OWNER's library (CR 701.x "put on the bottom of …
///     library" + CR 400.7 owner-relative destination).
///   - Creatures controlled by an opponent return to their OWNER's
///     library, not the controller's.
///   - Non-creature permanents (lands, enchantments, artifacts) are
///     untouched.
///   - Empty battlefield is a clean no-op.
/// </summary>
public class TerminusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Terminus_IsSorcery_At4WW()
    {
        var t = TerminusFactory.Create(_alice);

        t.Name.Should().Be("Terminus");
        t.ManaCost.Should().Be("{4}{W}{W}");
        t.HasType(CardType.Sorcery).Should().BeTrue();
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Terminus_CarriesMiracleKeywordMarker()
    {
        var t = TerminusFactory.Create(_alice);

        t.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Miracle", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Terminus has Miracle {W} (CR 702.94) surfaced as a keyword marker");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Terminus()
    {
        var card = NamedCardFactory.Create("Terminus", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Terminus");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{4}{W}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — tuck-to-bottom semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TucksCreaturesFromBothBattlefields_ToOwnerLibraryBottoms()
    {
        // Pre-seed a library card per player so "bottom" is observable
        // (the tucked creature must land BELOW the existing card).
        var aliceTop = SeedLibraryCard(_alice, "Alice-Existing");
        var bobTop = SeedLibraryCard(_bob, "Bob-Existing");

        var aliceCreatures = new[] { SeedCreature(_alice, "Alice-Bear"), SeedCreature(_alice, "Alice-Wolf") };
        var bobCreatures = new[] { SeedCreature(_bob, "Bob-Bear") };

        var effects = TerminusFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Battlefields cleared of creatures.
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        // Each tucked creature is in its OWNER's library, below the
        // pre-existing card (index 0 == top, last == bottom).
        var aliceLib = _alice.Zones.Library.GetCards().ToList();
        aliceLib.First().Should().BeSameAs(aliceTop, "the pre-existing card stays on top");
        aliceLib.Skip(1).Should().BeEquivalentTo(aliceCreatures, "tucked creatures sit on the bottom");
        aliceLib.Should().HaveCount(3);

        var bobLib = _bob.Zones.Library.GetCards().ToList();
        bobLib.First().Should().BeSameAs(bobTop);
        bobLib.Last().Should().BeSameAs(bobCreatures[0]);
        bobLib.Should().HaveCount(2);

        foreach (var c in aliceCreatures) c.Zone.Should().Be(ZoneType.Library);
        foreach (var c in bobCreatures) c.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_CreatureReturnsToOwnerLibrary_NotControllerLibrary()
    {
        // Alice controls a creature OWNED by Bob (e.g. via a steal effect).
        // Terminus puts it on the bottom of BOB's library (CR 400.7 —
        // owner-relative destination), not Alice's.
        var stolen = new Creature("Bob-Stolen-Ogre", "", power: 3, toughness: 3);
        stolen.SetOwner(_bob);
        stolen.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        var effects = TerminusFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty("the creature is owned by Bob");
        _bob.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(stolen);
        stolen.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_LeavesNonCreaturePermanentsAlone()
    {
        var aliceCreature = SeedCreature(_alice, "Alice-Bear");
        var aliceLand = SeedLand(_alice, "Alice-Plains");
        var aliceEnchantment = SeedEnchantment(_alice, "Alice-Aura");
        var aliceArtifact = SeedArtifact(_alice, "Alice-Sol-Ring");

        var effects = TerminusFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // Creature tucked; everything else stays on the battlefield.
        _alice.Zones.Battlefield.GetCards().Should().BeEquivalentTo(
            new ICard[] { aliceLand, aliceEnchantment, aliceArtifact });
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(aliceCreature);
        aliceCreature.Zone.Should().Be(ZoneType.Library);
        aliceLand.Zone.Should().Be(ZoneType.Battlefield);
        aliceEnchantment.Zone.Should().Be(ZoneType.Battlefield);
        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = TerminusFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().BeEmpty();
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

    private static ICard SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "", power: 1, toughness: 1);
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
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
