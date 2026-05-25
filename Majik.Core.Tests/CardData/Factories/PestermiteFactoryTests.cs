using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Pestermite (Lorwyn, {2}{U}).
///
/// Covers:
///   - Card shape (name, types, subtypes, P/T, mana cost).
///   - Flash + Flying keyword markers.
///   - ETB trigger structure (declares a 0..1 "target permanent" request,
///     printed "may" rider modeled by Min=0).
///   - Resolve-time behaviour: tap-or-untap deterministic "useful flip".
///   - NamedCardFactory dispatch.
/// </summary>
public class PestermiteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Pestermite_IsCreature_FaerieRogue_2_1_AtCost2U()
    {
        var p = PestermiteFactory.Create(_alice);

        p.Name.Should().Be("Pestermite");
        p.ManaCost.Should().Be("{2}{U}");
        p.HasType(CardType.Creature).Should().BeTrue();
        p.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        p.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        p.BasePower.Should().Be(2);
        p.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void Pestermite_HasFlashAndFlying()
    {
        var p = PestermiteFactory.Create(_alice);

        var keywords = p.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Pestermite_EtbTrigger_DeclaresOptionalTargetPermanent()
    {
        var p = PestermiteFactory.Create(_alice);

        var triggers = p.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0, "printed \"may\" tap-or-untap declines model as zero targets");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("permanent");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void Pestermite_Etb_UntapsTappedTargetPermanent()
    {
        var p = PestermiteFactory.Create(_alice);

        // Tapped target — Bob's Grizzly Bears
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);
        grizzly.Tap();

        var etb = p.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        foreach (var e in etb.Effects) e.Execute();

        grizzly.IsTapped.Should().BeFalse("Pestermite's deterministic \"useful flip\" untaps a tapped target");
    }

    [Fact]
    public void Pestermite_Etb_TapsUntappedTargetPermanent()
    {
        var p = PestermiteFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var etb = p.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        foreach (var e in etb.Effects) e.Execute();

        grizzly.IsTapped.Should().BeTrue("\"useful flip\" taps an untapped target");
    }

    [Fact]
    public void Pestermite_Etb_NoTargetChosen_NoOp()
    {
        var p = PestermiteFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var etb = p.Abilities.OfType<TriggeredAbility>().Single();
        // No targets supplied — printed "may" declined.

        foreach (var e in etb.Effects) e.Execute();

        grizzly.IsTapped.Should().BeFalse("untouched target retains its untapped state");
    }

    [Fact]
    public void Pestermite_Etb_TargetLeftBattlefield_NoOp()
    {
        var p = PestermiteFactory.Create(_alice);

        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Graveyard); // moved off battlefield before resolve
        _bob.Zones.Graveyard.AddCard(grizzly);

        var etb = p.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });

        foreach (var e in etb.Effects) e.Execute();

        // Card off battlefield should not be tapped/untapped — CR 608.2b.
        grizzly.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Pestermite_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Pestermite", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        ((Creature)card).BasePower.Should().Be(2);
    }
}
