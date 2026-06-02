using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="FellFactory"/> — Sorcery {1}{B} (various sets).
///
/// Oracle text: "Destroy target creature."
///
/// Covers:
///   - Card identity: {1}{B} black Sorcery, mana value 2.
///   - NamedCardFactory dispatch.
///   - SpellDefinition: one 1..1 "target creature" Removal request.
///   - Resolve destroys target creature → moves to graveyard (CR 701.7).
///   - Illegal target at resolution (creature left battlefield) → no-op (CR 608.2b).
///   - Non-creature resolved target (wrong type) → no-op.
/// </summary>
[Trait("Color", "B")]
public class FellFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Fell_Identity_SorceryOneBBlack_ManaValueTwo()
    {
        var card = FellFactory.Create(_alice);

        card.Name.Should().Be("Fell");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Instant).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Fell costs {1}{B} — generic 1 + coloured 1 = MV 2 (CR 202.3)");
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest_Removal()
    {
        var def = FellFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);

        var req = def.TargetRequests[0];
        req.Description.Should().Contain("creature");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Intent.Should().Be(BotIntent.Removal,
            "Fell unconditionally destroys a creature — bot treats it as Removal intent");
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectFactory_DestroysTargetCreature_MovesToGraveyard()
    {
        // Bob controls a creature on the battlefield.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var def = FellFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bears.Zone.Should().Be(ZoneType.Graveyard,
            "Fell destroys the targeted creature (CR 701.7)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bears);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal/non-creature target → no-op (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void EffectFactory_IllegalTarget_CreatureNotOnBattlefield_NoOp()
    {
        // Creature already left the battlefield before Fell resolves.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        bears.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bears);

        var def = FellFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bears } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        // Still in the graveyard — no double-move.
        bears.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — illegal target at resolution → Fell does nothing");
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle();
    }

    [Fact]
    public void EffectFactory_NonCreatureTarget_NoOp()
    {
        // Target is a non-creature permanent (e.g. a land) — type guard makes it a no-op.
        var forest = new Card("Forest", "");
        forest.SetOwner(_bob);
        forest.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(forest);

        var def = FellFactory.BuildSpellDefinition(t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { forest } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        // Should not throw; the non-Creature type check guards the destroy.
        var act = () =>
        {
            foreach (var fx in def.EffectFactory(chosen)) fx.Execute();
        };
        act.Should().NotThrow();
        forest.Zone.Should().Be(ZoneType.Battlefield,
            "Fell only destroys Creature targets; a non-creature is a no-op");
    }
}
