using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Kiki-Jiki, Mirror Breaker — Legendary Creature — Goblin
/// Shaman 2/2, {2}{R}{R}{R}.
///
///   "Haste
///    {T}: Create a token that's a copy of another target nonlegendary
///    creature you control, except it has haste. Exile it at the
///    beginning of the next end step."
/// </summary>
public class KikiJikiMirrorBreakerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public KikiJikiMirrorBreakerTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KikiJiki_IsLegendaryGoblinShaman_2_2_AtCost2RRR()
    {
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice);

        kiki.Name.Should().Be("Kiki-Jiki, Mirror Breaker");
        kiki.ManaCost.Should().Be("{2}{R}{R}{R}");
        kiki.BasePower.Should().Be(2);
        kiki.BaseToughness.Should().Be(2);
        kiki.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        kiki.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        kiki.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        kiki.Owner.Should().BeSameAs(_alice);
        kiki.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KikiJiki_HasHasteKeyword()
    {
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice);

        kiki.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Haste");
    }

    [Fact]
    public void KikiJiki_HasOneActivatedAbility_WithSingleTargetRequest()
    {
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice);

        var activated = kiki.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().ContainSingle("Kiki-Jiki has one printed activated ability");

        var ability = activated[0];
        ability.TargetRequests.Should().HaveCount(1);
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KikiJiki()
    {
        var kiki = NamedCardFactory.Create("Kiki-Jiki, Mirror Breaker", _alice);

        kiki.Should().BeOfType<Creature>();
        kiki.Name.Should().Be("Kiki-Jiki, Mirror Breaker");
        kiki.ManaCost.Should().Be("{2}{R}{R}{R}");
        kiki.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activate {T}: spawn a token copy of another creature you control
    // -----------------------------------------------------------------------

    /// <summary>
    /// Activate Kiki-Jiki's ability targeting a Grizzly Bears the
    /// controller owns: a 2/2 Bears token with Haste lands on the
    /// battlefield and lacks summoning sickness.
    /// </summary>
    [Fact]
    public void Activate_CreatesHasteTokenCopy_OfTargetCreature()
    {
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice, _zones, triggers: null);
        _zones.MoveCard(kiki, ZoneType.Library, ZoneType.Battlefield, _alice);
        kiki.HasSummoningSickness = false;

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = kiki.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)bears } });
        ability.Resolve();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().ContainSingle();
        var token = tokens[0];
        token.Name.Should().Be("Grizzly Bears", "CR 706.2 — copy uses target's name");
        token.BasePower.Should().Be(2);
        token.BaseToughness.Should().Be(2);
        token.IsToken.Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Haste",
                "the printed 'except it has haste' rider grants Haste even if the original didn't");
        token.HasSummoningSickness.Should().BeFalse(
            "CR 702.10b — Haste lets the token attack the turn it enters");
        token.Controller.Should().BeSameAs(_alice);
    }

    /// <summary>
    /// "Another" target: Kiki-Jiki cannot target itself. Resolve becomes a
    /// no-op (no token minted).
    /// </summary>
    [Fact]
    public void Activate_TargetingSelf_IsNoOp_ResolveTime()
    {
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice, _zones, triggers: null);
        _zones.MoveCard(kiki, ZoneType.Library, ZoneType.Battlefield, _alice);
        kiki.HasSummoningSickness = false;

        var ability = kiki.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)kiki } });
        ability.Resolve();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty(
                "the printed 'another target' restriction blocks self-targeting at resolve");
    }

    /// <summary>
    /// "Nonlegendary" restriction: targeting a legendary creature you
    /// control is a resolve-time no-op (CR 608.2b).
    /// </summary>
    [Fact]
    public void Activate_TargetingLegendaryCreature_IsNoOp_ResolveTime()
    {
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice, _zones, triggers: null);
        _zones.MoveCard(kiki, ZoneType.Library, ZoneType.Battlefield, _alice);
        kiki.HasSummoningSickness = false;

        var legend = new Creature(
            "Captain Sisay", "{1}{W}{W}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Human });
        legend.SetOwner(_alice);
        legend.SetController(_alice);
        _zones.MoveCard(legend, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = kiki.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)legend } });
        ability.Resolve();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty(
                "the printed 'nonlegendary' restriction blocks legendary targets at resolve");
    }

    /// <summary>
    /// "You control" restriction: a creature controlled by an opponent
    /// is not a legal resolve-time target.
    /// </summary>
    [Fact]
    public void Activate_TargetingOpponentCreature_IsNoOp_ResolveTime()
    {
        var bob = new Player("Bob", 20);
        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice, _zones, triggers: null);
        _zones.MoveCard(kiki, ZoneType.Library, ZoneType.Battlefield, _alice);
        kiki.HasSummoningSickness = false;

        var theirs = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        theirs.SetOwner(bob);
        theirs.SetController(bob);
        _zones.MoveCard(theirs, ZoneType.Library, ZoneType.Battlefield, bob);

        var ability = kiki.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)theirs } });
        ability.Resolve();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).Should().BeEmpty(
                "the printed 'you control' restriction blocks opponent's creatures at resolve");
    }

    // -----------------------------------------------------------------------
    // Delayed end-step exile (CR 603.7)
    // -----------------------------------------------------------------------

    /// <summary>
    /// When a <see cref="TriggerManager"/> is supplied, activating
    /// Kiki-Jiki registers a one-shot end-step exile (CR 603.7) on the
    /// spawned token. The next End step fires the trigger, queues it on
    /// the stack, and (on resolve) exiles the token.
    /// </summary>
    [Fact]
    public void Activate_RegistersDelayedEndStepExile_ForSpawnedToken()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var kiki = KikiJikiMirrorBreakerFactory.Create(_alice, _zones, triggers);
        _zones.MoveCard(kiki, ZoneType.Library, ZoneType.Battlefield, _alice);
        kiki.HasSummoningSickness = false;

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);
        bears.SetController(_alice);
        _zones.MoveCard(bears, ZoneType.Library, ZoneType.Battlefield, _alice);

        var ability = kiki.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new[] { new[] { (object)bears } });
        ability.Resolve();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);

        token.Zone.Should().Be(ZoneType.Battlefield);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        token.Zone.Should().Be(ZoneType.Exile, "CR 603.7 — delayed end-step exile fires");
        _alice.Zones.Exile.GetCards().Should().Contain(token);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(token);
    }
}
