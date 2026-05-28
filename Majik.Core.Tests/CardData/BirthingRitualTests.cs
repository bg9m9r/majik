using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Birthing Ritual (FDN, {1}{G}, Enchantment).
///
/// Oracle text:
///   "At the beginning of your end step, if you control a creature, look
///    at the top seven cards of your library. Then you may sacrifice a
///    creature. If you do, you may put a creature card with mana value
///    X or less from among those cards onto the battlefield, where X is
///    1 plus the sacrificed creature's mana value. Put the rest on the
///    bottom of your library in a random order."
///
/// Coverage:
///  - Identity (name / type / mana cost / colour) + NamedCardFactory dispatch.
///  - End-step trigger fires only on the controller's End step (CR 500.7
///    "your end step").
///  - CR 603.4 intervening-if: trigger does NOT fire when the controller
///    controls zero creatures.
///  - Resolution decline (no sacrifice): top 7 still bottomed in random
///    order — no creature lands on the battlefield.
///  - Resolution accept (sacrifice + put creature): sacrificed creature
///    routes to graveyard; X = 1 + sac.MV; pick from the peeked 7 must
///    satisfy MV ≤ X and creature type; remaining 6 bottomed.
///  - "Creature card MV ≤ X" filter: a higher-MV creature in the seven
///    is excluded.
///  - Short library (fewer than 7): peek operates on whatever is there;
///    sac+put still legal when an eligible creature is among them.
/// </summary>
public class BirthingRitualTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ----------------------------------------------------------------------
    // Identity + dispatch
    // ----------------------------------------------------------------------

    [Fact]
    public void BirthingRitual_Identity()
    {
        var c = BirthingRitualFactory.Create(_alice);

        c.Name.Should().Be("Birthing Ritual");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // {1}{G} → mv 2.
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void BirthingRitual_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Birthing Ritual", _alice);

        c.Should().BeOfType<Enchantment>("Birthing Ritual is an Enchantment");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the single end-step trigger is attached");
    }

    // ----------------------------------------------------------------------
    // End-step trigger gating — controller-only, end-step-only
    // ----------------------------------------------------------------------

    [Fact]
    public void Trigger_FiresOnControllerEndStep_NotOpponentEndStep_NotOtherSteps()
    {
        var rit = BirthingRitualFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rit);
        rit.SetZone(ZoneType.Battlefield);

        // Need a creature on Alice's battlefield so the intervening-if
        // does not nuke the trigger.
        SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        var trigger = rit.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue("printed text is 'at the beginning of your end step'");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _bob))
            .Should().BeFalse("'your' end step ≠ opponent's end step");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _alice))
            .Should().BeFalse("upkeep is not the end step");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.Draw, _alice))
            .Should().BeFalse("draw is not the end step");
    }

    [Fact]
    public void Trigger_InterveningIf_DoesNotFireWhenNoCreaturesControlled()
    {
        // CR 603.4 — "At the beginning of … end step, if you control a
        // creature, …" The intervening-if is checked at trigger time AND
        // again on resolution. With zero creatures the trigger does not
        // make it to the stack.
        var rit = BirthingRitualFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rit);
        rit.SetZone(ZoneType.Battlefield);
        // No other creatures on Alice's battlefield.

        var trigger = rit.Abilities.OfType<TriggeredAbility>().Single();
        trigger.InterveningIf.Should().NotBeNull("CR 603.4 'if you control a creature' is an intervening-if");
        trigger.CanBePutOnStack().Should().BeFalse("controller controls no creatures");
    }

    [Fact]
    public void Trigger_InterveningIf_PassesWhenControllerHasCreature()
    {
        var rit = BirthingRitualFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rit);
        rit.SetZone(ZoneType.Battlefield);
        SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        var trigger = rit.Abilities.OfType<TriggeredAbility>().Single();
        trigger.CanBePutOnStack().Should().BeTrue("controller controls at least one creature");
    }

    [Fact]
    public void Trigger_InterveningIf_OpponentCreaturesDoNotCount()
    {
        var rit = BirthingRitualFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rit);
        rit.SetZone(ZoneType.Battlefield);
        // Bob has a creature; Alice has none.
        SeedCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var trigger = rit.Abilities.OfType<TriggeredAbility>().Single();
        trigger.CanBePutOnStack().Should().BeFalse("intervening-if reads 'YOU control a creature'");
    }

    // ----------------------------------------------------------------------
    // Resolution — decline path (no sacrifice)
    // ----------------------------------------------------------------------

    [Fact]
    public void Resolve_DeclineSacrifice_BottomsAllSeven_NothingEntersBattlefield()
    {
        // Put a fodder creature on battlefield so the intervening-if
        // would pass, but we drive the agent to DECLINE the sacrifice.
        var fodder = SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        // Top 7 of library — mixed bag with a few eligible creatures.
        var pick    = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bigger  = SeedCreatureInLibrary(_alice, "Primeval Titan", "{4}{G}{G}", 6, 6);
        var bolt    = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");
        var wrath   = SeedSorceryInLibrary(_alice, "Wrath of God", "{2}{W}{W}");
        var forest  = SeedLandInLibrary(_alice, "Forest");
        var island  = SeedLandInLibrary(_alice, "Island");
        var swamp   = SeedLandInLibrary(_alice, "Swamp");

        var agent = new RitualTestAgent(sacrificePick: null /* decline */, libraryPick: null);

        BirthingRitualFactory.Resolve(_alice, agent: agent);

        // No new permanents — fodder stays, nothing else hit the battlefield.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().ContainSingle()
            .Which.Should().BeSameAs(fodder);

        // All seven library cards are still in the library (now bottomed).
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().BeEquivalentTo(new ICard[]
            { pick, bigger, bolt, wrath, forest, island, swamp });

        // Graveyard untouched (no sacrifice happened).
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoSacrificableCreature_BottomsAllSeven_NothingHappens()
    {
        // No creatures on Alice's battlefield: the intervening-if has
        // already blocked stack entry in normal flow. This test exercises
        // the resolve body directly for the defensive zero-creatures
        // branch (mirrors FieldOfTheDead's CR 603.4 re-check).
        var rit = BirthingRitualFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rit);
        rit.SetZone(ZoneType.Battlefield);

        var bolt = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");
        var bear = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        BirthingRitualFactory.Resolve(_alice);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(new ICard[] { bolt, bear });
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ----------------------------------------------------------------------
    // Resolution — accept path (sacrifice + put)
    // ----------------------------------------------------------------------

    [Fact]
    public void Resolve_AcceptSacrifice_PutsEligibleCreatureOntoBattlefield_RestBottomed()
    {
        // Fodder MV 1 (Llanowar Elves). X = 1 + 1 = 2 → can put MV-≤-2.
        var fodder = SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        // Top 7 (top-of-library order = insertion order via AddCard).
        var bears   = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2); // mv 2 — eligible
        var titan   = SeedCreatureInLibrary(_alice, "Primeval Titan", "{4}{G}{G}", 6, 6); // mv 6 — NOT eligible
        var bolt    = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");
        var wrath   = SeedSorceryInLibrary(_alice, "Wrath of God", "{2}{W}{W}");
        var forest  = SeedLandInLibrary(_alice, "Forest");
        var island  = SeedLandInLibrary(_alice, "Island");
        var swamp   = SeedLandInLibrary(_alice, "Swamp");

        // Agent: sacrifice the fodder, then pick the Grizzly Bears from the 7.
        var agent = new RitualTestAgent(sacrificePick: fodder, libraryPick: bears);

        BirthingRitualFactory.Resolve(_alice, agent: agent);

        // Bears ETB on Alice's battlefield, controlled by Alice.
        var bf = _alice.Zones.Battlefield.GetCards().ToList();
        bf.Should().Contain(bears);
        bears.Controller.Should().BeSameAs(_alice);
        bears.Zone.Should().Be(ZoneType.Battlefield);

        // Fodder went to graveyard (sacrifice).
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
        fodder.Zone.Should().Be(ZoneType.Graveyard);

        // Remaining 6 from the peek are in the library.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().BeEquivalentTo(new ICard[]
            { titan, bolt, wrath, forest, island, swamp });
        lib.Should().NotContain(bears);
    }

    [Fact]
    public void Resolve_AcceptSacrifice_XCappedBySacMv_HigherMvCreatureExcluded()
    {
        // Fodder MV 1 → X = 2. Top 7 has only an MV-3 creature, so the
        // agent's library pick of that creature must be REJECTED by the
        // factory (defensive predicate check) — the creature stays in
        // the library, but the sacrifice still happens.
        var fodder = SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3
        // Pad to 7 with non-creatures.
        var pads = new List<ICard>();
        for (int i = 0; i < 6; i++)
            pads.Add(SeedInstantInLibrary(_alice, $"Pad{i}", "{1}"));

        // Agent picks the (illegal) three-drop; factory must reject it.
        var agent = new RitualTestAgent(sacrificePick: fodder, libraryPick: threeDrop);

        BirthingRitualFactory.Resolve(_alice, agent: agent);

        // Three-drop stayed in the library.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(threeDrop);
        // Fodder still hit graveyard (CR 603 — "may" sacrifice + then "may" put;
        // the second "may" being declined / impossible doesn't undo the sac).
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public void Resolve_AcceptSacrifice_ShortLibrary_StillWorks()
    {
        // Library shorter than 7 is fine — peek takes what's there.
        var fodder = SeedCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        var bears = SeedCreatureInLibrary(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bolt = SeedInstantInLibrary(_alice, "Lightning Bolt", "{R}");

        var agent = new RitualTestAgent(sacrificePick: fodder, libraryPick: bears);

        BirthingRitualFactory.Resolve(_alice, agent: agent);

        _alice.Zones.Battlefield.GetCards().Should().Contain(bears);
        _alice.Zones.Library.GetCards().Should().BeEquivalentTo(new[] { bolt });
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    [Fact]
    public void Resolve_AcceptSacrifice_BiggerXEnablesBiggerPick()
    {
        // Sac MV 2 → X = 3. An MV-3 creature in the 7 is now eligible.
        var fodder = SeedCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var threeDrop = SeedCreatureInLibrary(_alice, "Reflector Mage", "{1}{W}{U}", 2, 3); // mv 3
        var titan = SeedCreatureInLibrary(_alice, "Primeval Titan", "{4}{G}{G}", 6, 6); // mv 6 — still NOT
        for (int i = 0; i < 5; i++)
            SeedInstantInLibrary(_alice, $"Pad{i}", "{1}");

        var agent = new RitualTestAgent(sacrificePick: fodder, libraryPick: threeDrop);

        BirthingRitualFactory.Resolve(_alice, agent: agent);

        _alice.Zones.Battlefield.GetCards().Should().Contain(threeDrop);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(titan);
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Creature SeedCreatureOnBattlefield(
        Player p, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

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

    private static ICard SeedLandInLibrary(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        return c;
    }

    /// <summary>
    /// Test agent that supplies a pre-canned sacrifice pick + library
    /// pick for Birthing Ritual's two-step "may sac / may put" prompt.
    /// </summary>
    private sealed class RitualTestAgent : IPlayerAgent
    {
        private readonly ICard? _sacrificePick;
        private readonly ICard? _libraryPick;

        public RitualTestAgent(ICard? sacrificePick, ICard? libraryPick)
        {
            _sacrificePick = sacrificePick;
            _libraryPick = libraryPick;
        }

        public Task<ICard?> ChooseFromBattlefieldAsync(
            Player chooser,
            IReadOnlyList<ICard> candidates,
            BotIntent intent,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(
                _sacrificePick != null && candidates.Contains(_sacrificePick)
                    ? _sacrificePick
                    : null);

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(
                _libraryPick != null && candidates.Contains(_libraryPick)
                    ? _libraryPick
                    : null);

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
    }
}
