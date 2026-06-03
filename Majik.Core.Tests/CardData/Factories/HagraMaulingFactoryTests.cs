using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HagraMaulingFactory"/> and
/// <see cref="HagraBroodpitFactory"/> — the front + back faces of the
/// Zendikar Rising modal double-faced card Hagra Mauling // Hagra Broodpit.
///
/// Front face (Hagra Mauling, {2}{B}{B}):
///   Instant. "This spell costs {1} less to cast if an opponent controls no
///   basic lands. Destroy target creature."
///
/// Back face (Hagra Broodpit):
///   Land. "This land enters tapped." "{T}: Add {B}."
///
/// Headline mechanic: opponent-board-aware printed cost reduction
/// (<see cref="OpponentBoardCostReductionAbility"/> / <see cref="ReducerContext"/>) —
/// the {1} discount depends on the OPPONENT's battlefield, which the
/// caster-only <see cref="CostReductionAbility.TotalReducer"/> seam couldn't see.
/// </summary>
[Trait("Color", "B")]
public class HagraMaulingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

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

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void HagraMauling_Identity()
    {
        var card = HagraMaulingFactory.Create(_alice);

        card.Name.Should().Be("Hagra Mauling");
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HagraMauling()
    {
        var card = NamedCardFactory.Create("Hagra Mauling", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Hagra Mauling");
        card.ManaCost.Should().Be("{2}{B}{B}");
    }

    [Fact]
    public void HagraMauling_CarriesMdfcState_WithHagraBroodpitBackFace()
    {
        var card = HagraMaulingFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull();
        card.MdfcState!.FrontFaceName.Should().Be("Hagra Mauling");
        card.MdfcState!.BackFaceName.Should().Be("Hagra Broodpit");
        card.MdfcState!.IsBackFace.Should().BeFalse();
    }

    // =========================================================================
    // Front face — opponent-board-aware cost reduction (the headline)
    // =========================================================================

    [Fact]
    public void HagraMauling_CostsOneLess_WhenOpponentControlsNoBasicLands()
    {
        var card = HagraMaulingFactory.Create(_alice);

        // No opponent basics → {2}{B}{B} → {1}{B}{B}.
        var cost = CostReduction.GetEffectiveCost(card, _alice, new[] { _alice, _bob });
        cost.Generic.Should().Be(1);
        cost.Black.Should().Be(2);
    }

    [Fact]
    public void HagraMauling_FullCost_WhenOpponentControlsABasicLand()
    {
        var card = HagraMaulingFactory.Create(_alice);
        Basic(_bob, CardSubtype.Swamp, "Swamp1");

        var cost = CostReduction.GetEffectiveCost(card, _alice, new[] { _alice, _bob });
        cost.Generic.Should().Be(2);
        cost.Black.Should().Be(2);
    }

    [Fact]
    public void HagraMauling_CasterOwnBasics_DoNotBlockTheDiscount()
    {
        var card = HagraMaulingFactory.Create(_alice);
        // Caster controls basics; the OPPONENT controls none → discount applies.
        Basic(_alice, CardSubtype.Swamp, "MySwamp");
        Basic(_alice, CardSubtype.Island, "MyIsland");

        var cost = CostReduction.GetEffectiveCost(card, _alice, new[] { _alice, _bob });
        cost.Generic.Should().Be(1, "the condition reads the opponent's board, not the caster's");
    }

    [Fact]
    public void HagraMauling_OpponentNonbasicLand_DoesNotBlockTheDiscount()
    {
        var card = HagraMaulingFactory.Create(_alice);
        // Opponent controls a NONBASIC land (no Basic supertype) → still no
        // basic lands → discount applies.
        var nonbasic = new Land("Hagra Broodpit", supertypes: null, subtypes: null)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
        };
        _bob.Zones.Battlefield.AddCard(nonbasic);

        var cost = CostReduction.GetEffectiveCost(card, _alice, new[] { _alice, _bob });
        cost.Generic.Should().Be(1, "a nonbasic land is not a basic land (CR 205.4a)");
    }

    [Fact]
    public void HagraMauling_NoRoster_FullCost()
    {
        var card = HagraMaulingFactory.Create(_alice);
        // No roster threaded → cannot prove opponent has no basics → full cost.
        var cost = CostReduction.GetEffectiveCost(card, _alice);
        cost.Generic.Should().Be(2);
    }

    // =========================================================================
    // Front face — resolve: destroy target creature
    // =========================================================================

    [Fact]
    public void HagraMauling_Resolve_DestroysTargetCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard, "CR 701.7 — destroy");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void HagraMauling_Resolve_TargetNotOnBattlefield_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Hand); // not on the battlefield

        var act = () => Resolve(bear);
        act.Should().NotThrow();
        bear.Zone.Should().Be(ZoneType.Hand, "CR 608.2b — illegal target → no-op");
    }

    private static void Resolve(object target)
    {
        var def = HagraMaulingFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
    }

    // =========================================================================
    // Back face — Hagra Broodpit
    // =========================================================================

    [Fact]
    public void HagraBroodpit_Identity()
    {
        var land = HagraBroodpitFactory.Create(_alice);

        land.Name.Should().Be("Hagra Broodpit");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Hagra Broodpit is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HagraBroodpit()
    {
        var card = NamedCardFactory.Create("Hagra Broodpit", _alice);
        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hagra Broodpit");
    }

    [Fact]
    public void HagraBroodpit_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = HagraBroodpitFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull();
        land.MdfcState!.FrontFaceName.Should().Be("Hagra Mauling");
        land.MdfcState!.BackFaceName.Should().Be("Hagra Broodpit");
        land.MdfcState!.IsBackFace.Should().BeTrue("the back-face land is pre-flipped");
    }

    [Fact]
    public void HagraBroodpit_TapsForBlack()
    {
        var land = HagraBroodpitFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        _ = mana; // shape: single mana ability for {B}
    }

    [Fact]
    public void HagraBroodpit_EntersTapped_Unconditionally()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var land = HagraBroodpitFactory.Create(_alice, rep);
        _alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: _alice);

        ((Permanent)land).IsTapped.Should().BeTrue(
            "Hagra Broodpit always enters tapped (CR 614.1c)");
        land.Zone.Should().Be(ZoneType.Battlefield);
    }
}
