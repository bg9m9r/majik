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
/// Tests for <see cref="GoblinCratermakerFactory"/> — 2/2 Goblin Warrior
/// {1}{R} with "Choose one — ~ deals 2 damage to target creature; or
/// destroy target colorless nonland permanent." (cost = {1} + sac self).
///
/// Mode A and Mode B are modelled as two separate <see cref="ActivatedAbility"/>s
/// sharing the {1} + sacrifice cost. The activating player picks which
/// activation to use (= which mode).
/// </summary>
public class GoblinCratermakerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GoblinCratermaker_IsRedGoblinWarrior_TwoTwo_For1R()
    {
        var cm = GoblinCratermakerFactory.Create(_alice);

        cm.HasType(CardType.Creature).Should().BeTrue();
        cm.Name.Should().Be("Goblin Cratermaker");
        cm.ManaCost.Should().Be("{1}{R}");
        cm.Power.Should().Be(2);
        cm.Toughness.Should().Be(2);
        cm.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        cm.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        cm.Owner.Should().BeSameAs(_alice);
        cm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinCratermaker()
    {
        var card = NamedCardFactory.Create("Goblin Cratermaker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Cratermaker");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTwoActivatedAbilities_OneTargetCreature_OneTargetColorlessNonlandPermanent()
    {
        var cm = GoblinCratermakerFactory.Create(_alice);
        var activated = cm.Abilities.OfType<ActivatedAbility>().ToList();

        activated.Should().HaveCount(2);

        activated.Should().ContainSingle(a =>
            a.TargetRequests.Count == 1 &&
            a.TargetRequests[0].Description == "target creature");
        activated.Should().ContainSingle(a =>
            a.TargetRequests.Count == 1 &&
            a.TargetRequests[0].Description.Contains("colorless nonland permanent"));
    }

    [Fact]
    public void BothAbilities_ShareOneManaSacrificeCostShape()
    {
        var cm = GoblinCratermakerFactory.Create(_alice);
        foreach (var ability in cm.Abilities.OfType<ActivatedAbility>())
        {
            ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
                "{1} mana payment");
            ability.Costs.OfType<AdditionalCost>()
                .Should().ContainSingle(c => c.CostType == AdditionalCostType.Sacrifice,
                    "self-sacrifice");
        }
    }

    // -----------------------------------------------------------------------
    // Mode A — 2 damage to target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void ModeA_DealsTwoToTargetCreature_AndSacrificesSelf()
    {
        var cm = GoblinCratermakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cm);
        cm.SetZone(ZoneType.Battlefield);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var modeA = cm.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests[0].Description == "target creature");
        modeA.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        modeA.Resolve();

        bears.Damage.Should().Be(2);
        _alice.Zones.Graveyard.GetCards().Should().Contain(cm);
        cm.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Mode B — destroy target colorless nonland permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void ModeB_DestroysColorlessNonlandPermanent_AndSacrificesSelf()
    {
        var cm = GoblinCratermakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cm);
        cm.SetZone(ZoneType.Battlefield);

        // Colorless artifact (no coloured pips).
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_bob);
        solRing.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(solRing);
        solRing.SetZone(ZoneType.Battlefield);

        var modeB = cm.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests[0].Description.Contains("colorless nonland permanent"));
        modeB.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { solRing },
        });

        modeB.Resolve();

        solRing.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(solRing);
        _alice.Zones.Graveyard.GetCards().Should().Contain(cm,
            "Cratermaker still sacrifices itself");
    }

    [Fact]
    public void ModeB_IllegalOnColoredPermanent_NoDestroy_ButSelfStillSacs()
    {
        // CR 608.2b — illegal target → effect does nothing. The activation
        // cost was already paid, so the sacrifice still resolves.
        var cm = GoblinCratermakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cm);
        cm.SetZone(ZoneType.Battlefield);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var modeB = cm.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests[0].Description.Contains("colorless nonland permanent"));
        modeB.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        modeB.Resolve();

        bears.Zone.Should().Be(ZoneType.Battlefield, "green creature isn't a legal target");
        cm.Zone.Should().Be(ZoneType.Graveyard, "Cratermaker sacrificed regardless");
    }

    [Fact]
    public void ModeB_IllegalOnLand_NoDestroy()
    {
        // Even a colourless land fails — oracle excludes lands explicitly.
        var cm = GoblinCratermakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cm);
        cm.SetZone(ZoneType.Battlefield);

        var wastes = new Land("Wastes");
        wastes.SetOwner(_bob);
        wastes.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(wastes);
        wastes.SetZone(ZoneType.Battlefield);

        var modeB = cm.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests[0].Description.Contains("colorless nonland permanent"));
        modeB.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { wastes },
        });

        modeB.Resolve();

        wastes.Zone.Should().Be(ZoneType.Battlefield);
        cm.Zone.Should().Be(ZoneType.Graveyard);
    }
}
