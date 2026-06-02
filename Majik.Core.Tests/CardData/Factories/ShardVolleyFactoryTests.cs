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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ShardVolleyFactory"/> (Time Spiral, {R}).
///
/// Shard Volley — Instant.
/// Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice a land.
///    Shard Volley deals 3 damage to any target."
///
/// Covers:
/// - Identity ({R} Instant, name, owner/controller) loaded from the embedded
///   JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: <see cref="SacrificeALandAdditionalCost"/>
///   additional cost + one 1..1 "any target" request, no X.
/// - Resolve: 3 damage to a player (CR 120.3).
/// - Resolve: 3 damage to a creature (CR 119.3, marked damage).
/// - Resolve: 3 damage to a planeswalker (CR 306.7, loyalty removal).
/// - Sacrifice cost: picks a land, moves it to graveyard (basic or nonbasic).
/// - Sacrifice cost: rejects when the caster controls no land.
/// - <see cref="SpellCastFlow"/> rejects the cast when the caster controls no
///   land (CR 601.2g — additional cost can't be paid).
/// </summary>
[Trait("Color", "R")]
public class ShardVolleyFactoryTests
{
    // ── Identity + dispatch ──────────────────────────────────────────────────

    [Fact]
    public void Identity_InstantAtR()
    {
        var owner = new Player("Alice", 20);
        var card = ShardVolleyFactory.Create(owner);

        card.Name.Should().Be("Shard Volley");
        card.ManaCost.ToString().Should().Be("{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(owner);
        card.Controller.Should().BeSameAs(owner);
    }
    // ── Spell definition shape ───────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_DeclaresSacLandCost_AndAnyTarget()
    {
        var def = ShardVolleyFactory.BuildSpellDefinition(resolver: t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeALandAdditionalCost>(
                "Shard Volley prints 'As an additional cost to cast this spell, sacrifice a land.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("any target");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    // ── Resolve — 3 damage routing ───────────────────────────────────────────

    [Fact]
    public void Resolve_DealsThreeDamageToPlayer()
    {
        var bob = new Player("Bob", 20);
        Resolve(bob);

        bob.LifeTotal.Should().Be(17,
            "Shard Volley deals 3 damage to the targeted player (CR 120.3)");
    }

    [Fact]
    public void Resolve_DealsThreeDamageToCreature()
    {
        var bob = new Player("Bob", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(bob);
        bear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        Resolve(bear);

        bear.Damage.Should().Be(3,
            "Shard Volley deals 3 damage to the targeted creature (CR 119.3) — lethal vs a 2/2");
    }

    [Fact]
    public void Resolve_DealsThreeDamageToPlaneswalker()
    {
        var bob = new Player("Bob", 20);
        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        liliana.SetOwner(bob);
        liliana.SetController(bob);
        bob.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        Resolve(liliana);

        liliana.Loyalty.Should().Be(0,
            "Shard Volley removes 3 loyalty from the targeted planeswalker (CR 306.7)");
    }

    // ── Sacrifice cost behaviour ─────────────────────────────────────────────

    [Fact]
    public void Cost_SacrificesABasicLandFromBattlefield()
    {
        var alice = new Player("Alice", 20);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(alice);
        mountain.SetController(alice);
        alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeALandAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(mountain);
        mountain.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(mountain);
        alice.Zones.Battlefield.GetCards().Should().NotContain(mountain);
    }

    [Fact]
    public void Cost_SacrificesANonbasicLand()
    {
        // Any land qualifies — basic or nonbasic (CR 305).
        var alice = new Player("Alice", 20);
        var nonbasic = new Land("Steam Vents",
            subtypes: new[] { CardSubtype.Island, CardSubtype.Mountain });
        nonbasic.SetOwner(alice);
        nonbasic.SetController(alice);
        alice.Zones.Battlefield.AddCard(nonbasic);
        nonbasic.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeALandAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(nonbasic);
        nonbasic.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoLandOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        // Only a creature — not a land.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var cost = new SacrificeALandAdditionalCost();
        cost.CanPay(alice).Should().BeFalse(
            "the only permanent available is not a land (CR 601.2f)");
    }

    [Fact]
    public void Cost_Pay_FailsWhenNoLandAvailable()
    {
        var alice = new Player("Alice", 20);
        var cost = new SacrificeALandAdditionalCost();
        cost.Pay(alice).Should().BeFalse();
        cost.Sacrificed.Should().BeNull();
    }

    // ── Cast-time: sacrifice unpayable → cast rejected ───────────────────────

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNoLandToSacrifice()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = ShardVolleyFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        // Alice controls no land.
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = ShardVolleyFactory.BuildSpellDefinition(resolver: t => t);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice*land*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        bob.LifeTotal.Should().Be(20);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void Resolve(object target)
    {
        var def = ShardVolleyFactory.BuildSpellDefinition(resolver: t => t);
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
