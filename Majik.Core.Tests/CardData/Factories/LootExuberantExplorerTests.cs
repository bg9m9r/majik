using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Loot, Exuberant Explorer (Bloomburrow, {2}{G},
/// Legendary Creature — Beast Noble 1/4).
///
/// "You may play an additional land on each of your turns.
///  {4}{G}{G}, {T}: Look at the top six cards of your library. You may
///  reveal a creature card with mana value less than or equal to the number
///  of lands you control from among them and put it onto the battlefield.
///  Put the rest on the bottom in a random order."
///
/// The factory-dispatch + well-formedness checks are covered for every
/// implemented card by CardFactoryContractTests, so this suite only asserts
/// the card's UNIQUE behaviour (the +1 land grant, the activated dig
/// ability's land-count-gated single pick) plus one identity assert for the
/// non-vanilla stats.
/// </summary>
[Trait("Color", "G")]
public class LootExuberantExplorerTests
{
    private static ResolutionContext Rc(Player controller) =>
        ResolutionContext.For(controller, agent: null, game: null, chosenTargets: null);

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Loot_Identity()
    {
        var c = LootExuberantExplorerFactory.Create(_alice);

        c.Name.Should().Be("Loot, Exuberant Explorer");
        c.ManaCost.Should().Be("{2}{G}");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(4);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        c.HasSubtype(CardSubtype.Noble).Should().BeTrue();
    }

    [Fact]
    public void Loot_GrantsOneAdditionalLandPlay()
    {
        // CR 305.2 / 720 — the static land-play permission is stamped on the
        // permanent and summed live by LandDropTracker.
        var loot = LootExuberantExplorerFactory.Create(_alice);

        loot.AdditionalLandPlaysGranted.Should().Be(1);

        _alice.Zones.Battlefield.AddCard(loot);
        loot.SetController(_alice);
        var bonus = LandDropTracker.AdditionalLandPlaysFromBattlefield(_alice);
        bonus.Should().Be(1);
    }

    [Fact]
    public void Loot_HasActivatedAbilityWithManaAndTapCost()
    {
        var loot = LootExuberantExplorerFactory.Create(_alice);

        var activated = loot.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<ManaCostCost>().Single().Cost.TotalValue.Should().Be(6);
        activated.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_PutsCreatureWithinLandCapOntoBattlefield()
    {
        // Four lands on the battlefield → cap = 4.
        for (int i = 0; i < 4; i++) SeedLandOnBattlefield(_alice, $"Forest{i}");

        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3 ≤ 4
        var bolt = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");
        var wrath = SeedSorceryInLibrary(_alice, "Wrath of God", "{2}{W}{W}");
        for (int i = 0; i < 3; i++) SeedInstantInLibrary(_alice, $"Pad{i}", "{1}"); // pad to 6

        await LootExuberantExplorerFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().Contain(threeDrop);
        _alice.Zones.Library.GetCards().Should().NotContain(threeDrop);
        // Non-creatures remain in the library (bottomed).
        _alice.Zones.Library.GetCards().Should().Contain(new ICard[] { bolt, wrath });
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_ExcludesCreatureOverLandCap()
    {
        // Only two lands → cap = 2. A mv-3 creature is NOT eligible.
        SeedLandOnBattlefield(_alice, "Forest0");
        SeedLandOnBattlefield(_alice, "Forest1");

        var overCap = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3 > 2
        for (int i = 0; i < 5; i++) SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        await LootExuberantExplorerFactory.ResolveAsync(_alice, Rc(_alice));

        // Nothing eligible → nothing onto the battlefield; the creature stays
        // in the library (bottomed).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(overCap);
        _alice.Zones.Library.GetCards().Should().Contain(overCap);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_NoLands_CapZero_NoCreaturePut()
    {
        // Zero lands → cap = 0. Even a mv-1 creature (e.g. Llanowar Elves) is
        // over the cap.
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        for (int i = 0; i < 5; i++) SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        await LootExuberantExplorerFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(elves);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_OnlyOneCreaturePut_NotMultiple()
    {
        // Two eligible creatures in the top six; only ONE may be put onto the
        // battlefield (the no-agent deterministic path takes the first).
        SeedLandOnBattlefield(_alice, "Forest0");
        SeedLandOnBattlefield(_alice, "Forest1");
        SeedLandOnBattlefield(_alice, "Forest2");

        SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);  // mv 2 ≤ 3
        SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);    // mv 1 ≤ 3
        for (int i = 0; i < 4; i++) SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        await LootExuberantExplorerFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Creature)).Should().Be(1);
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_AgentDeclines_NoCreaturePut()
    {
        SeedLandOnBattlefield(_alice, "Forest0");
        SeedLandOnBattlefield(_alice, "Forest1");

        var bear = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        for (int i = 0; i < 5; i++) SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        // "You may" — agent declines (returns null).
        var agent = new DeclineAgent();
        await LootExuberantExplorerFactory.ResolveAsync(_alice, Rc(_alice), zoneService: null, agent: agent);

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _alice.Zones.Library.GetCards().Should().Contain(bear); // bottomed, not put.
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_EmptyLibrary_NoOp()
    {
        SeedLandOnBattlefield(_alice, "Forest0");

        await LootExuberantExplorerFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ICard SeedCreatureInLibrary(
        Player p, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedInstantInLibrary(Player p, string name, string manaCost)
    {
        var c = new Instant(name, manaCost);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedSorceryInLibrary(Player p, string name, string manaCost)
    {
        var c = new Sorcery(name, manaCost);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard SeedLandOnBattlefield(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        return c;
    }

    /// <summary>Agent that always declines the optional pick (returns null).</summary>
    private sealed class DeclineAgent : IPlayerAgent
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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> a, IReadOnlyList<Permanent> e, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
