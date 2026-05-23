using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Murderous Cut (Khans of Tarkir, {4}{B}, Instant).
/// "Delve. Destroy target creature."
///
/// Covers:
///   - Card identity + Delve marker.
///   - NamedCardFactory dispatch.
///   - Resolve destroys the targeted creature (battlefield → graveyard).
///   - Cast with Delve: graveyard cards exiled + creature destroyed.
///   - Illegal target at resolution (creature left battlefield) → no-op.
/// </summary>
public class MurderousCutTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MurderousCutTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public void MurderousCut_Identity_AndDelveKeyword()
    {
        var mc = MurderousCutFactory.Create(_alice);

        mc.Name.Should().Be("Murderous Cut");
        mc.ManaCost.Should().Be("{4}{B}");
        mc.HasType(CardType.Instant).Should().BeTrue();
        mc.Owner.Should().BeSameAs(_alice);
        mc.Controller.Should().BeSameAs(_alice);

        mc.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MurderousCut()
    {
        var card = NamedCardFactory.Create("Murderous Cut", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Murderous Cut");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{4}{B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest()
    {
        var def = MurderousCutFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void EffectFactory_DestroysTargetCreature_MovesToGraveyard()
    {
        // Bob controls a creature on the battlefield.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var def = MurderousCutFactory.BuildSpellDefinition(t => t);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
    }

    [Fact]
    public void EffectFactory_IllegalTargetAtResolution_NoOp()
    {
        // Creature already left the battlefield before Cut resolves
        // (CR 608.2b — illegal target → effect does nothing).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        var def = MurderousCutFactory.BuildSpellDefinition(t => t);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        // Still in the graveyard, no double-move.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle();
    }

    [Fact]
    public async Task MurderousCut_CastWithDelve_ExilesGraveyardCards_AndDestroysCreature()
    {
        // Alice has 4 cards in her graveyard for delve. Murderous Cut {4}{B}
        // — delve all 4 generic, pay {B} (Alice goes mana-less for the test).
        var fodder = SeedGraveyard(_alice, 4);

        // Bob controls the target creature.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        // Alice has the Cut in hand.
        var cut = MurderousCutFactory.Create(_alice);
        cut.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cut);

        var delve = new DelveCost(cut, fodder);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bears });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, cut,
            MurderousCutFactory.BuildSpellDefinition(t => t),
            agent, ctx,
            delveCost: delve);

        // Delve payment exiled all 4 graveyard cards.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().HaveCount(4);
        foreach (var c in fodder) c.Zone.Should().Be(ZoneType.Exile);

        // Cut on the stack pre-resolution.
        cut.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Target destroyed.
        bears.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<ICard> SeedGraveyard(Player p, int count)
    {
        var list = new List<ICard>(count);
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Yard{i}", "");
            c.SetOwner(p);
            c.SetZone(ZoneType.Graveyard);
            p.Zones.Graveyard.AddCard(c);
            list.Add(c);
        }
        return list;
    }
}
