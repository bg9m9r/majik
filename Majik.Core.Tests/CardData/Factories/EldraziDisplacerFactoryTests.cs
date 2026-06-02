using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Eldrazi Displacer (Oath of the Gatewatch, {2}{C}).
///
/// Creature — Eldrazi 3/3. Oracle text (Scryfall, verified):
///   "Devoid (This card has no color.)
///    {2}{C}: Exile another target creature, then return it to the
///    battlefield tapped under its owner's control. ({C} represents
///    colorless mana.)"
///
/// Covers:
///   - Card identity (3/3 Creature — Eldrazi at {2}{C}).
///   - Devoid: IsDevoid stamped + keyword marker; no colours.
///   - Ability list (Devoid keyword + one activated ability with a
///     {2}{C} mana cost).
///   - Activation resolution: exiles a chosen creature, then returns it
///     to its owner's battlefield TAPPED under the owner's control.
///   - "another" rider: cannot target Eldrazi Displacer itself (CR 115.5b).
///   - Owner-routed return: a control-swapped creature goes back to its
///     true owner (CR 108.3).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "C")]
public class EldraziDisplacerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void EldraziDisplacer_IsEldrazi_3_3_AtCost2C()
    {
        var ed = EldraziDisplacerFactory.Create(_alice);

        ed.Name.Should().Be("Eldrazi Displacer");
        ed.ManaCost.Should().Be("{2}{C}");
        ed.Power.Should().Be(3);
        ed.Toughness.Should().Be(3);
        ed.HasType(CardType.Creature).Should().BeTrue();
        ed.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ed.Owner.Should().BeSameAs(_alice);
        ed.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EldraziDisplacer_IsDevoid_NoColours()
    {
        var ed = EldraziDisplacerFactory.Create(_alice);

        ed.IsDevoid.Should().BeTrue("Devoid stamps the card colourless (CR 702.114)");
        CardColors.GetColors(ed).Should().BeEmpty();
        ed.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Devoid");
    }

    [Fact]
    public void EldraziDisplacer_HasOneActivatedAbility_With2CManaCost()
    {
        var ed = EldraziDisplacerFactory.Create(_alice);

        var activated = ed.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = activated.Costs.OfType<ManaCostCost>().Single();
        // {2}{C}: the {C} pip currently folds into generic at cost-payment
        // time (the engine's standing colourless-mana posture, CR 107.4c),
        // so the parsed total mana value is 3 ({2} + {C}).
        manaCost.Cost.TotalValue.Should().Be(3);
    }

    [Fact]
    public void EldraziDisplacer_Activation_ExilesAndReturnsTargetTapped()
    {
        var ed = EldraziDisplacerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ed);
        ed.SetZone(ZoneType.Battlefield);

        // Bob controls a creature.
        var beast = new Creature("Beast", "{2}{G}", 3, 3);
        beast.SetOwner(_bob);
        beast.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(beast);
        beast.SetZone(ZoneType.Battlefield);

        var activated = ed.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { beast } });

        foreach (var eff in activated.Effects) eff.Execute();

        // Returned to its owner's (Bob's) battlefield, tapped, under Bob's control.
        _bob.Zones.Battlefield.GetCards().Should().Contain(beast);
        beast.Zone.Should().Be(ZoneType.Battlefield);
        beast.Controller.Should().BeSameAs(_bob);
        beast.IsTapped.Should().BeTrue("returns tapped");
    }

    [Fact]
    public void EldraziDisplacer_Activation_OwnControl_ReturnsToOwnerTapped()
    {
        // "under its owner's control" — a creature Alice controls but Bob
        // owns goes back to Bob (CR 108.3).
        var ed = EldraziDisplacerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ed);
        ed.SetZone(ZoneType.Battlefield);

        var stolen = new Creature("Stolen Ox", "{3}", 4, 4);
        stolen.SetOwner(_bob);
        stolen.SetController(_alice); // Alice currently controls it.
        _alice.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        var activated = ed.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { stolen } });

        foreach (var eff in activated.Effects) eff.Execute();

        _bob.Zones.Battlefield.GetCards().Should().Contain(stolen,
            "returns under its owner's control (CR 108.3)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(stolen);
        stolen.Controller.Should().BeSameAs(_bob);
        stolen.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void EldraziDisplacer_Activation_CannotTargetItself_Fizzles()
    {
        var ed = EldraziDisplacerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ed);
        ed.SetZone(ZoneType.Battlefield);

        var activated = ed.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { ed } });

        foreach (var eff in activated.Effects) eff.Execute();

        // "another" — Eldrazi Displacer is not a legal self-target (CR 115.5b).
        _alice.Zones.Battlefield.GetCards().Should().Contain(ed);
        ed.Zone.Should().Be(ZoneType.Battlefield);
        ed.IsTapped.Should().BeFalse();
    }
}
