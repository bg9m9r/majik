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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MagmaticSinkholeFactory"/> (Modern Horizons 3,
/// {5}{R}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Magmatic Sinkhole deals 5 damage to target creature or planeswalker."
///
/// Card identity + Delve cast flow mirror <see cref="MurderousCutFactory"/>;
/// the 5-damage-to-creature-or-planeswalker resolve clause mirrors
/// <see cref="RipApartFactory"/>'s mode-0 damage shape.
/// </summary>
[Trait("Color", "R")]
public class MagmaticSinkholeFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MagmaticSinkholeFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    private ChosenSpellParams Chosen(params object[] targets) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { targets },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void MagmaticSinkhole_Identity_AndDelveKeyword()
    {
        var card = MagmaticSinkholeFactory.Create(_alice);

        card.Name.Should().Be("Magmatic Sinkhole");
        card.ManaCost.Should().Be("{5}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MagmaticSinkhole()
    {
        var card = NamedCardFactory.Create("Magmatic Sinkhole", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Magmatic Sinkhole");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{5}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void BuildSpellDefinition_SingleCreatureOrPlaneswalkerTargetRequest()
    {
        var def = MagmaticSinkholeFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature or planeswalker");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve — 5 damage to target creature or planeswalker.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DealsFiveDamageToCreature()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var def = MagmaticSinkholeFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(bears))) e.Execute();

        bears.Damage.Should().Be(5, because: "Magmatic Sinkhole deals 5 damage to the target creature");
    }

    [Fact]
    public void Resolve_RemovesFiveLoyaltyFromPlaneswalker()
    {
        var pw = new Planeswalker("Test Walker", "{2}{R}", startingLoyalty: 7)
        { Owner = _bob, Controller = _bob };
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = MagmaticSinkholeFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(pw))) e.Execute();

        pw.Loyalty.Should().Be(2,
            because: "5 damage to a planeswalker removes 5 loyalty (CR 306.7)");
    }

    [Fact]
    public void Resolve_NoOp_OnNonCreatureNonPlaneswalkerTarget()
    {
        // CR 608.2b — a player is not a legal target; no damage.
        var def = MagmaticSinkholeFactory.BuildSpellDefinition(o => o);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            because: "Magmatic Sinkhole damages only creatures/planeswalkers, not players");
    }

    // -----------------------------------------------------------------------
    // Delve cast flow (CR 702.66).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MagmaticSinkhole_CastWithDelve_ExilesGraveyardCards_AndDealsDamage()
    {
        // Alice has 5 cards in her graveyard for delve. Magmatic Sinkhole
        // {5}{R} — delve all 5 generic, pay {R} (Alice goes mana-less here).
        var fodder = SeedGraveyard(_alice, 5);

        // Bob controls the target creature (3 toughness, so 5 damage is lethal
        // but we assert the damage marking — SBAs not run in this harness).
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bears.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bears);

        var sinkhole = MagmaticSinkholeFactory.Create(_alice);
        sinkhole.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(sinkhole);

        var delve = new DelveCost(sinkhole, fodder);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bears });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, sinkhole,
            MagmaticSinkholeFactory.BuildSpellDefinition(t => t),
            agent, ctx,
            delveCost: delve);

        // Delve payment exiled all 5 graveyard cards.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().HaveCount(5);
        foreach (var c in fodder) c.Zone.Should().Be(ZoneType.Exile);

        // Sinkhole on the stack pre-resolution.
        sinkhole.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Target took 5 damage.
        bears.Damage.Should().Be(5);
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
