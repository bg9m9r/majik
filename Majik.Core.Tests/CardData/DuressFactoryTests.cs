using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Enchantment = Majik.Core.Cards.Enchantment;
using Instant = Majik.Core.Cards.Instant;
using Land = Majik.Core.Cards.Land;
using Sorcery = Majik.Core.Cards.Sorcery;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="DuressFactory"/> — Sorcery {B} (Urza's Saga).
///
/// "Target opponent reveals their hand. You choose a noncreature, nonland
/// card from it. That player discards that card."
///
/// Covers:
///   - Identity (Sorcery, {B}, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape: one 1..1 "target opponent" request,
///     Discard intent, gatherer excludes the caster.
///   - Resolve: reveals the opponent's hand via CardRevealedEvent
///     (CR 701.16).
///   - Resolve: discards a noncreature nonland (sorcery / instant).
///   - Resolve: skips creatures (the printed type filter).
///   - Resolve: skips lands.
///   - Resolve: no eligible card → no-op (CR 701.16).
/// </summary>
public class DuressFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = DuressFactory.Create(_alice);

        card.Name.Should().Be("Duress");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Duress()
    {
        var card = NamedCardFactory.Create("Duress", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Duress");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresOneTargetOpponentWithDiscardIntent()
    {
        var def = DuressFactory.BuildSpellDefinition(_alice, t => t, eventBus: null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("opponent");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Discard);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    private void Execute(EventBus? bus = null)
    {
        var def = DuressFactory.BuildSpellDefinition(_alice, t => t, bus);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    [Fact]
    public void Resolve_RevealsEveryCardInOpponentsHandBeforeDiscard()
    {
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

        reveals.Should().HaveCount(3);
        reveals.Should().OnlyContain(r => r.Player == _bob);
        reveals.Should().OnlyContain(r => r.Reason == "Duress");
    }

    [Fact]
    public void Resolve_DiscardsNoncreatureNonland()
    {
        // Hand: Forest + Lightning Bolt + Grizzly Bears.
        // Only Bolt satisfies "noncreature, nonland".
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(bolt);
        _bob.Zones.Hand.AddCard(bear);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(bolt);
        _bob.Zones.Hand.GetCards().Should().BeEquivalentTo(new ICard[] { forest, bear });
    }

    [Fact]
    public void Resolve_SkipsCreatures()
    {
        // Hand contains only creatures + lands — nothing is a legal pick
        // even though the generic RevealHandThenDiscard stub would have
        // grabbed Grizzly Bears.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        var elf = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = _bob, Zone = ZoneType.Hand };
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bear);
        _bob.Zones.Hand.AddCard(elf);
        _bob.Zones.Hand.AddCard(forest);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty(
            "Duress only picks noncreature, nonland — pure creature+land hand has no legal pick");
        _bob.Zones.Hand.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Resolve_DiscardsEnchantmentWhenAvailable()
    {
        // Sanity — the noncreature filter only excludes the creature
        // type, not other nonland card types.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        var prison = new Enchantment("Ghostly Prison", "{2}{W}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bear);
        _bob.Zones.Hand.AddCard(prison);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().Be(prison);
    }

    [Fact]
    public void Resolve_AllLandsOrCreatures_NoOp()
    {
        var forest = new Land("Forest") { Owner = _bob, Zone = ZoneType.Hand };
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(forest);
        _bob.Zones.Hand.AddCard(bear);

        Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
    }
}
