using FluentAssertions;
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
/// Tests for <see cref="BoneSplintersFactory"/> — Sorcery {B} (Worldwake).
///
/// "As an additional cost to cast this spell, sacrifice a creature.
///  Destroy target creature."
///
/// Covers:
///   - Identity (Sorcery, {B}, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape: <see cref="SacrificeACreatureAdditionalCost"/>
///     additional cost + one 1..1 "target creature" target request.
///   - Resolve: destroys target creature (CR 701.7).
///   - Resolve: target left battlefield → no-op (CR 608.2b).
///   - SpellCastFlow rejects cast when caster controls no creature
///     (CR 601.2g — additional cost can't be paid).
/// </summary>
public class BoneSplintersTests
{
    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = BoneSplintersFactory.Create(owner);

        card.Name.Should().Be("Bone Splinters");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoneSplinters()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Bone Splinters", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Bone Splinters");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacrificeAdditionalCost_AndOneCreatureTarget()
    {
        var def = BoneSplintersFactory.BuildSpellDefinition(resolver: t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeACreatureAdditionalCost>(
                "Bone Splinters prints 'As an additional cost to cast this spell, sacrifice a creature.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysTargetCreature()
    {
        var bob = new Player("Bob", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(bob);
        bear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Bone Splinters destroys the target creature (CR 701.7)");
        bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_DoesNothing()
    {
        // Target leaves battlefield before resolution (CR 608.2b).
        var bob = new Player("Bob", 20);
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 1, 1);
        goyf.SetOwner(bob);
        goyf.SetController(bob);
        bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        // Snapshot zone before resolve — should stay put.
        Resolve(goyf);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "Bone Splinters does nothing when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Cast-time: no creature to sacrifice → cast rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNoCreatureToSacrifice()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = BoneSplintersFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Bob has a creature to target, but Alice controls none — the
        // additional cost can't be paid even though a legal target exists.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(bob);
        bear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.Main, stack);

        var def = BoneSplintersFactory.BuildSpellDefinition(resolver: t => t);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice a creature*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        card.Zone.Should().Be(ZoneType.Hand);
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target)
    {
        var def = BoneSplintersFactory.BuildSpellDefinition(resolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }
}
