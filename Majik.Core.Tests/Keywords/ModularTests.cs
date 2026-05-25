using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Tests for CR 702.43 — Modular keyword, implemented via
/// <see cref="ModularFactory"/>.
///
/// Covers:
///   - ETB +1/+1 counters via ReplacementBus → ZoneService (Hardened-Scales
///     aware route — see PR #494).
///   - Death trigger moves counters from the graveyard object to a target
///     artifact creature.
///   - "You may" rider — agent yes/no gating, with the
///     <see cref="BotIntent.CardAdvantage"/> default = yes.
///   - 0-counter death → no transfer (effect short-circuits).
///   - Multiple Modular creatures dying → each transfers independently.
///   - Keyword marker is attached with the printed N value.
/// </summary>
public class ModularTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeModularBeast(string name, int n, Player owner,
        ReplacementBus? replacements = null, TriggerManager? triggers = null,
        IPlayerAgent? agent = null)
    {
        var c = new Creature(name, "{2}", 0, 0, subtypes: new[] { CardSubtype.Beast });
        c.AddCardType(CardType.Artifact);
        c.SetOwner(owner);
        c.SetController(owner);
        ModularFactory.Build(c, n, effects: null, replacements: replacements,
            triggers: triggers, agent: agent);
        return c;
    }

    private static void PutOnBattlefield(Player owner, Permanent perm)
    {
        owner.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);
        perm.SetController(owner);
    }

    private static Creature MakeArtifactCreature(string name, Player owner)
    {
        var ac = new Creature(name, "{2}", 0, 0);
        ac.SetOwner(owner);
        ac.AddCardType(CardType.Artifact);
        PutOnBattlefield(owner, ac);
        return ac;
    }

    // -------------------------------------------------------------------------
    // 1. Keyword marker
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_AttachesKeywordMarker_WithPrintedN()
    {
        var beast = MakeModularBeast("Modular Test 2", n: 2, _alice);

        var marker = beast.Abilities.OfType<KeywordAbility>().SingleOrDefault();
        marker.Should().NotBeNull("Modular ships a KeywordAbility marker so inspectors can see it");
        marker!.Keyword.Should().Be("Modular 2",
            "the marker embeds the printed N (reminder-text convention)");
    }

    [Fact]
    public void Build_AttachesDeathTrigger_ToCardShape()
    {
        var beast = MakeModularBeast("Modular Test", n: 1, _alice);

        beast.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the Modular death trigger is attached at construction");
        var trig = beast.Abilities.OfType<TriggeredAbility>().Single();
        trig.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trig.ActiveZones.Should().Contain(ZoneType.Graveyard);
    }

    // -------------------------------------------------------------------------
    // 2. ETB +1/+1 counters — Hardened-Scales-aware route via ReplacementBus
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_EtbCounters_RewritesZoneMoveIntent_WhenBusSupplied()
    {
        var bus = new ReplacementBus();
        var beast = MakeModularBeast("Modular Test 3", n: 3, _alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: beast, FromZone: ZoneType.Hand, ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var rewritten = bus.Apply(intent);

        rewritten.Should().NotBeNull("the ETB replacement rewrites, not cancels");
        rewritten!.PlusOneCountersOnEnter.Should().Be(3,
            "Modular N stamps the ETB intent with N +1/+1 counters");
    }

    [Fact]
    public void Build_EtbCounters_NoBus_DoesNotStampBag()
    {
        // Shape-only path — no bus. The replacement isn't registered; tests
        // that put the creature on the battlefield by hand must call
        // MarkEntersWithCounters explicitly.
        var beast = MakeModularBeast("Modular Test", n: 1, _alice);
        PutOnBattlefield(_alice, beast);

        beast.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no bus → no replacement registered → counter not stamped yet");

        ModularFactory.MarkEntersWithCounters(beast, 1);
        beast.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "MarkEntersWithCounters stamps the counter directly");
    }

    [Fact]
    public void Build_EtbCounters_FullPipeline_StampsCounter_ViaZoneService()
    {
        // End-to-end: ReplacementBus + ZoneService → counter lands post-ETB.
        var bus = new EventBus();
        var replacements = new ReplacementBus();
        var zones = new ZoneService(bus, replacements);

        var beast = MakeModularBeast("Modular Test", n: 1, _alice,
            replacements: replacements);
        // Start in hand so the ETB-from-non-battlefield path fires the
        // replacement.
        _alice.Zones.Hand.AddCard(beast);
        beast.SetZone(ZoneType.Hand);

        zones.MoveCardTo(beast, ZoneType.Battlefield);

        beast.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "ZoneService landed the ETB and applied the PlusOneCountersOnEnter stamp");
    }

    // -------------------------------------------------------------------------
    // 3. Death trigger — counters move to target artifact creature
    // -------------------------------------------------------------------------

    [Fact]
    public void DeathTrigger_MovesCountersToArtifactCreature()
    {
        var beast = MakeModularBeast("Modular Test", n: 1, _alice);
        PutOnBattlefield(_alice, beast);
        ModularFactory.MarkEntersWithCounters(beast, 1);
        // Simulate two extra grow events → 3 counters total.
        beast.Counters.Add(CounterType.PlusOnePlusOne, 2);

        var bestowee = MakeArtifactCreature("Recipient", _alice);

        // Simulate death (battlefield → graveyard).
        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "all counters move to the chosen artifact creature");
        beast.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "counters are removed from the graveyard object after bestowal");
    }

    [Fact]
    public void DeathTrigger_NoCounters_NoOp()
    {
        var beast = MakeModularBeast("Modular Test", n: 1, _alice);
        PutOnBattlefield(_alice, beast);
        // Skip MarkEntersWithCounters — beast dies with 0 counters.

        var bestowee = MakeArtifactCreature("Recipient", _alice);

        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counters to move — bestowee is unchanged");
    }

    [Fact]
    public void DeathTrigger_NoTarget_LeavesCountersOnGraveObject()
    {
        var beast = MakeModularBeast("Modular Test", n: 1, _alice);
        PutOnBattlefield(_alice, beast);
        ModularFactory.MarkEntersWithCounters(beast, 1);
        // No artifact creature on battlefield to receive the counters.

        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        beast.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "no target → counters stay on the graveyard object");
    }

    // -------------------------------------------------------------------------
    // 4. "You may" — agent yes/no rider (CardAdvantage default = yes)
    // -------------------------------------------------------------------------

    [Fact]
    public void DeathTrigger_AgentYes_MovesCounters()
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var beast = MakeModularBeast("Modular Test", n: 1, _alice, agent: agent);
        PutOnBattlefield(_alice, beast);
        ModularFactory.MarkEntersWithCounters(beast, 1);

        var bestowee = MakeArtifactCreature("Recipient", _alice);

        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "agent said yes — counter moves to bestowee");
    }

    [Fact]
    public void DeathTrigger_AgentNo_LeavesCounters()
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        var beast = MakeModularBeast("Modular Test", n: 1, _alice, agent: agent);
        PutOnBattlefield(_alice, beast);
        ModularFactory.MarkEntersWithCounters(beast, 1);

        var bestowee = MakeArtifactCreature("Recipient", _alice);

        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "agent declined — bestowee unchanged");
        beast.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "agent declined — counter stays on the graveyard object");
    }

    [Fact]
    public void DeathTrigger_NoAgent_AutoAccepts_CardAdvantageDefault()
    {
        // Default IPlayerAgent.ChooseYesNoAsync returns true for
        // CardAdvantage. The null-agent path in ModularFactory likewise
        // auto-accepts (legacy posture). Verify the bestowal lands.
        var beast = MakeModularBeast("Modular Test", n: 1, _alice, agent: null);
        PutOnBattlefield(_alice, beast);
        ModularFactory.MarkEntersWithCounters(beast, 1);

        var bestowee = MakeArtifactCreature("Recipient", _alice);

        _alice.Zones.Battlefield.RemoveCard(beast);
        _alice.Zones.Graveyard.AddCard(beast);
        beast.SetZone(ZoneType.Graveyard);

        var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in modular.Effects) e.Execute();

        bestowee.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "null-agent path auto-accepts (matches CardAdvantage default = yes)");
    }

    // -------------------------------------------------------------------------
    // 5. Multiple Modular creatures dying — each transfers independently
    // -------------------------------------------------------------------------

    [Fact]
    public void MultipleModularDeaths_EachTransfersIndependently()
    {
        var first = MakeModularBeast("Modular A", n: 2, _alice);
        var second = MakeModularBeast("Modular B", n: 3, _alice);
        PutOnBattlefield(_alice, first);
        PutOnBattlefield(_alice, second);
        ModularFactory.MarkEntersWithCounters(first, 2);
        ModularFactory.MarkEntersWithCounters(second, 3);

        var recipient = MakeArtifactCreature("Recipient", _alice);

        // Both die.
        foreach (var beast in new[] { first, second })
        {
            _alice.Zones.Battlefield.RemoveCard(beast);
            _alice.Zones.Graveyard.AddCard(beast);
            beast.SetZone(ZoneType.Graveyard);
        }

        // Resolve each Modular trigger independently.
        foreach (var beast in new[] { first, second })
        {
            var modular = beast.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var e in modular.Effects) e.Execute();
        }

        recipient.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(5,
            "2 from first beast + 3 from second beast = 5 counters on the recipient");
        first.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        second.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }
}
