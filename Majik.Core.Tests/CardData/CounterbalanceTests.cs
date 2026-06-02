using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CounterbalanceFactory"/> (Coldsnap, {U}{U}).
///
/// Oracle text:
///   "Whenever an opponent casts a spell, you may reveal the top card of
///    your library. If you do, counter that spell if it has the same mana
///    value as the revealed card."
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Opponent casts a spell whose MV matches the revealed top card → countered.
/// - Opponent casts a spell whose MV does NOT match → trigger fires but the
///   spell is left on the stack (the reveal didn't match).
/// - The controller's OWN cast does NOT trigger ("an opponent casts" —
///   asymmetric, unlike Chalice).
/// - Empty library: trigger fires (reveal pile empty), nothing is countered.
/// </summary>
public class CounterbalanceTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CounterbalanceTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Counterbalance_Identity()
    {
        var cb = CounterbalanceFactory.Create(_alice);

        cb.Name.Should().Be("Counterbalance");
        cb.ManaCost.Should().Be("{U}{U}");
        cb.HasType(CardType.Enchantment).Should().BeTrue();
        cb.Owner.Should().BeSameAs(_alice);
        cb.Controller.Should().BeSameAs(_alice);

        // One triggered ability: the cast-trigger reveal-and-counter.
        cb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Counterbalance_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Counterbalance", _alice);

        card.Should().BeOfType<Enchantment>("Counterbalance is an Enchantment");
        card.Name.Should().Be("Counterbalance");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Reveal-and-counter — CR 701.5
    // -----------------------------------------------------------------------

    [Fact]
    public void Counterbalance_OpponentSpellMatchesRevealedTop_IsCountered()
    {
        var cb = CounterbalanceFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        // Top of Alice's library is a mv-2 card.
        var top = new Instant("Lightning Helix", "{R}{W}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Bob casts a mv-2 spell.
        var helix = new Instant("Counterspell", "{U}{U}");
        helix.SetOwner(_bob);
        helix.SetController(_bob);
        helix.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(helix, _bob);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(1,
            "Counterbalance triggers on an opponent's cast");

        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        helix.Zone.Should().Be(ZoneType.Graveyard,
            "revealed top (mv 2) == cast spell (mv 2) → countered per CR 701.5");
        _bob.Zones.Graveyard.GetCards().Should().Contain(helix);
        _stack.GetAll().Should().NotContain(spell);

        // The revealed card stays on top of the library (only revealed).
        _alice.Zones.Library.GetCards().Should().Contain(top);
    }

    [Fact]
    public void Counterbalance_OpponentSpellDoesNotMatchRevealedTop_IsNotCountered()
    {
        var cb = CounterbalanceFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        // Top of library is mv 3.
        var top = new Sorcery("Skred", "{2}{R}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Bob casts a mv-2 spell — does NOT match the revealed top (mv 3).
        var spellCard = new Instant("Counterspell", "{U}{U}");
        spellCard.SetOwner(_bob);
        spellCard.SetController(_bob);
        spellCard.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(spellCard, _bob);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        // The trigger still fires (it's "whenever an opponent casts"), but the
        // reveal doesn't match, so nothing is countered.
        _triggers.PendingCount.Should().Be(1);
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        spellCard.Zone.Should().Be(ZoneType.Stack,
            "mv mismatch (cast 2 ≠ revealed 3) → spell is not countered");
        _stack.GetAll().Should().Contain(spell);
    }

    [Fact]
    public void Counterbalance_ControllersOwnCast_DoesNotTrigger()
    {
        var cb = CounterbalanceFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);

        var top = new Instant("Lightning Helix", "{R}{W}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Alice (Counterbalance's controller) casts a mv-2 spell — the
        // ability is asymmetric ("an opponent casts"), so it must NOT fire.
        var own = new Instant("Counterspell", "{U}{U}");
        own.SetOwner(_alice);
        own.SetController(_alice);
        own.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(own, _alice);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(0,
            "Counterbalance only triggers on an OPPONENT's cast (CR 109.5 / asymmetric)");
        own.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public void Counterbalance_EmptyLibrary_NothingCountered()
    {
        var cb = CounterbalanceFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(cb);
        cb.SetZone(ZoneType.Battlefield);
        // Library is empty — no top card to reveal.

        var spellCard = new Instant("Counterspell", "{U}{U}");
        spellCard.SetOwner(_bob);
        spellCard.SetController(_bob);
        spellCard.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(spellCard, _bob);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(1);
        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        spellCard.Zone.Should().Be(ZoneType.Stack,
            "no revealed card → nothing to compare against → not countered");
        _stack.GetAll().Should().Contain(spell);
    }
}
