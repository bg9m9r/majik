using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PiecesOfThePuzzleFactory"/>.
///
/// Pieces of the Puzzle (Shadows over Innistrad, {2}{U}, Sorcery):
///   "Reveal the top five cards of your library. Put up to two instant and/or
///    sorcery cards from among them into your hand and the rest into your
///    graveyard."
///
/// Covers:
///   - Identity: name, sorcery type, {2}{U} mana cost, blue, owner/controller.
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - SpellDefinition shape: no modes / X / targets / additional costs.
///   - Resolve (no agent): up to two eligible instants/sorceries to hand
///     (first two), every other revealed card to graveyard.
///   - Eligibility filter: lands/creatures/etc. are never taken to hand.
///   - Fewer-than-two eligible: takes what's eligible, rest to graveyard.
///   - Zero eligible: all five revealed cards go to the graveyard.
///   - Agent picks specific cards (and declines after one).
///   - Short library (fewer than five): reveals what's there.
///   - Empty library: clean no-op, no empty-draw SBA flag.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "U")]
public class PiecesOfThePuzzleFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    // ── Identity + dispatch ──────────────────────────────────────────────────

    [Fact]
    public void PiecesOfThePuzzle_HasExpectedShape()
    {
        var card = PiecesOfThePuzzleFactory.Create(_alice);

        card.Name.Should().Be("Pieces of the Puzzle");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PiecesOfThePuzzle_IsBlue()
    {
        var card = PiecesOfThePuzzleFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "the {U} pip makes it blue");
        colors.Should().NotContain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PiecesOfThePuzzle()
    {
        var card = NamedCardFactory.Create("Pieces of the Puzzle", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Pieces of the Puzzle");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_NoModes_NoX_NoTargets_NoAdditionalCosts()
    {
        var def = PiecesOfThePuzzleFactory.BuildSpellDefinition(_alice);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty("Pieces of the Puzzle has no targets");
        def.AdditionalCostsOrEmpty.Should().BeEmpty();
    }

    // ── Resolve — no agent ───────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_NoAgent_TakesFirstTwoEligible_RestToGraveyard()
    {
        // Library top→bottom: Instant, Sorcery, Instant, Land, Creature, (extra).
        // Reveal 5: [i1, s1, i2, land, creature].
        // No agent → first two eligible (i1, s1) to hand; i2, land, creature → graveyard.
        var i1       = SeedInstant("Bolt1");
        var s1       = SeedSorcery("Sorc1");
        var i2       = SeedInstant("Bolt2");
        var land     = SeedLand("Island");
        var creature = SeedCreature("Bear");
        var sixth    = SeedInstant("Untouched"); // 6th card — never revealed.

        var result = await PiecesOfThePuzzleFactory
            .ResolveAsync(_alice, ResolutionContext.Legacy);

        result.Revealed.Should().HaveCount(5);
        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { i1, s1 });
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { i2, land, creature });
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(3);

        // The 6th card was never revealed — still on top of the (now 1-card) library.
        _alice.Zones.Library.GetCards().Should().Equal(new ICard[] { sixth });

        i1.Zone.Should().Be(ZoneType.Hand);
        s1.Zone.Should().Be(ZoneType.Hand);
        i2.Zone.Should().Be(ZoneType.Graveyard);
        land.Zone.Should().Be(ZoneType.Graveyard);
        creature.Zone.Should().Be(ZoneType.Graveyard);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_NoAgent_OnlyOneEligible_TakesIt_RestToGraveyard()
    {
        // One eligible among the five → only that card to hand; the other four
        // (all non-instant/sorcery) go to the graveyard.
        var land1    = SeedLand("Island");
        var sorc     = SeedSorcery("Divination");
        var land2    = SeedLand("Mountain");
        var creature = SeedCreature("Bear");
        var land3    = SeedLand("Forest");

        await PiecesOfThePuzzleFactory.ResolveAsync(_alice, ResolutionContext.Legacy);

        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { sorc });
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new ICard[] { land1, land2, creature, land3 })
            .And.HaveCount(4);
    }

    [Fact]
    public async Task Resolve_NoAgent_NoEligible_AllRevealedToGraveyard()
    {
        // No instant/sorcery revealed → nothing to hand; all five to graveyard.
        var c1 = SeedLand("Island");
        var c2 = SeedLand("Mountain");
        var c3 = SeedCreature("Bear");
        var c4 = SeedLand("Forest");
        var c5 = SeedCreature("Wolf");

        await PiecesOfThePuzzleFactory.ResolveAsync(_alice, ResolutionContext.Legacy);

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new ICard[] { c1, c2, c3, c4, c5 })
            .And.HaveCount(5);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Resolve — agent picks ────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_AgentPicksTwoSpecific_RestToGraveyard()
    {
        // Three eligible; agent grabs i2 and s1 (skipping i1). i1 → graveyard.
        var i1 = SeedInstant("Bolt1");
        var s1 = SeedSorcery("Sorc1");
        var i2 = SeedInstant("Bolt2");
        var land = SeedLand("Island");
        var creature = SeedCreature("Bear");

        AgentRegistry.Set(_alice, new PickByNameAgent("Bolt2", "Sorc1"));

        await PiecesOfThePuzzleFactory.ResolveAsync(_alice, ResolutionContext.Legacy);

        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { i2, s1 }).And.HaveCount(2);
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { i1, land, creature });
        _alice.Zones.Graveyard.GetCards().Should().NotContain(new ICard[] { i2, s1 });
    }

    [Fact]
    public async Task Resolve_AgentDeclinesAfterOne_OnlyOneToHand()
    {
        // Two eligible; the agent takes one then declines (null) → one to hand,
        // the rest (including the un-taken eligible card) to the graveyard.
        var i1 = SeedInstant("Bolt1");
        var s1 = SeedSorcery("Sorc1");
        var land = SeedLand("Island");
        var c1 = SeedCreature("Bear");
        var c2 = SeedCreature("Wolf");

        // Takes "Bolt1" on the first call, then null on the second (decline).
        AgentRegistry.Set(_alice, new PickByNameAgent("Bolt1"));

        await PiecesOfThePuzzleFactory.ResolveAsync(_alice, ResolutionContext.Legacy);

        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { i1 });
        _alice.Zones.Graveyard.GetCards()
            .Should().Contain(new ICard[] { s1, land, c1, c2 }).And.HaveCount(4);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_ShortLibrary_RevealsWhatsThere()
    {
        // Only three cards: two eligible + one land. Reveal 3; both spells to
        // hand; land to graveyard; library ends empty.
        var i1 = SeedInstant("Bolt1");
        var s1 = SeedSorcery("Sorc1");
        var land = SeedLand("Island");

        await PiecesOfThePuzzleFactory.ResolveAsync(_alice, ResolutionContext.Legacy);

        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { i1, s1 });
        _alice.Zones.Graveyard.GetCards().Should().Equal(new ICard[] { land });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_EmptyLibrary_NoOp_NoDrawFromEmptyFlag()
    {
        Func<Task> act = async () => { await PiecesOfThePuzzleFactory.ResolveAsync(_alice, ResolutionContext.Legacy); };

        await act.Should().NotThrowAsync();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void BuildResolveEffect_Single_RunsResolution()
    {
        var i1 = SeedInstant("Bolt1");
        var land = SeedLand("Island");

        var effect = PiecesOfThePuzzleFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { i1 });
        _alice.Zones.Graveyard.GetCards().Should().Equal(new ICard[] { land });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Instant SeedInstant(string name)
    {
        var c = new Instant(name, "{U}");
        Seed(c);
        return c;
    }

    private Sorcery SeedSorcery(string name)
    {
        var c = new Sorcery(name, "{U}");
        Seed(c);
        return c;
    }

    private Land SeedLand(string name)
    {
        var c = new Land(name);
        Seed(c);
        return c;
    }

    private Creature SeedCreature(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        Seed(c);
        return c;
    }

    private void Seed(Card c)
    {
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
    }

    /// <summary>
    /// Test agent that resolves <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
    /// against a fixed sequence of card names: each call returns the next named
    /// card found in the candidates, or <c>null</c> (decline) once the sequence
    /// is exhausted. Other decision hooks throw to flag accidental calls.
    /// </summary>
    private sealed class PickByNameAgent : IPlayerAgent
    {
        private readonly Queue<string> _names;
        public PickByNameAgent(params string[] names) => _names = new Queue<string>(names);

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            if (_names.Count == 0)
            {
                return Task.FromResult<ICard?>(null);
            }

            var name = _names.Dequeue();
            var match = candidates.FirstOrDefault(c => c.Name == name);
            return Task.FromResult<ICard?>(match);
        }

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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> a, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> a, IReadOnlyList<Creature> b, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
