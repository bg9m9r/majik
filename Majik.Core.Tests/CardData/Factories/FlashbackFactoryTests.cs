using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the card literally named "Flashback" (Secrets of Strixhaven,
/// {R} Instant). NOT the Flashback keyword (CR 702.34) — this is the card
/// from SoS #115 whose effect GRANTS the keyword.
///
/// Scryfall: <c>1b832fda-d7c4-4566-884c-2a8b6da15488</c>.
/// Oracle: "Target instant or sorcery card in your graveyard gains flashback
/// until end of turn. The flashback cost is equal to its mana cost."
/// </summary>
public class FlashbackFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity (Scryfall fields) ────────────────────────────────────────────

    [Fact]
    public void Flashback_IsInstant_AtCost_R()
    {
        var fb = FlashbackFactory.Create(_alice);

        fb.Name.Should().Be("Flashback");
        fb.ManaCost.Should().Be("{R}");
        fb.HasType(CardType.Instant).Should().BeTrue();
        fb.Owner.Should().Be(_alice);
        fb.Controller.Should().Be(_alice);
    }

    [Fact]
    public void Flashback_NameAndCost_AreScryfallExact()
    {
        // Re-asserts the printed name and cost match Scryfall exactly so
        // any future refactor that drifts the constants trips the test.
        FlashbackFactory.CardName.Should().Be("Flashback");
        FlashbackFactory.PrintedManaCost.Should().Be("{R}");
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_DeclaresSingleMandatoryGraveyardTarget()
    {
        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);

        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");

        // Reanimate is the closest BotIntent fit — re-castable buried spell.
        req.Intent.HasAny(BotIntent.Reanimate).Should().BeTrue();
    }

    // ── Printed clause: grant flashback EOT, cost = its mana cost ─────────────

    [Fact]
    public void Resolve_GrantsRuntimeFlashbackEqualToTargetsManaCost()
    {
        // Bolt sits in Alice's graveyard.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);
        var effects = def.EffectFactory(MakeChosen(bolt));
        effects.Should().HaveCount(1);
        effects[0].Execute();

        bolt.RuntimeFlashbackCost.Should().NotBeNull();
        bolt.RuntimeFlashbackCost!.TotalValue.Should().Be(1);

        // The grant feeds a real FlashbackAlternativeCost — sanity-check the
        // shape that callers will use to cast Bolt from the graveyard.
        var alt = new FlashbackAlternativeCost(bolt.RuntimeFlashbackCost);
        alt.CanCastFor(bolt, _alice).Should().BeTrue();
    }

    [Fact]
    public void Resolve_AcceptsSorceryTargetsToo()
    {
        // Sorceries are explicitly allowed by the oracle text.
        var ponder = new Sorcery("Ponder", "U") { Owner = _alice };
        ponder.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(ponder);

        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);
        def.EffectFactory(MakeChosen(ponder))[0].Execute();

        ponder.RuntimeFlashbackCost.Should().NotBeNull();
        ponder.RuntimeFlashbackCost!.TotalValue.Should().Be(1);
    }

    // ── CR 608.2b — illegal-on-resolution guards ──────────────────────────────

    [Fact]
    public void Resolve_NoTarget_IsCleanNoOp()
    {
        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);

        // Empty targets list — equivalent to "target left the graveyard
        // between choose-time and resolution and no replacement was made".
        var emptyChosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { Array.Empty<object>() },
            Mana: ManaPayment.Empty);

        var act = () => def.EffectFactory(emptyChosen)[0].Execute();
        act.Should().NotThrow();
    }

    [Fact]
    public void Resolve_TargetNoLongerInGraveyard_DoesNotGrant()
    {
        // Setup a Bolt, then yank it back to hand before resolution.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);
        var effects = def.EffectFactory(MakeChosen(bolt));

        // Card moved out of graveyard before the effect fires.
        _alice.Zones.Graveyard.RemoveCard(bolt);
        _alice.Zones.Hand.AddCard(bolt);
        bolt.SetZone(ZoneType.Hand);

        effects[0].Execute();

        bolt.RuntimeFlashbackCost.Should().BeNull(
            "CR 608.2b — target not in graveyard at resolution → no effect");
    }

    [Fact]
    public void Resolve_TargetInOpponentGraveyard_DoesNotGrant()
    {
        // Oracle says "your graveyard". A Bolt in Bob's graveyard is not
        // a legal target for Alice's Flashback even if the targeting layer
        // somehow let it through; the resolution gate rechecks ownership.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        bolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bolt);

        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);
        def.EffectFactory(MakeChosen(bolt))[0].Execute();

        bolt.RuntimeFlashbackCost.Should().BeNull();
    }

    [Fact]
    public void Resolve_NonInstantNonSorceryTarget_DoesNotGrant()
    {
        // Even if a Creature CARD somehow ended up as the chosen target,
        // CR 608.2b says the printed type constraint is rechecked at
        // resolution. A Creature card in the graveyard is rejected.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r);
        def.EffectFactory(MakeChosen(bear))[0].Execute();

        bear.RuntimeFlashbackCost.Should().BeNull();
    }

    // ── CR 514.2 — "until end of turn" cleanup ────────────────────────────────

    [Fact]
    public void Resolve_WithEventBus_GrantClearsAtCleanupStep()
    {
        var bus = new EventBus();
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r, bus);
        def.EffectFactory(MakeChosen(bolt))[0].Execute();

        bolt.RuntimeFlashbackCost.Should().NotBeNull("grant is live before EOT");

        // Non-cleanup steps don't clear.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        bolt.RuntimeFlashbackCost.Should().NotBeNull(
            "only Cleanup step clears the grant");

        // Cleanup step fires — grant goes away (CR 514.2).
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        bolt.RuntimeFlashbackCost.Should().BeNull(
            "runtime flashback grant expires at end of turn");
    }

    [Fact]
    public void Resolve_NoEventBus_GrantPersistsForManualClearing()
    {
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        // No event bus passed — shape-only / test path. Grant must still
        // land; cleanup is the caller's responsibility.
        var def = FlashbackFactory.BuildSpellDefinition(_alice, r => r, eventBus: null);
        def.EffectFactory(MakeChosen(bolt))[0].Execute();

        bolt.RuntimeFlashbackCost.Should().NotBeNull();

        bolt.ClearRuntimeFlashback();
        bolt.RuntimeFlashbackCost.Should().BeNull();
    }

    // ── Dispatcher ────────────────────────────────────────────────────────────

    [Fact]
    public void NamedCardFactory_Dispatches_Flashback()
    {
        var card = NamedCardFactory.Create("Flashback", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Flashback");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ChosenSpellParams MakeChosen(object target) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new[] { target } },
            Mana: ManaPayment.Empty);
}
