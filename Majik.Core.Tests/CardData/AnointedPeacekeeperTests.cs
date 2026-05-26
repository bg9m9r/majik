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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Anointed Peacekeeper (Dominaria United, {1}{W}{W}).
///
/// Oracle:
///   "Vigilance.
///    As Anointed Peacekeeper enters the battlefield, look at an
///    opponent's hand, then choose any card name.
///    Activated abilities of sources with the chosen name cost {2} more
///    to activate unless they're mana abilities.
///    Spells with the chosen name cost {2} more to cast."
///
/// Coverage:
///   * Identity — Human Cleric 2/3 {1}{W}{W} with Vigilance.
///   * NamedCardFactory dispatch.
///   * Spell-name cost increase: a Bob spell whose name matches the
///     chosen name costs {2} more; non-matching spells are unaffected.
///   * Off-battlefield → rider is inert (CostReduction scanner only
///     walks battlefield permanents).
///   * Null nameSelector → predicate matches nothing.
///
/// The activated-ability cost-tax half is documented as a v1 gap in
/// <see cref="AnointedPeacekeeperFactory"/>'s class xmldoc.
/// </summary>
public class AnointedPeacekeeperTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice);

        peacekeeper.Name.Should().Be("Anointed Peacekeeper");
        peacekeeper.HasType(CardType.Creature).Should().BeTrue();
        peacekeeper.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Anointed Peacekeeper is NOT printed as Legendary");
        peacekeeper.HasSubtype(CardSubtype.Human).Should().BeTrue();
        peacekeeper.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        peacekeeper.ManaCost.Should().Be("{1}{W}{W}");
        peacekeeper.ManaCostValue.Generic.Should().Be(1);
        peacekeeper.ManaCostValue.White.Should().Be(2);
        peacekeeper.Power.Should().Be(2);
        peacekeeper.Toughness.Should().Be(3);
        peacekeeper.Owner.Should().BeSameAs(_alice);
        peacekeeper.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasVigilanceKeyword()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice);

        peacekeeper.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k =>
                k.Keyword.Equals("Vigilance", StringComparison.OrdinalIgnoreCase),
                "CR 702.20 — Vigilance keyword marker must be attached");
    }

    [Fact]
    public void Create_HasSpellCostIncreaseAbility()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice);

        peacekeeper.Abilities.OfType<SpellCostIncreaseAbility>()
            .Should().HaveCount(1,
                "the chosen-name spell-cost-increase rider must be attached");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsPeacekeeperShape()
    {
        var card = NamedCardFactory.Create("Anointed Peacekeeper", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Anointed Peacekeeper");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        card.Abilities.OfType<SpellCostIncreaseAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Spell-name cost increase (CR 117.7 / CR 601.2f)
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedSpell_CostsTwoMoreGeneric_WhilePeacekeeperIsOut()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(
            _alice, nameSelector: _ => "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(peacekeeper);
        peacekeeper.SetZone(ZoneType.Battlefield);

        // Bob casts the named spell.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            bolt, _bob, new[] { _alice, _bob });

        effective.Generic.Should().Be(2,
            "Anointed Peacekeeper taxes spells with the chosen name +{2} generic");
        effective.Red.Should().Be(1, "coloured pips are untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NonMatchingSpell_NotTaxed()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(
            _alice, nameSelector: _ => "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(peacekeeper);
        peacekeeper.SetZone(ZoneType.Battlefield);

        // Bob casts a DIFFERENT spell.
        var counterspell = new Instant("Counterspell", "{U}{U}");
        counterspell.SetOwner(_bob);
        counterspell.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, _bob, new[] { _alice, _bob });

        effective.Generic.Should().Be(0,
            "the rider only matches the chosen name — Counterspell is untouched");
        effective.Blue.Should().Be(2);
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void PeacekeeperOffBattlefield_RiderIsInert()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(
            _alice, nameSelector: _ => "Lightning Bolt");
        // NOT on the battlefield.

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            bolt, _bob, new[] { _alice, _bob });

        effective.Generic.Should().Be(0,
            "Peacekeeper is not on the battlefield — CostReduction scanner skips its rider");
        effective.TotalValue.Should().Be(1, "printed cost stands");
    }

    [Fact]
    public void NullNameSelector_RiderIsDormant_NoSpellsAffected()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice); // no selector
        _alice.Zones.Battlefield.AddCard(peacekeeper);
        peacekeeper.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);

        var effective = CostReduction.GetEffectiveCost(
            bolt, _bob, new[] { _alice, _bob });

        effective.Generic.Should().Be(0,
            "with no nameSelector wired the rider can't pick a name and matches nothing");
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Symmetric_OwnSpellAlsoTaxed_WhenNameMatches()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(
            _alice, nameSelector: _ => "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(peacekeeper);
        peacekeeper.SetZone(ZoneType.Battlefield);

        // Alice — Peacekeeper's controller — also tries to cast the chosen name.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);

        var effective = CostReduction.GetEffectiveCost(
            bolt, _alice, new[] { _alice, _bob });

        effective.Generic.Should().Be(2,
            "Anointed Peacekeeper is symmetric (matches Thalia / Damping Sphere) — own matching spells also cost {2} more");
    }
}
