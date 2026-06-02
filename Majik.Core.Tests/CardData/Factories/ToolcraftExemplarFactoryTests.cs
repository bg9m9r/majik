using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ToolcraftExemplarFactory"/> (Kaladesh, {W}).
///
/// Toolcraft Exemplar — Creature — Dwarf Artificer 1/1. Oracle text
/// (verified against Scryfall 2026-06-01):
///   "At the beginning of combat on your turn, if you control an artifact,
///    this creature gets +2/+1 until end of turn. If you control three or
///    more artifacts, it also gains first strike until end of turn."
///
/// Same begin-combat-on-your-turn trigger shape as
/// <see cref="LegionWarbossFactory"/> (<see cref="Triggers.OnStepBegin"/>,
/// CR 508.1) plus a self-pump (<see cref="PumpUntilEndOfTurnEffect"/> +2/+1,
/// Layer 7c CR 613.1g, expiry CR 514.2) like
/// <see cref="PlatedGeopedeFactory"/>, gated by the artifact-count
/// intervening-if (CR 603.4) read through
/// <see cref="MasterOfEtheriumFactory.CountArtifactsControlled"/>.
///
/// Coverage:
/// - Identity (Creature — Dwarf Artificer, 1/1, {W}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Begin-combat trigger attached, self-affecting (no targets), fires only
///   on the controller's combat step ("on your turn").
/// - With 1 artifact: +2/+1, no first strike.
/// - With 3+ artifacts: +2/+1 AND first strike.
/// - With 0 artifacts: no pump (intervening-if fails).
/// - The pump + first strike expire in the cleanup step (CR 514.2).
/// </summary>
[Trait("Color", "W")]
public class ToolcraftExemplarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetBeginCombatTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

    private Artifact MakeArtifact(Player controller, string name)
    {
        var a = new Artifact(name, "{1}");
        a.SetOwner(controller);
        a.SetController(controller);
        controller.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ToolcraftExemplar_Identity_DwarfArtificer_1_1_W()
    {
        var c = ToolcraftExemplarFactory.Create(_alice);

        c.Name.Should().Be("Toolcraft Exemplar");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dwarf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void ToolcraftExemplar_BeginCombatTrigger_IsSelfAffecting_NoTargets()
    {
        var c = ToolcraftExemplarFactory.Create(_alice);

        var trigger = GetBeginCombatTrigger(c);
        trigger.Source.Should().BeSameAs(c);
        trigger.Controller.Should().BeSameAs(_alice);
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.TargetRequests.Should().BeEmpty(
            "the pump affects Toolcraft Exemplar itself — no target is chosen");
    }

    [Fact]
    public void ToolcraftExemplar_BeginCombatTrigger_FiresOnControllerCombatStepOnly()
    {
        var c = ToolcraftExemplarFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetBeginCombatTrigger(c);

        trigger.IsTriggered(
            new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice))
            .Should().BeTrue("fires at the beginning of combat on the controller's turn.");

        trigger.IsTriggered(
            new StepStartedEvent(PhaseStateType.BeginningOfCombat, _bob))
            .Should().BeFalse("'on your turn' — not on the opponent's combat.");
    }

    // -----------------------------------------------------------------------
    // Resolution — artifact-count gating
    // -----------------------------------------------------------------------

    [Fact]
    public void ToolcraftExemplar_OneArtifact_PumpsPlusTwoPlusOne_NoFirstStrike()
    {
        var exemplar = ToolcraftExemplarFactory.Create(_alice);
        exemplar.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(exemplar);
        exemplar.SetZone(ZoneType.Battlefield);

        MakeArtifact(_alice, "Bonesplitter");

        foreach (var e in GetBeginCombatTrigger(exemplar).Effects) e.Execute();

        exemplar.GetPower().Should().Be(1 + ToolcraftExemplarFactory.PumpPower);
        exemplar.GetToughness().Should().Be(1 + ToolcraftExemplarFactory.PumpToughness);
        CombatAbilities.HasFirstStrike(exemplar).Should().BeFalse(
            "first strike requires three or more artifacts.");
    }

    [Fact]
    public void ToolcraftExemplar_ThreeArtifacts_PumpsAndGainsFirstStrike()
    {
        var exemplar = ToolcraftExemplarFactory.Create(_alice);
        exemplar.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(exemplar);
        exemplar.SetZone(ZoneType.Battlefield);

        MakeArtifact(_alice, "Bonesplitter");
        MakeArtifact(_alice, "Cathar's Shield");
        MakeArtifact(_alice, "Accorder's Shield");

        foreach (var e in GetBeginCombatTrigger(exemplar).Effects) e.Execute();

        exemplar.GetPower().Should().Be(3);
        exemplar.GetToughness().Should().Be(2);
        CombatAbilities.HasFirstStrike(exemplar).Should().BeTrue(
            "CR 702.7 — three or more artifacts also grants first strike.");
    }

    [Fact]
    public void ToolcraftExemplar_NoArtifacts_DoesNotPump()
    {
        var exemplar = ToolcraftExemplarFactory.Create(_alice);
        exemplar.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(exemplar);
        exemplar.SetZone(ZoneType.Battlefield);

        // No artifacts under Alice's control — intervening-if fails (CR 603.4).
        foreach (var e in GetBeginCombatTrigger(exemplar).Effects) e.Execute();

        exemplar.GetPower().Should().Be(1);
        exemplar.GetToughness().Should().Be(1);
        CombatAbilities.HasFirstStrike(exemplar).Should().BeFalse();
    }

    [Fact]
    public void ToolcraftExemplar_OpponentsArtifactsDoNotCount()
    {
        var exemplar = ToolcraftExemplarFactory.Create(_alice);
        exemplar.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(exemplar);
        exemplar.SetZone(ZoneType.Battlefield);

        // Three artifacts, but Bob controls them (CR 109.5 — "you control").
        MakeArtifact(_bob, "Bonesplitter");
        MakeArtifact(_bob, "Cathar's Shield");
        MakeArtifact(_bob, "Accorder's Shield");

        foreach (var e in GetBeginCombatTrigger(exemplar).Effects) e.Execute();

        exemplar.GetPower().Should().Be(1, "the opponent's artifacts don't count.");
        CombatAbilities.HasFirstStrike(exemplar).Should().BeFalse();
    }

    [Fact]
    public void ToolcraftExemplar_PumpAndFirstStrike_ExpireAtEndOfTurn()
    {
        var exemplar = ToolcraftExemplarFactory.Create(_alice);
        var svc = new ContinuousEffectsService();
        exemplar.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(exemplar);
        exemplar.SetZone(ZoneType.Battlefield);

        MakeArtifact(_alice, "Bonesplitter");
        MakeArtifact(_alice, "Cathar's Shield");
        MakeArtifact(_alice, "Accorder's Shield");

        foreach (var e in GetBeginCombatTrigger(exemplar).Effects) e.Execute();

        exemplar.GetPower().Should().Be(3);
        CombatAbilities.HasFirstStrike(exemplar).Should().BeTrue();

        // CR 514.2 — "until end of turn" effects expire in the cleanup step.
        svc.ExpireEndOfTurn();

        exemplar.GetPower().Should().Be(1);
        exemplar.GetToughness().Should().Be(1);
        CombatAbilities.HasFirstStrike(exemplar).Should().BeFalse();
    }
}
