using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ElvesOfDeepShadowFactory"/> — Creature — Elf Druid
/// {G} 1/1 with a single mana ability:
///   "{T}: Add {B}. This creature deals 1 damage to you."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> attached, producing {B}.
///   - Activation produces {B}, taps the creature, AND deals 1 damage to
///     the controller (life 20 -> 19).
///   - <c>canActivateCheck</c> gate prevents re-activation while tapped.
///   - No life-floor gate (CR 119.4 does not apply — this is damage, not
///     "Pay 1 life"): can still activate at 1 life.
/// </summary>
[Trait("Color", "G")]
public class ElvesOfDeepShadowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ElvesOfDeepShadow_IsElfDruid_AtG_OneOne()
    {
        var c = ElvesOfDeepShadowFactory.Create(_alice);

        c.Name.Should().Be("Elves of Deep Shadow");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void ElvesOfDeepShadow_HasSingleBlackManaAbility()
    {
        var c = ElvesOfDeepShadowFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "Elves of Deep Shadow prints only {T}: Add {B}.");

        // ManaCost.ToString() returns the bare letter "B" — see ManaCost.cs.
        mana[0].ManaGenerated?.ToString().Should().Be("B");
    }

    [Fact]
    public void ElvesOfDeepShadow_Activate_ProducesBlackMana_TapsItself_AndDealsOneDamage()
    {
        var c = ElvesOfDeepShadowFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — the {T} mana ability is only legal once the creature
        // has shed summoning sickness.
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue("Elves of Deep Shadow is untapped.");

        var produced = ability.Activate();
        produced.ToString().Should().Be("B",
            "activating Elves of Deep Shadow yields one black mana.");
        c.IsTapped.Should().BeTrue("the {T} cost taps the creature.");
        _alice.LifeTotal.Should().Be(19,
            "CR 120.3 — 'This creature deals 1 damage to you' costs 1 life.");
    }

    [Fact]
    public void ElvesOfDeepShadow_CannotActivate_WhileTapped()
    {
        var c = ElvesOfDeepShadowFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();

        // First activation taps it.
        ability.Activate();
        c.IsTapped.Should().BeTrue();

        // Second activation gate must reject — IsTapped is true.
        ability.CanActivate().Should().BeFalse(
            "canActivateCheck = !IsTapped — duplicate activations are prevented.");
    }

    [Fact]
    public void ElvesOfDeepShadow_CanActivate_AtOneLife_NoLifeFloorGate()
    {
        // CR 119.4 ("you can't pay life you don't have") does NOT apply —
        // the rider is damage, not a "Pay 1 life" cost. Pain dorks can deal
        // lethal damage to you; SBAs handle the loss afterward.
        var lowLife = new Player("Bob", 1);
        var c = ElvesOfDeepShadowFactory.Create(lowLife);
        c.SetZone(ZoneType.Battlefield);
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue(
            "no life-floor gate — damage may be lethal to its own controller.");

        ability.Activate();
        lowLife.LifeTotal.Should().Be(0, "1 life - 1 damage = 0 (SBAs handle the loss).");
    }
}
