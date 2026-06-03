using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the split/fuse card Beck // Call (Dragon's Maze).
///   Beck {G}{U} — "Whenever a creature enters this turn, you may draw a card."
///   Call {4}{W}{U} — "Create four 1/1 white Bird creature tokens with flying."
///
/// The headline is the Beck face: a TURN-SCOPED REPEATING delayed trigger
/// (CR 603.7e). It fires every time a creature enters until end-of-turn
/// cleanup tears it down — the deferral this card unblocks.
/// </summary>
public class BeckCallTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);

    public BeckCallTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Beck_Identity()
    {
        var c = BeckFactory.Create(_alice);
        c.Name.Should().Be("Beck");
        c.ManaCost.Should().Be("{G}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Call_Identity()
    {
        var c = CallFactory.Create(_alice);
        c.Name.Should().Be("Call");
        c.ManaCost.Should().Be("{4}{W}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void Combined_Identity()
    {
        var c = BeckCallFactory.Create(_alice);
        c.Name.Should().Be("Beck // Call");
        c.ManaCost.Should().Be("{G}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CombinedName()
    {
        var card = NamedCardFactory.Create("Beck // Call", _alice);
        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Beck // Call");
    }

    // -----------------------------------------------------------------------
    // Call face — four 1/1 white Bird tokens with flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Call_CreatesFourFlyingWhiteBirdTokens()
    {
        var effects = CallFactory.BuildResolveEffect(_alice, _zones);
        foreach (var e in effects) e.Execute();

        var birds = _alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        birds.Should().HaveCount(4);
        birds.Should().OnlyContain(b =>
            b.Name == "Bird" && b.Power == 1 && b.Toughness == 1 && b.IsToken);
        birds.Should().OnlyContain(b => b.HasSubtype(CardSubtype.Bird));
        birds.Should().OnlyContain(b =>
            b.Abilities.OfType<KeywordAbility>().Any(k => k.Keyword == "Flying"));
    }

    // -----------------------------------------------------------------------
    // Beck face — turn-scoped repeating delayed trigger (CR 603.7e)
    // -----------------------------------------------------------------------

    [Fact]
    public void Beck_RegistersRepeatingTrigger_DrawsOnEachCreatureEnter()
    {
        SeedLibrary(_alice, 6);

        // Resolve Beck — registers the repeating delayed trigger.
        foreach (var e in BeckFactory.BuildResolveEffect(_alice, _triggers))
            e.Execute();

        // Now resolve Call — four Birds enter, each through the ZoneService so
        // CardMovedEvent fires; the repeating trigger fires four times.
        foreach (var e in CallFactory.BuildResolveEffect(_alice, _zones))
            e.Execute();

        // Drain + resolve every queued draw trigger.
        DrainAndResolve();

        _alice.Zones.Hand.GetCards().Should().HaveCount(4,
            because: "the repeating delayed trigger fires once per creature entering (CR 603.7e)");
    }

    [Fact]
    public void Beck_Trigger_ExpiresAtEndOfTurnCleanup()
    {
        SeedLibrary(_alice, 6);
        foreach (var e in BeckFactory.BuildResolveEffect(_alice, _triggers))
            e.Execute();

        // One creature enters before cleanup — draws one.
        EnterCreature(_alice);
        DrainAndResolve();
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);

        // CR 603.7e / CR 514.2 — end-of-turn cleanup tears the trigger down.
        _triggers.ExpireTurnScopedDelayedTriggers();

        // A creature entering NEXT turn no longer draws.
        EnterCreature(_alice);
        DrainAndResolve();
        _alice.Zones.Hand.GetCards().Should().HaveCount(1,
            because: "the 'this turn' repeating trigger expired at cleanup (CR 603.7e)");
    }

    [Fact]
    public void Beck_OptionalDraw_DeclineSkipsDraw()
    {
        SeedLibrary(_alice, 3);
        foreach (var e in BeckFactory.BuildResolveEffect(_alice, _triggers, mayDraw: () => false))
            e.Execute();

        EnterCreature(_alice);
        DrainAndResolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            because: "the controller declined the optional 'you may draw' (CR 603.5)");
    }

    [Fact]
    public void Beck_NoTriggerManager_ResolveIsNoOp()
    {
        var effects = BeckFactory.BuildResolveEffect(_alice, triggers: null);
        foreach (var e in effects) e.Execute();
        // No throw, nothing registered.
        EnterCreature(_alice);
        _triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void DrainAndResolve()
    {
        _triggers.PutPendingTriggersOnStack(_alice);
        while (_stack.Count > 0)
        {
            _stack.Pop()!.Resolve();
        }
    }

    private void EnterCreature(Player controller)
    {
        var creature = new Creature("Grizzly", manaCost: "", power: 2, toughness: 2)
        {
            Owner = controller,
            Controller = controller,
        };
        creature.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(creature);
        _zones.MoveCardTo(creature, ZoneType.Battlefield, controller);
    }

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Lib{i}", "");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
