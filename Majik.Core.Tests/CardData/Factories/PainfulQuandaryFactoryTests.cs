using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PainfulQuandaryFactory"/> — Painful Quandary
/// (Scars of Mirrodin, {3}{B}{B}, Enchantment).
///
/// Oracle text (Scryfall verified 2026-06-24):
///   "Whenever an opponent casts a spell, that player loses 5 life unless
///    they discard a card."
///
/// The opponent-cast trigger mirrors
/// <see cref="KambalConsulOfAllocationFactory"/> (opponent casts a spell →
/// triggered ability), but Painful Quandary fires on ANY spell (no noncreature
/// gate) and resolves a per-player "loses 5 life unless they discard a card"
/// choice — the discard-or-penalty shape of
/// <see cref="SolitaryConfinementFactory.ResolveUpkeep"/>, except the chooser is
/// the AFFECTED opponent and the penalty is life loss.
///
/// Covers:
/// - Identity (name, Enchantment type, {3}{B}{B}).
/// - The trigger fires on an OPPONENT's spell cast (CR 603.1 / 109.5) and NOT on
///   the controller's own spell.
/// - Resolve: affected opponent who declines (or can't) discards → loses 5 life.
/// - Resolve: affected opponent who discards → loses NO life, a card goes to
///   their graveyard.
/// - Resolve: empty hand → the discard cost can't be paid → loses 5 life.
/// </summary>
[Trait("Color", "B")]
public class PainfulQuandaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSpell(Player controller)
    {
        var card = new Instant("Shock", "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(card, controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PainfulQuandary_Identity()
    {
        var c = PainfulQuandaryFactory.Create(_alice);

        c.Name.Should().Be("Painful Quandary");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{B}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Painful Quandary has a single opponent-cast trigger");
    }

    // -----------------------------------------------------------------------
    // Trigger gating — opponent's spell fires it; controller's does not.
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastsSpell_FiresTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var quandary = PainfulQuandaryFactory.Create(
            _alice, triggers, bus, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(quandary);
        quandary.SetZone(ZoneType.Battlefield);

        // Bob (an opponent of Alice) casts a spell.
        bus.Publish(new SpellCastEvent(NewSpell(_bob)));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell cast fires Painful Quandary's trigger (CR 603.1)");
    }

    [Fact]
    public void ControllerCastsSpell_DoesNotFireTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var quandary = PainfulQuandaryFactory.Create(
            _alice, triggers, bus, agentSelector: null);
        _alice.Zones.Battlefield.AddCard(quandary);
        quandary.SetZone(ZoneType.Battlefield);

        // Alice (the controller) casts a spell — "an opponent" excludes her.
        bus.Publish(new SpellCastEvent(NewSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "Painful Quandary's own controller's casts do not fire it (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Resolve — loses 5 life unless they discard a card.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AffectedPlayerDeclinesDiscard_Loses5Life()
    {
        // Bob has a card but his agent declines to discard.
        var handCard = new Instant("Lightning Bolt", "R") { Owner = _bob };
        _bob.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline the discard

        PainfulQuandaryFactory.Resolve(_bob, eventBus: null, agentSelector: _ => agent);

        _bob.LifeTotal.Should().Be(15, "CR 119.3 — declining the discard loses 5 life");
        _bob.Zones.Hand.GetCards().Should().Contain(handCard,
            "the card stays in hand when the discard is declined");
    }

    [Fact]
    public void Resolve_AffectedPlayerDiscards_LosesNoLife()
    {
        var handCard = new Instant("Lightning Bolt", "R") { Owner = _bob };
        _bob.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);          // choose to discard
        agent.QueueFromHand(handCard);   // discard that specific card

        PainfulQuandaryFactory.Resolve(_bob, eventBus: null, agentSelector: _ => agent);

        _bob.LifeTotal.Should().Be(20, "CR 701.8 — discarding pays the cost, no life lost");
        _bob.Zones.Hand.GetCards().Should().NotContain(handCard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(handCard,
            "the discarded card goes to its owner's graveyard");
    }

    [Fact]
    public void Resolve_EmptyHand_Loses5Life()
    {
        // Bob has no card — the discard cost can't be paid (CR 608.2), so he
        // loses 5 life. No agent prompt is needed.
        PainfulQuandaryFactory.Resolve(_bob, eventBus: null, agentSelector: null);

        _bob.LifeTotal.Should().Be(15,
            "an empty hand cannot pay the discard cost, so the player loses 5 life");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty("nothing was discarded");
    }
}
