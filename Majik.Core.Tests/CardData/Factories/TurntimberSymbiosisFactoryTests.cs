using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TurntimberSymbiosisFactory"/> and
/// <see cref="TurntimberSerpentineWoodFactory"/> — the front + back faces of
/// the Zendikar Rising modal double-faced card
/// Turntimber Symbiosis // Turntimber, Serpentine Wood.
///
/// Front face (Turntimber Symbiosis, {4}{G}{G}{G}):
///   Sorcery. "Look at the top seven cards of your library. You may put a
///   creature card from among them onto the battlefield. If that card has
///   mana value 3 or less, it enters with three additional +1/+1 counters
///   on it. Put the rest on the bottom of your library in a random order."
///
/// Back face (Turntimber, Serpentine Wood):
///   Land. "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." "{T}: Add {G}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - NamedCardFactory dispatches both printed names.
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: no-X, no-target SpellDefinition shape.
/// - Front: puts a creature card (any mv) onto the battlefield.
/// - Front: mv ≤ 3 creature enters with three +1/+1 counters (CR 122).
/// - Front: mv ≥ 4 creature enters with NO counters.
/// - Front: rest go to the bottom of the library.
/// - Front: no creatures in top seven → no-op put, all bottomed.
/// - Front: short / empty library handled.
/// - Front: agent declines → nothing put, all bottomed.
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {G} mana ability.
/// - Back: pay 3 life → enters untapped.
/// - Back: decline → enters tapped.
/// - Back: life &lt; 3 → enters tapped (CR 119.4).
/// - Back: no agent → enters tapped.
/// </summary>
[Trait("Color", "G")]
public class TurntimberSymbiosisFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    private static ResolutionContext Rc(Player controller) =>
        ResolutionContext.For(controller, agent: null, game: null, chosenTargets: null);

    public TurntimberSymbiosisFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Front-face seeding helpers ───────────────────────────────────────

    private static Creature SeedCreatureInLibrary(
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

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void TurntimberSymbiosis_Identity_4GGG_Sorcery()
    {
        var card = TurntimberSymbiosisFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Turntimber Symbiosis");
        card.ManaCost.Should().Be("{4}{G}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void TurntimberSymbiosis_IsGreen()
    {
        var card = TurntimberSymbiosisFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green, "three {G} pips make it green");
        colors.Should().NotContain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Red);
    }

    // =========================================================================
    // MDFC face tracker — front face
    // =========================================================================

    [Fact]
    public void TurntimberSymbiosis_CarriesMdfcState_FrontFace()
    {
        var card = TurntimberSymbiosisFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Turntimber Symbiosis is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Turntimber Symbiosis");
        card.MdfcState!.BackFaceName.Should().Be("Turntimber, Serpentine Wood");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Turntimber Symbiosis");
    }

    // =========================================================================
    // Front face — SpellDefinition shape
    // =========================================================================

    [Fact]
    public void BuildSpellDefinition_NoX_NoTargets()
    {
        var def = TurntimberSymbiosisFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse("Turntimber Symbiosis is not an X-spell");
        def.TargetRequests.Should().BeEmpty(
            "the creature is chosen at resolution from the peeked cards, not targeted");
    }

    // =========================================================================
    // Front face — resolve: put a small creature, gets +1/+1 counters
    // =========================================================================

    [Fact]
    public async System.Threading.Tasks.Task Resolve_PutsCreatureMvAtMostThree_WithThreeCounters()
    {
        // Llanowar Elves: mana value 1 ≤ 3 → enters with three +1/+1 counters.
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        // Pad to seven so the rest get bottomed.
        for (int i = 0; i < 6; i++)
            SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice));

        elves.Zone.Should().Be(ZoneType.Battlefield,
            "a creature card is put onto the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(elves);
        elves.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "mana value 1 ≤ 3 → three additional +1/+1 counters (CR 122)");
        elves.Controller.Should().BeSameAs(_alice);
    }

    // =========================================================================
    // Front face — resolve: put a big creature, NO +1/+1 counters
    // =========================================================================

    [Fact]
    public async System.Threading.Tasks.Task Resolve_PutsCreatureMvAboveThree_WithoutCounters()
    {
        // Primeval Titan: mana value 6 > 3 → no bonus counters.
        var titan = SeedCreatureInLibrary(_alice, "Primeval Titan", "{4}{G}{G}", 6, 6);
        for (int i = 0; i < 6; i++)
            SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice));

        titan.Zone.Should().Be(ZoneType.Battlefield,
            "any creature card is eligible — the mv cap only gates the counters");
        titan.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "mana value 6 > 3 → no additional counters");
    }

    // =========================================================================
    // Front face — resolve: the rest go to the bottom of the library
    // =========================================================================

    [Fact]
    public async System.Threading.Tasks.Task Resolve_RestGoToBottomOfLibrary()
    {
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        var rest = new List<ICard>();
        for (int i = 0; i < 6; i++)
            rest.Add(SeedInstantInLibrary(_alice, $"Pad{i}", "{1}"));

        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().Contain(elves);
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().NotContain(elves);
        lib.Should().BeEquivalentTo(rest,
            "the rest of the peeked cards are bottomed (order irrelevant — random)");
    }

    // =========================================================================
    // Front face — resolve: no creatures in the top seven → nothing put
    // =========================================================================

    [Fact]
    public async System.Threading.Tasks.Task Resolve_NoCreaturesInTopSeven_NoPut_AllBottom()
    {
        var rest = new List<ICard>();
        for (int i = 0; i < 7; i++)
            rest.Add(SeedInstantInLibrary(_alice, $"Pad{i}", "{1}"));

        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "no creature card among the top seven → nothing to put");
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(rest);
    }

    // =========================================================================
    // Front face — resolve: short / empty library
    // =========================================================================

    [Fact]
    public async System.Threading.Tasks.Task Resolve_ShortLibrary_WorksOnAvailableCards()
    {
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        var bolt = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");

        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().Contain(elves);
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(new[] { bolt });
    }

    [Fact]
    public async System.Threading.Tasks.Task Resolve_EmptyLibrary_NoOp()
    {
        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice));

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // =========================================================================
    // Front face — resolve: agent declines → nothing put, all bottomed
    // =========================================================================

    [Fact]
    public async System.Threading.Tasks.Task Resolve_AgentDeclines_NothingPut_AllBottom()
    {
        var elves = SeedCreatureInLibrary(_alice, "Llanowar Elves", "{G}", 1, 1);
        var rest = new List<ICard> { elves };
        for (int i = 0; i < 6; i++)
            rest.Add(SeedInstantInLibrary(_alice, $"Pad{i}", "{1}"));

        // Agent that declines (returns null) — legal under "you may".
        var agent = new DeclineLibraryPickAgent();

        await TurntimberSymbiosisFactory.ResolveAsync(_alice, Rc(_alice), zoneService: null, agent: agent);

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "the agent declined the optional put");
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(rest,
            "all peeked cards (including the declined creature) are bottomed");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void TurntimberSerpentineWood_Identity_Land()
    {
        var land = TurntimberSerpentineWoodFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Turntimber, Serpentine Wood");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Turntimber, Serpentine Wood is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // =========================================================================
    // Back face — MDFC face tracker
    // =========================================================================

    [Fact]
    public void TurntimberSerpentineWood_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = TurntimberSerpentineWoodFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Turntimber, Serpentine Wood is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Turntimber Symbiosis");
        land.MdfcState!.BackFaceName.Should().Be("Turntimber, Serpentine Wood");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Turntimber, Serpentine Wood");
    }

    // =========================================================================
    // Back face — {T}: Add {G}
    // =========================================================================

    [Fact]
    public void TurntimberSerpentineWood_HasSingleManaAbility_AddingGreen()
    {
        var land = TurntimberSerpentineWoodFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {G} ability");
        manaAbilities[0].ManaGenerated.Green.Should().BeGreaterThan(0, "produces green mana");
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void TurntimberSerpentineWood_HasNoNonManaActivatedOrTriggeredAbilities()
    {
        var land = TurntimberSerpentineWoodFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("ETB is a replacement effect, not triggered (CR 614.1c)");
    }

    // =========================================================================
    // Back face — ETB pay-3-life replacement
    // =========================================================================

    [Fact]
    public void TurntimberSerpentineWood_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var land = TurntimberSerpentineWoodFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "enters untapped when the controller pays 3 life");
        _alice.LifeTotal.Should().Be(17, "20 - 3 = 17");
    }

    [Fact]
    public void TurntimberSerpentineWood_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var land = TurntimberSerpentineWoodFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue("enters tapped when agent declines");
        _alice.LifeTotal.Should().Be(20, "no life paid");
    }

    [Fact]
    public void TurntimberSerpentineWood_EntersTapped_WhenLifeBelowThree()
    {
        // CR 119.4 — can't pay life you don't have.
        var bus = new ReplacementBus();
        _alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        // No QueueYesNo — if prompted, ScriptedAgent would throw.
        AgentRegistry.Set(_alice, agent);

        var land = TurntimberSerpentineWoodFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeTrue(
            "can't pay 3 life with only 2 — enters tapped (CR 119.4)");
        _alice.LifeTotal.Should().Be(2, "no payment taken");
    }

    [Fact]
    public void TurntimberSerpentineWood_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        // No AgentRegistry.Set — no agent at all.

        var land = TurntimberSerpentineWoodFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after!.EntersTapped.Should().BeTrue("no agent → default decline → enters tapped");
        _alice.LifeTotal.Should().Be(20);
    }

    // ── Test agent: always declines the library pick ─────────────────────

    private sealed class DeclineLibraryPickAgent : IPlayerAgent
    {
        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(null);

        // ---- unused decision hooks (throw to flag unexpected use) -----
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
        public Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
