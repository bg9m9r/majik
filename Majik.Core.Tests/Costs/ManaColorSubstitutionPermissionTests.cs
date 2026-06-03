using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for the reusable payment-time mana-colour-substitution primitive
/// (CR 609.4b — "you may spend mana as though it were mana of any color"):
/// <see cref="ManaColorSubstitutionPermission"/> static ability +
/// <see cref="ManaColorSubstitutableManaCost"/> cost.
///
/// This generalizes Robber of the Rich's one-off exile-cast clause into a
/// reusable static permission that the mana-payment path consults, so cards
/// like Agatha's Soul Cauldron ("you may spend mana as though it were mana of
/// any color to activate abilities of creatures you control") can consume it.
/// </summary>
public class ManaColorSubstitutionPermissionTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // ManaColorSubstitutionPermission static ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Permission_ExposesPurpose_AndIsActiveOnBattlefield()
    {
        var source = new Artifact("Cauldron", "{2}");
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);

        var permission = new ManaColorSubstitutionPermission(
            source, _alice, ManaSpendPurpose.ActivateCreatureAbilities);

        permission.Purpose.Should().Be(ManaSpendPurpose.ActivateCreatureAbilities);
        permission.IsActive().Should().BeTrue("the source artifact is on the battlefield");
    }

    [Fact]
    public void Permission_IsInactive_WhenSourceNotOnBattlefield()
    {
        var source = new Artifact("Cauldron", "{2}");
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Hand);

        var permission = new ManaColorSubstitutionPermission(
            source, _alice, ManaSpendPurpose.ActivateCreatureAbilities);

        permission.IsActive().Should().BeFalse("a static ability only applies from the battlefield (CR 604.1)");
    }

    // -----------------------------------------------------------------------
    // Player-level query helper
    // -----------------------------------------------------------------------

    [Fact]
    public void Query_ReturnsTrue_WhenAnActivePermissionGrantsThePurpose()
    {
        var source = new Artifact("Cauldron", "{2}");
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        source.AddAbility(new ManaColorSubstitutionPermission(
            source, _alice, ManaSpendPurpose.ActivateCreatureAbilities));
        _alice.Zones.Battlefield.AddCard(source);

        ManaColorSubstitutionPermission
            .PlayerMaySpendAnyColorFor(_alice, ManaSpendPurpose.ActivateCreatureAbilities)
            .Should().BeTrue();
    }

    [Fact]
    public void Query_ReturnsFalse_ForAMismatchedPurpose()
    {
        var source = new Artifact("Cauldron", "{2}");
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        source.AddAbility(new ManaColorSubstitutionPermission(
            source, _alice, ManaSpendPurpose.ActivateCreatureAbilities));
        _alice.Zones.Battlefield.AddCard(source);

        ManaColorSubstitutionPermission
            .PlayerMaySpendAnyColorFor(_alice, ManaSpendPurpose.CastSpells)
            .Should().BeFalse("the permission only widens the colour requirement for its declared purpose");
    }

    [Fact]
    public void Query_ReturnsFalse_WhenSourceHasLeftTheBattlefield()
    {
        var source = new Artifact("Cauldron", "{2}");
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.AddAbility(new ManaColorSubstitutionPermission(
            source, _alice, ManaSpendPurpose.ActivateCreatureAbilities));
        // Source is in hand, not on the battlefield.
        source.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(source);

        ManaColorSubstitutionPermission
            .PlayerMaySpendAnyColorFor(_alice, ManaSpendPurpose.ActivateCreatureAbilities)
            .Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ManaColorSubstitutableManaCost — honours the permission at payment time
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_WithoutPermission_RequiresTheExactColour()
    {
        // Off-colour mana floating: 1 red. Cost is {G}.
        _alice.AddManaToPool(ManaCost.Parse("R"));
        var cost = new ManaColorSubstitutableManaCost(
            ManaCost.Parse("G"), _alice, ManaSpendPurpose.ActivateCreatureAbilities);

        cost.CanPay(_alice).Should().BeFalse(
            "without an active substitution permission, a green pip needs green mana");
    }

    [Fact]
    public void Cost_WithActivePermission_AcceptsAnyColour()
    {
        // Off-colour mana floating: 1 red. Cost is {G}.
        _alice.AddManaToPool(ManaCost.Parse("R"));

        var source = new Artifact("Cauldron", "{2}");
        source.SetOwner(_alice);
        source.SetController(_alice);
        source.SetZone(ZoneType.Battlefield);
        source.AddAbility(new ManaColorSubstitutionPermission(
            source, _alice, ManaSpendPurpose.ActivateCreatureAbilities));
        _alice.Zones.Battlefield.AddCard(source);

        var cost = new ManaColorSubstitutableManaCost(
            ManaCost.Parse("G"), _alice, ManaSpendPurpose.ActivateCreatureAbilities);

        cost.CanPay(_alice).Should().BeTrue(
            "the permission folds the green pip to generic so red mana qualifies (CR 609.4b)");

        cost.Pay(_alice);

        _alice.ManaPool.Total.Should().Be(0,
            "the red mana was spent on the {G} pip via the colour-substitution permission");
    }
}
