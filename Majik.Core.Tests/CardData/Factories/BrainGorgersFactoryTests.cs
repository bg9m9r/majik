using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BrainGorgersFactory"/>.
///
/// Brain Gorgers (Future Sight, {3}{B}). Creature — Zombie 4/2. Oracle text
/// (verified against Scryfall):
///   "When you cast this spell, any player may sacrifice a creature of their
///    choice. If a player does, counter Brain Gorgers.
///    Madness {1}{B}"
///
/// Madness is intrinsic (MadnessCatalog + the Fx.DiscardCard funnel) and is NOT
/// covered here. These tests cover ONLY the unique non-madness body: the
/// cast-trigger self-counter (CR 603.2 / 603.3a "When you cast this spell").
///
/// Coverage:
/// - Identity (name, Creature, Zombie subtype, {3}{B}, black, 4/2).
/// - Structural cast trigger: a single TriggeredAbility over
///   <see cref="SpellCastEvent"/> gated to this card, functioning on the Stack.
/// - Resolve, a player chooses to sacrifice a creature → that creature dies AND
///   Brain Gorgers is countered (removed from the stack to its owner's
///   graveyard, CR 701.5a).
/// - Resolve, no player sacrifices → Brain Gorgers is NOT countered (stays on
///   the stack to resolve as a creature).
/// </summary>
[Trait("Color", "B")]
public class BrainGorgersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static GameContext Game(Player self, Majik.Core.Stack.Stack stack, params Player[] all)
        => new(self, all, activePlayer: all[0], turnNumber: 1, currentPhase: null, stack: stack);

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void BrainGorgers_Identity()
    {
        var c = BrainGorgersFactory.Create(_alice);

        c.Name.Should().Be("Brain Gorgers");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.ManaCost.Should().Be("{3}{B}");
        c.ManaCostValue.TotalValue.Should().Be(4);
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(2);
        CardColors.GetColors(c).Should().Contain(ManaColor.Black);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Cast trigger — structural ─────────────────────────────────────────

    [Fact]
    public void BrainGorgers_HasStructuralCastTrigger()
    {
        var card = BrainGorgersFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Brain Gorgers prints one triggered ability — the cast trigger.");

        var trig = triggers[0];
        trig.Source.Should().BeSameAs(card);
        trig.Controller.Should().BeSameAs(_alice);
        trig.ActiveZones.Should().Contain(ZoneType.Stack,
            "a 'When you cast this spell' trigger functions while the spell is on the stack (CR 603.3a / 603.6e).");
        trig.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void CastTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = BrainGorgersFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Creature("Other Zombie", "{B}", 1, 1, subtypes: new[] { CardSubtype.Zombie });
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ── Resolve — a player sacrifices → counter ───────────────────────────

    [Fact]
    public async Task Resolve_PlayerSacrifices_CountersBrainGorgers()
    {
        var stack = new Majik.Core.Stack.Stack();
        using var scope = AgentRegistry.PushScope();

        var aliceCreature = MakeCreature(_alice);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(false);                                  // Alice declines the "may"

        var bobCreature = MakeCreature(_bob);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(true);                                     // Bob accepts the "may"
        bobAgent.QueueFromBattlefield(c => c.Count > 0 ? c[0] : null); // … and picks his creature

        AgentRegistry.Set(_alice, aliceAgent);
        AgentRegistry.Set(_bob, bobAgent);

        var card = BrainGorgersFactory.Create(_alice);
        var spell = new Majik.Core.Spells.Spell(card, _alice);
        stack.Push(spell);
        card.SetZone(ZoneType.Stack);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(spell), trigger).Should().BeTrue();

        var rc = ResolutionContext.For(
            _alice, AgentRegistry.Get(_alice), Game(_alice, stack, _alice, _bob), chosenTargets: null);
        foreach (var e in trigger.Effects) await e.ExecuteAsync(rc);

        bobCreature.Zone.Should().Be(ZoneType.Graveyard, "Bob sacrificed his creature.");
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield, "Alice declined the 'may' — her creature is untouched.");
        stack.GetAll().Should().NotContain(spell, "Brain Gorgers is countered when a player sacrifices.");
        card.Zone.Should().Be(ZoneType.Graveyard,
            "a countered spell goes to its owner's graveyard (CR 701.5a).");
    }

    // ── Resolve — no player sacrifices → not countered ────────────────────

    [Fact]
    public async Task Resolve_NoPlayerSacrifices_DoesNotCounter()
    {
        var stack = new Majik.Core.Stack.Stack();
        using var scope = AgentRegistry.PushScope();

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(false);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(false);

        var aliceCreature = MakeCreature(_alice);
        var bobCreature = MakeCreature(_bob);

        AgentRegistry.Set(_alice, aliceAgent);
        AgentRegistry.Set(_bob, bobAgent);

        var card = BrainGorgersFactory.Create(_alice);
        var spell = new Majik.Core.Spells.Spell(card, _alice);
        stack.Push(spell);
        card.SetZone(ZoneType.Stack);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var rc = ResolutionContext.For(
            _alice, AgentRegistry.Get(_alice), Game(_alice, stack, _alice, _bob), chosenTargets: null);
        foreach (var e in trigger.Effects) await e.ExecuteAsync(rc);

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield, "no player chose to sacrifice.");
        bobCreature.Zone.Should().Be(ZoneType.Battlefield, "no player chose to sacrifice.");
        stack.GetAll().Should().Contain(spell, "Brain Gorgers is not countered when nobody sacrifices.");
    }
}
