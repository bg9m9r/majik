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
/// Tests for <see cref="BoneShardsFactory"/> — Sorcery {B} (Modern Horizons 2).
///
/// "As an additional cost to cast this spell, sacrifice a creature or
///  discard a card. Destroy target creature or planeswalker."
///
/// Covers:
///   - Identity (Sorcery, {B}, owner / controller) + NamedCardFactory dispatch.
///   - SpellDefinition shape:
///     <see cref="SacrificeCreatureOrDiscardCardAdditionalCost"/>
///     additional cost + one 1..1 "target creature or planeswalker" target
///     request.
///   - Resolve: destroys target creature (CR 701.7).
///   - Resolve: destroys target planeswalker (CR 701.7).
///   - Resolve: target left battlefield → no-op (CR 608.2b).
///   - Cost picks sac mode when a creature is available, discard mode
///     otherwise (CR 601.2f — disjunctive additional cost).
///   - SpellCastFlow rejects cast when caster controls no creature AND
///     has no card in hand (CR 601.2g — additional cost can't be paid).
/// </summary>
public class BoneShardsTests
{
    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("Alice", 20);
        var card = BoneShardsFactory.Create(owner);

        card.Name.Should().Be("Bone Shards");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BoneShards()
    {
        var owner = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Bone Shards", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Bone Shards");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacOrDiscardCost_AndCreatureOrPlaneswalkerTarget()
    {
        var def = BoneShardsFactory.BuildSpellDefinition(resolver: t => t);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeCreatureOrDiscardCardAdditionalCost>(
                "Bone Shards prints 'As an additional cost to cast this spell, sacrifice a creature or discard a card.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature or planeswalker");
        def.TargetRequests[0].Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys target creature / planeswalker
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
            "Bone Shards destroys the target creature (CR 701.7)");
        bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_DestroysTargetPlaneswalker()
    {
        var bob = new Player("Bob", 20);
        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        liliana.SetOwner(bob);
        liliana.SetController(bob);
        bob.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        Resolve(liliana);

        liliana.Zone.Should().Be(ZoneType.Graveyard,
            "Bone Shards destroys the target planeswalker (CR 701.7) — unlike Bone Splinters which only hits creatures");
        bob.Zones.Graveyard.GetCards().Should().Contain(liliana);
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

        Resolve(goyf);

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "Bone Shards is a no-op when the target is no longer on the battlefield (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Cost: prefers sacrifice, falls back to discard
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_PrefersSacrificeWhenCreatureAvailable()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        var spareCard = new Sorcery("Bogus Spell", "{B}");
        spareCard.SetOwner(alice);
        alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new SacrificeCreatureOrDiscardCardAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Sacrificed.Should().Be(bear, "creature is available — sac mode wins (v1 deterministic)");
        cost.Discarded.Should().BeNull();
        bear.Zone.Should().Be(ZoneType.Graveyard);
        spareCard.Zone.Should().Be(ZoneType.Hand, "the spare hand card was NOT discarded");
    }

    [Fact]
    public void Cost_FallsBackToDiscardWhenNoCreature()
    {
        var alice = new Player("Alice", 20);
        var spareCard = new Sorcery("Bogus Spell", "{B}");
        spareCard.SetOwner(alice);
        alice.Zones.Hand.AddCard(spareCard);
        spareCard.SetZone(ZoneType.Hand);

        var cost = new SacrificeCreatureOrDiscardCardAdditionalCost();
        cost.CanPay(alice).Should().BeTrue();
        cost.Pay(alice).Should().BeTrue();

        cost.Sacrificed.Should().BeNull();
        cost.Discarded.Should().Be(spareCard,
            "no creature to sacrifice — discard mode is the only payable mode");
        spareCard.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoCreatureAndEmptyHand()
    {
        var alice = new Player("Alice", 20);
        var cost = new SacrificeCreatureOrDiscardCardAdditionalCost();
        cost.CanPay(alice).Should().BeFalse(
            "neither mode can be paid (CR 117.1)");
    }

    // -----------------------------------------------------------------------
    // Cast-time: neither sac nor discard payable → cast rejected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpellCastFlow_RejectsCast_WhenNeitherSacNorDiscardPossible()
    {
        // CR 601.2g — additional cost can't be paid → cast is illegal.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var card = BoneShardsFactory.Create(alice);
        alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        // Hand is now {Bone Shards} — Bone Shards itself is the only card,
        // and Pay's discard picker would pick the first card in hand. We
        // need to remove Bone Shards from the hand AFTER the cast flow
        // moves it to the stack, so that "no other card to discard" is
        // realised. But SpellCastFlow drains the spell off the hand before
        // pre-checking costs, so an "Alice's hand is empty + no creatures"
        // setup is what we want. Clear Alice's hand first; the cast flow
        // looks up the spell card by reference.
        alice.Zones.Hand.RemoveCard(card);

        // Bob has a creature to target, but Alice controls no creature AND
        // has no card in hand — neither mode payable.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(bob);
        bear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = BoneShardsFactory.BuildSpellDefinition(resolver: t => t);

        var act = async () => await flow.CastAsync(alice, card, def, agent, ctx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sacrifice*",
                "SpellCastFlow pre-checks every additional cost (CR 601.2g)");
        stack.Count.Should().Be(0);
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Permanent target)
    {
        var def = BoneShardsFactory.BuildSpellDefinition(resolver: t => t);
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
