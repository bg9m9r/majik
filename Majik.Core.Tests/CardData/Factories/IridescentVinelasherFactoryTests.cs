using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="IridescentVinelasherFactory"/> (Bloomburrow, {B}).
/// Creature — Lizard Assassin 1/2.
///
/// Oracle text (Scryfall, verified 2026-06-23):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Landfall — Whenever a land you control enters, this creature deals 1
///    damage to target opponent."
///
/// Covers ONLY the card's unique behaviour (dispatch + well-formedness are
/// asserted for every implemented card by CardFactoryContractTests):
///   - Identity (name, {B}, Lizard + Assassin subtypes, 1/2).
///   - Offspring keyword marker + the {2} additional cost are exposed
///     (CR 702.169).
///   - Landfall trigger (CR 603.6a) targets an opponent (1..1) and fires only
///     on a land entering under the controller's control.
///   - Trigger body deals 1 damage to a target opponent (CR 119.3).
/// </summary>
[Trait("Color", "B")]
public class IridescentVinelasherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetLandfallTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void IridescentVinelasher_Identity_LizardAssassin_1_2_AtCostB()
    {
        var card = IridescentVinelasherFactory.Create(_alice);

        card.Name.Should().Be("Iridescent Vinelasher");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        card.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Offspring {2} (CR 702.169)
    // -----------------------------------------------------------------------

    [Fact]
    public void IridescentVinelasher_HasOffspringKeyword_AndBuildsTheAdditionalCost()
    {
        var card = IridescentVinelasherFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Offspring",
                "CR 702.169 — Offspring is exposed on the keyword scan surface.");

        // CR 702.169a — the optional additional cost the caller layers onto the
        // cast is the {2} Offspring cost.
        IridescentVinelasherFactory.OffspringCost.TotalValue.Should().Be(2);
        IridescentVinelasherFactory.BuildOffspringCost(card).Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Landfall — target opponent (CR 603.6a / 115.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void IridescentVinelasher_LandfallTrigger_TargetsOneOpponent()
    {
        var card = IridescentVinelasherFactory.Create(_alice);

        var trigger = GetLandfallTrigger(card);
        trigger.Source.Should().BeSameAs(card);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);

        var req = trigger.TargetRequests.Should().ContainSingle().Subject;
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("opponent");
    }

    [Fact]
    public void IridescentVinelasher_OwnersLandEnters_QueuesTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var card = IridescentVinelasherFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        triggers.BindCard(card);

        var swamp = new Land("Swamp");
        swamp.SetOwner(_alice);
        swamp.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(swamp);

        zones.MoveCardTo(swamp, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "landfall must queue when a land enters under the controller's control (CR 603.6a).");
    }

    [Fact]
    public void IridescentVinelasher_OpponentsLandEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var card = IridescentVinelasherFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        triggers.BindCard(card);

        var bobLand = new Land("Forest");
        bobLand.SetOwner(_bob);
        bobLand.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobLand);

        zones.MoveCardTo(bobLand, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "landfall only triggers on a land entering under YOUR control (CR 603.6a).");
    }

    // -----------------------------------------------------------------------
    // Landfall body — deal 1 damage to target opponent (CR 119.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void IridescentVinelasher_LandfallBody_Deals1DamageToTargetOpponent()
    {
        // PROD PATH — dispatch the single-arg overload the routed build uses (no
        // resolver), then resolve the landfall trigger through a live
        // GameContext so the damage reads its target off ContextOpponents.Of.
        var card = (Creature)NamedCardFactory.Create("Iridescent Vinelasher", _alice);
        card.SetZone(ZoneType.Battlefield);

        var trigger = GetLandfallTrigger(card);
        Helpers.ContextResolve.Resolve(trigger, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "CR 119.3 — the target opponent takes 1 damage.");
        _alice.LifeTotal.Should().Be(20, "the controller is never the damage target.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (Majik.Core.Services.ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new Majik.Core.Services.ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
