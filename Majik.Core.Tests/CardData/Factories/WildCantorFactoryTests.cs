using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WildCantorFactory"/>.
///
/// Wild Cantor — Creature — Human Druid 1/1 {R/G}.
///   "({R/G} can be paid with either {R} or {G}.)
///    Sacrifice this creature: Add one mana of any color."
///
/// Covers:
/// - Card identity (Creature — Human Druid, 1/1, hybrid {R/G}).
/// - NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG) — "any color".
/// - The abilities are no-tap mana abilities (CR 605.1) gated on the
///   creature being on the battlefield.
/// - Activation produces the chosen colour AND sacrifices the creature to
///   its owner's graveyard (CR 701.16 — the sacrifice is the activation
///   cost). The creature is NOT tapped (no {T} in the cost).
/// - Sibling abilities become un-activatable once the creature has been
///   sacrificed.
/// - The ManaAbilityActivator path credits the chosen colour into the pool.
/// </summary>
public class WildCantorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void WildCantor_IsHumanDruid_OneOne_HybridRG()
    {
        var cantor = WildCantorFactory.Create(_alice);

        cantor.Name.Should().Be("Wild Cantor");
        cantor.HasType(CardType.Creature).Should().BeTrue();
        cantor.HasSubtype(CardSubtype.Human).Should().BeTrue();
        cantor.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        cantor.Power.Should().Be(1);
        cantor.Toughness.Should().Be(1);
        cantor.ManaCost.Should().Be("{R/G}");
        cantor.Owner.Should().BeSameAs(_alice);
        cantor.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WildCantor()
    {
        var card = NamedCardFactory.Create("Wild Cantor", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Wild Cantor");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{R/G}");
    }

    // --------------------------------------------------------------
    // Mana ability shape — one per WUBRG ("any color")
    // --------------------------------------------------------------

    [Fact]
    public void WildCantor_HasFiveManaAbilities_OnePerColor()
    {
        var cantor = WildCantorFactory.Create(_alice);
        var mas = cantor.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour ('any color')");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1
                                     && m.ManaGenerated.TotalValue == 1);
    }

    // --------------------------------------------------------------
    // Activation gate — only legal while on the battlefield
    // --------------------------------------------------------------

    [Fact]
    public void WildCantor_NotActivatable_WhileNotOnBattlefield()
    {
        var cantor = WildCantorFactory.Create(_alice);
        // Default zone is not Battlefield — the ability can't be activated.
        var ma = cantor.Abilities.OfType<ManaAbility>().First();

        ma.CanActivate().Should().BeFalse(
            "the sacrifice ability is only legal while the creature is on the battlefield");
    }

    [Fact]
    public void WildCantor_Activatable_WhileOnBattlefield()
    {
        var cantor = WildCantorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cantor);
        cantor.SetZone(ZoneType.Battlefield);

        foreach (var ma in cantor.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeTrue(
                "the creature is on the battlefield — the sacrifice ability is legal");
        }
    }

    // --------------------------------------------------------------
    // Activation — produces chosen colour, sacrifices creature, no tap
    // --------------------------------------------------------------

    [Fact]
    public void WildCantor_Activate_ProducesChosenColor_AndSacrifices()
    {
        var cantor = WildCantorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cantor);
        cantor.SetZone(ZoneType.Battlefield);

        var mas = cantor.Abilities.OfType<ManaAbility>().ToList();

        // Activate the blue option.
        var blue = mas.Single(m => m.ManaGenerated.Blue == 1);
        var produced = blue.Activate();

        produced.Blue.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        // Creature is sacrificed (cost), not tapped — there is no {T} in the cost.
        cantor.IsTapped.Should().BeFalse(
            "Wild Cantor's cost is 'Sacrifice this creature', not {T}");
        cantor.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — the sacrifice moves the creature from battlefield to its owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(cantor,
            "the creature has left the battlefield");
        _alice.Zones.Graveyard.GetCards().Should().Contain(cantor,
            "the creature is now in its owner's graveyard");

        // Sibling abilities are no longer activatable — the creature is gone.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "the creature has been sacrificed — no further activations possible");
        }
    }

    // --------------------------------------------------------------
    // ManaAbilityActivator path — pool gets credited with chosen colour
    // --------------------------------------------------------------

    [Fact]
    public void WildCantor_ActivateViaActivator_CreditsChosenColor()
    {
        var cantor = WildCantorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cantor);
        cantor.SetZone(ZoneType.Battlefield);

        var activator = new Majik.Core.Services.ManaAbilityActivator();
        var greenAbility = cantor.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

        _alice.ManaPool.Total.Should().Be(0);

        activator.ActivateManaAbility(greenAbility, _alice);

        _alice.ManaPool.Green.Should().Be(1);
        _alice.ManaPool.Total.Should().Be(1);
        cantor.Zone.Should().Be(ZoneType.Graveyard);
    }
}
