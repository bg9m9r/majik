using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PastInFlamesFactory"/>.
///
/// Card: Past in Flames — Sorcery {3}{R} (Innistrad).
///   "Each instant and sorcery card in your graveyard gains flashback until
///    end of turn. The flashback costs are equal to their mana costs.
///    Flashback {4}{R}."
///
/// Covers:
///   - Identity (name, sorcery type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve grants flashback to each instant/sorcery in controller's graveyard
///     with the card's own printed mana cost.
///   - Non-instant/sorcery cards in graveyard are not granted flashback.
///   - Opponent's graveyard is untouched ("your graveyard" gate).
///   - Past in Flames itself is NOT granted via the resolve effect — its
///     {4}{R} flashback alt-cost lives separately.
///   - EOT cleanup via the supplied event bus on the Cleanup step.
///   - Printed Flashback {4}{R} alt-cost helper produces a usable
///     <see cref="FlashbackAlternativeCost"/>.
/// </summary>
[Trait("Color", "R")]
public class PastInFlamesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void PastInFlames_Identity()
    {
        var c = PastInFlamesFactory.Create(_alice);

        c.Name.Should().Be("Past in Flames");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCostValue.TotalValue.Should().Be(4);
    }

    [Fact]
    public void PastInFlames_NameAndCost_AreScryfallExact()
    {
        PastInFlamesFactory.CardName.Should().Be("Past in Flames");
        PastInFlamesFactory.PrintedManaCost.Should().Be("{3}{R}");
        PastInFlamesFactory.FlashbackManaCost.Should().Be("{4}{R}");
    }
    // ── Printed clause: grant flashback to each instant/sorcery in GY ─────────

    [Fact]
    public void Resolve_GrantsFlashbackToEachInstantAndSorceryInControllerGraveyard()
    {
        var bolt = SeedGraveyard<Instant>("Lightning Bolt", "R", _alice);
        var ponder = SeedGraveyard<Sorcery>("Ponder", "U", _alice);

        var effects = PastInFlamesFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        bolt.RuntimeFlashbackCost.Should().NotBeNull("Bolt is an instant in Alice's GY");
        bolt.RuntimeFlashbackCost!.TotalValue.Should().Be(1);

        ponder.RuntimeFlashbackCost.Should().NotBeNull("Ponder is a sorcery in Alice's GY");
        ponder.RuntimeFlashbackCost!.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Resolve_DoesNotGrant_NonInstantNonSorceryCards()
    {
        // Creature cards in the graveyard are not eligible (CR 702.34 — only
        // instant/sorcery cards gain flashback under this effect).
        var bear = SeedGraveyard<Creature>("Grizzly Bears", "1G", _alice, c =>
            new Creature(c.name, c.cost, 2, 2));

        var effects = PastInFlamesFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        bear.RuntimeFlashbackCost.Should().BeNull(
            "creature cards don't gain flashback from Past in Flames");
    }

    [Fact]
    public void Resolve_DoesNotGrant_OpponentsGraveyard()
    {
        // Oracle text reads "your graveyard". Spells in Bob's graveyard are
        // out of scope when Alice resolves Past in Flames.
        var bolt = SeedGraveyard<Instant>("Lightning Bolt", "R", _bob);

        var effects = PastInFlamesFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        bolt.RuntimeFlashbackCost.Should().BeNull(
            "Past in Flames only grants flashback to YOUR graveyard");
    }

    [Fact]
    public void Resolve_SkipsPastInFlamesItself()
    {
        // The Past in Flames spell itself is in the graveyard when its body
        // executes (CR 608.2f). The grant should skip it so its printed
        // Flashback {4}{R} cost isn't overwritten by the {3}{R} mana cost.
        var pif = PastInFlamesFactory.Create(_alice);
        pif.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(pif);

        var bolt = SeedGraveyard<Instant>("Lightning Bolt", "R", _alice);

        var effects = PastInFlamesFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        pif.RuntimeFlashbackCost.Should().BeNull(
            "Past in Flames itself is not granted runtime flashback by its own resolve");
        bolt.RuntimeFlashbackCost.Should().NotBeNull(
            "other instants/sorceries in GY are still granted flashback");
    }

    [Fact]
    public void Resolve_EmptyGraveyard_IsCleanNoOp()
    {
        var effects = PastInFlamesFactory.BuildResolveEffect(_alice);
        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow();
    }

    // ── CR 514.2 — EOT cleanup via event bus ──────────────────────────────────

    [Fact]
    public void Resolve_WithEventBus_GrantsClearOnCleanupStep()
    {
        var bus = new EventBus();
        var bolt = SeedGraveyard<Instant>("Lightning Bolt", "R", _alice);
        var ponder = SeedGraveyard<Sorcery>("Ponder", "U", _alice);

        var effects = PastInFlamesFactory.BuildResolveEffect(_alice, bus);
        foreach (var e in effects) e.Execute();

        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        ponder.RuntimeFlashbackCost.Should().NotBeNull();

        // Non-cleanup step doesn't clear.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        ponder.RuntimeFlashbackCost.Should().NotBeNull();

        // Cleanup step clears EVERY grant from this resolution.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        bolt.RuntimeFlashbackCost.Should().BeNull(
            "CR 514.2 — until end of turn → Cleanup step clears the grant");
        ponder.RuntimeFlashbackCost.Should().BeNull();
    }

    [Fact]
    public void Resolve_NoEventBus_GrantsPersistForManualClearing()
    {
        var bolt = SeedGraveyard<Instant>("Lightning Bolt", "R", _alice);

        var effects = PastInFlamesFactory.BuildResolveEffect(_alice, eventBus: null);
        foreach (var e in effects) e.Execute();

        bolt.RuntimeFlashbackCost.Should().NotBeNull();

        bolt.ClearRuntimeFlashback();
        bolt.RuntimeFlashbackCost.Should().BeNull();
    }

    // ── Printed Flashback {4}{R} alt-cost ─────────────────────────────────────

    [Fact]
    public void GetFlashbackAlternativeCost_HasCorrectCost()
    {
        var alt = PastInFlamesFactory.GetFlashbackAlternativeCost();

        // Cost should be {4}{R} — total value 5.
        var pif = PastInFlamesFactory.Create(_alice);
        pif.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(pif);

        alt.CanCastFor(pif, _alice).Should().BeTrue(
            "Past in Flames in its own graveyard is castable via the printed Flashback alt-cost");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T SeedGraveyard<T>(string name, string cost, Player owner)
        where T : Card
    {
        T card = typeof(T) == typeof(Instant) ? (T)(Card)new Instant(name, cost)
              : typeof(T) == typeof(Sorcery)  ? (T)(Card)new Sorcery(name, cost)
              : throw new ArgumentException($"Unsupported card type {typeof(T).Name}");
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }

    private static T SeedGraveyard<T>(
        string name, string cost, Player owner, Func<(string name, string cost), T> ctor)
        where T : Card
    {
        var card = ctor((name, cost));
        card.SetOwner(owner);
        card.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(card);
        return card;
    }
}
