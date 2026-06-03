using System.Linq;
using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Per-card functional tests for the attack-tax paywall enchantments
/// (CR 508.1g — Ghostly Prison / Propaganda / Sphere of Safety). Each builds
/// through its <see cref="CardName"/> factory and registers a
/// <see cref="PayPerAttackerRestriction"/> on the per-game registry that
/// protects its controller.
/// </summary>
public class AttackTaxPaywallFactoryTests
{
    private readonly Player _owner = new("Owner", 20);
    private readonly Player _attacker = new("Attacker", 20);

    [Fact]
    public void GhostlyPrison_RegistersFlatTwoTaxOnController()
    {
        using var scope = AttackRestrictionRegistryProvider.PushScope();
        var prison = GhostlyPrisonFactory.Create(_owner);
        prison.SetZone(ZoneType.Battlefield);

        prison.HasType(CardType.Enchantment).Should().BeTrue();
        prison.Controller.Should().Be(_owner);

        var restriction = AttackRestrictionRegistryProvider.Current.Active
            .OfType<PayPerAttackerRestriction>().Single();
        restriction.ProtectedPlayer.Should().Be(_owner);
        restriction.ProtectsPlaneswalkers.Should().BeFalse();
        restriction.CostPerAttacker.TotalValue.Should().Be(2);
        restriction.Protects(_owner).Should().BeTrue();
    }

    [Fact]
    public void Propaganda_RegistersFlatTwoTaxOnController()
    {
        using var scope = AttackRestrictionRegistryProvider.PushScope();
        var prop = PropagandaFactory.Create(_owner);
        prop.SetZone(ZoneType.Battlefield);

        var restriction = AttackRestrictionRegistryProvider.Current.Active
            .OfType<PayPerAttackerRestriction>().Single();
        restriction.ProtectedPlayer.Should().Be(_owner);
        restriction.CostPerAttacker.TotalValue.Should().Be(2);
    }

    [Fact]
    public void PaywallDeactivatesWhenEnchantmentLeavesBattlefield()
    {
        using var scope = AttackRestrictionRegistryProvider.PushScope();
        var prison = GhostlyPrisonFactory.Create(_owner);
        prison.SetZone(ZoneType.Battlefield);
        var restriction = AttackRestrictionRegistryProvider.Current.Active
            .OfType<PayPerAttackerRestriction>().Single();

        restriction.Protects(_owner).Should().BeTrue();

        // Enchantment leaves the battlefield → the gated restriction goes
        // inert (CR 508.1g only applies while the source is in play).
        prison.SetZone(ZoneType.Graveyard);
        restriction.IsActive.Should().BeFalse();
        restriction.Protects(_owner).Should().BeFalse();
    }

    [Fact]
    public void SphereOfSafety_DynamicCost_CountsEnchantmentsAndProtectsPlaneswalkers()
    {
        using var scope = AttackRestrictionRegistryProvider.PushScope();
        var sphere = SphereOfSafetyFactory.Create(_owner);
        sphere.SetZone(ZoneType.Battlefield);
        _owner.Zones.Battlefield.AddCard(sphere);

        var restriction = AttackRestrictionRegistryProvider.Current.Active
            .OfType<PayPerAttackerRestriction>().Single();
        restriction.ProtectsPlaneswalkers.Should().BeTrue();

        // Sphere counts itself → X = 1.
        restriction.CostPerAttacker.TotalValue.Should().Be(1);

        // Add two more enchantments → X = 3.
        for (var i = 0; i < 2; i++)
        {
            var aura = new Enchantment($"Aura{i}", "W")
            { Owner = _owner, Controller = _owner, Zone = ZoneType.Battlefield };
            _owner.Zones.Battlefield.AddCard(aura);
        }
        restriction.CostPerAttacker.TotalValue.Should().Be(3, "X recomputes from the live board");

        // Protects the controller's planeswalkers too.
        var pw = new Planeswalker("Some Walker", "3W", 4)
        { Owner = _owner, Controller = _owner, Zone = ZoneType.Battlefield };
        restriction.Protects(pw).Should().BeTrue();
        restriction.Protects(_owner).Should().BeTrue();
        restriction.Protects(_attacker).Should().BeFalse("the attacking opponent is not protected");
    }
}
