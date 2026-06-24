using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CogworkWrestlerFactory"/>.
///
/// Cogwork Wrestler — Artifact Creature — Gnome 1/2, {U}. Oracle (Scryfall):
///   "Flash
///    When this creature enters, target creature an opponent controls gets
///    -2/-0 until end of turn."
///
/// Covers the card's UNIQUE behaviour:
/// - A single *_Identity assert (name, Artifact+Creature types, Gnome subtype,
///   1/2 P/T, {U} cost) since the printed stats are non-vanilla.
/// - Ability set: a Flash KeywordAbility marker (from the JSON keyword array) +
///   a single ETB TriggeredAbility with a 1..1 TargetRequest.
/// - ETB resolution: registers a −2/-0 PumpUntilEndOfTurnEffect on the chosen
///   opponent creature's ContinuousEffectsService.
/// - Fizzle paths: same-controller target, off-battlefield target, null
///   ActiveEffects (shape-only), no target chosen.
/// - EOT expiration: ContinuousEffectsService.ExpireEndOfTurn drops the debuff.
///
/// (CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness for every implemented card, so no dispatch test here.)
/// </summary>
[Trait("Color", "U")]
public class CogworkWrestlerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity (non-vanilla stats → one consolidated identity assert)
    // -----------------------------------------------------------------------

    [Fact]
    public void CogworkWrestler_Identity()
    {
        var w = CogworkWrestlerFactory.Create(_alice);

        w.Name.Should().Be("Cogwork Wrestler");
        w.HasType(CardType.Creature).Should().BeTrue();
        w.HasType(CardType.Artifact).Should().BeTrue("printed type line is Artifact Creature");
        w.HasSubtype(CardSubtype.Gnome).Should().BeTrue("printed subtype is Gnome");
        w.BasePower.Should().Be(1);
        w.BaseToughness.Should().Be(2);
        w.ManaCost.Should().Be("{U}");
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void CogworkWrestler_HasFlashKeyword()
    {
        var w = CogworkWrestlerFactory.Create(_alice);

        w.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Flash",
                "Cogwork Wrestler has Flash (CR 702.8)");
    }

    [Fact]
    public void CogworkWrestler_HasExactlyOneTriggeredAbility_WithOneTargetRequest()
    {
        var w = CogworkWrestlerFactory.Create(_alice);

        var triggers = w.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the ETB -2/-0 trigger is the only triggered ability");

        var etb = triggers[0];
        etb.TargetRequests.Should().HaveCount(1, "the ETB targets one creature");
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — -2/-0 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void CogworkWrestler_EtbEffect_Applies_Minus2_0_ToLegalTarget()
    {
        var w = CogworkWrestlerFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Hill Giant", "2R", 3, 3);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = w.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var effect in etb.Effects) effect.Execute();

        var computed = service.Compute(target);
        computed.Power.Should().Be(1, "3 power, -2 from Cogwork Wrestler's ETB");
        computed.Toughness.Should().Be(3, "-0 leaves toughness untouched");
    }

    [Fact]
    public void CogworkWrestler_EtbEffect_DebuffExpiresAtEndOfTurn()
    {
        var w = CogworkWrestlerFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Hill Giant", "2R", 3, 3);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = w.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var effect in etb.Effects) effect.Execute();
        service.Compute(target).Power.Should().Be(1, "debuff active during the turn");

        // CR 514.2 — cleanup step expires the until-end-of-turn effect.
        service.ExpireEndOfTurn();

        service.Compute(target).Power.Should().Be(3, "after EOT expiry, base power re-asserts");
    }

    [Fact]
    public void CogworkWrestler_EtbEffect_NoOp_WhenTargetSameController()
    {
        var w = CogworkWrestlerFactory.Create(_alice);

        // Alice's own creature — CR 608.2b re-checks "an opponent controls".
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = w.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var effect in etb.Effects) effect.Execute();

        service.Compute(target).Power.Should().Be(2,
            "target shares controller with the Wrestler — fizzle, no -2/-0 (CR 608.2b)");
    }

    [Fact]
    public void CogworkWrestler_EtbEffect_NoOp_WhenTargetLeftBattlefield()
    {
        var w = CogworkWrestlerFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Graveyard); // already left the battlefield
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = w.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        foreach (var effect in etb.Effects) effect.Execute();

        service.Compute(target).Power.Should().Be(2,
            "target is off-battlefield — CR 608.2b illegal-target check fizzles");
    }

    [Fact]
    public void CogworkWrestler_EtbEffect_NoActiveEffects_DoesNotThrow()
    {
        var w = CogworkWrestlerFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects is null — shape-only.

        var etb = w.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("the effect body guards on a null ActiveEffects");
    }

    [Fact]
    public void CogworkWrestler_EtbEffect_NoTargetChosen_NoOp()
    {
        var w = CogworkWrestlerFactory.Create(_alice);

        var etb = w.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("empty ChosenTargets path is a clean no-op");
    }
}
