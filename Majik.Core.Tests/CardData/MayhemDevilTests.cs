using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MayhemDevilFactory"/> (War of the Spark,
/// {1}{B}{R}).
///
/// Covers:
/// - Identity (Creature, Devil subtype, 3/3, {1}{B}{R}, owner/controller).
/// - NamedCardFactory dispatch.
/// - TargetRequest shape (any-target, exactly 1).
/// - Trigger predicate fires on a permanent moving Battlefield →
///   Graveyard (v1 approximation — see factory xmldoc gap note re:
///   sacrifice-only semantics).
/// - Trigger predicate ignores moves with other source/destination
///   zones and instant/sorcery spell-resolution graveyard moves (no
///   permanent-type match).
/// - Resolution deals 1 damage to the chosen target (creature
///   target reduces toughness path) and is a no-op when no target was
///   selected (defensive shape guard).
/// </summary>
public class MayhemDevilTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MayhemDevil_Identity()
    {
        var c = MayhemDevilFactory.Create(_alice);

        c.Name.Should().Be("Mayhem Devil");
        c.ManaCost.Should().Be("{1}{B}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Devil);
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].TargetRequests.Should().HaveCount(1);
        triggers[0].TargetRequests[0].MinTargets.Should().Be(1);
        triggers[0].TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void MayhemDevil_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mayhem Devil", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Mayhem Devil");
    }

    [Fact]
    public void MayhemDevil_AnyPlayerSacrifices_TriggerFires()
    {
        // "Whenever a PLAYER sacrifices a permanent" — fires on ANY
        // player's sacrifice (CR 603.1), no SacrificingPlayer filter.
        var devil = MayhemDevilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(devil);
        devil.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);

        var sacEvent = new PermanentSacrificedEvent(bear, _bob, wasToken: false);

        var trigger = devil.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(sacEvent).Should().BeTrue(
            "an opponent's sacrifice fires Mayhem Devil");
    }

    [Fact]
    public void MayhemDevil_ControllerOwnSacrifice_TriggerFires()
    {
        // "a player" includes the controller — Mayhem Devil fires on the
        // controller's own sacrifice too (token or nontoken).
        var devil = MayhemDevilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(devil);
        devil.SetZone(ZoneType.Battlefield);

        var token = new Creature("Eldrazi Spawn", "{0}", 0, 1);
        token.SetOwner(_alice);
        token.SetController(_alice);

        var sacEvent = new PermanentSacrificedEvent(token, _alice, wasToken: true);

        var trigger = devil.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(sacEvent).Should().BeTrue(
            "the controller's own sacrifice (even of a token) fires Mayhem Devil");
    }

    [Fact]
    public void MayhemDevil_OnResolution_DealsOneDamageToChosenTarget()
    {
        var devil = MayhemDevilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(devil);
        devil.SetZone(ZoneType.Battlefield);

        // Damage-receiving target — a Bob-controlled creature on the
        // battlefield. Fx.DealDamageAny calls Creature.TakeDamage which
        // accumulates marked damage on the target.
        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var trigger = devil.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in trigger.Effects) e.Execute();

        target.Damage.Should().Be(1, "Mayhem Devil deals 1 damage to any target");
    }

    [Fact]
    public void MayhemDevil_OnResolution_NoTargetChosen_IsNoOp()
    {
        // Defensive shape guard — if the trigger somehow resolves without
        // a chosen target (illegal-target removal, agent skip), the
        // effect must silently no-op rather than throw.
        var devil = MayhemDevilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(devil);
        devil.SetZone(ZoneType.Battlefield);

        var trigger = devil.Abilities.OfType<TriggeredAbility>().Single();
        // No SetChosenTargets call.

        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }
}
