using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

public class CostReductionTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AffinityForArtifacts_ReducesGenericByEachArtifact()
    {
        // 0/4 Kappa Cannoneer-style cost 6, "This spell costs {1} less to
        // cast for each artifact you control." With 3 artifacts on
        // battlefield, effective cost = {3}.
        var card = new Creature("Kappa", "6", 0, 4) { Owner = _alice };
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));

        for (var i = 0; i < 3; i++)
        {
            var art = new Artifact($"Art{i}", "") { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
            _alice.Zones.Battlefield.AddCard(art);
        }

        CostReduction.GetEffectiveCost(card, _alice).Generic.Should().Be(3);
    }

    [Fact]
    public void AffinityForArtifacts_NoArtifacts_FullCost()
    {
        var card = new Creature("Kappa", "6", 0, 4) { Owner = _alice };
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        CostReduction.GetEffectiveCost(card, _alice).Generic.Should().Be(6);
    }

    [Fact]
    public void AffinityForArtifacts_FloorsAtZero()
    {
        var card = new Creature("Cheap", "2U", 1, 1) { Owner = _alice };
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));
        for (var i = 0; i < 10; i++)
        {
            _alice.Zones.Battlefield.AddCard(
                new Artifact($"A{i}", "") { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield });
        }
        var cost = CostReduction.GetEffectiveCost(card, _alice);
        cost.Generic.Should().Be(0);
        cost.Blue.Should().Be(1); // colored pips untouched
    }

    [Fact]
    public void AffinityBinder_MatchesOracleText()
    {
        var card = new Creature("Test", "6", 0, 4);
        var ok = AffinityBinder.Bind(card,
            new CardEntity { Name = "Test",
              OracleText = "This spell costs {1} less to cast for each artifact you control." });
        ok.Should().BeTrue();
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AffinityBinder_NonMatching_NoBinding()
    {
        var card = new Creature("Test", "3", 1, 1);
        var ok = AffinityBinder.Bind(card,
            new CardEntity { Name = "Test", OracleText = "Flying. Vigilance." });
        ok.Should().BeFalse();
        card.Abilities.OfType<CostReductionAbility>().Should().BeEmpty();
    }
}
