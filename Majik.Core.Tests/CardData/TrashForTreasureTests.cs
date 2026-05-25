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
/// Tests for <see cref="TrashForTreasureFactory"/> — Sorcery {2}{R} (FDN / MH2).
///
/// "As an additional cost to cast this spell, sacrifice an artifact.
///  Return target artifact card from your graveyard to the battlefield."
///
/// Covers:
///   - Identity (Sorcery, {2}{R}, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape:
///     <see cref="SacrificeAnArtifactAdditionalCost"/> additional cost.
///   - Resolve reanimates target artifact card from caster's graveyard.
///   - Resolve filters out non-artifact graveyard cards.
///   - Resolve no-ops when the caster's graveyard has no artifact card.
///   - SpellCastFlow rejects cast when caster controls no artifact
///     (CR 601.2g — additional cost can't be paid).
/// </summary>
public class TrashForTreasureTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = TrashForTreasureFactory.Create(_alice);

        card.Name.Should().Be("Trash for Treasure");
        card.Should().BeOfType<Sorcery>();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TrashForTreasure()
    {
        var card = NamedCardFactory.Create("Trash for Treasure", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Trash for Treasure");
        card.ManaCost.Should().Be("{2}{R}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSacArtifactAdditionalCost()
    {
        var def = TrashForTreasureFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeAnArtifactAdditionalCost>(
                "Trash for Treasure prints 'As an additional cost to cast this spell, sacrifice an artifact.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty(
            "v1 picks the artifact card from the caster's graveyard deterministically (same posture as ReanimateFactory)");
    }

    [Fact]
    public void Resolve_ReanimatesArtifactFromGraveyard()
    {
        var alice = new Player("Alice", 20);

        // Sol Ring — plain Artifact card, mv 1 ({1}).
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(solRing);
        solRing.SetZone(ZoneType.Graveyard);

        foreach (var fx in TrashForTreasureFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        solRing.Zone.Should().Be(ZoneType.Battlefield,
            "the targeted artifact card was reanimated to the caster's battlefield (CR 701.20)");
        alice.Zones.Graveyard.GetCards().Should().NotContain(solRing);
        alice.Zones.Battlefield.GetCards().Should().Contain(solRing);
        solRing.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the caster's control (CR 110.2)");
        alice.LifeTotal.Should().Be(20,
            "Trash for Treasure has no printed life-loss rider — distinct from Reanimate");
    }

    [Fact]
    public void Resolve_IgnoresNonArtifactCardsInGraveyard()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        foreach (var fx in TrashForTreasureFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Grizzly Bears is a non-artifact creature — predicate filters it out");
        alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_NoArtifactInGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        foreach (var fx in TrashForTreasureFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Resolve_PicksArtifactWhenMixedGraveyard()
    {
        var alice = new Player("Alice", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var sigil = new Artifact("Mishra's Bauble", "{0}");
        sigil.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(sigil);
        sigil.SetZone(ZoneType.Graveyard);

        foreach (var fx in TrashForTreasureFactory.BuildResolveEffect(alice))
        {
            fx.Execute();
        }

        sigil.Zone.Should().Be(ZoneType.Battlefield, "the artifact card was the reanimation target");
        bear.Zone.Should().Be(ZoneType.Graveyard, "the non-artifact creature stays in the graveyard");
    }

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNoArtifactToSacrifice()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = TrashForTreasureFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Alice has an artifact card in her graveyard (a valid target),
        // but controls no artifact ON THE BATTLEFIELD — the additional
        // cost can't be paid even though the resolve target is legal.
        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bauble);
        bauble.SetZone(ZoneType.Graveyard);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.Main, stack);

        var def = TrashForTreasureFactory.BuildSpellDefinition(alice);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        card.Zone.Should().Be(ZoneType.Hand);
        bauble.Zone.Should().Be(ZoneType.Graveyard);
    }
}
