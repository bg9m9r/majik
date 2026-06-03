using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ThundermawHellkiteFactory"/> (Magic 2013, {3}{R}{R}).
/// Creature — Dragon, 5/5:
///   "Flying
///    Haste
///    When this creature enters, it deals 1 damage to each creature with
///    flying your opponents control. Tap those creatures."
///
/// Covers the factory SHAPE (identity, dispatch, Flying + Haste keyword
/// markers, and the JSON-shell ETB trigger). The ETB resolution behaviour —
/// the declarative <c>damage_and_tap_each_flyer_opponents_control</c> verb,
/// which enumerates opponents' flyers off the live <c>GameContext</c> — is
/// exercised against a real <c>ResolutionContext</c> in
/// <see cref="Majik.Core.Tests.CardData.Definitions.JsonDamageAndTapEachFlyerTests"/>.
/// </summary>
[Trait("Color", "R")]
public class ThundermawHellkiteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThundermawHellkite_Identity()
    {
        var c = ThundermawHellkiteFactory.Create(_alice);

        c.Name.Should().Be("Thundermaw Hellkite");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThundermawHellkite_DispatchesViaNamedFactory()
    {
        var c = NamedCardFactory.Create("Thundermaw Hellkite", _alice);

        c.Should().NotBeNull();
        c!.Name.Should().Be("Thundermaw Hellkite");
    }

    [Fact]
    public void ThundermawHellkite_HasFlyingAndHaste()
    {
        var c = ThundermawHellkiteFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue("CR 702.9 — Thundermaw has Flying.");
        CombatAbilities.HasHaste(c).Should().BeTrue("CR 702.10 — Thundermaw has Haste.");
    }

    [Fact]
    public void ThundermawHellkite_CarriesJsonShellEtbTrigger()
    {
        var c = ThundermawHellkiteFactory.Create(_alice);

        // The declarative ETB comes from the JSON shell; it is untargeted
        // (a group effect — CR 608.2), so it declares no target slot.
        var etb = c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle().Subject;
        etb.TargetRequests.Should().BeEmpty(
            "the damage_and_tap_each_flyer_opponents_control verb is untargeted.");
    }
}
