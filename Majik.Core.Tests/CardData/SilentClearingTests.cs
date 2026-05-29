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
/// Unit tests for "Silent Clearing" via <see cref="HorizonLandCycleFactory"/>.
///
/// Oracle text (verified against Scryfall, 2026-05-29):
///   {T}, Pay 1 life: Add {W} or {B}.
///   {1}, {T}, Sacrifice this land: Draw a card.
///
/// Silent Clearing is the W/B member of the Modern Horizons painless-dual
/// "Horizon Canopy" cycle; it shares the cycle's two ability shapes and
/// differs only in the colour pair it produces.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Two pay-1-life mana abilities ({W} + {B}) with life-cost activation gate
/// - Life total reduction + tap on mana ability activation
/// - {1}, {T}, Sacrifice: Draw a card activated ability shape
/// - Draw effect moves top library card to hand
/// </summary>
public class SilentClearingTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Clearing(Player owner) =>
        HorizonLandCycleFactory.Create(owner, new[] { "Silent Clearing", "W", "B" });

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SilentClearing_IsLand()
    {
        Clearing(_alice).HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SilentClearing_NameIsCorrect()
    {
        Clearing(_alice).Name.Should().Be("Silent Clearing");
    }

    [Fact]
    public void SilentClearing_OwnerAndControllerAreSet()
    {
        var land = Clearing(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SilentClearing_IsNotLegendary()
    {
        Clearing(_alice).HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — {T}, Pay 1 life: Add {W} or {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void SilentClearing_HasExactlyTwoManaAbilities()
    {
        Clearing(_alice).Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {W} and one for {B}");
    }

    [Fact]
    public void SilentClearing_HasWhiteManaAbility()
    {
        Clearing(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void SilentClearing_HasBlackManaAbility()
    {
        Clearing(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void SilentClearing_ManaActivation_ReducesLifeByOne()
    {
        var alice = new Player("Alice", 20);
        var land = HorizonLandCycleFactory.Create(alice, new[] { "Silent Clearing", "W", "B" });
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.Activate();

        alice.LifeTotal.Should().Be(19,
            "{T}, Pay 1 life: Add {W} loses the controller 1 life on activation");
    }

    [Fact]
    public void SilentClearing_ManaActivation_TapsTheLand()
    {
        var alice = new Player("Alice", 20);
        var land = HorizonLandCycleFactory.Create(alice, new[] { "Silent Clearing", "W", "B" });
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.Activate();

        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SilentClearing_CannotActivateManaAt1Life()
    {
        // CR 119.4 — players can't pay more life than they have.
        var alice = new Player("Alice", 1);
        var land = HorizonLandCycleFactory.Create(alice, new[] { "Silent Clearing", "W", "B" });
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "at 1 life, paying 1 life would drop life to 0 as part of the cost — illegal");
    }

    [Fact]
    public void SilentClearing_CannotActivateManaWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = HorizonLandCycleFactory.Create(alice, new[] { "Silent Clearing", "W", "B" });
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.Activate();

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);
        black.CanActivate().Should().BeFalse("the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Sac-draw activated ability — {1}, {T}, Sacrifice: Draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void SilentClearing_HasExactlyOneActivatedAbility()
    {
        Clearing(_alice).Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SilentClearing_SacDrawAbility_HasThreeCosts()
    {
        var ability = Clearing(_alice).Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3, "{1} + tap + sacrifice");
    }

    [Fact]
    public void SilentClearing_SacDrawAbility_HasManaCostOf1Generic()
    {
        var ability = Clearing(_alice).Abilities.OfType<ActivatedAbility>().Single();
        var mana = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        mana.Generic.Should().Be(1);
    }

    [Fact]
    public void SilentClearing_SacDrawAbility_HasTapAndSacrificeCosts()
    {
        var ability = Clearing(_alice).Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice);
    }

    [Fact]
    public void SilentClearing_SacDrawEffect_MovesTopLibraryCardToHand()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var land = HorizonLandCycleFactory.Create(alice, new[] { "Silent Clearing", "W", "B" });
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "the draw effect moves the top library card to hand");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void SilentClearing_SacDrawEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);
        var land = HorizonLandCycleFactory.Create(alice, new[] { "Silent Clearing", "W", "B" });
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("empty-library draw is a no-op; SBAs handle loss");
    }

    [Fact]
    public void SilentClearing_HasNoTriggeredAbilities()
    {
        Clearing(_alice).Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }
}
