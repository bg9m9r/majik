using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpymastersVaultFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Owner and controller assignment
/// - Single {B} mana ability present
/// - No triggered or non-mana activated abilities wired in v1
/// </summary>
public class SpymastersVaultTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SpymastersVault_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SpymastersVault_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Name.Should().Be("Spymaster's Vault");
    }

    [Fact]
    public void SpymastersVault_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SpymastersVault_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana ability — {T}: Add {B}
    // -----------------------------------------------------------------------

    [Fact]
    public void SpymastersVault_HasExactlyOneManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "only {T}: Add {B} is wired in v1");
    }

    [Fact]
    public void SpymastersVault_ManaAbility_ProducesBlack()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Black.Should().Be(1, "Spymaster's Vault taps for exactly one {B}");
    }

    [Fact]
    public void SpymastersVault_ManaAbility_ProducesOnlyBlack()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Generic.Should().Be(0);
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void SpymastersVault_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB and connive triggers are deferred in v1");
    }

    [Fact]
    public void SpymastersVault_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Spymaster's Vault", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "connive activated ability is deferred in v1");
    }
}
