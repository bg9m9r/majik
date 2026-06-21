using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Stensia Masquerade (Shadows over Innistrad,
/// Enchantment {2}{R}).
///   "Attacking creatures you control have first strike.
///    Whenever a Vampire you control deals combat damage to a player, put a
///    +1/+1 counter on it."
///
/// Validates:
///   * Card identity (Enchantment at {2}{R}) + dispatcher entry.
///   * The combat-damage trigger fires only for a Vampire YOU control that
///     deals combat damage to a PLAYER, and the +1/+1 counter lands on that
///     Vampire (the damage source), not the enchantment.
///   * Negative gates: non-Vampire, opponent-controlled Vampire, and
///     damage-to-a-creature all fail to trigger.
///   * The "attacking creatures you control have first strike" static grants
///     First strike only while the creature is a declared attacker.
/// </summary>
[Trait("Color", "R")]
public class StensiaMasqueradeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature NewVampire(string name, Player controller, int power = 2, int toughness = 2)
    {
        var c = new Creature(name, "{1}{B}", power, toughness, subtypes: new[] { CardSubtype.Vampire });
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Permanent card, Player controller)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void StensiaMasquerade_IsEnchantment_AtCost2R()
    {
        var card = StensiaMasqueradeFactory.Create(_alice);

        card.Name.Should().Be("Stensia Masquerade");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Trigger_ControlledVampire_HitsPlayer_PutsCounterOnTheVampire()
    {
        var card = StensiaMasqueradeFactory.Create(_alice);
        PlaceOnBattlefield(card, _alice);

        var vamp = NewVampire("Vampire Nighthawk", _alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var dmg = new CombatDamageDealtEvent(vamp, _bob, amount: 2);
        trigger.IsTriggered(dmg).Should().BeTrue(
            "a Vampire you control dealt combat damage to a player (CR 510 / CR 603.1)");

        foreach (var effect in trigger.Effects) effect.Execute();

        vamp.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "\"put a +1/+1 counter on it\" targets the Vampire (the damage source), not the enchantment (CR 122.1)");
        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the counter goes on the Vampire, not on Stensia Masquerade");
    }

    [Fact]
    public void Trigger_NonVampire_DoesNotFire()
    {
        var card = StensiaMasqueradeFactory.Create(_alice);
        PlaceOnBattlefield(card, _alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        PlaceOnBattlefield(bear, _alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(bear, _bob, amount: 2)).Should().BeFalse(
            "the source isn't a Vampire");
    }

    [Fact]
    public void Trigger_OpponentControlledVampire_DoesNotFire()
    {
        var card = StensiaMasqueradeFactory.Create(_alice);
        PlaceOnBattlefield(card, _alice);

        var enemyVamp = NewVampire("Bob's Vampire", _bob);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(enemyVamp, _alice, amount: 2)).Should().BeFalse(
            "\"a Vampire YOU control\" — an opponent's Vampire doesn't trigger (CR 109.4)");
    }

    [Fact]
    public void Trigger_DamageToCreature_DoesNotFire()
    {
        var card = StensiaMasqueradeFactory.Create(_alice);
        PlaceOnBattlefield(card, _alice);

        var vamp = NewVampire("Vampire Nighthawk", _alice);
        var blocker = new Creature("Wall", "{2}", 0, 4);
        blocker.SetOwner(_bob);
        blocker.SetController(_bob);
        PlaceOnBattlefield(blocker, _bob);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CombatDamageDealtEvent(vamp, (ICard)blocker, amount: 2)).Should().BeFalse(
            "\"combat damage to a player\" — damage to a creature doesn't trigger (CR 510.1c)");
    }

    [Fact]
    public void FirstStrikeStatic_GrantsFirstStrike_OnlyWhileAttacking()
    {
        var registry = new CombatMembershipRegistry();
        using var scope = CombatMembershipRegistryProvider.PushScope(registry);

        // Wire to a bus so a combat-membership change rides an event and bumps
        // the continuous-effects memoization generation — exactly as the live
        // CombatFlow's AttackersDeclared event does in a real game (the
        // service bumps unconditionally on every bus event, CR 611.2c).
        var bus = new Majik.Core.Tests.Helpers.TestEventBus();
        var effects = new ContinuousEffectsService(bus);
        var card = StensiaMasqueradeFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(card, _alice);

        var vamp = NewVampire("Vampire Nighthawk", _alice);
        // Wire the creature's layers view to the same per-match service so
        // CombatAbilities.HasFirstStrike reads the granted keyword (prod wires
        // every permanent's ActiveEffects to the GameFacade's service).
        vamp.ActiveEffects = effects;

        // Not attacking → no first strike.
        effects.Compute(vamp).Keywords.Should().NotContain("First strike",
            "the static grants first strike only to ATTACKING creatures (CR 508.1)");
        CombatAbilities.HasFirstStrike(vamp).Should().BeFalse();

        // Declared as an attacker → gains first strike (CR 702.7 / CR 613.1f).
        registry.RecordAttacker(vamp);
        bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(vamp, _bob));
        effects.Compute(vamp).Keywords.Should().Contain("First strike",
            "attacking creatures you control have first strike");
        CombatAbilities.HasFirstStrike(vamp).Should().BeTrue();
    }

    [Fact]
    public void FirstStrikeStatic_DoesNotGrantToOpponentsAttackers()
    {
        var registry = new CombatMembershipRegistry();
        using var scope = CombatMembershipRegistryProvider.PushScope(registry);

        var bus = new Majik.Core.Tests.Helpers.TestEventBus();
        var effects = new ContinuousEffectsService(bus);
        var card = StensiaMasqueradeFactory.Create(_alice, continuousEffects: effects);
        PlaceOnBattlefield(card, _alice);

        var enemyVamp = NewVampire("Bob's Vampire", _bob);
        registry.RecordAttacker(enemyVamp);
        bus.Publish(new Majik.Core.Domain.DomainEvents.CreatureAttacksEvent(enemyVamp, _alice));

        effects.Compute(enemyVamp).Keywords.Should().NotContain("First strike",
            "\"attacking creatures YOU control\" — an opponent's attacker isn't granted first strike (CR 109.4)");
    }

    [Fact]
    public void ProdRail_EffectsAwareDispatch_RegistersFirstStrikeAnthem()
    {
        // Prod rail: the effects-aware NamedCardFactory.Create(name, owner,
        // effects) overload is EXACTLY the entry point DeckCardBuilder's routed
        // (approach-B instance-swap) build calls for a non-Land permanent with a
        // real [CardName] factory (CR 613.7c). This is the production cast/build
        // path — proving the "Attacking creatures you control have first strike"
        // anthem is registered HERE (not only via a directly-supplied
        // ContinuousEffectsService overload) is the deferral's stated concern.
        var registry = new CombatMembershipRegistry();
        using var scope = CombatMembershipRegistryProvider.PushScope(registry);

        var bus = new Majik.Core.Tests.Helpers.TestEventBus();
        var effects = new ContinuousEffectsService(bus);

        // Build via the effects-aware dispatcher BY NAME — the prod rail.
        var built = NamedCardFactory.Create("Stensia Masquerade", _alice, effects);
        built.Should().BeOfType<Enchantment>();
        var card = (Enchantment)built;
        PlaceOnBattlefield(card, _alice);

        var vamp = NewVampire("Vampire Nighthawk", _alice);
        vamp.ActiveEffects = effects;

        // Not attacking → no first strike.
        CombatAbilities.HasFirstStrike(vamp).Should().BeFalse(
            "the static grants first strike only to ATTACKING creatures (CR 508.1)");

        // Declared as an attacker → the prod-registered anthem grants first
        // strike (CR 702.7 / CR 613.1f), confirming the instance-swap dispatch
        // wired the continuous effect.
        registry.RecordAttacker(vamp);
        bus.Publish(new CreatureAttacksEvent(vamp, _bob));
        effects.Compute(vamp).Keywords.Should().Contain("First strike",
            "the effects-aware prod dispatch registered the attacking-creatures first-strike anthem");
        CombatAbilities.HasFirstStrike(vamp).Should().BeTrue();
    }
}
