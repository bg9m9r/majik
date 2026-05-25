using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Worldly Tutor ({G}, Instant — Mirage).
///
/// "Search your library for a creature card, reveal it, put it on top
/// of your library, then shuffle." (CR 701.19a / 701.20a)
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks a creature card and places it at index 0.
///  - Library with no creatures → resolve is a no-op.
///  - Agent decline (returns null) → no-op even when candidates exist
///    (CR 701.19a explicitly allows declining).
///  - Post-search LibraryShuffledEvent is published (CR 701.20a).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class WorldlyTutorTests
{
    private static ChosenSpellParams EmptyChoices() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(EmptyChoices()))
        {
            fx.Execute();
        }
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = WorldlyTutorFactory.Create(owner);

        card.Name.Should().Be("Worldly Tutor");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WorldlyTutor()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Worldly Tutor", owner);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Worldly Tutor");
        card.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void Resolve_PicksCreature_PlacesOnTopOfLibrary()
    {
        // Library: bear (eligible creature), instant (filtered out).
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(caster); bolt.SetController(caster);
        caster.Zones.Library.AddCard(bolt);
        caster.Zones.Library.AddCard(bear);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        // Seed the per-player RNG so the CR 701.20a shuffle of the
        // remaining library is deterministic.
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));

        Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

        // Hand untouched — Worldly Tutor goes to top of library, not hand.
        caster.Zones.Hand.GetCards().Should().BeEmpty();

        // Library still holds both cards; the creature must be at index 0.
        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(2);
        libCards[0].Name.Should().Be("Grizzly Bears");
        libCards[1].Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public void Resolve_NoCreatureInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(caster); bolt.SetController(caster);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        caster.Zones.Library.AddCard(bolt);
        caster.Zones.Library.AddCard(forest);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

        // No creature → predicate matched nothing → no-op (and no shuffle).
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(2);
        libCards[0].Name.Should().Be("Lightning Bolt");
        libCards[1].Name.Should().Be("Forest");
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist.
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        caster.Zones.Library.AddCard(bear);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Grizzly Bears");
    }

    [Fact]
    public void Resolve_PublishesLibraryShuffledEvent()
    {
        // CR 701.20a — shuffle after the search resolves.
        var caster = new Player("A", 20);
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(caster); bear.SetController(caster);
        caster.Zones.Library.AddCard(bear);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        var bus = new EventBus();
        LibraryShuffledEvent? captured = null;
        bus.Subscribe<LibraryShuffledEvent>(e => captured = e);
        EventBusRegistry.Set(caster, bus);
        try
        {
            Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(caster);
            captured.Reason.Should().Be("worldly-tutor");
        }
        finally
        {
            EventBusRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns
    /// null), exercising the CR 701.19a "no card found" branch even
    /// when candidates exist.
    /// </summary>
    private sealed class DeclineLibraryPickAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int m, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int n, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest req, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard src, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> a, IReadOnlyList<Creature> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
