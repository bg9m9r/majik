using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DwarvenMineFactory"/> — Dwarven Mine (Throne of
/// Eldraine), the red "creature land" of the ELD cycle. Oracle text (verified
/// against Scryfall 2026-06-14):
///   "({T}: Add {R}.)
///    This land enters tapped unless you control three or more other
///    Mountains.
///    When this land enters untapped, create a 1/1 red Dwarf creature token."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: Land — Mountain, nonbasic, no mana cost (a single *_Identity
///   assert for the non-vanilla subtype/supertype shape).
/// - {T}: Add {R} intrinsic Mountain mana ability (CR 605.1 / 305.6).
/// - Count-conditional enters-tapped (CR 614.1c): &lt;3 other Mountains =>
///   tapped; >=3 => untapped; self excluded from the count; opponent's
///   Mountains don't count.
/// - ETB-untapped token trigger (CR 603.6a): fires only when the Mine is
///   untapped on the battlefield; mints a 1/1 red Dwarf token.
///
/// Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so this file does not re-test those.
/// </summary>
[Trait("Color", "R")]
public class DwarvenMineFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helper: add a basic Mountain to a player's battlefield.
    // -----------------------------------------------------------------------
    private static Land AddMountain(Player controller)
    {
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = controller, Controller = controller };
        mountain.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(mountain);
        return mountain;
    }

    private static ZoneMoveIntent EtbIntent(Land mine, Player controller) =>
        new(Card: mine,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_Identity_IsNonbasicMountainLand_NoManaCost()
    {
        var mine = DwarvenMineFactory.Create(_alice);

        mine.Name.Should().Be("Dwarven Mine");
        mine.HasType(CardType.Land).Should().BeTrue();
        mine.HasSubtype(CardSubtype.Mountain).Should().BeTrue(
            "Dwarven Mine's type line is 'Land — Mountain'");
        mine.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Dwarven Mine is nonbasic");
        mine.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void Create_HasSingleRedManaAbility()
    {
        var mine = DwarvenMineFactory.Create(_alice);
        var mana = mine.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(1, "Dwarven Mine taps for {R} (intrinsic Mountain mana ability)");
        mana[0].ManaGenerated.Red.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Enters tapped unless you control three or more other Mountains (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_WhenControllerHasNoOtherMountains()
    {
        var bus = new ReplacementBus();
        var mine = DwarvenMineFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(EtbIntent(mine, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with zero other Mountains the Mine enters tapped");
    }

    [Fact]
    public void EntersTapped_WhenControllerHasTwoOtherMountains()
    {
        var bus = new ReplacementBus();
        AddMountain(_alice);
        AddMountain(_alice);
        var mine = DwarvenMineFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(EtbIntent(mine, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "two other Mountains is short of the required three");
    }

    [Fact]
    public void EntersUntapped_WhenControllerHasThreeOtherMountains()
    {
        var bus = new ReplacementBus();
        AddMountain(_alice);
        AddMountain(_alice);
        AddMountain(_alice);
        var mine = DwarvenMineFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(EtbIntent(mine, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "three or more other Mountains lets the Mine enter untapped");
    }

    [Fact]
    public void EntersTapped_SelfDoesNotCountAsAnOtherMountain()
    {
        var bus = new ReplacementBus();
        // Exactly two other Mountains + the Mine itself already on the
        // battlefield. The Mine must NOT count itself toward the three.
        AddMountain(_alice);
        AddMountain(_alice);
        var mine = DwarvenMineFactory.Create(_alice, replacements: bus);
        mine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mine);

        var after = bus.Apply(EtbIntent(mine, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the Mine itself is a Mountain but the count is 'three or more OTHER Mountains'");
    }

    [Fact]
    public void EntersTapped_OpponentsMountainsDoNotCount()
    {
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        AddMountain(bob);
        AddMountain(bob);
        AddMountain(bob);
        var mine = DwarvenMineFactory.Create(_alice, replacements: bus);

        var after = bus.Apply(EtbIntent(mine, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "'you control' counts only the controller's Mountains, not the opponent's");
    }

    // -----------------------------------------------------------------------
    // ETB-untapped token trigger (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenTrigger_FiresWhenMineIsUntappedOnBattlefield()
    {
        var mine = DwarvenMineFactory.Create(_alice);
        mine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mine);
        // entered untapped — IsTapped is false by default.

        var trigger = mine.Abilities.OfType<TriggeredAbility>().Single();
        var etb = new CardMovedEvent(mine, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(etb).Should().BeTrue(
            "the Mine entered untapped, so 'When this land enters untapped' fires");
    }

    [Fact]
    public void TokenTrigger_DoesNotFireWhenMineEnteredTapped()
    {
        var mine = DwarvenMineFactory.Create(_alice);
        mine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mine);
        mine.Tap(); // entered tapped (enters-tapped replacement would have done this)

        var trigger = mine.Abilities.OfType<TriggeredAbility>().Single();
        var etb = new CardMovedEvent(mine, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(etb).Should().BeFalse(
            "a Mine that entered tapped does not trigger its 'enters untapped' ability");
    }

    [Fact]
    public void TokenTrigger_MintsOneOneOneRedDwarf()
    {
        var mine = DwarvenMineFactory.Create(_alice);
        mine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mine);

        var trigger = mine.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Effects.Single().Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1, "the trigger creates exactly one Dwarf token");
        var dwarf = tokens[0];
        dwarf.Name.Should().Be("Dwarf");
        dwarf.Power.Should().Be(1);
        dwarf.Toughness.Should().Be(1);
        dwarf.HasSubtype(CardSubtype.Dwarf).Should().BeTrue();
        CardColors.GetColors(dwarf).Should().Contain(ManaColor.Red, "the token is red");
        CardColors.GetColors(dwarf).Should().HaveCount(1, "the token is mono-red");
    }
}
