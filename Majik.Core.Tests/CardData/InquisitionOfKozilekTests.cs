using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Instant = Majik.Core.Cards.Instant;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Inquisition of Kozilek — Sorcery {B}. "Target player reveals their hand.
/// You choose a nonland card from it with mana value 3 or less. That player
/// discards that card." Shape is identical to Thoughtseize except (a) no life
/// loss to the caster, and (b) the chosen card's mana value must be 3 or less.
/// </summary>
public class InquisitionOfKozilekTests
{
    private const string OracleText =
        "Target player reveals their hand. You choose a nonland card from it with mana value 3 or less. That player discards that card.";

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static SpellBindContext Ctx(string oracle, Player caster) =>
        new(new CardEntity { Name = "Inquisition of Kozilek", ManaCost = "{B}", OracleText = oracle },
            caster, raw => raw, null, null);

    private void Execute(IEventBus? bus = null)
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Inquisition of Kozilek", ManaCost = "{B}", OracleText = OracleText },
            _alice, raw => raw, effects: null, stack: null, replacements: null,
            triggers: null, eventBus: bus, zones: null);
        def.Should().NotBeNull("Inquisition of Kozilek should bind");
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
    }

    // ──────────────────────────────────────────────────────────────────
    // Identity + dispatch
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void InquisitionOfKozilekTemplate_MatchesOracleText()
    {
        new InquisitionOfKozilekPatternTemplate().TryBind(Ctx(OracleText, _alice))
            .Should().NotBeNull("the bespoke template should bind Inquisition's printed text");
    }

    [Fact]
    public void InquisitionOfKozilekTemplate_HasDiscardIntent()
    {
        new InquisitionOfKozilekPatternTemplate().Intent.Should().Be(BotIntent.Discard);
    }

    [Fact]
    public void InquisitionOfKozilekTemplate_DoesNotMatchThoughtseizeOracle()
    {
        // Thoughtseize lacks the mv cap and has the life-loss rider — must
        // route through ThoughtseizePatternTemplate, not Inquisition's.
        new InquisitionOfKozilekPatternTemplate().TryBind(Ctx(
            "Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life.",
            _alice))
            .Should().BeNull();
    }

    [Fact]
    public void OracleSpellBinder_RoutesInquisitionThroughDedicatedTemplate()
    {
        // Inquisition's printed text is matched by RevealHandThenDiscardTemplate
        // too, but the dedicated template has higher priority. The
        // Inquisition template's reveal reason is "InquisitionOfKozilekPattern";
        // the Duress template's is "RevealHandThenDiscard". Use that as a
        // wire-level tracer for which template actually bound.
        new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand }.Also(c => _bob.Zones.Hand.AddCard(c));
        new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand }.Also(c => _bob.Zones.Hand.AddCard(c));

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(reveals.Add);

        Execute(bus);

        reveals.Should().NotBeEmpty();
        reveals.Should().OnlyContain(r => r.Reason == "InquisitionOfKozilekPattern",
            "the dedicated template should claim the bind ahead of RevealHandThenDiscard");
    }

    // ──────────────────────────────────────────────────────────────────
    // Resolution semantics — the mv-3 cap is the defining differentiator.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Inquisition_OnlyDiscardsCardsWithManaValueThreeOrLess()
    {
        // Hand: Lightning Bolt (mv 1) + Cryptic Command (mv 4).
        // Only Bolt is eligible; Cryptic Command exceeds the cap.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var cryptic = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(cryptic);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt,
            "Cryptic Command exceeds the mv-3 cap and must be skipped");
        _bob.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().Be(cryptic);
    }

    [Fact]
    public void Inquisition_PicksFirstEligibleWhenAllSatisfyCap()
    {
        // All three cards have mv ≤ 3; v1 deterministic pick is the first.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        var elf = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(bear);
        _bob.Zones.Hand.AddCard(elf);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt);
    }

    [Fact]
    public void Inquisition_DoesNotDiscardWhenNoLegalTarget()
    {
        // Hand: only lands + a mv-4 spell — nothing eligible to discard.
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var island = new Land("Island") { Owner = _bob, Zone = ZoneType.Hand };
        var cryptic = new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(island);
        _bob.Zones.Hand.AddCard(cryptic);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty("nothing in hand satisfies nonland + mv ≤ 3");
        _bob.Zones.Hand.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Inquisition_DoesNotCauseLifeLoss()
    {
        // Thoughtseize differentiator — Inquisition costs no life on resolution.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bolt);

        var startLife = _alice.LifeTotal;

        Execute();

        _alice.LifeTotal.Should().Be(startLife, "Inquisition costs the caster no life (unlike Thoughtseize)");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt);
    }

    [Fact]
    public void Inquisition_RevealsEntireHandBeforeDiscard()
    {
        // CR 701.16 — reveal contract: one CardRevealedEvent per card in
        // the target's hand fires before any discard happens.
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(bear);

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(reveals.Add);

        Execute(bus);

        reveals.Should().HaveCount(3, "every card in the target's hand becomes public");
        reveals.Select(r => r.Card.InstanceId).Should().BeEquivalentTo(
            new[] { forest.InstanceId, bolt.InstanceId, bear.InstanceId });
        reveals.Should().OnlyContain(r => r.Player == _bob);
        reveals.Should().OnlyContain(r => r.From == ZoneType.Hand);
    }
}

internal static class InquisitionTestExtensions
{
    /// <summary>
    /// Inline "do something with this" combinator — keeps the fixture seeding
    /// terse where a card needs to be added to a zone immediately after
    /// construction.
    /// </summary>
    public static T Also<T>(this T self, Action<T> action)
    {
        action(self);
        return self;
    }
}
