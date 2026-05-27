using FluentAssertions;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ThoughtseizeFactory"/>.
///
/// Card: Thoughtseize — Sorcery {B} (Lorwyn).
///   "Target player reveals their hand. You choose a nonland card from
///    it. That player discards that card. You lose 2 life."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Agent-driven nonland pick (highest-MV branch — bot prefers
///     Tarmogoyf over Mountain).
///   - Deterministic fallback when no agent is supplied (first nonland
///     card in the revealed hand).
///   - Lands are excluded from the candidate set.
///   - Lands-only / empty hand → no discard, but caster STILL loses 2
///     life (CR 119.3).
///   - Caster always loses 2 life on resolution.
///   - Hand reveal publishes one <see cref="CardRevealedEvent"/> per
///     card (parity with Cabal Therapy's RevealHelper.RevealHand).
/// </summary>
public class ThoughtseizeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Thoughtseize_Identity()
    {
        var ts = ThoughtseizeFactory.Create(_alice);

        ts.Name.Should().Be("Thoughtseize");
        ts.ManaCost.Should().Be("{B}");
        ts.HasType(CardType.Sorcery).Should().BeTrue();
        ts.Owner.Should().BeSameAs(_alice);
        ts.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Thoughtseize()
    {
        var card = NamedCardFactory.Create("Thoughtseize", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Thoughtseize");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — agent pick path
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AgentPicksTarmogoyf_DiscardsThatCard_CasterLoses2Life()
    {
        // Bob's hand: Tarmogoyf + Lightning Bolt + Mountain. Agent picks
        // Tarmogoyf (the heaviest nonland in a real game).
        var goyf = SeedHandCard(_bob, "Tarmogoyf");
        var bolt = SeedHandCard(_bob, "Lightning Bolt");
        var mountain = SeedLandHandCard(_bob, "Mountain");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(candidates => candidates.First(c => c.Name == "Tarmogoyf"));

        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: agent, eventBus: null);

        var aliceStartingLife = _alice.LifeTotal;
        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        // Goyf moved to graveyard; bolt + mountain still in hand.
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(goyf);
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { bolt, mountain });
        // Caster pays 2 life (CR 119.3).
        _alice.LifeTotal.Should().Be(aliceStartingLife - 2);
    }

    [Fact]
    public void Resolve_NoAgent_FirstNonlandFallback()
    {
        // Bob's hand: Mountain + Lightning Bolt + Tarmogoyf. With no
        // agent supplied, the deterministic fallback = first nonland in
        // hand-order; Mountain is filtered out, so the first nonland is
        // Bolt.
        var mountain = SeedLandHandCard(_bob, "Mountain");
        var bolt = SeedHandCard(_bob, "Lightning Bolt");
        var goyf = SeedHandCard(_bob, "Tarmogoyf");

        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);

        var aliceStartingLife = _alice.LifeTotal;
        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle().Which.Should().BeSameAs(bolt);
        // Land + Goyf untouched.
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { mountain, goyf });
        _alice.LifeTotal.Should().Be(aliceStartingLife - 2);
    }

    [Fact]
    public void Resolve_AgentReturnsNullPick_FallsBackToFirstNonland()
    {
        // Agent declines (returns null). Engine MUST NOT crash — it
        // falls back to the deterministic first-nonland pick.
        var bolt = SeedHandCard(_bob, "Lightning Bolt");
        var counter = SeedHandCard(_bob, "Counterspell");

        var agent = new ScriptedAgent();
        agent.QueueFromHand((ICard?)null);

        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: agent, eventBus: null);

        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard);
        counter.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_LandsOnlyHand_NoDiscard_ButCasterStillLoses2Life()
    {
        // Bob's only cards are lands → no nonland to pick → discard is a
        // no-op, but the printed life-loss clause still resolves
        // (CR 119.3, parity with Inquisition of Kozilek's failed-cap
        // discard branch).
        var mt1 = SeedLandHandCard(_bob, "Mountain");
        var mt2 = SeedLandHandCard(_bob, "Mountain");

        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);

        var aliceStartingLife = _alice.LifeTotal;
        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { mt1, mt2 });
        _alice.LifeTotal.Should().Be(aliceStartingLife - 2);
    }

    [Fact]
    public void Resolve_EmptyHand_NoDiscard_ButCasterStillLoses2Life()
    {
        // Bob has an empty hand — Thoughtseize still resolves, no
        // discard, but the caster pays 2 life.
        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);

        var aliceStartingLife = _alice.LifeTotal;
        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.LifeTotal.Should().Be(aliceStartingLife - 2);
    }

    // -----------------------------------------------------------------------
    // Reveal event fan-out
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PublishesCardRevealedEventPerHandCard_ReasonIsThoughtseize()
    {
        // RevealHelper.RevealHand emits one event per card with
        // Reason = "Thoughtseize" (parity with Cabal Therapy's reveal
        // posture).
        var h1 = SeedHandCard(_bob, "Lightning Bolt");
        var h2 = SeedHandCard(_bob, "Counterspell");
        var h3 = SeedLandHandCard(_bob, "Mountain");

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(r => reveals.Add(r));

        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: bus);

        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        reveals.Should().HaveCount(3);
        reveals.Select(r => r.Card).Should().Contain(new[] { h1, h2, h3 });
        reveals.Select(r => r.Reason).Should().AllBe("Thoughtseize");
    }

    [Fact]
    public void Resolve_IllegalTarget_DoesNothing_NoLifeLoss()
    {
        // CR 608.2b — single-target spell with the only target illegal
        // does nothing. The life-loss clause is part of the same
        // resolution so it ALSO fizzles (parity with Lightning Helix).
        // Simulate by resolving a non-Player target token.
        var def = ThoughtseizeFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => "not-a-player", agent: null, eventBus: null);

        var aliceStartingLife = _alice.LifeTotal;
        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        _alice.LifeTotal.Should().Be(aliceStartingLife, "fizzled spell deals no damage AND triggers no life-loss");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedHandCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedLandHandCard(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ChosenSpellParams MakeChosen(Player targetPlayer) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetPlayer } },
            Mana: ManaPayment.Empty);
}
