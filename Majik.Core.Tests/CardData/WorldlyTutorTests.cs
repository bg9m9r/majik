using FluentAssertions;
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
/// Tests for Worldly Tutor (Mirage, {G}, Instant).
///
/// "Search your library for a creature card, reveal that card, then
///  shuffle and put that card on top of your library." (CR 701.19a /
///  CR 701.20a)
///
/// Distinguishing feature vs. Eladamri's Call: Worldly Tutor places the
/// pick on top of the library (not in hand), at half the mana cost
/// ({G} vs. {G}{W}), but trades flexibility for sequencing — the tutored
/// creature must be drawn next turn (or via a same-turn draw effect)
/// before it can be cast.
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Resolve picks the only creature card and places it at library
///    index 0.
///  - Library with no creature card → resolve is a no-op.
///  - Non-creature cards in the library are not touched (predicate
///    pre-filters them).
///  - Agent decline (returns null) → no-op even when candidates exist
///    (CR 701.19a explicitly allows declining).
///  - CR 701.20a — library is shuffled after the search; the picked
///    card lands on top.
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

    private static Creature MakeCreature(string name, Player owner, string cost = "1G", int p = 2, int t = 2)
    {
        var c = new Creature(name, cost, p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
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
        // Library contains a Forest (filtered), a Bolt (filtered Instant),
        // and a Tarmogoyf (eligible — Creature). Deterministic agent picks
        // the first eligible candidate.
        var caster = new Player("A", 20);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(caster);
        var goyf = MakeCreature("Tarmogoyf", caster, "1G", 0, 1);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(bolt);
        caster.Zones.Library.AddCard(goyf);

        AgentRegistry.Set(caster, new DeterministicBotAgent());
        // CR 701.20a — shuffle is wired; seed the per-player RNG so the
        // shuffle of the remaining (Forest, Bolt) pile is deterministic
        // for this assertion.
        GameRandomRegistry.Set(caster, new GameRandom(seed: 1));

        Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

        // Hand untouched — Worldly Tutor places on top of library, not hand.
        caster.Zones.Hand.GetCards().Should().BeEmpty();

        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(3);
        libCards[0].Name.Should().Be("Tarmogoyf");
        // Non-eligible cards still present (any order — the post-search
        // shuffle randomizes them).
        libCards.Skip(1).Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Forest", "Lightning Bolt" });
    }

    [Fact]
    public void Resolve_PublishesLibraryShuffledEvent()
    {
        // CR 701.20a — shuffle after the search resolves; the helper
        // publishes LibraryShuffledEvent so replay / UI can observe.
        var caster = new Player("A", 20);
        var bear = MakeCreature("Grizzly Bears", caster);
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

    [Fact]
    public void Resolve_NoCreatureInLibrary_IsNoOp()
    {
        var caster = new Player("A", 20);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(caster); forest.SetController(caster);
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(caster);
        caster.Zones.Library.AddCard(forest);
        caster.Zones.Library.AddCard(bolt);

        AgentRegistry.Set(caster, new DeterministicBotAgent());

        Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

        // No creature in library — predicate produces an empty candidate
        // list, so the entire effect (including the post-search shuffle)
        // is a no-op. Library order untouched.
        caster.Zones.Hand.GetCards().Should().BeEmpty();
        var libCards = caster.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(2);
        libCards[0].Name.Should().Be("Forest");
        libCards[1].Name.Should().Be("Lightning Bolt");
    }

    [Fact]
    public void Resolve_AgentDeclines_IsNoOp()
    {
        // CR 701.19a — declining to find a card is legal even when
        // candidates exist.
        var caster = new Player("A", 20);
        var goyf = MakeCreature("Tarmogoyf", caster);
        caster.Zones.Library.AddCard(goyf);

        AgentRegistry.Set(caster, new DeclineLibraryPickAgent());

        Resolve(WorldlyTutorFactory.BuildSpellDefinition(caster));

        caster.Zones.Hand.GetCards().Should().BeEmpty();
        caster.Zones.Library.GetCards().Should().HaveCount(1)
            .And.OnlyContain(c => c.Name == "Tarmogoyf");
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

        // ---- unused decision hooks (mirror MysticalTutorTests' DeclineLibraryPickAgent) ----
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
        public Task<IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default)
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
