using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Reliquary Tower (Conflux and many reprints).
///
/// Land. Oracle text (verified against Scryfall):
///   "You have no maximum hand size.
///    {T}: Add {C}."
///
/// Covers:
///   - Identity (Land, "Reliquary Tower", owner/controller).
///   - NamedCardFactory dispatch resolves the printed name.
///   - {T}: Add {C} mana ability is present (exactly one, producing one
///     colourless mana).
///   - No other (non-mana) activated abilities — Reliquary Tower's only
///     printed activated ability is the mana ability.
///
/// The static "You have no maximum hand size." rider (CR 402.2) is a documented
/// no-op against the current engine — there is no maximum-hand-size enforcement
/// for it to remove — so it is not asserted here (see
/// <see cref="ReliquaryTowerFactory"/>'s Deferred section).
/// </summary>
[Trait("Color", "C")]
public class ReliquaryTowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------ Identity

    [Fact]
    public void ReliquaryTower_Identity()
    {
        var land = ReliquaryTowerFactory.Create(_alice);

        land.Name.Should().Be("Reliquary Tower");
        land.HasType(CardType.Land).Should().BeTrue("Reliquary Tower is a Land (CR 305.1)");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ReliquaryTower_DispatchesThroughNamedCardFactory()
    {
        var land = (Land)NamedCardFactory.Create("Reliquary Tower", _alice);

        land.Name.Should().Be("Reliquary Tower");
        land.HasType(CardType.Land).Should().BeTrue();
    }

    // -------------------------------------------------------------- Mana ability

    [Fact]
    public void ReliquaryTower_HasColorlessManaAbility()
    {
        var land = ReliquaryTowerFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle("{T}: Add {C} is the single mana ability printed on Reliquary Tower")
            .Which.ManaGenerated.Generic.Should().Be(1, "{C} produces exactly one colourless mana");
    }

    [Fact]
    public void ReliquaryTower_HasNoOtherActivatedAbilities()
    {
        var land = ReliquaryTowerFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Reliquary Tower's only printed activated ability is the mana ability");
    }

    // ------------------------------------------------------------------ Null guard

    [Fact]
    public void ReliquaryTower_ThrowsOnNullOwner()
    {
        var act = () => ReliquaryTowerFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
