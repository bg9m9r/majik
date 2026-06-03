using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Rules;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Oracle of Mul Daya (Zendikar, {3}{G}) — additional land per turn
/// + play-lands-from-top-revealed grant (CR 305.2 / 601.3e / 715.4).
/// </summary>
public class OracleOfMulDayaFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private static (ZoneService zones, ContinuousEffectsService effects) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var effects = new ContinuousEffectsService(bus);
        return (zones, effects);
    }

    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    [Fact]
    public void Identity_ElfShaman_2_2_At3G()
    {
        var oracle = OracleOfMulDayaFactory.Create(_alice);

        oracle.Name.Should().Be("Oracle of Mul Daya");
        oracle.ManaCost.Should().Be("{3}{G}");
        oracle.HasType(CardType.Creature).Should().BeTrue();
        oracle.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        oracle.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        oracle.BasePower.Should().Be(2);
        oracle.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OracleOfMulDaya()
    {
        var card = NamedCardFactory.Create("Oracle of Mul Daya", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Oracle of Mul Daya");
    }

    [Fact]
    public void GrantsOneAdditionalLandPlay()
    {
        var oracle = OracleOfMulDayaFactory.Create(_alice);
        ((Permanent)oracle).AdditionalLandPlaysGranted.Should().Be(1);
    }

    [Fact]
    public void OnBattlefield_TopLand_IsPlayable_AndRevealed()
    {
        var (zones, effects) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, effects);
        var land = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        _alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeFalse();

        EnterBattlefield(zones, _alice, oracle);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, land).Should().BeTrue();
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue();
    }

    [Fact]
    public void OnBattlefield_TopNonland_NotCastable()
    {
        // Oracle is a LANDS-only grant — it never makes a nonland castable.
        var (zones, effects) = BuildEngine();
        var oracle = OracleOfMulDayaFactory.Create(_alice, effects);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, oracle);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, bolt).Should().BeFalse();
    }
}
