using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TidebinderMageFactory"/>.
///
/// Card: Tidebinder Mage — {U}{U} Creature — Merfolk Wizard 2/2.
/// Oracle text (Scryfall, verified 2026-06-02):
///   "When this creature enters, tap target red or green creature an opponent
///    controls. That creature doesn't untap during its controller's untap step
///    for as long as you control this creature."
///
/// Covers:
/// - Identity ({U}{U}, blue, 2/2, Creature — Merfolk Wizard, mana value 2).
/// - Exactly one battlefield-active ETB TriggeredAbility.
/// - ETB target request: 1..1 "target red or green creature an opponent controls".
/// - Candidate gatherer offers only red/green opponent creatures (CR 109.5).
/// - ETB effect taps the chosen target (CR 701.20).
/// - ETB effect marks the target to skip its controller's untap step (CR 502.1).
/// - "as long as you control this creature" (CR 611.2b): the skip-untap lock is
///   removed when the source (Tidebinder Mage) leaves the battlefield.
/// - CR 608.2b: illegal / off-battlefield target at resolution → clean no-op.
/// </summary>
[Trait("Color", "U")]
public class TidebinderMageFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        // Clear the global untap-skip registry so skip-untap assertions
        // don't bleed into other test cases.
        UntapStepRestrictions.Clear();
    }

    private Creature NewOpponentCreature(string name, string manaCost)
    {
        var c = new Creature(name, manaCost, 2, 2);
        c.SetOwner(_bob);
        c.SetController(_bob);
        c.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TidebinderMage_Identity()
    {
        var c = TidebinderMageFactory.Create(_alice);

        c.Name.Should().Be("Tidebinder Mage");
        c.ManaCost.Should().Be("{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TidebinderMage_IsBlue()
    {
        var c = TidebinderMageFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Tidebinder Mage has {U}{U} in its mana cost");
    }

    [Fact]
    public void TidebinderMage_ManaValueIsTwo()
    {
        var c = TidebinderMageFactory.Create(_alice);

        // {U}{U} → two coloured pips = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TidebinderMage_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = TidebinderMageFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Tidebinder Mage has exactly one triggered ability — the ETB tap + skip-untap");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    [Fact]
    public void TidebinderMage_EtbTrigger_DeclaresOneRequiredRedOrGreenTarget()
    {
        var c = TidebinderMageFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1,
            "Tidebinder Mage's ETB names exactly one target");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1, "the tap is mandatory (not a 'may' clause)");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("red or green",
            because: "the target is a red or green creature an opponent controls");
    }

    [Fact]
    public void TidebinderMage_EtbTrigger_CandidateGatherer_OnlyRedOrGreenOpponentCreatures()
    {
        var tide = TidebinderMageFactory.Create(_alice);
        tide.SetOwner(_alice);
        tide.SetController(_alice);
        tide.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tide);

        var redOpp = NewOpponentCreature("Goblin Guide", "{R}");
        var greenOpp = NewOpponentCreature("Llanowar Elves", "{G}");
        var blueOpp = NewOpponentCreature("Merfolk Looter", "{1}{U}");
        // A red creature Alice controls — must be excluded (opponent-only).
        var redMine = new Creature("Mogg Fanatic", "{R}", 1, 1);
        redMine.SetOwner(_alice);
        redMine.SetController(_alice);
        redMine.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(redMine);

        var etb = tide.Abilities.OfType<TriggeredAbility>().Single();
        var req = etb.TargetRequests[0];

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));
        var candidates = req.CandidateGatherer!(ctx).Cast<Creature>().ToList();

        candidates.Should().Contain(redOpp, "a red creature an opponent controls is legal");
        candidates.Should().Contain(greenOpp, "a green creature an opponent controls is legal");
        candidates.Should().NotContain(blueOpp, "a blue (non-red, non-green) creature is illegal");
        candidates.Should().NotContain(redMine, "creatures you control are not opponent-controlled");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — tap effect (CR 701.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void TidebinderMage_Etb_TapsTargetCreature()
    {
        var tide = TidebinderMageFactory.Create(_alice);

        var target = NewOpponentCreature("Goblin Guide", "{R}");

        var etb = tide.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in etb.Effects) e.Execute();

        target.IsTapped.Should().BeTrue(
            "Tidebinder Mage's ETB taps the target creature (CR 701.20)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — skip-untap effect (CR 502.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void TidebinderMage_Etb_MarksTargetToSkipUntapStep()
    {
        var tide = TidebinderMageFactory.Create(_alice);

        var target = NewOpponentCreature("Llanowar Elves", "{G}");

        var etb = tide.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var e in etb.Effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(target, _bob).Should().BeTrue(
            "Tidebinder Mage marks the target to skip its controller's untap step (CR 502.1)");
    }

    // -----------------------------------------------------------------------
    // "as long as you control this creature" (CR 611.2b) — lock ends when the
    // SOURCE leaves the battlefield, NOT on the next untap step.
    // -----------------------------------------------------------------------

    [Fact]
    public void TidebinderMage_Lock_PersistsUntilSourceLeavesBattlefield()
    {
        var bus = new EventBus();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);

        var tide = TidebinderMageFactory.Create(_alice, bus, triggers);
        tide.SetOwner(_alice);
        tide.SetController(_alice);
        tide.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tide);

        var target = NewOpponentCreature("Goblin Guide", "{R}");

        var etb = tide.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(target, _bob).Should().BeTrue(
            "the lock holds while Alice still controls Tidebinder Mage");

        // Tidebinder Mage leaves the battlefield -> lock ends (CR 611.2b).
        bus.Publish(new CardMovedEvent(tide, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ShouldSkipUntap(target, _bob).Should().BeFalse(
            "the lock is removed once Alice no longer controls Tidebinder Mage (CR 611.2b)");
    }

    // -----------------------------------------------------------------------
    // CR 608.2b — illegal target at resolution → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void TidebinderMage_Etb_TargetLeftBattlefield_NoOp()
    {
        var tide = TidebinderMageFactory.Create(_alice);

        var target = new Creature("Goblin Guide", "{R}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var etb = tide.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow("CR 608.2b — illegal target at resolution is a clean no-op");
        target.IsTapped.Should().BeFalse("off-battlefield card is not tapped");
        UntapStepRestrictions.ShouldSkipUntap(target, _bob).Should().BeFalse(
            "off-battlefield card is not marked for skip-untap");
    }
}
