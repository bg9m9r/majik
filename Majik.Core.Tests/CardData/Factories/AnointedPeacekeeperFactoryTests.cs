using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Anointed Peacekeeper (Dominaria United, {1}{W}{W}).
///
/// Covers:
///   - Card shape: name, type, Human + Cleric subtypes, P/T 2/4, mana
///     cost, owner / controller wiring.
///   - SpellCostIncreaseAbility wiring — the chosen-name spell tax is
///     visible through <see cref="CostReduction.GetEffectiveCost"/>:
///       • Matching-name spell costs +{2}.
///       • Non-matching-name spell unaffected.
///       • Null name selector → no tax.
///   - NamedCardFactory dispatch routes the card name to this factory.
///
/// The activated-ability cost tax is a documented v1 gap (no shared
/// primitive — see class xmldoc). No tests assert activated-ability
/// semantics until the primitive lands.
/// </summary>
public class AnointedPeacekeeperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Peacekeeper_IsCreature_HumanCleric_2_4_AtCost1WW()
    {
        var c = AnointedPeacekeeperFactory.Create(_alice);

        c.Name.Should().Be("Anointed Peacekeeper");
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Peacekeeper_AttachesSpellCostIncreaseAbility()
    {
        var c = AnointedPeacekeeperFactory.Create(_alice, _ => "Lightning Bolt");

        c.Abilities.OfType<SpellCostIncreaseAbility>().Should().ContainSingle();
    }

    [Fact]
    public void Peacekeeper_TaxesMatchingNamedSpell_By2()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice, _ => "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(peacekeeper);

        var bolt = new Sorcery("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        // Peacekeeper taxes any caster (symmetric) — Bob casting his own
        // Bolt is taxed.
        var effective = CostReduction.GetEffectiveCost(
            bolt, _bob, allPlayers: new[] { _alice, _bob });

        // Printed cost {R} (mv 1) + {2} tax = mv 3 generic + {R}.
        effective.TotalValue.Should().Be(1 + 2);
    }

    [Fact]
    public void Peacekeeper_DoesNotTaxNonMatchingSpell()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice, _ => "Lightning Bolt");
        _alice.Zones.Battlefield.AddCard(peacekeeper);

        var counterspell = new Sorcery("Counterspell", "{U}{U}");
        counterspell.SetOwner(_bob);

        var effective = CostReduction.GetEffectiveCost(
            counterspell, _bob, allPlayers: new[] { _alice, _bob });

        // Untouched — printed cost {U}{U} (mv 2).
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Peacekeeper_NoSelector_NoTax()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(peacekeeper);

        var bolt = new Sorcery("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        var effective = CostReduction.GetEffectiveCost(
            bolt, _bob, allPlayers: new[] { _alice, _bob });

        // No name chosen → predicate always false → no tax.
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Peacekeeper_NullNameSelected_NoTax()
    {
        var peacekeeper = AnointedPeacekeeperFactory.Create(_alice, _ => null);
        _alice.Zones.Battlefield.AddCard(peacekeeper);

        var bolt = new Sorcery("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        var effective = CostReduction.GetEffectiveCost(
            bolt, _bob, allPlayers: new[] { _alice, _bob });

        // Selector returned null → predicate false → no tax.
        effective.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Peacekeeper_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Anointed Peacekeeper", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Anointed Peacekeeper");
        ((Creature)card).Power.Should().Be(2);
        ((Creature)card).Toughness.Should().Be(4);
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }
}
