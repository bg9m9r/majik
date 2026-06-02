using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoldhoundFactory"/> (The Lost Caverns of Ixalan,
/// {R}).
///
/// Goldhound — Artifact Creature — Treasure Dog 1/1.
///   "First strike
///    Menace (This creature can't be blocked except by two or more creatures.)
///    {T}, Sacrifice this creature: Add one mana of any color."
///
/// Covers:
/// - Identity (Artifact Creature — Treasure Dog, {R}, 1/1) + owner/controller.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - First strike (CR 702.7) + Menace (CR 702.110) keyword markers, read by
///   <see cref="CombatAbilities"/>.
/// - Five mana abilities (one per WUBRG) — "Add one mana of any color".
/// - Activating a colour ability sacrifices Goldhound (Battlefield -> Graveyard)
///   AND produces the chosen colour (CR 605 — the cost includes {T} + sacrifice).
/// - The mana abilities are un-activatable when Goldhound is not on the
///   battlefield (the {T} / sacrifice cost can't be paid).
/// </summary>
public class GoldhoundTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature OnBattlefield()
    {
        var card = GoldhoundFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        // CR 302.6 — the {T} mana ability requires Goldhound to have been
        // controlled since the most recent turn began (no summoning sickness).
        card.HasSummoningSickness = false;
        return card;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Goldhound_Is_ArtifactCreature_TreasureDog_1_1_AtCostR()
    {
        var dog = GoldhoundFactory.Create(_alice);

        dog.Name.Should().Be("Goldhound");
        dog.ManaCost.Should().Be("{R}");
        dog.HasType(CardType.Creature).Should().BeTrue();
        dog.HasType(CardType.Artifact).Should().BeTrue();
        dog.HasSubtype(CardSubtype.Treasure).Should().BeTrue();
        dog.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        dog.BasePower.Should().Be(1);
        dog.BaseToughness.Should().Be(1);
        dog.Owner.Should().BeSameAs(_alice);
        dog.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Goldhound()
    {
        var card = NamedCardFactory.Create("Goldhound", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goldhound");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Treasure).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dog).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Keywords
    // -----------------------------------------------------------------------

    [Fact]
    public void Goldhound_HasFirstStrike_And_Menace()
    {
        var dog = GoldhoundFactory.Create(_alice);

        CombatAbilities.HasFirstStrike(dog).Should().BeTrue("CR 702.7");
        CombatAbilities.HasMenace(dog).Should().BeTrue("CR 702.110");
    }

    // -----------------------------------------------------------------------
    // Mana ability — "Add one mana of any color"
    // -----------------------------------------------------------------------

    [Fact]
    public void Goldhound_HasFiveManaAbilities_OnePerColor()
    {
        var dog = GoldhoundFactory.Create(_alice);

        dog.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "\"Add one mana of any color\" is modelled as one ManaAbility per WUBRG");
    }

    [Fact]
    public void ManaAbility_CannotActivate_WhenNotOnBattlefield()
    {
        var dog = GoldhoundFactory.Create(_alice);

        // Not on the battlefield -> the {T} / sacrifice cost can't be paid.
        dog.Abilities.OfType<ManaAbility>()
            .All(m => !m.CanActivate()).Should().BeTrue();
    }

    [Fact]
    public void ManaAbility_CanActivate_WhenOnBattlefield()
    {
        var dog = OnBattlefield();

        dog.Abilities.OfType<ManaAbility>()
            .All(m => m.CanActivate()).Should().BeTrue();
    }

    [Fact]
    public void Activation_AddsWhiteMana_AndSacrificesGoldhound()
    {
        var dog = OnBattlefield();

        var white = dog.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        var produced = white.Activate();
        _alice.AddManaToPool(produced);

        produced.White.Should().Be(1, "chose the {W} ability slot");
        _alice.ManaPool.White.Should().Be(1,
            "controller's mana pool receives the chosen colour");
        dog.Zone.Should().Be(ZoneType.Graveyard,
            "{T}, Sacrifice this creature -> Goldhound goes to the graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dog);
        _alice.Zones.Graveyard.GetCards().Should().Contain(dog);
    }

    [Fact]
    public void Activation_CanProduceAnyOfTheFiveColors()
    {
        // Each colour slot produces exactly its own pip when activated.
        var expectations = new (string color, System.Func<ManaCost, int> read)[]
        {
            ("W", c => c.White),
            ("U", c => c.Blue),
            ("B", c => c.Black),
            ("R", c => c.Red),
            ("G", c => c.Green),
        };

        foreach (var (color, read) in expectations)
        {
            var alice = new Player("Alice", 20);
            var dog = GoldhoundFactory.Create(alice);
            alice.Zones.Battlefield.AddCard(dog);
            dog.SetZone(ZoneType.Battlefield);
            dog.HasSummoningSickness = false; // CR 302.6 — {T} needs no sickness

            var ability = dog.Abilities.OfType<ManaAbility>()
                .Single(m => m.ManaGenerated == ManaCost.Parse(color));

            var produced = ability.Activate();
            read(produced).Should().Be(1, $"the {color} slot produces {{{color}}}");
        }
    }
}
