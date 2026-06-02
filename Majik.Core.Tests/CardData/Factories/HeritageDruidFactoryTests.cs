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
/// Tests for <see cref="HeritageDruidFactory"/> — Creature — Elf Druid {G}
/// 1/1 with one mana ability:
///   "Tap three untapped Elves you control: Add {G}{G}{G}."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> (the Heritage Druid slot) attached.
///   - Activation taps three untapped Elves and produces {G}{G}{G}, without
///     tapping Heritage Druid via a {T} symbol (the Druid only taps if it is
///     itself one of the three chosen bodies).
///   - CanActivate gate: false with fewer than three untapped Elves.
///   - No summoning-sickness gate on the ability (CR 302.6 doesn't apply —
///     the cost is the word "Tap", not a {T} symbol).
/// </summary>
[Trait("Color", "G")]
public class HeritageDruidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>Add an untapped Elf to Alice's battlefield (summoning-sick
    /// by default, mirroring Permanent's default state).</summary>
    private Creature AddElf(string name)
    {
        var elf = new Creature(name, "{G}", 1, 1, subtypes: new[] { CardSubtype.Elf });
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        elf.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(elf);
        return elf;
    }

    [Fact]
    public void HeritageDruid_IsElfDruid_AtG_OneOne()
    {
        var c = HeritageDruidFactory.Create(_alice);

        c.Name.Should().Be("Heritage Druid");
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
    public void HeritageDruid_HasSingleManaAbility()
    {
        var c = HeritageDruidFactory.Create(_alice);

        c.Abilities.OfType<HeritageDruidManaAbility>().Should().HaveCount(1,
            "Heritage Druid prints only \"Tap three untapped Elves you control: Add {G}{G}{G}.\"");
    }

    [Fact]
    public void HeritageDruid_Activate_TapsThreeElves_ProducesGGG()
    {
        var druid = HeritageDruidFactory.Create(_alice);
        druid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(druid);

        // Two other untapped Elves; with the Druid that's three eligible.
        var e1 = AddElf("Llanowar Elves");
        var e2 = AddElf("Elvish Mystic");

        var ability = druid.Abilities.OfType<HeritageDruidManaAbility>().Single();
        ability.CanActivate().Should().BeTrue(
            "three untapped Elves (the Druid + two) are controlled.");

        // Explicitly choose the two other Elves + the Druid as the three.
        ability.TapChoice.Targets = new[] { e1, e2, druid };

        var produced = ability.Activate();
        produced.Green.Should().Be(3, "activating Heritage Druid yields {G}{G}{G}.");
        produced.Generic.Should().Be(0);

        e1.IsTapped.Should().BeTrue();
        e2.IsTapped.Should().BeTrue();
        druid.IsTapped.Should().BeTrue("the Druid was chosen as one of the three Elves.");
    }

    [Fact]
    public void HeritageDruid_DoesNotSelfTap_WhenThreeOtherElvesChosen()
    {
        var druid = HeritageDruidFactory.Create(_alice);
        druid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(druid);

        var e1 = AddElf("Elf A");
        var e2 = AddElf("Elf B");
        var e3 = AddElf("Elf C");

        var ability = druid.Abilities.OfType<HeritageDruidManaAbility>().Single();
        ability.TapChoice.Targets = new[] { e1, e2, e3 };

        ability.Activate().Green.Should().Be(3);

        e1.IsTapped.Should().BeTrue();
        e2.IsTapped.Should().BeTrue();
        e3.IsTapped.Should().BeTrue();
        druid.IsTapped.Should().BeFalse(
            "Heritage Druid has no {T} in its cost — it stays untapped when not one of the three.");
    }

    [Fact]
    public void HeritageDruid_FallsBack_ToFirstEligibleElves_WhenNoTargetsSet()
    {
        var druid = HeritageDruidFactory.Create(_alice);
        druid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(druid);
        AddElf("Elf A");
        AddElf("Elf B");

        var ability = druid.Abilities.OfType<HeritageDruidManaAbility>().Single();

        // Targets intentionally unset — deterministic first-three-eligible.
        var produced = ability.Activate();
        produced.Green.Should().Be(3);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.IsTapped).Should().Be(3,
                "exactly three Elves are tapped to pay the cost.");
    }

    [Fact]
    public void HeritageDruid_CannotActivate_WithFewerThanThreeUntappedElves()
    {
        var druid = HeritageDruidFactory.Create(_alice);
        druid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(druid);
        AddElf("Lone Elf"); // only two Elves total (Druid + one)

        var ability = druid.Abilities.OfType<HeritageDruidManaAbility>().Single();
        ability.CanActivate().Should().BeFalse(
            "only two untapped Elves are controlled; the cost requires three.");
    }

    [Fact]
    public void HeritageDruid_CanActivate_DespiteSummoningSickness()
    {
        // CR 302.6 only restricts a creature tapping ITSELF via a {T} symbol
        // in an activation cost. Heritage Druid's cost is the word "Tap" on
        // a set of Elves — so summoning-sick Elves are still eligible bodies,
        // and the Druid may activate the turn it enters.
        var druid = HeritageDruidFactory.Create(_alice);
        druid.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(druid);
        AddElf("Sick Elf A");
        AddElf("Sick Elf B"); // all three summoning-sick (default)

        var ability = druid.Abilities.OfType<HeritageDruidManaAbility>().Single();
        ability.CanActivate().Should().BeTrue(
            "the tap-three-Elves cost is not gated on summoning sickness (CR 302.6 N/A).");
        ability.Activate().Green.Should().Be(3);
    }
}
