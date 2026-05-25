using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.Vehicles;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Smuggler's Copter (Kaladesh, {2}, Artifact — Vehicle 3/3).
///
/// Covers:
///   - Identity (Artifact + Creature, Vehicle subtype, 3/3, {2}, owner/controller).
///   - NamedCardFactory dispatches via the [CardName] generator.
///   - Flying keyword marker is attached.
///   - Loot trigger fires on attack (CR 508.1f) and on block
///     (CR 509.1g) — draws 1 then discards 1.
///   - Crew 1 promotes the vehicle to a 3/3 creature via VehicleCrewEffect.
/// </summary>
public class SmugglersCopterTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SmugglersCopter_Identity()
    {
        var c = SmugglersCopterFactory.Create(_alice);

        c.Name.Should().Be("Smuggler's Copter");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Smuggler's Copter is an Artifact (Vehicle)");
        c.HasType(CardType.Creature).Should().BeTrue(
            "v1 vehicle shell is a Creature so CrewAction flows P/T through " +
            "VehicleCrewEffect");
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.ManaCost.Should().Be("{2}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SmugglersCopter_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Smuggler's Copter", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Smuggler's Copter");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vehicle).Should().BeTrue();
    }

    [Fact]
    public void SmugglersCopter_HasFlyingKeyword()
    {
        var c = SmugglersCopterFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying",
                "CR 702.9 — Flying keyword marker present for CombatAbilities.HasFlying");
    }

    // -----------------------------------------------------------------------
    // Loot trigger — attack leg (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void SmugglersCopter_AttackTrigger_LootsOnce()
    {
        // Seed the library with 1 known top card and the hand with 1 known card.
        SeedLibrary(_alice, "Top1", "Top2");
        SeedHand(_alice, "Hand1");

        var copter = SmugglersCopterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(copter);
        copter.SetZone(ZoneType.Battlefield);

        var trigger = copter.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new CreatureAttacksEvent(copter, _alice)).Should().BeTrue(
            "attack leg matches CR 508.1f");

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1",
                "drew 1 (Top1), then discarded 1 (the original Hand1 is first in hand " +
                "and is the deterministic discard pick — leaving Top1 in hand)");
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Contain("Hand1");
    }

    // -----------------------------------------------------------------------
    // Loot trigger — block leg (CR 509.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void SmugglersCopter_BlockTrigger_FiresOnDeclaredBlock()
    {
        var bob = new Player("Bob", 20);
        var copter = SmugglersCopterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(copter);
        copter.SetZone(ZoneType.Battlefield);

        // Build a minimal Combat with one attacker + one blocker (the copter).
        var attackerCreature = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob,
            Controller = bob,
            HasSummoningSickness = false,
        };
        var attacker = new Attacker(attackerCreature, _alice);
        var combat = new Majik.Core.Combat.Combat(bob, _alice);
        combat.AddAttacker(attacker);
        combat.TransitionToDeclaringBlockers();
        attacker.AddBlocker(new Blocker(
            creature: copter,
            blockedAttacker: attacker,
            hasFirstStrike: false,
            hasDoubleStrike: false,
            hasDeathtouch: false));

        var trigger = copter.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(new BlockersDeclaredEvent(combat)).Should().BeTrue(
            "block leg matches CR 509.1g — the copter is a declared blocker in " +
            "this combat");
    }

    // -----------------------------------------------------------------------
    // Crew 1 (CR 702.122) — drives the existing VehicleCrewEffect machinery.
    // -----------------------------------------------------------------------

    [Fact]
    public void SmugglersCopter_Crew1_PromotesToCreatureUntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var copter = SmugglersCopterFactory.Create(_alice);
        copter.ActiveEffects = effects;
        copter.HasSummoningSickness = false;

        var crew = new Creature("Mouse", "W", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
            HasSummoningSickness = false,
        };

        var result = CrewAction.Crew(
            copter,
            crewCost: SmugglersCopterFactory.CrewCost,
            vehiclePower: SmugglersCopterFactory.VehiclePower,
            vehicleToughness: SmugglersCopterFactory.VehicleToughness,
            new[] { crew },
            effects);

        result.Success.Should().BeTrue("1 power ≥ crew cost 1");
        crew.IsTapped.Should().BeTrue("crewmates tap to crew");
        copter.Power.Should().Be(3, "VehicleCrewEffect ships base 3 through Layer 7b");
        copter.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedLibrary(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Instant(n, "1") { Owner = p };
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }

    private static void SeedHand(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Instant(n, "1") { Owner = p };
            p.Zones.Hand.AddCard(card);
            card.SetZone(ZoneType.Hand);
        }
    }
}
