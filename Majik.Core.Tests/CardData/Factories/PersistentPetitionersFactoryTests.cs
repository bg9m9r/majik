using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PersistentPetitionersFactory"/> — Creature — Human
/// Advisor {1}{U} 1/3 (Scryfall-verified 2026-06-02):
///   "{1}, {T}: Target player mills a card.
///    Tap four untapped Advisors you control: Target player mills twelve cards.
///    A deck can have any number of cards named Persistent Petitioners."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, color, owner /
///     controller) materialised from the embedded JSON definition.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Two activated abilities attached: the {1},{T} mill-1 and the
///     tap-four-Advisors mill-12 (<see cref="PersistentPetitionersMillTwelveAbility"/>).
///   - {1},{T} ability costs ({1} mana + self-tap) and a target-player request;
///     resolution mills 1 to the chosen player (CR 701.13).
///   - tap-four-Advisors cost gate (CR 602.2b): false with fewer than four
///     untapped Advisors, true with four; Pay taps four Advisors.
///   - No summoning-sickness gate on the tap-four cost (CR 302.6 N/A — the cost
///     is the word "Tap", not a {T} symbol).
///   - tap-four resolution mills 12 to the chosen player.
///
/// The "any number of cards named Persistent Petitioners" deck-construction
/// rider is N/A (deck legality is outside per-card engine scope) — see the
/// factory XML doc.
/// </summary>
[Trait("Color", "U")]
public class PersistentPetitionersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature AddAdvisor(Player owner, string name)
    {
        var a = new Creature(name, "{1}{U}", 1, 3,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Advisor });
        a.SetOwner(owner);
        a.SetController(owner);
        a.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(a);
        return a;
    }

    private static void SeedLibrary(Player owner, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(owner);
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Petitioners_Identity_HumanAdvisor_1_3_BlueU()
    {
        var c = PersistentPetitionersFactory.Create(_alice);

        c.Name.Should().Be("Persistent Petitioners");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Petitioners()
    {
        var card = NamedCardFactory.Create("Persistent Petitioners", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Persistent Petitioners");
        card.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
        card.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Petitioners_HasTwoActivatedAbilities()
    {
        var c = PersistentPetitionersFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "Persistent Petitioners prints two activated abilities: "
            + "the {1},{T} mill-1 and the tap-four-Advisors mill-12.");
        c.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // First ability: {1}, {T}: Target player mills a card. (CR 602 / 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void MillOneAbility_HasManaPlusTapCost_AndTargetPlayerRequest()
    {
        var c = PersistentPetitionersFactory.Create(_alice);

        var millOne = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not PersistentPetitionersMillTwelveAbility);

        millOne.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        millOne.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(ac => ac.CostType == AdditionalCostType.Tap);

        millOne.TargetRequests.Should().HaveCount(1);
        millOne.TargetRequests[0].Description.Should().Be("target player");
        millOne.TargetRequests[0].MinTargets.Should().Be(1);
        millOne.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void MillOneAbility_Resolve_MillsOneToChosenPlayer()
    {
        var c = PersistentPetitionersFactory.Create(_alice);
        SeedLibrary(_bob, 5);

        var millOne = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not PersistentPetitionersMillTwelveAbility);
        millOne.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        millOne.Resolve();

        _bob.Zones.Graveyard.Count.Should().Be(PersistentPetitionersFactory.MillOneCount);
        _bob.Zones.Library.Count.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // Second ability: Tap four untapped Advisors you control. (CR 602.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TapFourCost_CannotPay_WithFewerThanFourUntappedAdvisors()
    {
        var p = PersistentPetitionersFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(p);
        AddAdvisor(_alice, "Advisor 2");
        AddAdvisor(_alice, "Advisor 3");
        // Petitioners + 2 = three untapped Advisors, one short.

        var ability = p.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Single();
        ability.TapChoice.CanPay(_alice).Should().BeFalse(
            "the cost requires four untapped Advisors; only three exist.");
    }

    [Fact]
    public void TapFourCost_CanPay_WithFourAdvisors_DespiteSummoningSickness()
    {
        // CR 302.6 only restricts a creature tapping ITSELF via a {T} symbol.
        // This cost is the printed word "Tap" on a set of Advisors — so
        // summoning-sick Advisors are still eligible bodies.
        var p = PersistentPetitionersFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(p);
        AddAdvisor(_alice, "Advisor 2");
        AddAdvisor(_alice, "Advisor 3");
        AddAdvisor(_alice, "Advisor 4");

        var ability = p.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Single();
        ability.TapChoice.CanPay(_alice).Should().BeTrue(
            "four untapped Advisors (Petitioners + three) exist; not gated on summoning sickness.");
    }

    [Fact]
    public void TapFourCost_Pay_TapsFourChosenAdvisors()
    {
        var p = PersistentPetitionersFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(p);
        var a2 = AddAdvisor(_alice, "Advisor 2");
        var a3 = AddAdvisor(_alice, "Advisor 3");
        var a4 = AddAdvisor(_alice, "Advisor 4");

        var ability = p.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Single();
        ability.TapChoice.Targets = new[] { p, a2, a3, a4 };
        ability.TapChoice.Pay(_alice);

        p.IsTapped.Should().BeTrue();
        a2.IsTapped.Should().BeTrue();
        a3.IsTapped.Should().BeTrue();
        a4.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void MillTwelveAbility_Resolve_MillsTwelveToChosenPlayer()
    {
        var p = PersistentPetitionersFactory.Create(_alice);
        SeedLibrary(_bob, 20);

        var ability = p.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        ability.Resolve();

        _bob.Zones.Graveyard.Count.Should().Be(PersistentPetitionersFactory.MillTwelveCount);
        _bob.Zones.Library.Count.Should().Be(8);
    }

    [Fact]
    public void MillTwelveAbility_Resolve_MillsAllRemaining_WhenLibraryShort()
    {
        // CR 701.13a — milling more than the library holds mills all remaining
        // cards (no loss-of-game from this alone).
        var p = PersistentPetitionersFactory.Create(_alice);
        SeedLibrary(_bob, 5);

        var ability = p.Abilities.OfType<PersistentPetitionersMillTwelveAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        ability.Resolve();

        _bob.Zones.Graveyard.Count.Should().Be(5);
        _bob.Zones.Library.Count.Should().Be(0);
    }
}
