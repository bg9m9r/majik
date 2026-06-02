using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VoltaicBrawlerFactory"/>.
///
/// Card: Voltaic Brawler (Kaladesh, {R}{G}). Creature — Human Warrior 3/2.
///   "When this creature enters, you get {E}{E} (two energy counters).
///    Whenever this creature attacks, you may pay {E}. If you do, it gets
///    +1/+1 and gains trample until end of turn."
///
/// Covers:
/// - Card identity (name, mana cost via JSON shape, Creature type, Human +
///   Warrior subtypes, 3/2 P/T, owner/controller).
/// - Ability shape: exactly two TriggeredAbilities (ETB energy + pay-{E}
///   attack), no ActivatedAbilities, no TargetRequests on either trigger.
/// - ETB predicate: fires on self entering battlefield; does NOT fire on a
///   non-battlefield zone move.
/// - ETB resolution: controller gains {E}{E} (two energy).
/// - Attack predicate: fires when this creature attacks; not for another.
/// - Attack resolution (no-agent fallback): pays {E}, registers +1/+1 +
///   Trample EOT; both expire on ExpireEndOfTurn.
/// - Attack resolution: insufficient energy → no pay, no grants.
/// - Attack resolution: agent declines → no pay, no grants.
/// - Null ActiveEffects → energy still spent but no throw.
/// - Dispatcher integration via NamedCardFactory.
/// </summary>
[Trait("Color", "M")]
public class VoltaicBrawlerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicBrawler_NameIsCorrect()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.Name.Should().Be("Voltaic Brawler");
    }

    [Fact]
    public void VoltaicBrawler_IsCreature()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void VoltaicBrawler_HasCorrectSubtypes()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.HasSubtype(CardSubtype.Human).Should().BeTrue("printed type is Human Warrior");
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void VoltaicBrawler_HasCorrectStats()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void VoltaicBrawler_OwnerAndControllerAreSet()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicBrawler_HasExactlyTwoTriggeredAbilities()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "the ETB energy trigger and the pay-{E} attack trigger");
    }

    [Fact]
    public void VoltaicBrawler_HasNoActivatedAbilities()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "both abilities are triggered, not activated");
    }

    [Fact]
    public void VoltaicBrawler_TriggersHaveNoTargetRequests()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);

        foreach (var t in c.Abilities.OfType<TriggeredAbility>())
        {
            t.TargetRequests.Should().BeEmpty(
                "ETB names the controller and the attack rider names 'it' (this creature) — neither targets");
        }
    }

    // -----------------------------------------------------------------------
    // ETB trigger — CR 603.6a + CR 106.13b
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicBrawler_OwnEtb_FiresAndGrantsTwoEnergy()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);
        var etb = EtbTrigger(c);

        var evt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(evt, etb).Should().BeTrue(
            "Voltaic Brawler's own ETB triggers the energy ability");

        _alice.EnergyCounters.Should().Be(0);
        foreach (var effect in etb.Effects) effect.Execute();
        _alice.EnergyCounters.Should().Be(2,
            "ETB grants the controller {E}{E} — two energy (CR 106.13b)");
    }

    [Fact]
    public void VoltaicBrawler_NonBattlefieldZoneMove_EtbDoesNotFire()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);
        var etb = EtbTrigger(c);

        // Battlefield → Graveyard (death) is not an ETB.
        var evt = new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard);
        etb.Condition.Matches(evt, etb).Should().BeFalse(
            "the ETB trigger requires ToZone == Battlefield (entering, not leaving)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — CR 508.1f / CR 117.5 / CR 613 / CR 514.2
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicBrawler_AttackTrigger_FiresForSelf_NotForAnother()
    {
        var c = VoltaicBrawlerFactory.Create(_alice);
        var atk = AttackTrigger(c);

        var selfEvt = new CreatureAttacksEvent(c, _bob);
        atk.Condition.Matches(selfEvt, atk).Should().BeTrue(
            "the attack trigger fires when this creature attacks (CR 508.1f)");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        var otherEvt = new CreatureAttacksEvent(other, _bob);
        atk.Condition.Matches(otherEvt, atk).Should().BeFalse(
            "a different attacker does not trigger this creature's per-attacker trigger");
    }

    [Fact]
    public void VoltaicBrawler_AttackPaid_GrantsPumpAndTrampleEot_NoAgentFallback()
    {
        AgentRegistry.Remove(_alice); // ensure no-agent fallback (pay when affordable)

        var c = VoltaicBrawlerFactory.Create(_alice);
        var atk = AttackTrigger(c);

        c.SetZone(ZoneType.Battlefield);
        var svc = new ContinuousEffectsService();
        c.ActiveEffects = svc;
        _alice.GainEnergy(2);

        // Sanity: base 3/2, no trample before resolution.
        c.GetPower().Should().Be(3);
        c.GetToughness().Should().Be(2);
        svc.Compute(c).Keywords.Should().NotContain("Trample");

        foreach (var effect in atk.Effects) effect.Execute();

        _alice.EnergyCounters.Should().Be(1, "one of two energy spent on the optional {E}");
        c.GetPower().Should().Be(4, "+1/+1 EOT registered (Layer 7c)");
        c.GetToughness().Should().Be(3);
        svc.Compute(c).Keywords.Should().Contain("Trample",
            "Trample grant registered (CR 613.1c Layer 6)");

        // CR 514.2 — cleanup step removes EOT effects.
        svc.ExpireEndOfTurn();
        c.GetPower().Should().Be(3, "pump expired");
        c.GetToughness().Should().Be(2);
        svc.Compute(c).Keywords.Should().NotContain("Trample", "Trample grant expired");
    }

    [Fact]
    public void VoltaicBrawler_AttackWithNoEnergy_NoPayNoGrants()
    {
        AgentRegistry.Remove(_alice);

        var c = VoltaicBrawlerFactory.Create(_alice);
        var atk = AttackTrigger(c);

        c.SetZone(ZoneType.Battlefield);
        var svc = new ContinuousEffectsService();
        c.ActiveEffects = svc;
        // No energy banked.

        foreach (var effect in atk.Effects) effect.Execute();

        _alice.EnergyCounters.Should().Be(0, "nothing to pay — CR 117.5 'may pay' bounded by affordability");
        c.GetPower().Should().Be(3, "no pump without payment");
        svc.Compute(c).Keywords.Should().NotContain("Trample", "no trample without payment");
    }

    [Fact]
    public void VoltaicBrawler_AttackAgentDeclines_NoPayNoGrants()
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the optional {E}
        AgentRegistry.Set(_alice, agent);
        try
        {
            var c = VoltaicBrawlerFactory.Create(_alice);
            var atk = AttackTrigger(c);

            c.SetZone(ZoneType.Battlefield);
            var svc = new ContinuousEffectsService();
            c.ActiveEffects = svc;
            _alice.GainEnergy(2);

            foreach (var effect in atk.Effects) effect.Execute();

            _alice.EnergyCounters.Should().Be(2, "agent declined — no energy spent");
            c.GetPower().Should().Be(3, "no pump when the optional cost is declined");
            svc.Compute(c).Keywords.Should().NotContain("Trample");
        }
        finally
        {
            AgentRegistry.Remove(_alice);
        }
    }

    [Fact]
    public void VoltaicBrawler_AttackPaid_NullActiveEffects_DoesNotThrow()
    {
        AgentRegistry.Remove(_alice);

        var c = VoltaicBrawlerFactory.Create(_alice);
        var atk = AttackTrigger(c);

        c.SetZone(ZoneType.Battlefield);
        // c.ActiveEffects intentionally null.
        _alice.GainEnergy(2);

        var act = () => { foreach (var effect in atk.Effects) effect.Execute(); };
        act.Should().NotThrow("shape-only path: grants guard on null ActiveEffects");
        _alice.EnergyCounters.Should().Be(1, "energy still spent before the null-guarded grants");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VoltaicBrawler_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Voltaic Brawler", _alice);

        card.Should().BeOfType<Creature>("Voltaic Brawler is a Creature");
        card.Name.Should().Be("Voltaic Brawler");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "the dispatcher attaches the ETB energy + pay-{E} attack triggers");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility EtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static TriggeredAbility AttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);
}
