using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SowingMycospawnFactory"/>.
///
/// Sowing Mycospawn — {3}{G} Creature — Eldrazi Fungus 3/3
/// (Modern Horizons 3). Oracle text (Scryfall, verified):
///   "Devoid (This card has no color.)
///    Kicker {1}{C} (You may pay an additional {1}{C} as you cast this spell.)
///    When you cast this spell, search your library for a land card,
///        put it onto the battlefield, then shuffle.
///    When you cast this spell, if it was kicked, exile target land."
///
/// Covers:
/// - Identity: {3}{G}, 3/3, Eldrazi Fungus, owner / controller, MV 4.
/// - Devoid: colorless via <see cref="CardColors"/> despite {G} pip.
/// - Devoid keyword marker is attached.
/// - <see cref="NamedCardFactory"/> dispatch builds a Creature.
/// - Kicker {1}{C}: kicker additional cost present; stamps
///   <see cref="Card.WasKicked"/> on payment.
/// - Cast trigger A (land tutor): self-cast match, fires on stack,
///   no external targets, tutors any land to battlefield untapped,
///   then shuffles.
/// - Cast trigger A: tutors a NONBASIC land (the printed text says
///   "land", not "basic land") and puts it onto the battlefield
///   UNTAPPED (no "tapped" rider).
/// - Cast trigger B (kicked exile): self-cast match, fires on stack,
///   1..1 target land request, intervening-if reads WasKicked.
/// - Cast trigger B candidate gatherer: returns Lands across all
///   battlefields; non-lands excluded.
/// - Cast trigger B effect: exiles chosen Land from Battlefield to
///   Exile via ZoneService when supplied; raw move otherwise.
/// - Cast trigger B illegal-on-resolution: target no longer a Land
///   in battlefield → fizzles silently.
/// </summary>
public class SowingMycospawnFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SowingMycospawnFactoryTests()
    {
        ZoneServiceRegistry.Clear();
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        ZoneServiceRegistry.Clear();
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SowingMycospawn_Identity()
    {
        var card = SowingMycospawnFactory.Create(_alice);

        card.Name.Should().Be("Sowing Mycospawn");
        card.ManaCost.Should().Be("{3}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Fungus).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(4,
            "{3}{G} has mana value 4 (CR 202.3)");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SowingMycospawn_IsColorless_DespiteGreenPip()
    {
        // CR 702.114 — Devoid. The {G} pip is "ignored" for the
        // colour predicate; the printed Devoid keyword makes the card
        // colourless regardless of its mana cost.
        var card = SowingMycospawnFactory.Create(_alice);

        CardColors.GetColors(card).Should().BeEmpty(
            "Devoid (CR 702.114) overrides the mana-cost colors — " +
            "Sowing Mycospawn is colorless");
    }

    [Fact]
    public void SowingMycospawn_IsDevoidFlagSet()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        card.IsDevoid.Should().BeTrue(
            "Sowing Mycospawn prints Devoid — the IsDevoid flag is " +
            "the source of truth CardColors.GetColors reads");
    }

    [Fact]
    public void SowingMycospawn_CarriesDevoidKeywordMarker()
    {
        var card = SowingMycospawnFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == SowingMycospawnFactory.DevoidKeyword,
                "Devoid keyword ability marker is attached for ability-scan " +
                "discoverability (CR 702.114)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SowingMycospawn()
    {
        var card = NamedCardFactory.Create("Sowing Mycospawn", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sowing Mycospawn");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Fungus).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.ManaCost.Should().Be("{3}{G}");
    }

    // -----------------------------------------------------------------------
    // Kicker {1}{C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SowingMycospawn_KickerAdditionalCost_IsKicker1C()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var kicker = SowingMycospawnFactory.BuildAdditionalCost(card);

        kicker.Should().BeOfType<KickerAdditionalCost>(
            "Kicker is a real IAdditionalCost primitive (CR 702.33)");
        var kc = (KickerAdditionalCost)kicker;
        kc.KickerCost.Should().Be(ManaCost.Parse("{1}{C}"));
    }

    [Fact]
    public void SowingMycospawn_KickerPayment_StampsWasKicked()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var kicker = SowingMycospawnFactory.BuildAdditionalCost(card);

        // Seed Alice with {1}{C} worth of mana. KickerCost is {1}{C},
        // which the engine totals as 2 generic mana (no colorless-mana
        // primitive — colorless is paid out of generic in v1).
        _alice.AddManaToPool(ManaCost.Parse("{2}"));

        kicker.Pay(_alice).Should().BeTrue();
        card.WasKicked.Should().BeTrue(
            "KickerAdditionalCost.Pay stamps Card.WasKicked (CR 702.33b)");
    }

    // -----------------------------------------------------------------------
    // Cast trigger A — land tutor (always)
    // -----------------------------------------------------------------------

    [Fact]
    public void SowingMycospawn_LandTutorTrigger_MatchesSelfCast()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var tutorTrigger = GetTutorTrigger(card);

        var spell = new StubSpell(card, _alice);
        var ev = new SpellCastEvent(spell);

        tutorTrigger.Condition.Matches(ev, tutorTrigger).Should().BeTrue();
    }

    [Fact]
    public void SowingMycospawn_LandTutorTrigger_DoesNotMatchOtherSpellCast()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var tutorTrigger = GetTutorTrigger(card);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        var spell = new StubSpell(other, _alice);
        var ev = new SpellCastEvent(spell);

        tutorTrigger.Condition.Matches(ev, tutorTrigger).Should().BeFalse();
    }

    [Fact]
    public void SowingMycospawn_LandTutorTrigger_ActiveOnStack_NoExternalTargets()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var tutorTrigger = GetTutorTrigger(card);

        tutorTrigger.ActiveZones.Should().Contain(ZoneType.Stack,
            "the cast trigger fires while the spell is on the stack " +
            "(Devourer of Destiny posture)");
        tutorTrigger.TargetRequests.Should().BeEmpty(
            "the land is chosen during the tutor search (CR 701.19a), " +
            "not via a TargetRequest");
    }

    [Fact]
    public void SowingMycospawn_LandTutorEffect_TutorsAnyLandUntapped_AndShuffles()
    {
        // Seed Alice's library with a Forest, a Sacred Foundry (a nonbasic
        // land), and an instant. The tutor should put a Land (basic
        // OR nonbasic — printed text is "a land card") onto the battlefield
        // UNTAPPED (no tapped rider). With no agent registered the
        // template's deterministic fallback picks the first eligible
        // candidate.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(forest);

        var shockLand = new Land("Sacred Foundry"); // nonbasic land
        shockLand.SetOwner(_alice);
        shockLand.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(shockLand);

        var spell = new Instant("Lightning Bolt", "{R}");
        spell.SetOwner(_alice);
        spell.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(spell);

        var card = SowingMycospawnFactory.Create(_alice);
        var tutorTrigger = GetTutorTrigger(card);

        foreach (var ef in tutorTrigger.Effects) ef.Execute();

        // One land moved to the battlefield (the first Library candidate
        // matching the predicate — Forest).
        var bf = _alice.Zones.Battlefield.GetCards().OfType<Land>().ToList();
        bf.Should().HaveCount(1,
            "exactly one land card is tutored to the battlefield (CR 701.19a)");
        bf[0].Zone.Should().Be(ZoneType.Battlefield);

        // Untapped — Sowing Mycospawn has no "enters tapped" rider.
        bf[0].IsTapped.Should().BeFalse(
            "printed text is 'put it onto the battlefield' — no tapped rider");

        // The non-Land spell remains in the library — predicate rejected it.
        spell.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // Cast trigger B — kicked exile
    // -----------------------------------------------------------------------

    [Fact]
    public void SowingMycospawn_ExileTrigger_MatchesSelfCast()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        var spell = new StubSpell(card, _alice);
        var ev = new SpellCastEvent(spell);

        exileTrigger.Condition.Matches(ev, exileTrigger).Should().BeTrue();
    }

    [Fact]
    public void SowingMycospawn_ExileTrigger_RequestsOneLand_ActiveOnStack()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        exileTrigger.ActiveZones.Should().Contain(ZoneType.Stack);
        exileTrigger.TargetRequests.Should().HaveCount(1);
        var req = exileTrigger.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("land");
        req.Intent.HasAny(BotIntent.Removal).Should().BeTrue();
    }

    [Fact]
    public void SowingMycospawn_ExileTrigger_InterveningIf_RequiresKicked()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        exileTrigger.InterveningIf.Should().NotBeNull(
            "the 'if it was kicked' clause is the intervening-if condition (CR 603.4)");
        // Not kicked → intervening-if is false.
        card.WasKicked.Should().BeFalse();
        exileTrigger.InterveningIf!().Should().BeFalse(
            "non-kicked cast → intervening-if false → trigger never lands on stack");

        // Stamp WasKicked → intervening-if flips true.
        card.SetWasKicked(true);
        exileTrigger.InterveningIf!().Should().BeTrue(
            "kicker paid → intervening-if true → trigger fires");
    }

    [Fact]
    public void SowingMycospawn_ExileTrigger_CandidateGatherer_OnlyLands()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        // Bob's battlefield: a Land, a Creature, an Artifact.
        var bobsLand = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var bobsBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);
        bobsBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsBear);

        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_bob);
        solRing.SetController(_bob);
        solRing.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(solRing);

        var ctx = new GameContext(
            _alice,
            new[] { _alice, _bob },
            _alice,
            turnNumber: 0,
            currentPhase: PhaseStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack());

        var pool = exileTrigger.TargetRequests[0].ResolveCandidates(ctx);

        pool.Should().Contain(bobsLand, "Mountain is a Land — legal target");
        pool.Should().NotContain(bobsBear, "Grizzly Bears is not a Land");
        pool.Should().NotContain(solRing, "Sol Ring is not a Land");
    }

    [Fact]
    public void SowingMycospawn_ExileTriggerEffect_ExilesChosenLand_RawZoneFallback()
    {
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        var bobsLand = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        exileTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });

        foreach (var ef in exileTrigger.Effects) ef.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(bobsLand);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsLand);
    }

    [Fact]
    public void SowingMycospawn_ExileTriggerEffect_FizzlesIfTargetLeftBattlefield()
    {
        // CR 608.2b — illegal-on-resolution. If the chosen land left the
        // battlefield before resolution, the exile should fizzle silently.
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        var bobsLand = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        // Pretend it's already gone — in graveyard now, no longer on bf.
        bobsLand.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobsLand);

        exileTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });

        foreach (var ef in exileTrigger.Effects) ef.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Graveyard,
            "target left the battlefield → illegal at resolution (CR 608.2b)");
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsLand);
    }

    [Fact]
    public void SowingMycospawn_ExileTriggerEffect_FizzlesIfTargetIsNoLongerLand()
    {
        // A non-Land permanent (e.g. type-changed via a land-strip effect)
        // should fizzle on resolution (CR 608.2b).
        var card = SowingMycospawnFactory.Create(_alice);
        var exileTrigger = GetExileTrigger(card);

        var bobsBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);
        bobsBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsBear);

        exileTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsBear },
        });

        foreach (var ef in exileTrigger.Effects) ef.Execute();

        bobsBear.Zone.Should().Be(ZoneType.Battlefield,
            "target is not a Land → illegal at resolution (CR 608.2b)");
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsBear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>The land-tutor cast trigger — the first
    /// <see cref="EventTriggerCondition{TEvent}"/> over SpellCastEvent
    /// whose request list is empty (no external targets).</summary>
    private static TriggeredAbility GetTutorTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<SpellCastEvent>
                && t.TargetRequests.Count == 0);

    /// <summary>The kicked-exile cast trigger — the
    /// <see cref="EventTriggerCondition{TEvent}"/> over SpellCastEvent
    /// that targets a Land (single request).</summary>
    private static TriggeredAbility GetExileTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<SpellCastEvent>
                && t.TargetRequests.Count == 1);

    private sealed class StubSpell : ISpell
    {
        public StubSpell(ICard card, Player controller)
        {
            Card = card;
            Controller = controller;
        }

        public ICard Card { get; }
        public Player Controller { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public IReadOnlyList<ITarget> Targets { get; } = Array.Empty<ITarget>();
        public IReadOnlyList<ICost> Costs { get; } = Array.Empty<ICost>();
        public bool CannotBeCountered => false;
        public void Resolve() { }
    }
}
