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
/// Tests for Steelshaper's Gift (Darksteel, {W}, Sorcery).
///
/// "Search your library for an Equipment card, reveal that card, put it
///  into your hand, then shuffle." (CR 701.19a search + CR 701.20a
///  post-search shuffle.)
///
/// Distinguishing feature vs. Eladamri's Call / Worldly Tutor: a white
/// Equipment-only tutor to hand. Equipment is an artifact SUBTYPE
/// (CR 205.3g), so the predicate is a subtype filter (mirroring Stoneforge
/// Mystic) rather than a card-type filter.
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks the only Equipment card and places it in hand.
///  - Non-Equipment cards (including a non-Equipment artifact) stay in the
///    library — the subtype predicate pre-filters them.
///  - Library with no Equipment card → resolve is a no-op (CR 701.19a).
///  - Agent decline (returns null) → no-op even when candidates exist
///    (CR 701.19a explicitly allows declining).
///  - CR 701.20a — the library is shuffled after the search; a
///    LibraryShuffledEvent is published.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class SteelshapersGiftTests
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

    private static Artifact MakeEquipment(string name, Player owner, string cost = "{1}")
    {
        var c = new Artifact(name, cost, subtypes: new[] { CardSubtype.Equipment });
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var owner = new Player("A", 20);
        var card = SteelshapersGiftFactory.Create(owner);

        card.Name.Should().Be("Steelshaper's Gift");
        card.ManaCost.Should().Be("{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(owner);
        card.Controller.Should().Be(owner);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SteelshapersGift()
    {
        var owner = new Player("A", 20);
        var card = NamedCardFactory.Create("Steelshaper's Gift", owner);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Steelshaper's Gift");
        card.ManaCost.Should().Be("{W}");
    }

    [Fact]
    public void Resolve_PicksEquipment_PlacesInHand()
    {
        // Library: a Forest (filtered), a non-Equipment Artifact (filtered —
        // Equipment is a SUBTYPE), and a Bonesplitter (eligible Equipment).
        var caster = new Player("A", 20);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        var manalith = new Artifact("Manalith", "{3}");
        manalith.SetOwner(caster); manalith.SetController(caster);
        var splitter = MakeEquipment("Bonesplitter", caster);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(manalith);
        caster.Zones.Library.AddCard(splitter);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(SteelshapersGiftFactory.BuildSpellDefinition(caster));

            caster.Zones.Hand.GetCards().Select(c => c.Name)
                .Should().ContainSingle().Which.Should().Be("Bonesplitter");
            // Non-Equipment cards stay in the library (the post-search
            // shuffle may reorder them).
            caster.Zones.Library.GetCards().Select(c => c.Name)
                .Should().BeEquivalentTo(new[] { "Forest", "Manalith" });
        }
        finally
        {
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Resolve_NoEquipmentInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var manalith = new Artifact("Manalith", "{3}");
        manalith.SetOwner(caster); manalith.SetController(caster);
        caster.Zones.Library.AddCard(manalith);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(SteelshapersGiftFactory.BuildSpellDefinition(caster));

            // No Equipment → empty candidate list → nothing moves to hand.
            caster.Zones.Hand.GetCards().Should().BeEmpty();
            caster.Zones.Library.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Manalith");
        }
        finally
        {
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist.
        var caster = new Player("A", 20);
        var splitter = MakeEquipment("Bonesplitter", caster);
        caster.Zones.Library.AddCard(splitter);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        try
        {
            Resolve(SteelshapersGiftFactory.BuildSpellDefinition(caster));

            caster.Zones.Hand.GetCards().Should().BeEmpty();
            caster.Zones.Library.GetCards().Should().ContainSingle()
                .Which.Name.Should().Be("Bonesplitter");
        }
        finally
        {
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void Resolve_PublishesLibraryShuffledEvent()
    {
        // CR 701.20a — shuffle after the search resolves; the helper
        // publishes LibraryShuffledEvent so replay / UI can observe.
        var caster = new Player("A", 20);
        var splitter = MakeEquipment("Bonesplitter", caster);
        caster.Zones.Library.AddCard(splitter);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));
        var bus = new EventBus();
        LibraryShuffledEvent? captured = null;
        bus.Subscribe<LibraryShuffledEvent>(e => captured = e);
        EventBusRegistry.Set(caster, bus);
        try
        {
            Resolve(SteelshapersGiftFactory.BuildSpellDefinition(caster));

            captured.Should().NotBeNull();
            captured!.Player.Should().BeSameAs(caster);
            captured.Reason.Should().Be("steelshapers-gift");
        }
        finally
        {
            EventBusRegistry.Clear();
            AgentRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    /// <summary>
    /// Test-only agent that always declines a library pick (returns null),
    /// exercising the CR 701.19a "no card found" branch even when
    /// candidates exist.
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
