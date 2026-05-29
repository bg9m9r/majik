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
/// Tests for <see cref="WoodElvesFactory"/> — Creature — Elf Scout {2}{G} 1/1
/// (Tempest / many reprints). Oracle text (verified against Scryfall):
///   "When this creature enters, search your library for a Forest card,
///    put that card onto the battlefield, then shuffle."
///
/// Covers:
///   - Card identity (Creature, Elf Scout, 1/1, {2}{G}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single ETB triggered ability.
///   - Resolve: tutors a Forest card to the battlefield UNTAPPED (no "tapped"
///     qualifier), then shuffles.
///   - Resolve: a nonbasic land with the Forest subtype (dual) IS eligible.
///   - Resolve: a non-Forest land is NOT eligible.
///   - Resolve: zero Forests in library → no-op (legal under CR 701.19a).
/// </summary>
public class WoodElvesTests
{
    private readonly Player _alice = new("Alice", 20);

    // ───────────────────────────────────────────────────────────────────
    // Identity / dispatch
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void WoodElves_IsElfScout1_1_AtCost2G()
    {
        var card = WoodElvesFactory.Create(_alice);

        card.Name.Should().Be("Wood Elves");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        card.Power.Should().Be(1);
        card.Toughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WoodElves()
    {
        var card = NamedCardFactory.Create("Wood Elves", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Wood Elves");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{G}");
    }

    [Fact]
    public void WoodElves_HasExactlyOneTriggeredAbility()
    {
        var card = WoodElvesFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB trigger on Wood Elves.");
    }

    // ───────────────────────────────────────────────────────────────────
    // Resolve
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EtbTrigger_TutorsBasicForestToBattlefieldUntapped()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var card = WoodElvesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest,
            "the tutored Forest enters the battlefield");
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeFalse(
            "Wood Elves has no 'tapped' qualifier — the Forest enters untapped.");
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void EtbTrigger_NonbasicForest_IsEligible()
    {
        // A dual with the Forest land type (e.g. Stomping Ground) is a
        // "Forest card" per CR 305.6 even though it isn't basic.
        var dual = new Land("Stomping Ground",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Forest });
        dual.SetOwner(_alice);
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var card = WoodElvesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(dual,
            "a nonbasic land with the Forest subtype is a legal target (CR 305.6).");
    }

    [Fact]
    public void EtbTrigger_NonForestLand_IsNotEligible()
    {
        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var card = WoodElvesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Island has no Forest subtype; not a legal Wood Elves target.");
        _alice.Zones.Library.GetCards().Should().Contain(island);
    }

    [Fact]
    public void EtbTrigger_NoForestsInLibrary_IsNoOp()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bears);

        var card = WoodElvesFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "no Forests → no-op (CR 701.19a — finding nothing is legal).");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(bears);
    }
}
