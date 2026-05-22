using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FieryIsletFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Two pay-1-life mana abilities ({U} + {R}) with life-cost activation gate
/// - Life total reduction on mana ability activation
/// - {1}, {T}, Sacrifice: Draw a card activated ability shape
/// - Draw effect moves top library card to hand
/// </summary>
public class FieryIsletTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryIslet_IsLand()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void FieryIslet_NameIsCorrect()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Name.Should().Be("Fiery Islet");
    }

    [Fact]
    public void FieryIslet_OwnerAndControllerAreSet()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FieryIslet_IsNotLegendary()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}, Pay 1 life: Add {U} or {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryIslet_HasExactlyTwoManaAbilities()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {U} and one for {R}");
    }

    [Fact]
    public void FieryIslet_HasBlueManaAbility()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void FieryIslet_HasRedManaAbility()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void FieryIslet_ManaActivation_ReducesLifeByOne()
    {
        var alice = new Player("Alice", 20);
        var land = FieryIsletFactory.Create(alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.Activate();

        alice.LifeTotal.Should().Be(19,
            "{T}, Pay 1 life: Add {U} loses the controller 1 life on activation");
    }

    [Fact]
    public void FieryIslet_ManaActivation_TapsTheLand()
    {
        var alice = new Player("Alice", 20);
        var land = FieryIsletFactory.Create(alice);
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.Activate();

        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void FieryIslet_CannotActivateManaAt1Life()
    {
        // CR 119.4 — players can't pay more life than they have.
        var alice = new Player("Alice", 1);
        var land = FieryIsletFactory.Create(alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeFalse(
            "at 1 life, paying 1 life would drop life to 0 as part of the cost — illegal");
    }

    [Fact]
    public void FieryIslet_CannotActivateManaWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = FieryIsletFactory.Create(alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.Activate();

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);
        red.CanActivate().Should().BeFalse("the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Sac-draw activated ability — {1}, {T}, Sacrifice: Draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void FieryIslet_HasExactlyOneActivatedAbility()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void FieryIslet_SacDrawAbility_HasThreeCosts()
    {
        var land = FieryIsletFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3, "{1} + tap + sacrifice");
    }

    [Fact]
    public void FieryIslet_SacDrawAbility_HasManaCostOf1Generic()
    {
        var land = FieryIsletFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        mana.Generic.Should().Be(1);
    }

    [Fact]
    public void FieryIslet_SacDrawAbility_HasTapAndSacrificeCosts()
    {
        var land = FieryIsletFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice);
    }

    [Fact]
    public void FieryIslet_SacDrawEffect_MovesTopLibraryCardToHand()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var land = FieryIsletFactory.Create(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the draw effect moves the top library card to hand");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void FieryIslet_SacDrawEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);
        var land = FieryIsletFactory.Create(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("empty-library draw is a no-op; SBAs handle loss");
    }

    [Fact]
    public void FieryIslet_HasNoTriggeredAbilities()
    {
        var land = FieryIsletFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }
}
