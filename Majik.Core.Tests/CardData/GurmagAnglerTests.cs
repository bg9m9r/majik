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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Gurmag Angler (Khans of Tarkir, {7}{B}).
///
/// Covers:
///   - Card shape (name, type, subtypes Zombie + Fish, P/T, mana cost).
///   - NamedCardFactory dispatch.
///   - Delve marker keyword + absence of any triggered/activated abilities.
///   - Casting with DelveCost reduces the effective mana cost and stamps the
///     delve count on the card (no ETB consumer for the count — Gurmag Angler
///     has no printed triggers — but the stamp lifecycle is exercised to
///     match the engine's contract).
/// </summary>
public class GurmagAnglerTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GurmagAnglerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void GurmagAngler_IsCreature_ZombieFish_5_5_AtCost7B()
    {
        var ang = GurmagAnglerFactory.Create(_alice);

        ang.Name.Should().Be("Gurmag Angler");
        ang.ManaCost.Should().Be("{7}{B}");
        ang.HasType(CardType.Creature).Should().BeTrue();
        ang.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        ang.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        ang.BasePower.Should().Be(5);
        ang.BaseToughness.Should().Be(5);
        ang.Owner.Should().Be(_alice);
        ang.Controller.Should().Be(_alice);
    }

    [Fact]
    public void GurmagAngler_HasDelveKeyword_AndNoTriggeredOrActivatedAbilities()
    {
        var ang = GurmagAnglerFactory.Create(_alice);

        var keywords = ang.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Delve");

        // Vanilla delve creature — no printed triggers or activated abilities.
        ang.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Gurmag Angler has no printed triggered abilities");
        ang.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Gurmag Angler has no printed activated abilities");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GurmagAngler()
    {
        var card = NamedCardFactory.Create("Gurmag Angler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Gurmag Angler");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(5);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public async Task GurmagAngler_CastWithDelve_ExilesGraveyardCards_AndResolvesToBattlefield()
    {
        // Seed Alice's graveyard with 7 cards — enough to delve away the
        // entire generic portion of {7}{B}, leaving only {B} to pay.
        var fodder = SeedGraveyard(_alice, 7);
        var ang = AnglerInHand(_alice);

        var delve = new DelveCost(ang, fodder);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, ang,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx,
            delveCost: delve);

        // All 7 delve-paid cards are now in exile.
        _alice.Zones.Exile.GetCards().Should().HaveCount(7);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();

        // The cast flow stamps the delve count on the card per the
        // PendingDelveExiledCount contract — even though Gurmag Angler has
        // no consumer, the stamp lifecycle still runs.
        ang.PendingDelveExiledCount.Should().Be(7);

        _resolver.ResolveTop(_stack);
        ang.Zone.Should().Be(ZoneType.Battlefield);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature AnglerInHand(Player owner)
    {
        var a = GurmagAnglerFactory.Create(owner);
        a.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(a);
        return a;
    }

    private IReadOnlyList<ICard> SeedGraveyard(Player p, int count)
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
