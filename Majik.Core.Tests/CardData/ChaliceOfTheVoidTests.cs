using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ChaliceOfTheVoidFactory"/> (Mirrodin, {X}{X}).
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB with X=2 → 2 charge counters (via SpellCastFlow.PendingCastX).
/// - Opponent casts mv-2 spell → countered (spell → graveyard).
/// - Opponent casts mv-3 spell → not countered.
/// - Controller casts mv-2 spell → ALSO countered (symmetric).
/// - X=0 → counters mv-0 spells (Mox Opal etc.).
/// </summary>
public class ChaliceOfTheVoidTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;
    private readonly StackResolver _resolver;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ChaliceOfTheVoidTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ChaliceOfTheVoid_Identity()
    {
        var chalice = ChaliceOfTheVoidFactory.Create(_alice);

        chalice.Name.Should().Be("Chalice of the Void");
        chalice.ManaCost.Should().Be("{X}{X}");
        chalice.ManaCostValue.HasX.Should().BeTrue("printed cost has X (CR 202.3b)");
        chalice.HasType(CardType.Artifact).Should().BeTrue();
        chalice.Owner.Should().BeSameAs(_alice);
        chalice.Controller.Should().BeSameAs(_alice);

        // Two triggered abilities: ETB-with-X-counters + cast-MV counter.
        chalice.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ChaliceOfTheVoid_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Chalice of the Void", _alice);

        card.Should().BeOfType<Artifact>("Chalice of the Void is an Artifact");
        card.Name.Should().Be("Chalice of the Void");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // ETB with X charge counters (CR 122.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void ChaliceOfTheVoid_EtbWithXEquals2_GainsTwoChargeCounters()
    {
        var chalice = ChaliceOfTheVoidFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);

        // SpellCastFlow stamps PendingCastX = X right after ChooseXAsync.
        // Simulate that here so the ETB effect picks it up.
        chalice.SetPendingCastX(2);

        chalice.Counters.Count(CounterType.Charge).Should().Be(0);

        var etb = chalice.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        chalice.Counters.Count(CounterType.Charge).Should().Be(2,
            "Chalice enters with X (=2) charge counters per CR 122.1g");
        chalice.PendingCastX.Should().BeNull(
            "PendingCastX stamp is consumed once the ETB effect reads it — re-entries don't double-count");
    }

    [Fact]
    public async Task ChaliceOfTheVoid_CastFlow_StampsPendingCastX_DrivenByETB()
    {
        // End-to-end via SpellCastFlow: agent picks X=3, Chalice lands
        // with 3 charge counters once the ETB effect runs.
        var chalice = ChaliceOfTheVoidFactory.Create(_alice);
        chalice.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(chalice);

        var agent = new ScriptedAgent();
        agent.QueueX(3);
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        var definition = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

        await _flow.CastAsync(_alice, chalice, definition, agent, ctx);

        chalice.PendingCastX.Should().Be(3,
            "SpellCastFlow stamps the chosen X on the card for ETB consumption");

        // Resolve the spell (lands on battlefield via StackResolver).
        _resolver.ResolveTop(_stack);
        chalice.Zone.Should().Be(ZoneType.Battlefield);

        // Run the ETB trigger effect (would land via TriggerManager in a
        // full game loop; here we drive it inline for determinism).
        var etb = chalice.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        chalice.Counters.Count(CounterType.Charge).Should().Be(3,
            "X=3 → 3 charge counters");
    }

    // -----------------------------------------------------------------------
    // Counter-spell trigger — CR 701.5
    // -----------------------------------------------------------------------

    [Fact]
    public void ChaliceOfTheVoid_OpponentCastsManaValue2Spell_IsCountered()
    {
        // Chalice (controlled by Alice) has 2 charge counters; Bob casts
        // a mv-2 instant. The trigger fires; resolving the trigger sends
        // the instant to Bob's graveyard.
        var chalice = ChaliceOfTheVoidFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        chalice.Counters.Add(CounterType.Charge, 2);

        // Bob casts a Lightning Helix (mv = 2 — {R}{W}).
        var helix = new Instant("Lightning Helix", "{R}{W}");
        helix.SetOwner(_bob);
        helix.SetController(_bob);
        helix.SetZone(ZoneType.Stack);
        var helixSpell = new Majik.Core.Spells.Spell(helix, _bob);
        _stack.Push(helixSpell);
        _bus.Publish(new SpellCastEvent(helixSpell));

        _triggers.PendingCount.Should().Be(1,
            "Chalice trigger fires on Bob's mv-2 cast (symmetric)");

        _triggers.PutPendingTriggersOnStack(_alice);
        // Top of stack is now the chalice trigger; underneath is the helix.
        _resolver.ResolveTop(_stack);

        helix.Zone.Should().Be(ZoneType.Graveyard,
            "countered spell goes to its owner's graveyard per CR 701.5");
        _bob.Zones.Graveyard.GetCards().Should().Contain(helix);
        _stack.GetAll().Should().NotContain(helixSpell,
            "countered spell is removed from the stack");
    }

    [Fact]
    public void ChaliceOfTheVoid_OpponentCastsManaValue3Spell_IsNotCountered()
    {
        var chalice = ChaliceOfTheVoidFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        chalice.Counters.Add(CounterType.Charge, 2);

        // Bob casts a mv-3 spell ({2}{R}) — does NOT match.
        var bolt = new Instant("Skred", "{2}{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(0,
            "mv-3 ≠ 2 charge counters → no Chalice trigger");

        // The spell sits on the stack — uncountered.
        _stack.GetAll().Should().Contain(spell);
        bolt.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public void ChaliceOfTheVoid_ControllerCastsManaValue2Spell_IsAlsoCountered()
    {
        // Symmetric: Chalice doesn't care who casts the spell. Alice
        // (Chalice's controller) casts a mv-2 spell — same result.
        var chalice = ChaliceOfTheVoidFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        chalice.Counters.Add(CounterType.Charge, 2);

        // Alice casts a mv-2 instant.
        var bolt = new Instant("Counterspell", "{U}{U}");
        bolt.SetOwner(_alice);
        bolt.SetController(_alice);
        bolt.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bolt, _alice);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(1,
            "Chalice's counter trigger is symmetric (CR 603.2): " +
            "fires on its controller's own mv-2 cast too");

        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "Chalice's controller's spell is countered all the same");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void ChaliceOfTheVoid_WithXEqualsZero_CountersManaValueZeroSpells()
    {
        // Chalice for X=0 has 0 charge counters → matches mv-0 spells
        // (Mox Opal, Mishra's Bauble, Memnite, etc.).
        var chalice = ChaliceOfTheVoidFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        // No counters added — 0 charge counters.

        // Bob casts Mox Opal (mv 0).
        var mox = new Artifact("Mox Opal", "{0}");
        mox.SetOwner(_bob);
        mox.SetController(_bob);
        mox.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(mox, _bob);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(1,
            "X=0 Chalice still triggers on every mv-0 cast — the iconic Chalice@0 line");

        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        mox.Zone.Should().Be(ZoneType.Graveyard,
            "mv-0 spells get countered just like any other matching mv");
        _bob.Zones.Graveyard.GetCards().Should().Contain(mox);
    }

    // -----------------------------------------------------------------------
    // Mana value comparison reads PendingCastX (CR 202.3b)
    // -----------------------------------------------------------------------

    [Fact]
    public void ChaliceOfTheVoid_XSpellCastForMatchingX_IsCountered()
    {
        // Chalice with 3 charge counters; opponent casts an X-cost spell
        // with X=3 (printed mv 0 + X = 3). Should be countered.
        var chalice = ChaliceOfTheVoidFactory.Create(
            _alice, stack: _stack, eventBus: _bus, triggers: _triggers);
        _alice.Zones.Battlefield.AddCard(chalice);
        chalice.SetZone(ZoneType.Battlefield);
        chalice.Counters.Add(CounterType.Charge, 3);

        // Bob casts a hypothetical X-cost spell — printed {X}, cast for X=3.
        var xspell = new Sorcery("Banefire", "{X}{R}");
        xspell.SetOwner(_bob);
        xspell.SetController(_bob);
        xspell.SetZone(ZoneType.Stack);
        // SpellCastFlow would stamp PendingCastX = 2 (X=2 → printed 1 + 2 = 3).
        xspell.SetPendingCastX(2);
        var spell = new Majik.Core.Spells.Spell(xspell, _bob);
        _stack.Push(spell);
        _bus.Publish(new SpellCastEvent(spell));

        _triggers.PendingCount.Should().Be(1,
            "MV = printed (1 for {R}) + chosen X (2) = 3 — matches Chalice's 3 counters");

        _triggers.PutPendingTriggersOnStack(_alice);
        _resolver.ResolveTop(_stack);

        xspell.Zone.Should().Be(ZoneType.Graveyard);
    }
}
