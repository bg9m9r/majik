using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Ral, Monsoon Mage // Ral, Leyline Prodigy (MH3-style
/// transform DFC). Front face: Legendary Creature — Human Wizard {1}{R} 1/3.
///   "Instant and sorcery spells you cast cost {1} less to cast.
///    Whenever you cast an instant or sorcery spell during your turn, flip a
///    coin. If you lose the flip, Ral deals 1 damage to you. If you win the
///    flip, you may exile Ral. If you do, return him to the battlefield
///    transformed under his owner's control."
/// Back face: Legendary Planeswalker — Ral (Ral, Leyline Prodigy), loyalty 2 —
/// shape-only tracked through MdfcState (same posture as Tamiyo / Ajani).
///
/// Validates:
///   * Card identity + dispatch + MdfcState faces.
///   * CR 117.7 — instant/sorcery cost reduction (Baral shape).
///   * CR 603.1 — cast trigger fires only on your-turn instant/sorcery casts.
///   * Coin: lose → 1 damage to you; win → transform to the back face.
/// </summary>
public class RalMonsoonMageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static (EventBus bus, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (bus, stack, triggers);
    }

    private static ISpell MakeSpell(string name, string cost, CardType type, Player controller)
    {
        Card card = type == CardType.Instant
            ? new Instant(name, cost)
            : type == CardType.Sorcery
                ? new Sorcery(name, cost)
                : new Creature(name, cost, 1, 1);
        card.SetOwner(controller);
        card.SetController(controller);
        return new Majik.Core.Spells.Spell(card, controller, new List<ITarget>(), new List<ICost>());
    }

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void Ral_Identity_HumanWizard_1_3_At1R()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);

        ral.Name.Should().Be("Ral, Monsoon Mage");
        ral.HasType(CardType.Creature).Should().BeTrue();
        ral.HasSubtype(CardSubtype.Human).Should().BeTrue();
        ral.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ral.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ral.ManaCost.Should().Be("{1}{R}");
        ral.BasePower.Should().Be(1);
        ral.BaseToughness.Should().Be(3);
        ral.Owner.Should().BeSameAs(_alice);
        ral.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ral_HasMdfcState_OnFrontFace()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);

        ral.MdfcState.Should().NotBeNull("DFC card must carry an MdfcState (CR 711)");
        ral.MdfcState!.FrontFaceName.Should().Be("Ral, Monsoon Mage");
        ral.MdfcState.BackFaceName.Should().Be("Ral, Leyline Prodigy");
        ral.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
        ral.MdfcState.ActiveFaceName.Should().Be("Ral, Monsoon Mage");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Ral_AsCreatureWithMdfc()
    {
        var dispatched = NamedCardFactory.Create(
            "Ral, Monsoon Mage // Ral, Leyline Prodigy", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Ral, Monsoon Mage");
        dispatched.ManaCost.Should().Be("{1}{R}");

        var ral = (Creature)dispatched;
        ral.MdfcState.Should().NotBeNull(
            "the dispatcher route must attach the DFC face-tracker");
        ral.MdfcState!.BackFaceName.Should().Be("Ral, Leyline Prodigy");
    }

    // ------------------------------------------------------------------
    // CR 117.7 — instant/sorcery cost reduction (Baral shape)
    // ------------------------------------------------------------------

    [Fact]
    public void Ral_ReducesInstantAndSorceryCost_ByOneGeneric()
    {
        var ral = RalMonsoonMageFactory.Create(_alice);
        ral.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ral);

        // {R} has no generic to shave, so verify the reducer is discovered and
        // applied on a spell that has generic mana: {2}{U} drops to {1}{U}.
        var divination = new Sorcery("Divination", "{2}{U}");
        divination.SetOwner(_alice);
        divination.SetController(_alice);
        var divCost = CostReduction.GetEffectiveCost(divination, _alice);
        divCost.Generic.Should().Be(1, "Ral shaves {1} generic off instant/sorcery casts");
    }

    // ------------------------------------------------------------------
    // CR 603.1 — cast trigger: your-turn instant/sorcery
    // ------------------------------------------------------------------

    [Fact]
    public void Ral_CastTrigger_FiresOnYourTurnInstantOrSorcery()
    {
        var (bus, _, triggers) = BuildEngine();

        var ral = RalMonsoonMageFactory.Create(_alice, triggers, bus, coinLoses: () => true);
        ral.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ral);

        // Establish that it is Alice's turn.
        bus.Publish(new TurnStartedEvent(_alice, 1));

        bus.Publish(new SpellCastEvent(MakeSpell("Opt", "{U}", CardType.Instant, _alice)));

        triggers.PendingCount.Should().Be(1,
            "casting an instant on your turn fires the coin-flip trigger (CR 603.1)");
    }

    [Fact]
    public void Ral_CastTrigger_DoesNotFire_ForCreatureSpell()
    {
        var (bus, _, triggers) = BuildEngine();

        var ral = RalMonsoonMageFactory.Create(_alice, triggers, bus, coinLoses: () => true);
        ral.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ral);
        bus.Publish(new TurnStartedEvent(_alice, 1));

        bus.Publish(new SpellCastEvent(MakeSpell("Grizzly Bears", "{1}{G}", CardType.Creature, _alice)));

        triggers.PendingCount.Should().Be(0, "only instant/sorcery casts trigger");
    }

    [Fact]
    public void Ral_CastTrigger_DoesNotFire_OnOpponentTurn()
    {
        var (bus, _, triggers) = BuildEngine();

        var ral = RalMonsoonMageFactory.Create(_alice, triggers, bus, coinLoses: () => true);
        ral.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ral);

        // It is Bob's turn — Alice casting an instant must NOT fire ("during
        // your turn").
        bus.Publish(new TurnStartedEvent(_bob, 2));

        bus.Publish(new SpellCastEvent(MakeSpell("Opt", "{U}", CardType.Instant, _alice)));

        triggers.PendingCount.Should().Be(0, "the trigger is gated on the controller's own turn");
    }

    // ------------------------------------------------------------------
    // Coin flip resolution
    // ------------------------------------------------------------------

    [Fact]
    public void Ral_LoseFlip_Deals1DamageToYou_NoTransform()
    {
        var (bus, stack, triggers) = BuildEngine();

        var ral = RalMonsoonMageFactory.Create(_alice, triggers, bus, coinLoses: () => true);
        ral.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ral);
        bus.Publish(new TurnStartedEvent(_alice, 1));

        bus.Publish(new SpellCastEvent(MakeSpell("Opt", "{U}", CardType.Instant, _alice)));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(19, "losing the flip deals 1 damage to you");
        ral.MdfcState!.IsBackFace.Should().BeFalse("a lost flip does not transform Ral");
    }

    [Fact]
    public void Ral_WinFlip_Transforms_ToLeylineProdigy()
    {
        var (bus, stack, triggers) = BuildEngine();

        var ral = RalMonsoonMageFactory.Create(_alice, triggers, bus, coinLoses: () => false);
        ral.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ral);
        bus.Publish(new TurnStartedEvent(_alice, 1));

        bus.Publish(new SpellCastEvent(MakeSpell("Opt", "{U}", CardType.Instant, _alice)));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(20, "winning the flip deals no damage");
        ral.MdfcState!.IsBackFace.Should().BeTrue(
            "winning the flip transforms Ral to the back face (CR 701.28)");
        ral.MdfcState.ActiveFaceName.Should().Be("Ral, Leyline Prodigy");
    }
}
