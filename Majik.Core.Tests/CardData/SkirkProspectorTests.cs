using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Skirk Prospector (Onslaught / many reprints, {R}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Goblin subtype, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Mana ability shape: one <see cref="ManaAbility"/>; non-tap activation
///     ("Sacrifice a Goblin: Add {R}." has no {T} in the printed cost).
///   - Activation gate: no Goblins on battlefield = cannot activate; at
///     least one Goblin (including self) = can activate.
///   - Activation effect: sacrificing a Goblin produces {R} on the
///     controller's mana pool; chosen Goblin lands in the graveyard.
///   - Self-sacrifice: when Prospector is the only Goblin available, it
///     sacrifices itself (oracle has no "another" qualifier).
///   - Picker preference: when another Goblin is on the battlefield,
///     Prospector prefers sacrificing the other Goblin first (deterministic
///     v1 heuristic — saves Prospector for the chain end).
/// </summary>
public class SkirkProspectorTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SkirkProspector_Is_GoblinCreature_1_1_AtCostR()
    {
        var pros = SkirkProspectorFactory.Create(_alice);

        pros.Name.Should().Be("Skirk Prospector");
        pros.ManaCost.Should().Be("{R}");
        pros.HasType(CardType.Creature).Should().BeTrue();
        pros.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        pros.BasePower.Should().Be(1);
        pros.BaseToughness.Should().Be(1);
        pros.Owner.Should().BeSameAs(_alice);
        pros.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SkirkProspector()
    {
        var card = NamedCardFactory.Create("Skirk Prospector", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Skirk Prospector");
        card.ManaCost.Should().Be("{R}");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "sacrifice-a-Goblin mana ability is wired");
    }

    [Fact]
    public void SkirkProspector_HasExactlyOneManaAbility()
    {
        var pros = SkirkProspectorFactory.Create(_alice);
        pros.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        pros.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "mana abilities (CR 605) don't use the activated-ability infrastructure");
    }

    [Fact]
    public void ManaAbility_CannotActivate_WhenNoGoblinsOnBattlefield()
    {
        var pros = SkirkProspectorFactory.Create(_alice);
        // Prospector itself NOT on the battlefield yet — no Goblin to sac.
        var mana = pros.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse(
            "no Goblin to sacrifice = mana ability cannot activate");
    }

    [Fact]
    public void ManaAbility_CanActivate_WhenSelfIsOnlyGoblin()
    {
        var pros = SkirkProspectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pros);
        pros.SetZone(ZoneType.Battlefield);

        var mana = pros.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeTrue(
            "Prospector is a Goblin he controls — sacrifice self is legal (no 'another' qualifier in oracle)");
    }

    [Fact]
    public void Activation_SelfOnly_SacrificesSelf_AndAddsRedMana()
    {
        var pros = SkirkProspectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pros);
        pros.SetZone(ZoneType.Battlefield);

        var mana = pros.Abilities.OfType<ManaAbility>().Single();

        // ManaAbility.Activate() returns the produced mana cost and pays
        // the side-effect cost (sacrifice). Mana is added to the player's
        // pool by ManaAbilityActivator; we model that step inline so the
        // controller-side pool ends up with the produced {R}.
        var produced = mana.Activate();
        _alice.AddManaToPool(produced);

        produced.Red.Should().Be(1, "printed output is {R}");
        pros.Zone.Should().Be(ZoneType.Graveyard,
            "self was the only Goblin available, so Prospector sacrifices himself");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(pros);
        _alice.Zones.Graveyard.GetCards().Should().Contain(pros);
        _alice.ManaPool.Red.Should().Be(1,
            "controller's mana pool receives the {R} produced");
    }

    [Fact]
    public void Activation_PrefersOtherGoblinOverSelf()
    {
        var pros = SkirkProspectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pros);
        pros.SetZone(ZoneType.Battlefield);

        var fodder = new Creature(
            name: "Goblin Piker",
            manaCost: "{1}{R}",
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Goblin });
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var mana = pros.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();
        _alice.AddManaToPool(produced);

        fodder.Zone.Should().Be(ZoneType.Graveyard,
            "deterministic v1 picker: prefer another Goblin over self");
        pros.Zone.Should().Be(ZoneType.Battlefield,
            "Prospector stays on the battlefield when other Goblins are available to feed it");
        _alice.ManaPool.Red.Should().Be(1);
    }

    [Fact]
    public void ManaAbility_DoesNotTapSource()
    {
        // "Sacrifice a Goblin: Add {R}." has no {T} in the printed cost —
        // Prospector should NOT be tapped by the activation (when self is
        // not the sacrificed Goblin).
        var pros = SkirkProspectorFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pros);
        pros.SetZone(ZoneType.Battlefield);

        var fodder = new Creature(
            "Goblin Piker", "{1}{R}", 2, 1,
            subtypes: new[] { CardSubtype.Goblin });
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var mana = pros.Abilities.OfType<ManaAbility>().Single();
        mana.Activate();

        pros.IsTapped.Should().BeFalse(
            "the printed mana ability has no {T} cost (CR 605.1 — cost is the sacrifice only)");
    }
}
