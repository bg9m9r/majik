using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Genesis Wave (Scars of Mirrodin, {X}{G}{G}{G}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Reveal the top X cards of your library. You may put any number of
///    permanent cards with mana value X or less from among them onto the
///    battlefield. Then put all cards revealed this way that weren't put
///    onto the battlefield into your graveyard."
///
/// Coverage:
///  - Identity (Sorcery {X}{G}{G}{G}) + NamedCardFactory dispatch.
///  - Resolve at X=3 → reveals top 3, agent puts eligible permanents (mv ≤ 3)
///    onto the battlefield, rest (incl. nonpermanents) to graveyard.
///  - Mana-value filter (mv ≤ X): an over-cost permanent is never offered and
///    goes to the graveyard.
///  - Nonpermanent (instant/sorcery) revealed cards always go to graveyard.
///  - "You may" / "any number" — agent declining puts that card in graveyard.
///  - X = 0 → no cards revealed, no-op.
///  - Library shorter than X → reveals whatever is available.
/// </summary>
public class GenesisWaveTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ResolutionContext Rc(Player controller) =>
        ResolutionContext.For(controller, agent: null, game: null, chosenTargets: null);

    private static ChosenSpellParams Choose(int? x) =>
        new(ModeIndex: null, X: x,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell, int? x, Player caster,
        IPlayerAgent? agent = null)
    {
        // The factory exposes a public Resolve for direct driving; but exercise
        // the SpellDefinition path here to mirror Green Sun's Zenith.
        foreach (var fx in spell.EffectFactory(Choose(x)))
        {
            fx.Execute();
        }
    }

    private Creature SeedCreatureInLibrary(string name, string manaCost, int p, int t)
    {
        var c = new Creature(name, manaCost, p, t);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private Land SeedLandInLibrary(string name)
    {
        var c = new Land(name);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private Instant SeedInstantInLibrary(string name, string manaCost)
    {
        var c = new Instant(name, manaCost);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // ── Shape / dispatch ────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = GenesisWaveFactory.Create(_alice);

        card.Name.Should().Be("Genesis Wave");
        card.ManaCost.Should().Be("{X}{G}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchesGenesisWave()
    {
        var card = NamedCardFactory.Create("Genesis Wave", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Genesis Wave");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{X}{G}{G}{G}");
    }

    // ── Resolution ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_X3_PutsEligiblePermanents_RestToGraveyard()
    {
        // Top 3: a mv-2 creature, a land (mv 0), a mv-2 instant (nonpermanent).
        var bear = SeedCreatureInLibrary("Grizzly Bears", "{1}{G}", 2, 2);
        var forest = SeedLandInLibrary("Forest");
        var bolt = SeedInstantInLibrary("Lightning Bolt", "{R}");

        // Default agent (none registered) takes every eligible permanent.
        var spell = GenesisWaveFactory.BuildSpellDefinition(_alice, GenesisWaveFactory.Create(_alice));
        Resolve(spell, x: 3, _alice);

        var bf = _alice.Zones.Battlefield.GetCards().ToList();
        bf.Should().Contain(new ICard[] { bear, forest });

        var gy = _alice.Zones.Graveyard.GetCards().ToList();
        gy.Should().Contain(bolt);           // nonpermanent → graveyard
        gy.Should().NotContain(new ICard[] { bear, forest });

        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_OverCostPermanent_NotOffered_GoesToGraveyard()
    {
        // X = 2; the only revealed card is a mv-4 creature → ineligible.
        var titan = SeedCreatureInLibrary("Primeval Titan", "{4}{G}{G}", 6, 6);
        SeedLandInLibrary("Forest"); // pad to 2

        var spell = GenesisWaveFactory.BuildSpellDefinition(_alice, GenesisWaveFactory.Create(_alice));
        Resolve(spell, x: 2, _alice);

        _alice.Zones.Battlefield.GetCards().Should().NotContain(titan);
        _alice.Zones.Graveyard.GetCards().Should().Contain(titan);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_AgentDeclines_CardGoesToGraveyard()
    {
        var elves = SeedCreatureInLibrary("Llanowar Elves", "{G}", 1, 1);

        var declining = new DecliningAgent();
        var spell = GenesisWaveFactory.BuildSpellDefinition(_alice, GenesisWaveFactory.Create(_alice));
        // Drive Resolve directly so we can inject the declining agent.
        await GenesisWaveFactory.ResolveAsync(_alice, x: 3, Rc(_alice), agent: declining);

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(elves);
    }

    [Fact]
    public void Resolve_XZero_NoOp()
    {
        SeedCreatureInLibrary("Llanowar Elves", "{G}", 1, 1);

        var spell = GenesisWaveFactory.BuildSpellDefinition(_alice, GenesisWaveFactory.Create(_alice));
        Resolve(spell, x: 0, _alice);

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_ShortLibrary_RevealsWhatIsAvailable()
    {
        var elves = SeedCreatureInLibrary("Llanowar Elves", "{G}", 1, 1);

        // X = 5 but only one card in library.
        await GenesisWaveFactory.ResolveAsync(_alice, x: 5, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().Contain(elves);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    /// <summary>
    /// Test agent that always declines (returns null) every library pick.
    /// </summary>
    private sealed class DecliningAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel,
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
