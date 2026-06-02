using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlintSleeveSiphonerFactory"/>.
///
/// Card: Glint-Sleeve Siphoner (Aether Revolt, {1}{B}). Creature — Human
/// Rogue 2/1.
///   "Menace
///    Whenever this creature enters or attacks, you get {E} (an energy
///    counter).
///    At the beginning of your upkeep, you may pay {E}{E}. If you do, you
///    draw a card and you lose 1 life."
///
/// Covers:
/// - Card identity (name, {1}{B} via JSON shape, Creature type, Human +
///   Rogue subtypes, 2/1 P/T, owner/controller).
/// - Menace keyword marker (CR 702.111).
/// - Ability shape: three TriggeredAbilities (enters energy, attacks energy,
///   upkeep draw), no ActivatedAbilities, no TargetRequests.
/// - Enters predicate: fires on self entering battlefield; not on a
///   non-battlefield zone move. Resolution grants one energy.
/// - Attacks predicate: fires when this creature attacks; not another.
///   Resolution grants one energy.
/// - Upkeep predicate: fires at controller's upkeep step.
/// - Upkeep resolution (no-agent fallback): pays {E}{E}, draws a card,
///   loses 1 life.
/// - Upkeep resolution: insufficient energy → no pay, no draw, no life loss.
/// - Upkeep resolution: agent declines → no pay, no draw, no life loss.
/// - Upkeep resolution: empty library flags the draw-from-empty SBA.
/// - Dispatcher integration via NamedCardFactory.
/// </summary>
[Trait("Color", "B")]
public class GlintSleeveSiphonerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintSleeveSiphoner_NameIsCorrect()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.Name.Should().Be("Glint-Sleeve Siphoner");
    }

    [Fact]
    public void GlintSleeveSiphoner_IsCreature()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void GlintSleeveSiphoner_HasCorrectSubtypes()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.HasSubtype(CardSubtype.Human).Should().BeTrue("printed type is Human Rogue");
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void GlintSleeveSiphoner_HasCorrectStats()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void GlintSleeveSiphoner_OwnerAndControllerAreSet()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Menace — CR 702.111
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintSleeveSiphoner_HasMenace()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        CombatAbilities.HasMenace(c).Should().BeTrue(
            "the printed keyword line is Menace (CR 702.111)");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintSleeveSiphoner_HasThreeTriggeredAbilities()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(3,
            "enters-energy, attacks-energy, and the pay-{E}{E} upkeep draw triggers");
    }

    [Fact]
    public void GlintSleeveSiphoner_HasNoActivatedAbilities()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "all behaviours are triggered, not activated");
    }

    [Fact]
    public void GlintSleeveSiphoner_TriggersHaveNoTargetRequests()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);

        foreach (var t in c.Abilities.OfType<TriggeredAbility>())
        {
            t.TargetRequests.Should().BeEmpty(
                "every trigger names the controller ('you') — none targets");
        }
    }

    // -----------------------------------------------------------------------
    // Enters / attacks energy triggers — CR 603.6a / CR 508.1f / CR 106.13b
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintSleeveSiphoner_OwnEtb_FiresAndGrantsOneEnergy()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var etb = EntersTrigger(c);

        var evt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(evt, etb).Should().BeTrue(
            "Glint-Sleeve Siphoner's own ETB triggers the energy ability");

        _alice.EnergyCounters.Should().Be(0);
        foreach (var effect in etb.Effects) effect.Execute();
        _alice.EnergyCounters.Should().Be(1, "enters grants the controller {E} (CR 106.13b)");
    }

    [Fact]
    public void GlintSleeveSiphoner_NonBattlefieldZoneMove_EtbDoesNotFire()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var etb = EntersTrigger(c);

        var evt = new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard);
        etb.Condition.Matches(evt, etb).Should().BeFalse(
            "the enters trigger requires ToZone == Battlefield (entering, not leaving)");
    }

    [Fact]
    public void GlintSleeveSiphoner_AttackTrigger_FiresForSelf_NotForAnother()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var atk = AttacksTrigger(c);

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
    public void GlintSleeveSiphoner_AttackResolution_GrantsOneEnergy()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var atk = AttacksTrigger(c);

        _alice.EnergyCounters.Should().Be(0);
        foreach (var effect in atk.Effects) effect.Execute();
        _alice.EnergyCounters.Should().Be(1, "attacking grants the controller {E} (CR 106.13b)");
    }

    // -----------------------------------------------------------------------
    // Upkeep draw trigger — CR 500.4 / CR 117.5 / CR 120.2 / CR 119.3
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintSleeveSiphoner_UpkeepTrigger_FiresAtControllerUpkeep()
    {
        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var up = UpkeepTrigger(c);

        var evt = new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.Upkeep, _alice);
        up.Condition.Matches(evt, up).Should().BeTrue(
            "the trigger fires at the controller's upkeep (CR 500.4)");

        var oppEvt = new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.Upkeep, _bob);
        up.Condition.Matches(oppEvt, up).Should().BeFalse(
            "'your upkeep' is the controller's upkeep, not the opponent's");
    }

    [Fact]
    public void GlintSleeveSiphoner_UpkeepPaid_DrawsCardAndLosesLife_NoAgentFallback()
    {
        AgentRegistry.Remove(_alice); // no-agent fallback: pay when affordable

        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var up = UpkeepTrigger(c);
        _alice.GainEnergy(2);

        foreach (var effect in up.Effects) effect.Execute();

        _alice.EnergyCounters.Should().Be(0, "{E}{E} spent on the optional upkeep draw");
        _alice.Zones.Hand.GetCards().Should().Contain(top, "drew a card (CR 120.2)");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(19, "lost 1 life (CR 119.3)");
    }

    [Fact]
    public void GlintSleeveSiphoner_UpkeepWithInsufficientEnergy_NoPayNoDrawNoLifeLoss()
    {
        AgentRegistry.Remove(_alice);

        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var up = UpkeepTrigger(c);
        _alice.GainEnergy(1); // only one energy — cannot pay {E}{E}

        foreach (var effect in up.Effects) effect.Execute();

        _alice.EnergyCounters.Should().Be(1, "cannot pay {E}{E} with one energy — CR 117.5");
        _alice.Zones.Hand.GetCards().Should().NotContain(top, "no draw without payment");
        _alice.LifeTotal.Should().Be(20, "no life loss without payment");
    }

    [Fact]
    public void GlintSleeveSiphoner_UpkeepAgentDeclines_NoPayNoDrawNoLifeLoss()
    {
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the optional {E}{E}
        AgentRegistry.Set(_alice, agent);
        try
        {
            var top = new Card("Top of library", "");
            top.SetOwner(_alice);
            _alice.Zones.Library.AddCard(top);
            top.SetZone(ZoneType.Library);

            var c = GlintSleeveSiphonerFactory.Create(_alice);
            var up = UpkeepTrigger(c);
            _alice.GainEnergy(2);

            foreach (var effect in up.Effects) effect.Execute();

            _alice.EnergyCounters.Should().Be(2, "agent declined — no energy spent");
            _alice.Zones.Hand.GetCards().Should().NotContain(top, "no draw when declined");
            _alice.LifeTotal.Should().Be(20, "no life loss when declined");
        }
        finally
        {
            AgentRegistry.Remove(_alice);
        }
    }

    [Fact]
    public void GlintSleeveSiphoner_UpkeepPaid_EmptyLibrary_FlagsDrawFromEmpty()
    {
        AgentRegistry.Remove(_alice);

        var c = GlintSleeveSiphonerFactory.Create(_alice);
        var up = UpkeepTrigger(c);
        _alice.GainEnergy(2);
        // Empty library.

        var act = () => { foreach (var effect in up.Effects) effect.Execute(); };
        act.Should().NotThrow("empty-library draw is a graceful SBA flag, not a throw");

        _alice.EnergyCounters.Should().Be(0, "{E}{E} still spent before the empty draw");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue("drew from empty library — CR 704.5b");
        _alice.LifeTotal.Should().Be(19, "lost 1 life regardless of the empty draw (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GlintSleeveSiphoner_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Glint-Sleeve Siphoner", _alice);

        card.Should().BeOfType<Creature>("Glint-Sleeve Siphoner is a Creature");
        card.Name.Should().Be("Glint-Sleeve Siphoner");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(3,
            "the dispatcher attaches the enters/attacks energy + upkeep draw triggers");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility EntersTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static TriggeredAbility AttacksTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    private static TriggeredAbility UpkeepTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
}
