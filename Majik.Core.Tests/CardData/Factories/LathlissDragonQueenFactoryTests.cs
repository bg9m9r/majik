using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LathlissDragonQueenFactory"/>.
///
/// Oracle (Scryfall-confirmed, Dominaria, {4}{R}{R}, Legendary Creature —
/// Dragon 6/6):
///   "Flying
///    Whenever another nontoken Dragon you control enters, create a 5/5 red
///    Dragon creature token with flying.
///    {1}{R}: Dragons you control get +1/+0 until end of turn."
///
/// Covers ONLY the card's unique behaviour:
/// - Identity ({4}{R}{R}, 6/6, Legendary, Dragon).
/// - Printed Flying keyword (CR 702.9).
/// - The "another nontoken Dragon you control enters" trigger condition gates
///   (another / nontoken / Dragon / you-control) + the token-mint resolve.
/// - The "{1}{R}: Dragons you control get +1/+0 until end of turn." activated
///   ability cost + Dragon-scoped pump.
///
/// Dispatch + well-formedness are asserted globally by
/// <see cref="Majik.Core.Tests.CardData.CardFactoryContractTests"/>.
/// </summary>
[Trait("Color", "R")]
public class LathlissDragonQueenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // Build a controller-Dragon (nontoken by default) for ETB-trigger probes.
    private static Creature MakeDragon(
        Player controller, string name = "Some Dragon", bool token = false)
    {
        var d = new Creature(name, "{4}{R}", 4, 4,
            subtypes: new[] { CardSubtype.Dragon })
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
        };
        if (token) d.MarkAsToken();
        return d;
    }

    private TriggeredAbility EntersTrigger(Creature lathliss) =>
        lathliss.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private ActivatedAbility PumpAbility(Creature lathliss) =>
        lathliss.Abilities.OfType<ActivatedAbility>().Single();

    private static bool Fires(TriggeredAbility trigger, Creature entering) =>
        trigger.Condition.Matches(
            new CardMovedEvent(entering, ZoneType.Hand, ZoneType.Battlefield), trigger);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Lathliss_Identity()
    {
        var c = LathlissDragonQueenFactory.Create(_alice);

        c.Name.Should().Be("Lathliss, Dragon Queen");
        c.ManaCost.Should().Be("{4}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Flying (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void Lathliss_HasFlying()
    {
        var c = LathlissDragonQueenFactory.Create(_alice);
        c.Zone = ZoneType.Battlefield;

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying", "CR 702.9 — Lathliss's first printed keyword is Flying.");
        CombatAbilities.HasFlying(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // "Whenever another nontoken Dragon you control enters" — condition gates
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTrigger_Fires_ForAnotherNontokenDragonYouControl()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;

        Fires(EntersTrigger(lathliss), MakeDragon(_alice))
            .Should().BeTrue("another nontoken Dragon you control entering fires it (CR 603.6e).");
    }

    [Fact]
    public void EntersTrigger_DoesNotFire_ForTokenDragon()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;

        Fires(EntersTrigger(lathliss), MakeDragon(_alice, token: true))
            .Should().BeFalse(
                "the 'nontoken' clause (CR 111) excludes token Dragons — load-bearing, " +
                "since the created 5/5 token is itself a Dragon and would otherwise cascade.");
    }

    [Fact]
    public void EntersTrigger_DoesNotFire_ForNonDragon()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };

        Fires(EntersTrigger(lathliss), bear)
            .Should().BeFalse("only a Dragon entering fires it (CR 205.3).");
    }

    [Fact]
    public void EntersTrigger_DoesNotFire_ForOpponentDragon()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;

        Fires(EntersTrigger(lathliss), MakeDragon(_bob))
            .Should().BeFalse("'you control' scopes to the controller's Dragons (CR 109.5).");
    }

    [Fact]
    public void EntersTrigger_DoesNotFire_ForLathlissOwnEntry()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;

        Fires(EntersTrigger(lathliss), lathliss)
            .Should().BeFalse("the 'another' clause excludes Lathliss's own entry (CR 603.6e).");
    }

    // -----------------------------------------------------------------------
    // Token-mint resolve: 5/5 red Dragon token with flying
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTrigger_Resolve_CreatesFiveFiveRedFlyingDragonToken()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(lathliss);

        foreach (var e in EntersTrigger(lathliss).Effects) e.Execute();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        token.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        token.GetPower().Should().Be(5, "the created Dragon token is 5/5.");
        token.GetToughness().Should().Be(5);
        CombatAbilities.HasFlying(token).Should().BeTrue("the token has flying.");
        token.GetEffectiveColors().Should().Contain(ManaColor.Red, "the token is red (CR 111.4).");
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // "{1}{R}: Dragons you control get +1/+0 until end of turn."
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_HasManaCostOneRed()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        var ability = PumpAbility(lathliss);

        var mana = ability.Costs.OfType<ManaCostCost>().Single();
        mana.Cost.Red.Should().Be(1, "the activation cost is {1}{R}.");
        mana.Cost.Generic.Should().Be(1);
        ability.Costs.Should().NotContain(c => c is AdditionalCost,
            "the ability has no tap cost — it is repeatable.");
    }

    [Fact]
    public void PumpAbility_Resolve_PumpsControllerDragonsPlusOnePlusZero_IncludingSelf()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;
        lathliss.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(lathliss);

        var otherDragon = MakeDragon(_alice);
        otherDragon.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(otherDragon);

        PumpAbility(lathliss).Effects.Single().Execute();

        otherDragon.GetPower().Should().Be(5, "+1/+0 raises 4 power to 5.");
        otherDragon.GetToughness().Should().Be(4, "+1/+0 leaves toughness unchanged.");
        lathliss.GetPower().Should().Be(7,
            "'Dragons you control' includes Lathliss herself — 6 power +1 = 7.");
        lathliss.GetToughness().Should().Be(6);
    }

    [Fact]
    public void PumpAbility_Resolve_DoesNotPumpNonDragonsOrOpponents()
    {
        var lathliss = LathlissDragonQueenFactory.Create(_alice);
        lathliss.Zone = ZoneType.Battlefield;
        lathliss.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(lathliss);

        var myBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
            ActiveEffects = new ContinuousEffectsService(),
        };
        _alice.Zones.Battlefield.AddCard(myBear);

        var oppDragon = MakeDragon(_bob);
        oppDragon.ActiveEffects = new ContinuousEffectsService();
        _bob.Zones.Battlefield.AddCard(oppDragon);

        PumpAbility(lathliss).Effects.Single().Execute();

        myBear.GetPower().Should().Be(2, "only Dragons are pumped (CR 205.3).");
        oppDragon.GetPower().Should().Be(4, "only Dragons YOU control are pumped (CR 109.5).");
    }
}
