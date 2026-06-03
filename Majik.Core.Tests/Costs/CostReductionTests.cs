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

    // -----------------------------------------------------------------------
    // Opponent-board-aware cost reduction (ContextReducer / ReducerContext).
    // The caster-only TotalReducer seam could only see the caster's own board;
    // ContextReducer widens the closure's input to the full roster so a
    // reducer can count permanents an OPPONENT controls (Hagra Mauling).
    // -----------------------------------------------------------------------

    private static Land Basic(Player owner, CardSubtype subtype, string name)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    [Fact]
    public void OpponentBoardReducer_CountsOpponentPermanents_NotCasterOwn()
    {
        var bob = new Player("Bob", 20);

        // "Costs {1} less for each basic land an OPPONENT controls."
        // {4} printed. Caster's own basics must NOT count; only opponent's.
        var card = new Creature("Probe", "4", 0, 1) { Owner = _alice };
        card.AddAbility(new OpponentBoardCostReductionAbility(
            ctx => ctx.Opponents
                .SelectMany(p => p.Zones.Battlefield.GetCards())
                .Count(c => c is Land l && l.HasSupertype(CardSupertype.Basic)),
            "costs {1} less for each basic land an opponent controls"));

        // Caster controls 3 basics (must be ignored), opponent controls 2.
        Basic(_alice, CardSubtype.Plains, "P1");
        Basic(_alice, CardSubtype.Island, "I1");
        Basic(_alice, CardSubtype.Swamp, "S1");
        Basic(bob, CardSubtype.Mountain, "M1");
        Basic(bob, CardSubtype.Forest, "F1");

        var roster = new[] { _alice, bob };
        CostReduction.GetEffectiveCost(card, _alice, roster).Generic.Should().Be(2);
    }

    [Fact]
    public void OpponentBoardReducer_NoRoster_SeesNoOpponents()
    {
        var bob = new Player("Bob", 20);
        var card = new Creature("Probe", "4", 0, 1) { Owner = _alice };
        card.AddAbility(new OpponentBoardCostReductionAbility(
            ctx => ctx.Opponents.SelectMany(p => p.Zones.Battlefield.GetCards()).Count(),
            "costs {1} less for each permanent an opponent controls"));

        Basic(bob, CardSubtype.Mountain, "M1");

        // allPlayers null → caster-only context → opponent count is 0 → no reduction.
        CostReduction.GetEffectiveCost(card, _alice).Generic.Should().Be(4);
    }

    [Fact]
    public void HagraMauling_CostsOneLess_WhenOpponentControlsNoBasicLands()
    {
        var bob = new Player("Bob", 20);
        var hagra = HagraMaulingFactory.Create(_alice);

        // Opponent controls no basic lands → {2}{B}{B} → {1}{B}{B} (generic 2→1).
        var roster = new[] { _alice, bob };
        var cost = CostReduction.GetEffectiveCost(hagra, _alice, roster);
        cost.Generic.Should().Be(1);
        cost.Black.Should().Be(2);
    }

    [Fact]
    public void HagraMauling_FullCost_WhenOpponentControlsABasicLand()
    {
        var bob = new Player("Bob", 20);
        var hagra = HagraMaulingFactory.Create(_alice);
        Basic(bob, CardSubtype.Swamp, "Swamp1");

        var roster = new[] { _alice, bob };
        var cost = CostReduction.GetEffectiveCost(hagra, _alice, roster);
        cost.Generic.Should().Be(2);
        cost.Black.Should().Be(2);
    }
}
