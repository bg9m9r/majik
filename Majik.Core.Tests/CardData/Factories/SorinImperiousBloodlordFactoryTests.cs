using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Sorin, Imperious Bloodlord (M19/M20, {2}{B}).
///
/// Legendary Planeswalker — Sorin, starting loyalty 4. Oracle text (Scryfall,
/// verified):
///   "+1: Target creature you control gains deathtouch and lifelink until end
///        of turn. If it's a Vampire, put a +1/+1 counter on it.
///    +1: You may sacrifice a Vampire. When you do, Sorin deals 3 damage to any
///        target and you gain 3 life.
///    −3: You may put a Vampire creature card from your hand onto the
///        battlefield."
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Sorin, loyalty 4, {2}{B}),
///     materialised from the embedded JSON definition.
///   - Three loyalty abilities: +1, +1, −3.
///   - +1 (a): target creature gains deathtouch + lifelink until EOT; a Vampire
///     also gets a +1/+1 counter, a non-Vampire does not.
///   - +1 (b): sacrifice a Vampire → 3 damage to any target + gain 3 life;
///     declining (no Vampire) fires nothing.
///   - −3: put a Vampire creature card from hand onto the battlefield.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "B")]
public class SorinImperiousBloodlordFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Vampire(string name, Player owner)
    {
        var c = new Creature(name, "{B}", 2, 2,
            subtypes: new[] { CardSubtype.Vampire });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static Creature Bear(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Sorin_IsLegendaryPlaneswalker_Sorin_4Loyalty_AtCost2B()
    {
        var sorin = SorinImperiousBloodlordFactory.Create(_alice);

        sorin.Name.Should().Be("Sorin, Imperious Bloodlord");
        sorin.ManaCost.Should().Be("{2}{B}");
        sorin.HasType(CardType.Planeswalker).Should().BeTrue();
        sorin.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        sorin.HasSubtype(CardSubtype.Sorin).Should().BeTrue();
        sorin.Loyalty.Should().Be(4);
        sorin.StartingLoyalty.Should().Be(4);
        sorin.Owner.Should().BeSameAs(_alice);
        sorin.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sorin_HasThreeLoyaltyAbilities_Plus1_Plus1_Minus3()
    {
        var sorin = SorinImperiousBloodlordFactory.Create(_alice);

        var loyalty = sorin.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, +1, -3 });
    }

    [Fact]
    public void Sorin_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Sorin, Imperious Bloodlord", _alice);
        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Sorin, Imperious Bloodlord");
    }

    // -----------------------------------------------------------------------
    // +1 (a): deathtouch + lifelink until EOT; Vampire → +1/+1 counter.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1Grant_GivesDeathtouchAndLifelink_AndCounterOnVampire()
    {
        var effects = new ContinuousEffectsService();
        var vamp = Vampire("Vampire Nighthawk", _alice);
        vamp.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(vamp);
        vamp.SetZone(ZoneType.Battlefield);

        var sorin = SorinImperiousBloodlordFactory.Create(
            _alice,
            ownCreatureResolver: () => new[] { vamp },
            sacrificeVampireResolver: null,
            anyTargetResolver: null,
            handVampireResolver: null);

        CombatAbilities.HasDeathtouch(vamp).Should().BeFalse();
        CombatAbilities.HasLifelink(vamp).Should().BeFalse();

        var plus1Grant = sorin.Abilities.OfType<LoyaltyAbility>()
            .First(a => a.LoyaltyChange == +1);
        plus1Grant.Activate();

        sorin.Loyalty.Should().Be(5); // 4 + 1
        CombatAbilities.HasDeathtouch(vamp).Should().BeTrue(
            "CR 702.2 — gains deathtouch until EOT");
        CombatAbilities.HasLifelink(vamp).Should().BeTrue(
            "CR 702.15 — gains lifelink until EOT");
        // CR 121.1 — Vampire gets a +1/+1 counter.
        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // CR 514.2 — the keyword grants expire in the cleanup step.
        effects.ExpireEndOfTurn();
        CombatAbilities.HasDeathtouch(vamp).Should().BeFalse();
        CombatAbilities.HasLifelink(vamp).Should().BeFalse();
        // The counter is permanent — it does not expire.
        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Plus1Grant_NonVampire_GetsKeywordsButNoCounter()
    {
        var effects = new ContinuousEffectsService();
        var bear = Bear("Grizzly Bears", _alice);
        bear.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var sorin = SorinImperiousBloodlordFactory.Create(
            _alice,
            ownCreatureResolver: () => new[] { bear },
            sacrificeVampireResolver: null,
            anyTargetResolver: null,
            handVampireResolver: null);

        sorin.Abilities.OfType<LoyaltyAbility>().First(a => a.LoyaltyChange == +1).Activate();

        CombatAbilities.HasDeathtouch(bear).Should().BeTrue();
        CombatAbilities.HasLifelink(bear).Should().BeTrue();
        // Not a Vampire — no +1/+1 counter.
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // +1 (b): sacrifice a Vampire → 3 damage to any target + gain 3 life.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1Sacrifice_SacsVampire_Deals3ToTarget_AndGains3Life()
    {
        var fodder = Vampire("Vampire of the Dire Moon", _alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var sorin = SorinImperiousBloodlordFactory.Create(
            _alice,
            ownCreatureResolver: null,
            sacrificeVampireResolver: () => fodder,
            anyTargetResolver: () => _bob, // "any target" — a player here
            handVampireResolver: null);

        // The second +1 is the sacrifice ability.
        var sacAbility = sorin.Abilities.OfType<LoyaltyAbility>()
            .Where(a => a.LoyaltyChange == +1).ElementAt(1);
        sacAbility.Activate();

        sorin.Loyalty.Should().Be(5); // 4 + 1
        // Vampire sacrificed to the graveyard.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
        fodder.Zone.Should().Be(ZoneType.Graveyard);
        // 3 damage to the any target (CR 119) + you gain 3 life (CR 119.3).
        _bob.LifeTotal.Should().Be(17);
        _alice.LifeTotal.Should().Be(23);
    }

    [Fact]
    public void Plus1Sacrifice_NoVampire_FiresNothing_ButLoyaltyApplies()
    {
        var sorin = SorinImperiousBloodlordFactory.Create(
            _alice,
            ownCreatureResolver: null,
            sacrificeVampireResolver: () => null, // declined
            anyTargetResolver: () => _bob,
            handVampireResolver: null);

        sorin.Abilities.OfType<LoyaltyAbility>()
            .Where(a => a.LoyaltyChange == +1).ElementAt(1).Activate();

        sorin.Loyalty.Should().Be(5);
        // No sacrifice → reflexive trigger never fires.
        _bob.LifeTotal.Should().Be(20);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // −3: put a Vampire creature card from hand onto the battlefield.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus3_PutsVampireFromHandOntoBattlefield()
    {
        var vamp = Vampire("Sorin's Guide", _alice);
        _alice.Zones.Hand.AddCard(vamp);
        vamp.SetZone(ZoneType.Hand);

        var sorin = SorinImperiousBloodlordFactory.Create(
            _alice,
            ownCreatureResolver: null,
            sacrificeVampireResolver: null,
            anyTargetResolver: null,
            handVampireResolver: () => vamp);

        sorin.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        sorin.Loyalty.Should().Be(1); // 4 - 3
        _alice.Zones.Hand.GetCards().Should().NotContain(vamp);
        _alice.Zones.Battlefield.GetCards().Should().Contain(vamp);
        vamp.Zone.Should().Be(ZoneType.Battlefield);
        vamp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Minus3_NoVampire_NoOps_ButLoyaltyApplies()
    {
        var sorin = SorinImperiousBloodlordFactory.Create(
            _alice,
            ownCreatureResolver: null,
            sacrificeVampireResolver: null,
            anyTargetResolver: null,
            handVampireResolver: () => null);

        sorin.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        sorin.Loyalty.Should().Be(1); // 4 - 3, cost paid even though the effect declines
    }
}
