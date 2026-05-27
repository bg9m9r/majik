using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GoblinSledderFactory"/> — 1/1 Goblin {R} with
/// "Sacrifice a Goblin: Target creature gets +1/+1 until end of turn."
///
/// Covers:
/// - Identity (Creature, {R}, 1/1, Goblin).
/// - NamedCardFactory dispatch.
/// - Activated ability shape: SacrificeAGoblinCost + 1..1 target creature.
/// - Cost cannot be paid without a Goblin on the battlefield.
/// - Cost CAN be paid when only self is on the battlefield (no "another").
/// - Resolution: pump applies via PumpUntilEndOfTurnEffect.
/// - Cost prefers another Goblin over self.
/// </summary>
public class GoblinSledderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinSledder_IsRedGoblin_OneOne_ForR()
    {
        var sledder = GoblinSledderFactory.Create(_alice);

        sledder.HasType(CardType.Creature).Should().BeTrue();
        sledder.Name.Should().Be("Goblin Sledder");
        sledder.ManaCost.Should().Be("{R}");
        sledder.Power.Should().Be(1);
        sledder.Toughness.Should().Be(1);
        sledder.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        sledder.Owner.Should().BeSameAs(_alice);
        sledder.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinSledder()
    {
        var card = NamedCardFactory.Create("Goblin Sledder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Sledder");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Ability_HasSacrificeAGoblinCost_AndOneTargetCreature()
    {
        var sledder = GoblinSledderFactory.Create(_alice);

        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<SacrificeAGoblinCost>().Should().HaveCount(1);

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Be("target creature");
    }

    // -----------------------------------------------------------------------
    // SacrificeAGoblinCost legality
    // -----------------------------------------------------------------------

    [Fact]
    public void CanPay_False_WhenNoGoblinOnBattlefield()
    {
        var sledder = GoblinSledderFactory.Create(_alice);
        // Sledder NOT on battlefield → no Goblin available.
        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ability.Costs.OfType<SacrificeAGoblinCost>().Single();

        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_True_WhenSelfIsOnBattlefield_NoAnotherQualifier()
    {
        // Oracle has no "another" — Sledder itself satisfies the cost.
        var sledder = GoblinSledderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sledder);
        sledder.SetZone(ZoneType.Battlefield);

        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ability.Costs.OfType<SacrificeAGoblinCost>().Single();

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void Pay_PrefersAnotherGoblin_OverSelf()
    {
        var sledder = GoblinSledderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sledder);
        sledder.SetZone(ZoneType.Battlefield);

        var lackey = new Creature("Goblin Lackey", "{R}", 1, 1,
            subtypes: new[] { CardSubtype.Goblin });
        lackey.SetOwner(_alice);
        lackey.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(lackey);
        lackey.SetZone(ZoneType.Battlefield);

        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ability.Costs.OfType<SacrificeAGoblinCost>().Single();

        cost.Pay(_alice);

        cost.Sacrificed.Should().BeSameAs(lackey,
            "the picker prefers another Goblin before self");
        lackey.Zone.Should().Be(ZoneType.Graveyard);
        sledder.Zone.Should().Be(ZoneType.Battlefield, "Sledder still on board");
    }

    [Fact]
    public void Pay_FallsBackToSelf_WhenOnlyGoblinOnBattlefield()
    {
        var sledder = GoblinSledderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sledder);
        sledder.SetZone(ZoneType.Battlefield);

        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        var cost = ability.Costs.OfType<SacrificeAGoblinCost>().Single();

        cost.Pay(_alice);

        cost.Sacrificed.Should().BeSameAs(sledder);
        sledder.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_TargetCreatureGetsPlusOnePlusOne_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();

        var attacker = new Creature("Attacker", "{R}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var sledder = GoblinSledderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sledder);
        sledder.SetZone(ZoneType.Battlefield);

        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { attacker },
        });

        attacker.GetPower().Should().Be(2);
        attacker.GetToughness().Should().Be(2);

        ability.Resolve();

        attacker.GetPower().Should().Be(3, "+1 power EOT");
        attacker.GetToughness().Should().Be(3, "+1 toughness EOT");
    }

    [Fact]
    public void Activate_IllegalTarget_NoOp_ButEffectStillRuns()
    {
        // Target left the battlefield between activation and resolution.
        // CR 608.2b — effect involving an illegal target does nothing.
        var svc = new ContinuousEffectsService();
        var attacker = new Creature("Attacker", "{R}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Graveyard, // already off the battlefield
            ActiveEffects = svc,
        };

        var sledder = GoblinSledderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sledder);
        sledder.SetZone(ZoneType.Battlefield);

        var ability = sledder.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { attacker },
        });

        ability.Resolve();

        attacker.GetPower().Should().Be(2, "illegal target — no pump applied");
        attacker.GetToughness().Should().Be(2);
    }
}
