using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DevotedDruidFactory"/> (Shadowmoor, {1}{G}).
///
/// Card: Devoted Druid — Creature — Elf Druid 0/2.
///   "{T}: Add {G}.
///    Put a -1/-1 counter on Devoted Druid: Untap Devoted Druid."
///
/// Covers:
///   - Identity / dispatch.
///   - {T}: Add {G} mana ability shape.
///   - Untap activated ability: pays -1/-1 counter, untaps self.
///   - Vizier of Remedies integration via ReplacementBus (the Druid Combo).
/// </summary>
public class DevotedDruidTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DevotedDruid_Identity()
    {
        var c = DevotedDruidFactory.Create(_alice);

        c.Name.Should().Be("Devoted Druid");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(2);
        c.Subtypes.Should().Contain(CardSubtype.Elf);
        c.Subtypes.Should().Contain(CardSubtype.Druid);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DevotedDruid()
    {
        var card = NamedCardFactory.Create("Devoted Druid", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Devoted Druid");
    }

    [Fact]
    public void DevotedDruid_HasGreenManaAbility()
    {
        var druid = DevotedDruidFactory.Create(_alice);

        druid.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "Devoted Druid prints exactly one mana ability: {T}: Add {G}");
    }

    [Fact]
    public void DevotedDruid_HasUntapActivatedAbility()
    {
        var druid = DevotedDruidFactory.Create(_alice);

        druid.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Devoted Druid prints exactly one non-mana activated ability: the untap line.");
    }

    [Fact]
    public void UntapAbility_PlacesMinusOneCounter_AndUntapsSelf()
    {
        var druid = DevotedDruidFactory.Create(_alice);
        PlaceOnBattlefield(druid, _alice);
        druid.Tap();
        druid.IsTapped.Should().BeTrue();

        var untap = druid.Abilities.OfType<ActivatedAbility>().Single();

        // Pay the cost (put a -1/-1 counter on self).
        foreach (var cost in untap.Costs) cost.Pay(_alice);
        // Run the effect (untap self).
        foreach (var fx in untap.Effects) fx.Execute();

        druid.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
        druid.IsTapped.Should().BeFalse("untap effect untaps the druid");
    }

    [Fact]
    public void UntapAbility_WithVizierOnBattlefield_PutsNoCounter()
    {
        var bus = new ReplacementBus();
        var vizier = VizierOfRemediesFactory.Create(_alice, bus);
        PlaceOnBattlefield(vizier, _alice);

        var druid = DevotedDruidFactory.Create(_alice, bus);
        PlaceOnBattlefield(druid, _alice);
        druid.Tap();

        var untap = druid.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in untap.Costs) cost.Pay(_alice);
        foreach (var fx in untap.Effects) fx.Execute();

        druid.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0,
            "Vizier of Remedies replaces the -1/-1 cost-counter with no counter (Druid Combo)");
        druid.IsTapped.Should().BeFalse("untap effect still fires; cost was paid (replaced)");
    }

    [Fact]
    public void DruidCombo_RepeatedUntap_NeverAccumulatesCounters()
    {
        var bus = new ReplacementBus();
        var vizier = VizierOfRemediesFactory.Create(_alice, bus);
        PlaceOnBattlefield(vizier, _alice);

        var druid = DevotedDruidFactory.Create(_alice, bus);
        PlaceOnBattlefield(druid, _alice);

        var untap = druid.Abilities.OfType<ActivatedAbility>().Single();

        // Simulate the infinite-mana loop: tap, untap (10 iterations).
        for (var i = 0; i < 10; i++)
        {
            druid.Tap();
            foreach (var cost in untap.Costs) cost.Pay(_alice);
            foreach (var fx in untap.Effects) fx.Execute();
        }

        druid.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0,
            "with Vizier on the battlefield, the loop is arbitrarily long — zero counters accumulate");
        druid.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void UntapAbility_NoBus_AccumulatesCounter_OneShot()
    {
        // Without a replacement bus the counter lands directly.
        var druid = DevotedDruidFactory.Create(_alice);
        PlaceOnBattlefield(druid, _alice);
        druid.Tap();

        var untap = druid.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in untap.Costs) cost.Pay(_alice);

        druid.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
    }

    private static void PlaceOnBattlefield(Permanent p, Player owner)
    {
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
    }
}
