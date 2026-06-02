using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Etali, Primal Storm (Rivals of Ixalan, {4}{R}, Legendary
/// Creature — Elder Dinosaur 6/6).
///
/// Covers:
/// - Identity (name, type, cost, P/T, supertype, subtypes).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Attack trigger condition matches CreatureAttacksEvent for Etali only
///   (CR 508.1f per-attacker self-match).
/// - <see cref="EtaliPrimalStormFactory.ResolveAttack"/> exiles the top
///   card of each player's library and reports it via
///   <see cref="EtaliPrimalStormFactory.Result"/>.
/// - Eligible nonland cards in the exile pile are reported as castable.
/// - Empty libraries are skipped without throwing.
/// - Picker callback receives the eligible pile and the returned subset
///   appears in <see cref="EtaliPrimalStormFactory.Result.Picked"/>.
/// </summary>
[Trait("Color", "R")]
public class EtaliPrimalStormFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    [Fact]
    public void Identity_NameTypeCostPT()
    {
        var c = EtaliPrimalStormFactory.Create(_alice);

        c.Name.Should().Be("Etali, Primal Storm");
        c.ManaCost.Should().Be("{4}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elder).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void HasOneAttackTriggeredAbility()
    {
        var c = EtaliPrimalStormFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Etali prints one triggered ability — its attack clause.");
    }

    [Fact]
    public void AttackTrigger_MatchesSelfOnly()
    {
        var etali = EtaliPrimalStormFactory.Create(_alice);
        etali.SetZone(ZoneType.Battlefield);
        var trigger = GetAttackTrigger(etali);

        trigger.IsTriggered(new CreatureAttacksEvent(etali, _bob)).Should().BeTrue(
            "Etali's own attack matches (CR 508.1f per-attacker self-match).");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the attack trigger does not fire when another creature attacks.");
    }

    [Fact]
    public void ResolveAttack_ExilesTopOfEachPlayersLibrary()
    {
        // Alice library: Lightning Bolt (Instant), Mountain (Land), Soul Warden
        // Bob library: Counterspell (Instant), Island (Land)
        var aBolt = new Instant("Lightning Bolt", "{R}"); aBolt.SetOwner(_alice);
        var aMountain = NamedCardFactory.Create("Mountain", _alice);
        var aWarden = new Creature("Soul Warden", "{W}", 1, 1); aWarden.SetOwner(_alice);

        var bCS = new Instant("Counterspell", "{U}{U}"); bCS.SetOwner(_bob);
        var bIsland = NamedCardFactory.Create("Island", _bob);

        foreach (var c in new ICard[] { aBolt, aMountain, aWarden })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
        foreach (var c in new ICard[] { bCS, bIsland })
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = EtaliPrimalStormFactory.ResolveAttack(
            controller: _alice,
            allPlayersResolver: () => new[] { _alice, _bob });

        result.Exiled.Should().HaveCount(2,
            "one card exiled per player with a non-empty library.");
        result.Exiled.Select(c => c.Name).Should().Contain(new[] { "Lightning Bolt", "Counterspell" });

        // Both exiled cards are nonland → both eligible.
        result.Eligible.Should().HaveCount(2);
        result.Eligible.Select(c => c.Name).Should().Contain(new[] { "Lightning Bolt", "Counterspell" });

        // Default picker = pile (accept all).
        result.Picked.Should().HaveCount(2);

        _alice.Zones.Exile.GetCards().Should().Contain(aBolt,
            "Alice's card lands in Alice's exile (owner-keyed per-player exile mirrors the shared MTG exile zone, CR 406.1).");
        _bob.Zones.Exile.GetCards().Should().Contain(bCS,
            "Bob's card lands in Bob's exile.");
    }

    [Fact]
    public void ResolveAttack_LandsAreNotEligible()
    {
        // Both players' top card is a land — exile pile has 2 cards, 0 eligible.
        var aMountain = NamedCardFactory.Create("Mountain", _alice);
        var bIsland = NamedCardFactory.Create("Island", _bob);

        _alice.Zones.Library.AddCard(aMountain); aMountain.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bIsland); bIsland.SetZone(ZoneType.Library);

        var result = EtaliPrimalStormFactory.ResolveAttack(
            controller: _alice,
            allPlayersResolver: () => new[] { _alice, _bob });

        result.Exiled.Should().HaveCount(2);
        result.Eligible.Should().BeEmpty(
            "Lands are not eligible nonland cards (CR 305.1).");
        result.Picked.Should().BeEmpty();
    }

    [Fact]
    public void ResolveAttack_EmptyLibrary_IsSkipped()
    {
        // Alice library: 1 card. Bob library: empty.
        var aBolt = new Instant("Lightning Bolt", "{R}"); aBolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(aBolt); aBolt.SetZone(ZoneType.Library);

        var result = EtaliPrimalStormFactory.ResolveAttack(
            controller: _alice,
            allPlayersResolver: () => new[] { _alice, _bob });

        result.Exiled.Should().HaveCount(1,
            "only Alice's library produces an exile — Bob's is empty.");
        result.Eligible.Should().HaveCount(1);
    }

    [Fact]
    public void ResolveAttack_PickerSelectsSubset()
    {
        // Two eligible spells; picker selects only the first.
        var aBolt = new Instant("Lightning Bolt", "{R}"); aBolt.SetOwner(_alice);
        var bCS = new Instant("Counterspell", "{U}{U}"); bCS.SetOwner(_bob);

        _alice.Zones.Library.AddCard(aBolt); aBolt.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(bCS); bCS.SetZone(ZoneType.Library);

        var result = EtaliPrimalStormFactory.ResolveAttack(
            controller: _alice,
            allPlayersResolver: () => new[] { _alice, _bob },
            chooseSpells: pile => new[] { pile[0] });

        result.Eligible.Should().HaveCount(2);
        result.Picked.Should().HaveCount(1);
        result.Picked[0].Should().BeSameAs(result.Eligible[0]);
    }
}
