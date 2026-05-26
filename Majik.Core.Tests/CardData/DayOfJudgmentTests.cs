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
/// Tests for Day of Judgment (Magic 2010, {2}{W}{W}, Sorcery).
///
/// Oracle: "Destroy all creatures."
///
/// Coverage:
///   - Identity + NamedCardFactory dispatch.
///   - Sweep destroys every creature on every supplied battlefield —
///     each creature lands in its owner's graveyard (CR 701.7).
///   - Non-creature permanents (lands, enchantments, artifacts) survive
///     the sweep.
///   - Distinct from Wrath of God: Day of Judgment uses
///     <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (regen
///     honoured) rather than <c>DestroyNoRegeneration</c>.
/// </summary>
public class DayOfJudgmentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DayOfJudgment_IsSorcery_At2WW()
    {
        var w = DayOfJudgmentFactory.Create(_alice);

        w.Name.Should().Be("Day of Judgment");
        w.ManaCost.Should().Be("{2}{W}{W}");
        w.HasType(CardType.Sorcery).Should().BeTrue();
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DayOfJudgment()
    {
        var card = NamedCardFactory.Create("Day of Judgment", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Day of Judgment");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — sweep semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysCreaturesOnBothBattlefields_ToOwnerGraveyards()
    {
        var aliceCreatures = new[] { SeedCreature(_alice, "Alice-Bear"), SeedCreature(_alice, "Alice-Wolf") };
        var bobCreatures = new[] { SeedCreature(_bob, "Bob-Bear"), SeedCreature(_bob, "Bob-Wolf") };

        var effects = DayOfJudgmentFactory.BuildResolveEffect(new[] { _alice, _bob });
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
        var aliceCreature = SeedCreature(_alice, "Alice-Bear");
        var aliceLand = SeedLand(_alice, "Alice-Plains");
        var aliceEnchantment = SeedEnchantment(_alice, "Alice-Aura");
        var aliceArtifact = SeedArtifact(_alice, "Alice-Sol-Ring");

        var effects = DayOfJudgmentFactory.BuildResolveEffect(new[] { _alice, _bob });
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
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = DayOfJudgmentFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
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
