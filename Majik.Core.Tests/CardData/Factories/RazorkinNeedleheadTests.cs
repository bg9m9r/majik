using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Razorkin Needlehead (Duskmourn: House of Horror, {R}{R},
/// Creature — Human Assassin 2/2).
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (mana cost / P-T / subtypes — non-vanilla stats assert).
///   - "First strike during your turn" (CR 613.1f): granted while the active
///     player is the controller; absent during an opponent's turn.
///   - "Whenever an opponent draws a card, deal 1 damage to them" (CR 603.1 /
///     CR 119.3): fires on an opponent's draw, NOT on the controller's own
///     draw.
/// </summary>
[Trait("Color", "R")]
public class RazorkinNeedleheadTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats)
    // -----------------------------------------------------------------------

    [Fact]
    public void RazorkinNeedlehead_Identity_HumanAssassin_2_2_AtCostRR()
    {
        var card = RazorkinNeedleheadFactory.Create(_alice);

        card.Name.Should().Be("Razorkin Needlehead");
        card.ManaCost.Should().Be("{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // "This creature has first strike during your turn." (CR 613.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstStrike_GrantedDuringControllersTurn()
    {
        var effects = new ContinuousEffectsService();
        var card = RazorkinNeedleheadFactory.Create(_alice, effects);
        card.SetZone(ZoneType.Battlefield);

        // It is Alice's turn → first strike granted.
        effects.ActivePlayer = _alice;
        CombatAbilities.HasFirstStrike(card).Should().BeTrue();
    }

    [Fact]
    public void FirstStrike_AbsentDuringOpponentsTurn()
    {
        var effects = new ContinuousEffectsService();
        var card = RazorkinNeedleheadFactory.Create(_alice, effects);
        card.SetZone(ZoneType.Battlefield);

        // It is Bob's turn → no first strike (the static gates on the
        // controller's turn — CR 611.2c).
        effects.ActivePlayer = _bob;
        CombatAbilities.HasFirstStrike(card).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // "Whenever an opponent draws a card, this creature deals 1 damage to
    // them." (CR 603.1 / CR 119.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentDraw_FiresTrigger_Deals1DamageToDrawer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = RazorkinNeedleheadFactory.Create(_alice, effects: null);
        card.SetZone(ZoneType.Battlefield);
        foreach (var t in card.Abilities.OfType<TriggeredAbility>())
        {
            triggers.RegisterTriggeredAbility(t);
        }

        var drawn = new Card("Drawn", "");
        drawn.SetOwner(_bob);
        bus.Publish(new CardDrawnEvent(drawn, _bob));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 119.3 — Bob (the drawing opponent) takes 1 damage.
        _bob.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void ControllerDraw_DoesNotFireTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = RazorkinNeedleheadFactory.Create(_alice, effects: null);
        card.SetZone(ZoneType.Battlefield);
        foreach (var t in card.Abilities.OfType<TriggeredAbility>())
        {
            triggers.RegisterTriggeredAbility(t);
        }

        var drawn = new Card("Drawn", "");
        drawn.SetOwner(_alice);
        bus.Publish(new CardDrawnEvent(drawn, _alice));

        // Alice is the controller — "an opponent" excludes her (CR 102.2).
        triggers.PendingCount.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
    }
}
