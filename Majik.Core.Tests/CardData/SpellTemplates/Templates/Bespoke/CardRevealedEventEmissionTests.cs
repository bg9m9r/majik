using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Instant = Majik.Core.Cards.Instant;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

/// <summary>
/// CR 701.16 — "Reveal" makes the card public until the revealing effect
/// stops applying. These tests lock the wire contract: every "reveal hand"
/// template emits one <see cref="CardRevealedEvent"/> per card in the
/// target's hand, before the discard / exile / pick fires.
/// </summary>
public class CardRevealedEventEmissionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private (Instant bolt, Land forest, Creature bear) SeedBobHand()
    {
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(bear);
        return (bolt, forest, bear);
    }

    private (EventBus bus, List<CardRevealedEvent> captured) NewBus()
    {
        var bus = new EventBus();
        var captured = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(captured.Add);
        return (bus, captured);
    }

    private void ExecuteSpell(string name, string mana, string oracle, IEventBus bus)
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = name, ManaCost = mana, OracleText = oracle },
            _alice, raw => raw, effects: null, stack: null, replacements: null,
            triggers: null, eventBus: bus, zones: null);
        def.Should().NotBeNull($"{name} should bind");
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
    }

    [Fact]
    public void Thoughtseize_RevealsEntireOpponentHand_BeforeDiscard()
    {
        var (bolt, forest, bear) = SeedBobHand();
        var (bus, reveals) = NewBus();

        ExecuteSpell("Thoughtseize", "{B}",
            "Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life.",
            bus);

        reveals.Should().HaveCount(3, "every card in the target's hand becomes public");
        reveals.Select(r => r.Card.InstanceId).Should().BeEquivalentTo(
            new[] { forest.InstanceId, bolt.InstanceId, bear.InstanceId });
        reveals.Should().OnlyContain(r => r.Player == _bob);
        reveals.Should().OnlyContain(r => r.From == ZoneType.Hand);
        reveals.Should().OnlyContain(r => r.Reason == "Thoughtseize");
        // And bolt was discarded — discard fires after the reveals.
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt);
    }

    [Fact]
    public void Duress_EmitsRevealEventsPerHandCard()
    {
        SeedBobHand();
        var (bus, reveals) = NewBus();

        ExecuteSpell("Duress", "{B}",
            "Target opponent reveals their hand. You choose a noncreature, nonland card from it. That player discards that card.",
            bus);

        reveals.Should().HaveCount(3);
        reveals.Should().OnlyContain(r => r.Reason == "RevealHandThenDiscard");
    }

    [Fact]
    public void Castigate_EmitsRevealEventsPerHandCard()
    {
        SeedBobHand();
        var (bus, reveals) = NewBus();

        ExecuteSpell("Castigate", "{W}{B}",
            "Target opponent reveals their hand. You choose a nonland card from it and exile that card.",
            bus);

        reveals.Should().HaveCount(3);
        reveals.Should().OnlyContain(r => r.Reason == "RevealHandThenExile");
        reveals.Should().OnlyContain(r => r.From == ZoneType.Hand);
    }

    [Fact]
    public void Nightsnare_MayChooseVariant_EmitsRevealEvents()
    {
        SeedBobHand();
        var (bus, reveals) = NewBus();

        ExecuteSpell("Nightsnare", "{2}{B}{B}",
            "Target opponent reveals their hand. You may choose a nonland card from it. If you do, that player discards that card.",
            bus);

        reveals.Should().HaveCount(3);
        reveals.Should().OnlyContain(r => r.Reason == "RevealHandMayChoose");
    }

    [Fact]
    public void PsychicIntrusion_GraveyardOrHandVariant_EmitsRevealEvents()
    {
        SeedBobHand();
        var (bus, reveals) = NewBus();

        ExecuteSpell("Psychic Intrusion", "{2}{U}{B}",
            "Target opponent reveals their hand. You choose a card from that player's graveyard or hand and exile it.",
            bus);

        reveals.Should().HaveCount(3);
        reveals.Should().OnlyContain(r => r.Reason == "RevealHandGraveOrHandExile");
    }

    [Fact]
    public void RevealHand_WithoutEventBus_DoesNotThrow()
    {
        // Pre-existing callers that pre-date the reveal event (no bus plumbed)
        // must still resolve the spell — the engine is UI-agnostic and may
        // run head-less without subscribers.
        SeedBobHand();

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Thoughtseize", ManaCost = "{B}",
              OracleText = "Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life." },
            _alice, raw => raw, stack: null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        var act = () => { foreach (var e in def!.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void RevealHand_EmptyHand_EmitsNoEvents()
    {
        // Edge: no cards in hand -> no reveal events, but the spell still resolves.
        var (bus, reveals) = NewBus();

        ExecuteSpell("Thoughtseize", "{B}",
            "Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life.",
            bus);

        reveals.Should().BeEmpty();
    }
}
