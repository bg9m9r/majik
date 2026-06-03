using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FeedTheSwarmFactory"/> (Zendikar Rising, {B}).
/// "Destroy target creature or enchantment an opponent controls. You lose
///  life equal to that permanent's mana value."
/// </summary>
[Trait("Color", "B")]
public class FeedTheSwarmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams Chosen(object target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_SorceryAtB_BlackColoured()
    {
        var card = FeedTheSwarmFactory.Create(_alice);

        card.Name.Should().Be("Feed the Swarm");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_SingleCreatureOrEnchantmentTargetRequest()
    {
        var def = FeedTheSwarmFactory.BuildDefinition(_alice, o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature or enchantment");
        def.TargetRequests[0].Description.Should().Contain("opponent");
    }

    [Fact]
    public void Gatherer_OffersOnlyOpponentControlledCreaturesAndEnchantments()
    {
        // Alice (caster) controls a creature — must NOT be offered.
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        aliceBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBear);

        // Bob (opponent) controls a creature and an enchantment — both offered.
        var bobOgre = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        bobOgre.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobOgre);

        var bobAura = new Enchantment("Pacifism", "{1}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        bobAura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobAura);

        var def = FeedTheSwarmFactory.BuildDefinition(_alice, o => o);
        var ctx = new GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, new Majik.Core.Stack.Stack());
        var candidates = def.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(bobOgre);
        candidates.Should().Contain(bobAura);
        candidates.Should().NotContain(aliceBear,
            "Feed the Swarm only targets permanents an opponent controls");
    }

    // ── Happy path: opponent creature, mv 4 ──────────────────────────────────

    [Fact]
    public void Resolve_OpponentCreature_MovesToGraveyard_CasterLosesManaValueLife()
    {
        // Opponent creature with mana value 4.
        var ogre = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        ogre.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ogre);

        var def = FeedTheSwarmFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(ogre))) e.Execute();

        ogre.Zone.Should().Be(ZoneType.Graveyard,
            "Feed the Swarm destroys the target — it goes to the graveyard");
        _alice.LifeTotal.Should().Be(16,
            "Alice loses life equal to the permanent's mana value (4)");
    }

    // ── Happy path: opponent enchantment, mv 2 ───────────────────────────────

    [Fact]
    public void Resolve_OpponentEnchantment_MovesToGraveyard_CasterLosesManaValueLife()
    {
        var aura = new Enchantment("Pacifism", "{1}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        aura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(aura);

        var def = FeedTheSwarmFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(aura))) e.Execute();

        aura.Zone.Should().Be(ZoneType.Graveyard,
            "Feed the Swarm destroys the target enchantment");
        _alice.LifeTotal.Should().Be(18,
            "Alice loses life equal to the enchantment's mana value (2)");
    }

    // ── No-op: own creature (not an opponent's) ──────────────────────────────

    [Fact]
    public void Resolve_OwnCreature_NoEffect_NoCasterLifeLoss()
    {
        // Caster's OWN creature — illegal target ("an opponent controls").
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var def = FeedTheSwarmFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(bear))) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "the caster's own creature is not 'an opponent controls' — no destroy");
        _alice.LifeTotal.Should().Be(20,
            "CR 608.2b — illegal target → whole spell does nothing, no life loss");
    }

    // ── No-op: target left battlefield (CR 608.2b) ───────────────────────────

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoEffect_NoCasterLifeLoss()
    {
        var ogre = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            Owner = _bob,
            Controller = _bob,
        };
        ogre.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(ogre);

        var def = FeedTheSwarmFactory.BuildDefinition(_alice, o => o);
        foreach (var e in def.EffectFactory(Chosen(ogre))) e.Execute();

        _alice.LifeTotal.Should().Be(20,
            "CR 608.2b — target not on the battlefield → spell does nothing, no life loss");
    }
}
