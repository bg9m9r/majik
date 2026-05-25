using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="NykthosShrineToNyxFactory"/> — Legendary Land.
///
/// Covers:
/// - Card identity (Legendary Land, no printed subtypes).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <c>{T}: Add {C}</c> mana ability — tap produces 1 colourless
///   (bucketed as Generic; CR 605.1).
/// - Five additional <see cref="ManaAbility"/> slots (WUBRG) for the
///   "{2}, {T}: Choose a color. Add N {colour}" devotion ability.
/// - Devotion gating: <c>CanActivate</c> false when controller has zero
///   devotion to that colour OR can't afford {2} OR Nykthos is tapped.
/// - Activation: pays {2}, taps Nykthos, produces N {colour} where N =
///   live devotion (sampled at activation time — CR 700.5).
/// - <see cref="NykthosShrineToNyxFactory.ComputeDevotion"/> sums
///   pure-colour pips across the controller's battlefield; ignores
///   {C} / colourless permanents; excludes opponents' permanents.
/// </summary>
public class NykthosShrineToNyxTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Nykthos_IsLegendaryLand_NoSubtypes()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Nykthos is Legendary — matters for the legend rule (CR 704.5j)");
        land.Subtypes.Should().BeEmpty("Nykthos has no printed subtypes");
        land.Name.Should().Be("Nykthos, Shrine to Nyx");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Nykthos()
    {
        var card = NamedCardFactory.Create("Nykthos, Shrine to Nyx", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Nykthos, Shrine to Nyx");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void Nykthos_HasColorlessTapAbility_ProducesOneGeneric()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);

        // Six total ManaAbility instances: one {T}: Add {C}, plus five
        // devotion slots (WUBRG). The {C} one is the first attached.
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(6);

        var colorlessAbility = manaAbilities[0];
        colorlessAbility.CanActivate().Should().BeTrue(
            "the vanilla {T}: Add {C} only requires the land to be untapped");

        var produced = colorlessAbility.Activate();
        produced.Generic.Should().Be(1,
            "{C} is bucketed as Generic +1 per ManaCost.Parse (see Mutavault, " +
            "Phyrexian Tower)");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Devotion ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Nykthos_HasFiveDevotionSlots_OnePerWUBRG()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);

        // The five WUBRG slots are the ManaAbility instances after the
        // colourless {T}: Add {C} one. Each is a separate slot because
        // the engine has no ChooseColor agent prompt — same pattern
        // Cavern of Souls uses.
        var devotionSlots = land.Abilities.OfType<ManaAbility>().Skip(1).ToList();
        devotionSlots.Should().HaveCount(5,
            "one ManaAbility slot per WUBRG colour for the {2}, {T} devotion " +
            "ability — stand-in for the 'choose a color' prompt");
    }

    // -----------------------------------------------------------------------
    // Devotion gating — CanActivate
    // -----------------------------------------------------------------------

    [Fact]
    public void DevotionAbility_CannotActivate_WhenDevotionIsZero()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);
        // Give Alice {2} so the affordability gate passes — devotion is
        // the only barrier.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var devotionSlots = land.Abilities.OfType<ManaAbility>().Skip(1).ToList();
        foreach (var slot in devotionSlots)
        {
            slot.CanActivate().Should().BeFalse(
                "Alice has no permanents → devotion to every colour is 0; " +
                "the devotion-gate short-circuits to avoid burning {2}");
        }
    }

    [Fact]
    public void DevotionAbility_CannotActivate_WhenCannotAfford_TwoGeneric()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);

        // Bring devotion to red up — but withhold {2}.
        var goblin = new Creature("Goblin Guide", "R", 2, 2);
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);

        var devotionSlots = land.Abilities.OfType<ManaAbility>().Skip(1).ToList();
        // The red slot is index 3 in WUBRG order (W=0, U=1, B=2, R=3, G=4).
        var redSlot = devotionSlots[3];
        redSlot.CanActivate().Should().BeFalse(
            "Alice can't pay the {2} extra cost without mana in pool");
    }

    [Fact]
    public void DevotionAbility_CannotActivate_WhenNykthosTapped()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        // Build red devotion + give Alice {2}.
        var goblin = new Creature("Goblin Guide", "R", 2, 2);
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(goblin);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        land.Tap();

        var redSlot = land.Abilities.OfType<ManaAbility>().Skip(1).ToList()[3];
        redSlot.CanActivate().Should().BeFalse(
            "{T} is part of the activation cost — already-tapped Nykthos " +
            "can't be re-tapped");
    }

    // -----------------------------------------------------------------------
    // Devotion ability — activation produces N {colour}
    // -----------------------------------------------------------------------

    [Fact]
    public void DevotionAbility_ProducesNRed_WhenRedDevotionIsN()
    {
        var land = NykthosShrineToNyxFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        // Three red permanents with one pure-{R} pip each → devotion to
        // red = 3.
        for (int i = 0; i < 3; i++)
        {
            var goblin = new Creature($"Goblin Guide {i}", "R", 2, 2);
            goblin.SetOwner(_alice);
            goblin.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(goblin);
        }
        // Fund the {2} extra cost.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var redSlot = land.Abilities.OfType<ManaAbility>().Skip(1).ToList()[3];
        redSlot.CanActivate().Should().BeTrue();

        var produced = redSlot.Activate();
        produced.Red.Should().Be(3,
            "devotion to red is 3 → Nykthos pumps three {R}");
        produced.TotalValue.Should().Be(3);
        land.IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0,
            "the {2} was deducted as the extra activation cost");
    }

    [Fact]
    public void DevotionAbility_DevotionSampledAtActivationTime_NotConstruction()
    {
        // CR 700.5 / ManaAbility dynamic generator: devotion is read at
        // activation time, NOT at factory construction. Cast a few green
        // permanents AFTER building Nykthos and confirm activation reads
        // the live count.
        var land = NykthosShrineToNyxFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);

        // No green permanents yet → green slot should refuse activation
        // even with {2} in pool.
        _alice.AddManaToPool(ManaCost.Parse("2"));
        var greenSlot = land.Abilities.OfType<ManaAbility>().Skip(1).ToList()[4];
        greenSlot.CanActivate().Should().BeFalse();

        // Now drop two GG permanents — devotion to green = 4.
        var elf = new Creature("Elvish Mystic", "G", 1, 1);
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(elf);

        var bigGreen = new Creature("Leatherback Baloth", "GGG", 4, 5);
        bigGreen.SetOwner(_alice);
        bigGreen.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bigGreen);

        greenSlot.CanActivate().Should().BeTrue(
            "devotion to green is now 1 + 3 = 4 → activation legal");
        var produced = greenSlot.Activate();
        produced.Green.Should().Be(4,
            "devotion sampled at activation time; one {G} from Elvish Mystic " +
            "plus three {G} from Leatherback Baloth");
    }

    // -----------------------------------------------------------------------
    // ComputeDevotion helper — direct surface
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeDevotion_ReturnsZero_ForEmptyBattlefield()
    {
        foreach (var color in new[]
        {
            ManaColor.White, ManaColor.Blue, ManaColor.Black,
            ManaColor.Red, ManaColor.Green,
        })
        {
            NykthosShrineToNyxFactory.ComputeDevotion(_alice, color)
                .Should().Be(0);
        }
    }

    [Fact]
    public void ComputeDevotion_CountsPureColorPips_AcrossControllerBattlefield()
    {
        // Two cards: {W}{W} (Thalia-shaped) + {1}{W} → devotion to W = 3.
        var thalia = new Creature("Thalia, Heretic Cathar", "WW", 3, 2);
        thalia.SetOwner(_alice);
        thalia.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(thalia);

        var soldier = new Creature("Bygone Soldier", "1W", 2, 2);
        soldier.SetOwner(_alice);
        soldier.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(soldier);

        NykthosShrineToNyxFactory.ComputeDevotion(_alice, ManaColor.White)
            .Should().Be(3,
                "WW + 1W contribute 2 + 1 white pips = 3 devotion");
    }

    [Fact]
    public void ComputeDevotion_IgnoresOpponentPermanents()
    {
        var bobGoblin = new Creature("Bob's Goblin", "RR", 2, 2);
        bobGoblin.SetOwner(_bob);
        bobGoblin.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobGoblin);

        NykthosShrineToNyxFactory.ComputeDevotion(_alice, ManaColor.Red)
            .Should().Be(0,
                "devotion reads the player's OWN battlefield (CR 700.5)");
    }

    [Fact]
    public void ComputeDevotion_ColorlessReturnsZero()
    {
        var artifact = new Creature("Walking Ballista", "", 0, 0);
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);

        NykthosShrineToNyxFactory.ComputeDevotion(_alice, ManaColor.Colorless)
            .Should().Be(0,
                "devotion is per *colour* — colourless is not a colour " +
                "(CR 700.5)");
    }

    [Fact]
    public void ComputeDevotion_PerColor_SegregatesPips()
    {
        // Multicolour creature 1W1U → contributes 1 to white AND 1 to blue.
        var multicolor = new Creature("Soulherder", "1WU", 2, 2);
        multicolor.SetOwner(_alice);
        multicolor.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(multicolor);

        NykthosShrineToNyxFactory.ComputeDevotion(_alice, ManaColor.White)
            .Should().Be(1);
        NykthosShrineToNyxFactory.ComputeDevotion(_alice, ManaColor.Blue)
            .Should().Be(1);
        NykthosShrineToNyxFactory.ComputeDevotion(_alice, ManaColor.Black)
            .Should().Be(0);
    }

    [Fact]
    public void ComputeDevotion_NullPlayer_ReturnsZero()
    {
        NykthosShrineToNyxFactory.ComputeDevotion(null!, ManaColor.Red)
            .Should().Be(0);
    }
}
