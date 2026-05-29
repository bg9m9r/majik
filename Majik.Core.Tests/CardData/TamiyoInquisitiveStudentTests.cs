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
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Tamiyo, Inquisitive Student // Tamiyo, Seasoned
/// Scholar (MH3 transform DFC front face) — Legendary Creature — Moonfolk
/// Wizard {U} 0/3, Flying.
///   "Whenever Tamiyo attacks, investigate."
///   "When you draw your third card in a turn, exile Tamiyo, then return her
///    to the battlefield transformed under her owner's control."
///
/// Validates:
///   * Card identity + dispatch + Flying keyword + MdfcState faces.
///   * CR 508.1f / CR 701.30 — attack trigger fires and creates a Clue token.
///   * CR 603.2 / CR 701.28 — the third draw in a turn flips MdfcState to the
///     back face (Tamiyo, Seasoned Scholar); the first/second draws do not.
///   * The draw count resets across turns.
/// </summary>
public class TamiyoInquisitiveStudentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static (EventBus bus, ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (bus, zones, stack, triggers);
    }

    private static ICard MakeLibraryCard(string name, Player owner)
    {
        var c = new Creature(name, "{1}", 1, 1);
        c.SetOwner(owner);
        return c;
    }

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void Tamiyo_Identity_MoonfolkWizard_0_3_AtU()
    {
        var t = TamiyoInquisitiveStudentFactory.Create(_alice);

        t.Name.Should().Be("Tamiyo, Inquisitive Student");
        t.HasType(CardType.Creature).Should().BeTrue();
        t.HasSubtype(CardSubtype.Moonfolk).Should().BeTrue();
        t.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        t.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        t.ManaCost.Should().Be("{U}");
        t.BasePower.Should().Be(0);
        t.BaseToughness.Should().Be(3);
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);

        // CR 702.9 — Flying.
        t.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying");

        // Two front-face triggers: attack→investigate + draw-third→transform.
        t.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Tamiyo_HasMdfcState_OnFrontFace()
    {
        var t = TamiyoInquisitiveStudentFactory.Create(_alice);

        t.MdfcState.Should().NotBeNull("DFC card must carry an MdfcState (CR 711)");
        t.MdfcState!.FrontFaceName.Should().Be("Tamiyo, Inquisitive Student");
        t.MdfcState.BackFaceName.Should().Be("Tamiyo, Seasoned Scholar");
        t.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
        t.MdfcState.ActiveFaceName.Should().Be("Tamiyo, Inquisitive Student");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Tamiyo_AsCreatureWithMdfc()
    {
        var dispatched = NamedCardFactory.Create(
            "Tamiyo, Inquisitive Student // Tamiyo, Seasoned Scholar", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Tamiyo, Inquisitive Student");
        dispatched.ManaCost.Should().Be("{U}");

        var t = (Creature)dispatched;
        t.MdfcState.Should().NotBeNull(
            "the dispatcher route must attach the DFC face-tracker");
        t.MdfcState!.BackFaceName.Should().Be("Tamiyo, Seasoned Scholar");
    }

    // ------------------------------------------------------------------
    // CR 508.1f / CR 701.30 — attack → investigate
    // ------------------------------------------------------------------

    [Fact]
    public void Tamiyo_WhenAttacks_Investigates_CreatingOneClue()
    {
        var (bus, zones, stack, triggers) = BuildEngine();

        var t = TamiyoInquisitiveStudentFactory.Create(_alice, zones, triggers, bus);
        t.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(t);

        bus.Publish(new CreatureAttacksEvent(t, _bob));

        triggers.PendingCount.Should().Be(1,
            "the attack trigger fires when Tamiyo is declared as an attacker");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var clues = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Clue))
            .ToList();
        clues.Should().HaveCount(1, "investigate creates one Clue token (CR 701.30)");
        clues[0].IsToken.Should().BeTrue();
    }

    [Fact]
    public void Tamiyo_AttackTrigger_DoesNotFire_ForAnotherAttacker()
    {
        var (bus, zones, _, triggers) = BuildEngine();

        var t = TamiyoInquisitiveStudentFactory.Create(_alice, zones, triggers, bus);
        t.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(t);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        bears.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bears);

        // A different creature attacking must not fire Tamiyo's per-attacker
        // trigger (CR 508.1f — "Whenever Tamiyo attacks").
        bus.Publish(new CreatureAttacksEvent(bears, _bob));

        triggers.PendingCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // CR 603.2 / CR 701.28 — draw your third card → transform
    // ------------------------------------------------------------------

    [Fact]
    public void Tamiyo_OnThirdDrawInTurn_Transforms()
    {
        var (bus, zones, stack, triggers) = BuildEngine();

        var t = TamiyoInquisitiveStudentFactory.Create(_alice, zones, triggers, bus);
        t.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(t);

        // First two draws — no transform yet.
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c1", _alice), _alice));
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c2", _alice), _alice));
        triggers.PendingCount.Should().Be(0, "first two draws do not trigger");
        t.MdfcState!.IsBackFace.Should().BeFalse();

        // Third draw — trigger fires.
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c3", _alice), _alice));
        triggers.PendingCount.Should().Be(1,
            "the third draw in a turn fires the transform trigger (CR 603.2)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        t.Zone.Should().Be(ZoneType.Battlefield, "Tamiyo stays on the battlefield (v1 flip-in-place)");
        t.MdfcState!.IsBackFace.Should().BeTrue(
            "CR 701.28 — transform flips MdfcState to the back face");
        t.MdfcState.ActiveFaceName.Should().Be("Tamiyo, Seasoned Scholar");
    }

    [Fact]
    public void Tamiyo_OpponentDraws_DoNotCountTowardThird()
    {
        var (bus, zones, _, triggers) = BuildEngine();

        var t = TamiyoInquisitiveStudentFactory.Create(_alice, zones, triggers, bus);
        t.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(t);

        // Bob drawing three cards must not advance Alice's "you draw" counter.
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("b1", _bob), _bob));
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("b2", _bob), _bob));
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("b3", _bob), _bob));

        triggers.PendingCount.Should().Be(0, "the trigger is owner-scoped (\"you draw\")");
        t.MdfcState!.IsBackFace.Should().BeFalse();
    }

    [Fact]
    public void Tamiyo_DrawCount_ResetsBetweenTurns()
    {
        var (bus, zones, _, triggers) = BuildEngine();

        var t = TamiyoInquisitiveStudentFactory.Create(_alice, zones, triggers, bus);
        t.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(t);

        // Two draws this turn, then a new turn resets the count.
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c1", _alice), _alice));
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c2", _alice), _alice));
        bus.Publish(new TurnStartedEvent(_bob, 2));

        // Two more draws on the new turn — still only the second of THIS turn,
        // so the trigger must not fire (count was reset).
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c3", _alice), _alice));
        bus.Publish(new CardDrawnEvent(MakeLibraryCard("c4", _alice), _alice));

        triggers.PendingCount.Should().Be(0,
            "the per-turn draw count resets at the start of each turn");
        t.MdfcState!.IsBackFace.Should().BeFalse();
    }
}
