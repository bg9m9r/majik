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
/// Tests for <see cref="ShrapnelBlastFactory"/> — Instant {1}{R} (Fifth Dawn / Darksteel).
///
/// "As an additional cost to cast this spell, sacrifice an artifact.
///  Shrapnel Blast deals 5 damage to any target."
///
/// Structural twin of <see cref="GoblinGrenadeFactory"/>: mandatory
/// additional sacrifice cost (CR 601.2f) + a single 1..1 "any target"
/// 5-damage clause routed through <see cref="Primitives.Fx.DealDamageAny"/>.
/// The cost swaps "sacrifice a Goblin" for the existing
/// <see cref="SacrificeAnArtifactAdditionalCost"/>.
///
/// Covers:
/// - Identity (Instant, {1}{R}, owner / controller) + NamedCardFactory dispatch.
/// - SpellDefinition shape: <see cref="SacrificeAnArtifactAdditionalCost"/>
///   additional cost + one 1..1 "any target" target request.
/// - Resolve: 5 damage to a player (CR 120.3).
/// - Resolve: 5 damage to a creature (CR 119.3, marked damage).
/// - Resolve: 5 damage to a planeswalker (CR 306.7, loyalty removal).
/// - SpellCastFlow rejects cast when caster controls no artifact
///   (CR 601.2g — additional cost can't be paid).
/// </summary>
public class ShrapnelBlastTests
{
    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = ShrapnelBlastFactory.Create(owner);

        card.Name.Should().Be("Shrapnel Blast");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ShrapnelBlast()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Shrapnel Blast", owner);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Shrapnel Blast");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacArtifactCost_AndAnyTarget()
    {
        var def = ShrapnelBlastFactory.BuildSpellDefinition(resolver: t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeAnArtifactAdditionalCost>(
                "Shrapnel Blast prints 'As an additional cost to cast this spell, sacrifice an artifact.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("any target");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — 5 damage routing
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsFiveDamageToPlayer()
    {
        var bob = new Player("Bob", 20);
        Resolve(bob);

        bob.LifeTotal.Should().Be(15,
            "Shrapnel Blast deals 5 damage to the targeted player (CR 120.3)");
    }

    [Fact]
    public void Resolve_DealsFiveDamageToCreature()
    {
        var bob = new Player("Bob", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(bob);
        bear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        Resolve(bear);

        bear.Damage.Should().Be(5,
            "Shrapnel Blast deals 5 damage to the targeted creature (CR 119.3) — lethal vs a 2/2");
    }

    [Fact]
    public void Resolve_DealsFiveDamageToPlaneswalker()
    {
        var bob = new Player("Bob", 20);
        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        liliana.SetOwner(bob);
        liliana.SetController(bob);
        bob.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        Resolve(liliana);

        liliana.Loyalty.Should().Be(0,
            "Shrapnel Blast removes 5 loyalty from the targeted planeswalker (CR 306.7) — capped at 0");
    }

    // -----------------------------------------------------------------------
    // Sacrifice cost behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_SacrificesAnArtifactFromBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bauble = new Artifact("Mishra's Bauble", string.Empty);
        bauble.SetOwner(alice);
        bauble.SetController(alice);
        alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeAnArtifactAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Sacrificed.Should().Be(bauble);
        bauble.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(bauble);
        alice.Zones.Battlefield.GetCards().Should().NotContain(bauble);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoArtifactOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeAnArtifactAdditionalCost();
        cost.CanPay(alice).Should().BeFalse(
            "the only permanent available is not an artifact (CR 601.2f)");
    }

    // -----------------------------------------------------------------------
    // Cast-time: sacrifice unpayable → cast rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNoArtifactToSacrifice()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = ShrapnelBlastFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Alice controls no artifact.
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = ShrapnelBlastFactory.BuildSpellDefinition(resolver: t => t);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice*artifact*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object target)
    {
        var def = ShrapnelBlastFactory.BuildSpellDefinition(resolver: t => t);
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
