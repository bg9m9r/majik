using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Stack;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VoldarenEpicureFactory"/> (Innistrad: Crimson
/// Vow, {R}).
///
/// Creature — Vampire Citizen 1/1. Oracle text:
///   "When Voldaren Epicure enters, it deals 1 damage to each opponent and
///    you create a Blood token."
///
/// Covers:
///   - Identity (Vampire Citizen 1/1 at {R}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB trigger attached structurally on shape-only path.
///   - Full-wiring overload: trigger registered with the supplied
///     <see cref="TriggerManager"/>; on resolution opponents lose 1 life
///     and a Blood token enters the battlefield under the controller.
/// </summary>
public class VoldarenEpicureFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void VoldarenEpicure_Identity()
    {
        var c = VoldarenEpicureFactory.Create(_alice);

        c.Name.Should().Be("Voldaren Epicure");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Citizen).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VoldarenEpicure_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Voldaren Epicure", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Voldaren Epicure");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Citizen).Should().BeTrue();
    }

    [Fact]
    public void VoldarenEpicure_HasOneEtbTriggeredAbility()
    {
        var c = VoldarenEpicureFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the printed ETB trigger");
    }

    [Fact]
    public void EtbEffect_DealsOneDamageToEachOpponent_AndCreatesBloodToken()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = VoldarenEpicureFactory.Create(
            _alice,
            triggers,
            zones,
            opponentResolver: () => new[] { _bob });

        // Seat the Epicure so subsequent zone-targeted effects observe it
        // on the battlefield (token creation moves a different card; the
        // ETB effect itself is invoked directly below to bypass the
        // priority / stack drain that's exercised in TriggerManagerTests).
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Directly execute the ETB effect — same posture as Insolent
        // Neonate / Bloodghast tests that drive the effect closure
        // independently of priority / stack mechanics.
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Burn half — Bob lost 1 life (CR 119.3 — damage to a player is
        // life loss).
        _bob.LifeTotal.Should().Be(19,
            "Voldaren Epicure deals 1 damage to each opponent on ETB");

        // Blood token created under Alice — exactly one Blood-subtype
        // artifact token on her battlefield.
        var bloodTokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.HasSubtype(CardSubtype.Blood))
            .ToList();
        bloodTokens.Should().HaveCount(1, "a single Blood token is created on ETB");
        bloodTokens[0].Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the Blood token carries the printed sac-for-draw activated ability");
    }

    [Fact]
    public void EtbEffect_WithoutOpponentResolver_StillCreatesBloodToken()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = VoldarenEpicureFactory.Create(
            _alice,
            triggers,
            zones,
            opponentResolver: null);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Burn half no-ops (no resolver) — Bob untouched.
        _bob.LifeTotal.Should().Be(20);

        // Blood token still appears.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().ContainSingle(a => a.IsToken && a.HasSubtype(CardSubtype.Blood));
    }
}
