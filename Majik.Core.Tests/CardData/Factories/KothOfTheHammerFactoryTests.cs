using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Koth of the Hammer (Scars of Mirrodin, {2}{R}{R}).
///
/// Legendary Planeswalker — Koth, starting loyalty 3. Oracle text
/// (Scryfall, verified 2026-06-02):
///   "+1: Untap target Mountain. It becomes a 4/4 red Elemental creature until
///        end of turn. It's still a land.
///    −2: Add {R} for each Mountain you control.
///    −5: You get an emblem with 'Mountains you control have "{T}: This land
///        deals 1 damage to any target."'"
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Koth, loyalty 3, {2}{R}{R}),
///     materialised from the embedded JSON definition.
///   - Three loyalty abilities: +1, −2, −5.
///   - +1: untaps the target Mountain and animates it to a 4/4 red Elemental
///     (still a land) via the continuous-effects service.
///   - −2: adds {R} for each Mountain the controller controls.
///   - −5: mints a structural emblem.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "R")]
public class KothOfTheHammerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Land Mountain(string name = "Mountain")
    {
        var m = new Land(name, subtypes: new[] { CardSubtype.Mountain }) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(m);
        m.SetZone(ZoneType.Battlefield);
        m.SetController(_alice);
        return m;
    }

    [Fact]
    public void Koth_IsLegendaryPlaneswalker_Koth_3Loyalty_AtCost2RR()
    {
        var koth = KothOfTheHammerFactory.Create(_alice);

        koth.Name.Should().Be("Koth of the Hammer");
        koth.ManaCost.Should().Be("{2}{R}{R}");
        koth.HasType(CardType.Planeswalker).Should().BeTrue();
        koth.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        koth.HasSubtype(CardSubtype.Koth).Should().BeTrue();
        koth.Loyalty.Should().Be(3);
        koth.StartingLoyalty.Should().Be(3);
        koth.Owner.Should().BeSameAs(_alice);
        koth.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Koth_HasThreeLoyaltyAbilities_Plus1_Minus2_Minus5()
    {
        var koth = KothOfTheHammerFactory.Create(_alice);

        var loyalty = koth.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -2, -5 });
    }

    // -----------------------------------------------------------------------
    // +1: Untap target Mountain. It becomes a 4/4 red Elemental creature until
    //     end of turn. It's still a land.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_UntapsTargetMountain_AndAnimatesToFourFourRedElemental_StillALand()
    {
        var effects = new ContinuousEffectsService();
        var mountain = Mountain();
        mountain.Tap(); // start tapped so we can observe the untap.

        var koth = KothOfTheHammerFactory.Create(
            _alice,
            targetMountainResolver: () => new[] { mountain },
            continuousEffects: effects);

        koth.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        koth.Loyalty.Should().Be(4); // 3 + 1
        mountain.IsTapped.Should().BeFalse("the Mountain was untapped");

        var chars = effects.Compute((Permanent)mountain);
        chars.Types.Should().Contain(CardType.Land, "it's still a land (CR 613.1c)");
        chars.Types.Should().Contain(CardType.Creature, "animated into a creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental, "4/4 red Elemental");
        chars.Subtypes.Should().Contain(CardSubtype.Mountain, "printed Mountain subtype stays");
        chars.Colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Red, "becomes red");
    }

    [Fact]
    public void Plus1_RecordsFourFourBody_OnLayer7b()
    {
        var effects = new ContinuousEffectsService();
        var mountain = Mountain();

        var koth = KothOfTheHammerFactory.Create(
            _alice,
            targetMountainResolver: () => new[] { mountain },
            continuousEffects: effects);

        koth.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        var pt = RegisteredEffects(effects).OfType<ManlandCycleBecomesPTEffect>().Single();
        pt.NewPower.Should().Be(KothOfTheHammerFactory.AnimatedPower);
        pt.NewToughness.Should().Be(KothOfTheHammerFactory.AnimatedToughness);
        pt.ExpiresAtEndOfTurn.Should().BeTrue("the animation lasts until end of turn (CR 514.2)");
    }

    [Fact]
    public void Plus1_NoTargetResolver_NoOpsButLoyaltyStillApplies()
    {
        var koth = KothOfTheHammerFactory.Create(_alice);

        koth.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        koth.Loyalty.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // −2: Add {R} for each Mountain you control.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_AddsOneRed_PerMountainControlled()
    {
        Mountain("Mountain1");
        Mountain("Mountain2");
        Mountain("Mountain3");
        // A non-Mountain land must not count.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest }) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);
        forest.SetController(_alice);

        var koth = KothOfTheHammerFactory.Create(_alice);

        koth.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        koth.Loyalty.Should().Be(1); // 3 - 2
        _alice.ManaPool.Red.Should().Be(3, "one {R} per Mountain controlled");
    }

    [Fact]
    public void Minus2_NoMountains_AddsNoMana()
    {
        var koth = KothOfTheHammerFactory.Create(_alice);
        // 3 loyalty is exactly enough for −2 (would go to 1).
        koth.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        koth.Loyalty.Should().Be(1);
        _alice.ManaPool.Red.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // −5: emblem with "Mountains you control have '{T}: This land deals 1
    //     damage to any target.'"
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus5_CreatesEmblem_WhenLoyaltyIsEnough()
    {
        var koth = KothOfTheHammerFactory.Create(_alice);

        var ult = koth.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -5);
        ult.CanActivate().Should().BeFalse("3 loyalty is not enough for −5");

        koth.AddLoyalty(2); // 3 + 2 = 5
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        koth.Loyalty.Should().Be(0); // 5 - 5
        _alice.Emblems.Should().HaveCount(1);
        _alice.Emblems.Single().SourceName.Should().Contain("Mountains you control have");
    }

    [Fact]
    public void NamedCardFactory_DispatchesKoth()
    {
        var koth = NamedCardFactory.Create("Koth of the Hammer", _alice);

        koth.Should().BeOfType<Planeswalker>();
        koth.Name.Should().Be("Koth of the Hammer");
        koth.HasSubtype(CardSubtype.Koth).Should().BeTrue();
    }

    private static IEnumerable<ContinuousEffect> RegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IEnumerable<ContinuousEffect>)field!.GetValue(svc)!;
    }
}
