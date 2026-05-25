using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Heliod, Sun-Crowned — Legendary Enchantment Creature — God
/// {1}{W}{W} 5/5 (Theros Beyond Death).
///
/// Covers:
/// - Card identity (multi-type Creature + Enchantment + Legendary, P/T,
///   subtype, mana cost).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Indestructible <see cref="KeywordAbility"/> marker.
/// - Lifegain triggered ability — fires on
///   <see cref="LifeChangedEvent"/> for the controller (and not opponents
///   / life-loss); resolution places one +1/+1 counter on the chosen
///   target (creature OR enchantment) on the controller's battlefield.
/// - Activated lifelink-grant ability — wires a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> for "Lifelink" onto
///   the target creature's <see cref="ContinuousEffectsService"/>;
///   "Another" enforced at resolve.
/// - Devotion-to-white computation — pure-{W} pip count across the
///   controller's battlefield.
/// </summary>
public class HeliodSunCrownedTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Heliod_HasCorrectIdentity_AndPT_AndTypes()
    {
        var heliod = HeliodSunCrownedFactory.Create(_alice);

        heliod.Name.Should().Be("Heliod, Sun-Crowned");
        heliod.ManaCost.Should().Be("{1}{W}{W}");
        heliod.Power.Should().Be(5);
        heliod.Toughness.Should().Be(5);
        heliod.HasType(CardType.Creature).Should().BeTrue();
        heliod.HasType(CardType.Enchantment).Should().BeTrue();
        heliod.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        heliod.HasSubtype(CardSubtype.God).Should().BeTrue();
        heliod.Owner.Should().BeSameAs(_alice);
        heliod.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesHeliod_ToFactory()
    {
        var card = NamedCardFactory.Create("Heliod, Sun-Crowned", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Heliod, Sun-Crowned");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.God).Should().BeTrue();
        ((Creature)card).Power.Should().Be(5);
        ((Creature)card).Toughness.Should().Be(5);
    }

    [Fact]
    public void Heliod_HasIndestructibleKeywordMarker()
    {
        var heliod = HeliodSunCrownedFactory.Create(_alice);

        heliod.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Indestructible", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifegain triggered ability (CR 119.3 / 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifegainTrigger_FiresForController_NotOpponent()
    {
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        var trigger = heliod.Abilities.OfType<TriggeredAbility>().First();

        // Controller gains life — trigger condition matches.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 22), trigger).Should().BeTrue();
        // Opponent gains life — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 22), trigger).Should().BeFalse();
        // Controller LOSES life — does NOT match (strictly positive delta).
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger).Should().BeFalse();
        // No-op (same life total, e.g. gain 0) — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger).Should().BeFalse();
    }

    [Fact]
    public void LifegainTrigger_OnResolve_PutsPlusOnePlusOneCounter_OnTargetCreature()
    {
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var trigger = heliod.Abilities.OfType<TriggeredAbility>().First();
        trigger.SetChosenTargets(new[] { new object[] { bear } });

        foreach (var effect in trigger.Effects) effect.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void LifegainTrigger_OnResolve_PutsCounter_OnTargetEnchantment()
    {
        // CR 119.3 — "creature OR enchantment" target. Enchantment-only
        // permanents (non-creature) qualify.
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var shrine = new Enchantment("Honden of Cleansing Fire", "{2}{W}");
        shrine.SetOwner(_alice);
        shrine.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(shrine);
        shrine.SetZone(ZoneType.Battlefield);

        var trigger = heliod.Abilities.OfType<TriggeredAbility>().First();
        trigger.SetChosenTargets(new[] { new object[] { shrine } });

        foreach (var effect in trigger.Effects) effect.Execute();

        shrine.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void LifegainTrigger_OnResolve_NoOp_WhenTargetIsOpponentControlled()
    {
        // CR 608.2b — illegal-on-resolution: target must be controlled by
        // Heliod's controller.
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var enemyBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        enemyBear.SetOwner(_bob);
        enemyBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(enemyBear);
        enemyBear.SetZone(ZoneType.Battlefield);

        var trigger = heliod.Abilities.OfType<TriggeredAbility>().First();
        trigger.SetChosenTargets(new[] { new object[] { enemyBear } });

        foreach (var effect in trigger.Effects) effect.Execute();

        enemyBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Activated lifelink grant (CR 602.1 / 702.15)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifelinkGrant_OnResolve_RegistersLifelinkEot_OnTarget()
    {
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var ability = heliod.Abilities.OfType<ActivatedAbility>().First();
        ability.SetChosenTargets(new[] { new object[] { bear } });

        foreach (var effect in ability.Effects) effect.Execute();

        bear.ActiveEffects.Compute(bear).Keywords
            .Any(k => string.Equals(k, "Lifelink", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public void LifelinkGrant_OnResolve_NoOp_WhenTargetIsHeliodItself()
    {
        // "Another" — Heliod can't target itself, enforced at resolve.
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        heliod.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var ability = heliod.Abilities.OfType<ActivatedAbility>().First();
        ability.SetChosenTargets(new[] { new object[] { heliod } });

        foreach (var effect in ability.Effects) effect.Execute();

        heliod.ActiveEffects.Compute(heliod).Keywords
            .Any(k => string.Equals(k, "Lifelink", StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Devotion to white (CR 700.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Devotion_EmptyBattlefield_Zero()
    {
        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice).Should().Be(0);
    }

    [Fact]
    public void Devotion_HeliodAlone_TwoWhitePips()
    {
        // Heliod's printed cost is {1}{W}{W} — two pure {W} pips.
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice).Should().Be(2);
    }

    [Fact]
    public void Devotion_HeliodPlusThreeWhitePips_HitsThreshold()
    {
        var heliod = HeliodSunCrownedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        // Add a 3-pip white creature (e.g. {W}{W}{W}).
        var triplePip = new Creature("White Knight 3W", "{W}{W}{W}", 2, 2);
        triplePip.SetOwner(_alice);
        triplePip.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(triplePip);
        triplePip.SetZone(ZoneType.Battlefield);

        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice)
            .Should().BeGreaterOrEqualTo(HeliodSunCrownedFactory.DevotionToWhiteThreshold);
    }

    [Fact]
    public void Devotion_DoesNotCount_OpponentPermanents()
    {
        // Devotion is scoped to controller's battlefield only.
        var oppHeliod = HeliodSunCrownedFactory.Create(_bob);
        _bob.Zones.Battlefield.AddCard(oppHeliod);
        oppHeliod.SetZone(ZoneType.Battlefield);

        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Layer 4 devotion-gated type-strip (CR 205.2 / 613.1d) — "As long as
    // your devotion to white is less than five, Heliod isn't a creature."
    // -----------------------------------------------------------------------

    [Fact]
    public void Heliod_WithDevotionFour_LosesCreatureType()
    {
        // Heliod alone on battlefield → devotion = 2 ({W}{W}). Add a 2-pip
        // white permanent so devotion = 4 (just below the threshold).
        // Layer 4 type-strip should fire; Heliod's layered characteristics
        // should NOT include Creature.
        var service = new ContinuousEffectsService();
        var heliod = HeliodSunCrownedFactory.Create(_alice, triggers: null, effects: service);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var twoPipWhite = new Creature("Soldier-2W", "{W}{W}", 2, 2);
        twoPipWhite.SetOwner(_alice);
        twoPipWhite.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(twoPipWhite);
        twoPipWhite.SetZone(ZoneType.Battlefield);

        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice).Should().Be(4);

        var chars = service.Compute((Permanent)heliod);
        chars.Types.Should().NotContain(CardType.Creature);
        // Enchantment (printed) is preserved — strip is creature-only.
        chars.Types.Should().Contain(CardType.Enchantment);
    }

    [Fact]
    public void Heliod_NotCreature_IneligibleAsDoomBladeTarget()
    {
        // Doom Blade reads "Destroy target nonblack creature." A creature-only
        // target predicate must filter Heliod out when his effective types
        // (after Layer 4 strip) lack Creature.
        var service = new ContinuousEffectsService();
        var heliod = HeliodSunCrownedFactory.Create(_alice, triggers: null, effects: service);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);
        // Devotion = 2 (Heliod alone) → strip active.

        bool IsLegalDoomBladeTarget(Permanent p)
        {
            var chars = (p is Creature c && c.ActiveEffects != null)
                ? c.ActiveEffects.Compute(p)
                : new PermanentCharacteristics();
            return chars.Types.Contains(CardType.Creature);
        }

        IsLegalDoomBladeTarget(heliod).Should().BeFalse();
    }

    [Fact]
    public void Heliod_DevotionBumpsToFive_BecomesCreatureAgain()
    {
        // Heliod alone → devotion = 2 → not a creature. Cast a 3-pip white
        // permanent → devotion = 5 → predicate flips false → Creature
        // type is restored on the next Compute pass without re-registering
        // the effect.
        var service = new ContinuousEffectsService();
        var heliod = HeliodSunCrownedFactory.Create(_alice, triggers: null, effects: service);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        service.Compute((Permanent)heliod).Types.Should().NotContain(CardType.Creature);

        var triplePip = new Creature("White Knight 3W", "{W}{W}{W}", 2, 2);
        triplePip.SetOwner(_alice);
        triplePip.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(triplePip);
        triplePip.SetZone(ZoneType.Battlefield);

        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice)
            .Should().BeGreaterOrEqualTo(HeliodSunCrownedFactory.DevotionToWhiteThreshold);

        service.Compute((Permanent)heliod).Types.Should().Contain(CardType.Creature);
    }

    [Fact]
    public void Heliod_DevotionDropsBelowFive_LosesCreatureTypeAgain()
    {
        // Devotion hits 5 (Heliod + 3-pip white) → Creature.
        // White permanent LTB → devotion drops to 2 → not a Creature.
        var service = new ContinuousEffectsService();
        var heliod = HeliodSunCrownedFactory.Create(_alice, triggers: null, effects: service);
        _alice.Zones.Battlefield.AddCard(heliod);
        heliod.SetZone(ZoneType.Battlefield);

        var triplePip = new Creature("White Knight 3W", "{W}{W}{W}", 2, 2);
        triplePip.SetOwner(_alice);
        triplePip.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(triplePip);
        triplePip.SetZone(ZoneType.Battlefield);

        service.Compute((Permanent)heliod).Types.Should().Contain(CardType.Creature);

        // White permanent LTB's — drop from battlefield + clear zone.
        _alice.Zones.Battlefield.RemoveCard(triplePip);
        triplePip.SetZone(ZoneType.Graveyard);

        HeliodSunCrownedFactory.ComputeDevotionToWhite(_alice).Should().Be(2);
        service.Compute((Permanent)heliod).Types.Should().NotContain(CardType.Creature);
    }
}
