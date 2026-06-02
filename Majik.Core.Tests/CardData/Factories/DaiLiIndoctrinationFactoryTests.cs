using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DaiLiIndoctrinationFactory"/> (Avatar: The Last
/// Airbender, {1}{B}). Sorcery — Lesson:
///   "Choose one —
///     • Target opponent reveals their hand. You choose a nonland permanent
///       card from it. That player discards that card.
///     • Earthbend 2."
/// Mode 0 cribs the reveal → caster-picks → discard pattern of
/// <see cref="DespiseFactory"/> (nonland-permanent filter); mode 1 routes
/// through <see cref="Majik.Core.Keywords.EarthbendAction"/> (CR 701.59).
/// </summary>
[Trait("Color", "B")]
public class DaiLiIndoctrinationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ICard SeedCard(Player p, string name, string cost = "")
    {
        var c = new Card(name, cost);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedCreature(Player p, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedArtifact(Player p, string name)
    {
        var c = new Artifact(name, "{2}");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedLand(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private Land SeedBattlefieldLand(Player p, ContinuousEffectsService? svc, string name = "Swamp")
    {
        var land = new Land(name)
        {
            Owner = p,
            Controller = p,
            Zone = ZoneType.Battlefield,
        };
        if (svc != null) land.ActiveEffects = svc;
        p.Zones.Battlefield.AddCard(land);
        return land;
    }

    /// <summary>Build the chosen-params for a single mode pick with a single
    /// target in the slot for that mode (other slots empty).</summary>
    private static ChosenSpellParams Chosen(int mode, object? target)
    {
        var targets = new IReadOnlyList<object>[DaiLiIndoctrinationFactory.TotalModes];
        for (var i = 0; i < targets.Length; i++) targets[i] = System.Array.Empty<object>();
        if (target != null) targets[mode] = new[] { target };
        return new ChosenSpellParams(
            ModeIndex: mode, X: null, Targets: targets, Mana: ManaPayment.Empty);
    }

    [Fact]
    public void Identity_SorceryAt1B()
    {
        var card = DaiLiIndoctrinationFactory.Create(_alice);
        card.Name.Should().Be("Dai Li Indoctrination");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{B}");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — reveal hand → caster picks a nonland permanent → discard
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_DiscardsChosenNonlandPermanent()
    {
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var bolt = SeedCard(_bob, "Lightning Bolt");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));

        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeDiscard, _bob)))
            e.Execute();

        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Mode0_ArtifactIsLegalNonlandPermanent()
    {
        var sol = SeedArtifact(_bob, "Sol Ring");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Sol Ring"));

        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeDiscard, _bob)))
            e.Execute();

        sol.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Mode0_ExcludesLandsAndInstants_FirstLegalFallback()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var swamp = SeedLand(_bob, "Swamp");
        var goyf = SeedCreature(_bob, "Tarmogoyf");

        // No agent → deterministic first-legal pick (the only nonland
        // permanent is the creature).
        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeDiscard, _bob)))
            e.Execute();

        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Mode0_NoNonlandPermanentInHand_NoDiscard()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var swamp = SeedLand(_bob, "Swamp");

        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeDiscard, _bob)))
            e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Earthbend 2 (CR 701.59)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_Earthbend2_AnimatesTargetLandTo2_2()
    {
        var svc = new ContinuousEffectsService();
        var land = SeedBattlefieldLand(_alice, svc);

        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null, continuousEffects: svc);
        foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeEarthbend, land)))
            e.Execute();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Earthbend 2 puts two +1/+1 counters on the land (CR 701.59b)");

        var chars = svc.Compute(land);
        chars.Should().BeOfType<CreatureCharacteristics>();
        chars.Types.Should().Contain(CardType.Creature, "the land becomes a creature (CR 701.59a)");
        chars.Types.Should().Contain(CardType.Land, "it's still a land (CR 701.59a)");
        chars.Keywords.Should().Contain("Haste", "Earthbend grants haste (CR 701.59a)");

        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(2, "0/0 base + two +1/+1 counters = 2/2");
        cc.Toughness.Should().Be(2);
    }

    [Fact]
    public void Mode1_NonLandTargetIsNoOp()
    {
        var svc = new ContinuousEffectsService();
        var notALand = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };

        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null, continuousEffects: svc);

        // Resolving with a non-land target is a no-op (CR 608.2b) — must not throw.
        System.Action act = () =>
        {
            foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeEarthbend, notALand)))
                e.Execute();
        };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Modal shape — exactly one mode resolves
    // -----------------------------------------------------------------------

    [Fact]
    public void Modal_PicksOnlyChosenMode_EarthbendDoesNotDiscard()
    {
        // Bob holds a creature; Alice picks the Earthbend mode → Bob's hand
        // is untouched (only the chosen mode's effect runs — CR 700.2d).
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var svc = new ContinuousEffectsService();
        var land = SeedBattlefieldLand(_alice, svc);

        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null, continuousEffects: svc);
        foreach (var e in def.EffectFactory(Chosen(DaiLiIndoctrinationFactory.ModeEarthbend, land)))
            e.Execute();

        goyf.Zone.Should().Be(ZoneType.Hand, "the discard mode was not chosen");
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void Modal_ExposesTwoModes()
    {
        var def = DaiLiIndoctrinationFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);
        def.Modes.Should().HaveCount(2);
        def.TargetRequests.Should().HaveCount(2);
    }
}
