using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MagmaticHellkiteFactory"/> — Creature — Dragon
/// {2}{R}{R} 4/5 (Tarkir: Dragonstorm). Oracle (Scryfall, verified 2026-06-24):
///   "Flying
///    When this creature enters, destroy target nonbasic land an opponent
///    controls. Its controller searches their library for a basic land card,
///    puts it onto the battlefield tapped with a stun counter on it, then
///    shuffles. (If a permanent with a stun counter would become untapped,
///    remove one from it instead.)"
///
/// Covers ONLY the card's unique behaviour (its ETB ability) plus a single
/// identity assert; <see cref="CardFactoryContractTests"/> already covers
/// NamedCardFactory dispatch + well-formedness automatically.
///   - Identity: name / {2}{R}{R} / Creature — Dragon / 4/5 + Flying marker.
///   - ETB target gatherer: only NONBASIC lands an OPPONENT controls
///     (excludes own lands and the opponent's basics, CR 109.5 / CR 305.6).
///   - ETB resolution: destroys the chosen nonbasic land (CR 701.7), then ITS
///     CONTROLLER (the opponent) searches their library for a basic, puts it
///     onto the battlefield TAPPED with one stun counter (CR 701.18 / 122.1c),
///     then shuffles.
///   - Illegal target at resolution = clean no-op (CR 608.2b) — no tutor.
/// </summary>
[Trait("Color", "R")]
public class MagmaticHellkiteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land Basic(string name, CardSubtype sub) =>
        new(name, supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { sub });

    private static Land Nonbasic(Player owner, string name)
    {
        var land = new Land(name);
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MagmaticHellkite_IsDragon_4_5_AtCost2RR_WithFlying()
    {
        var c = MagmaticHellkiteFactory.Create(_alice);

        c.Name.Should().Be("Magmatic Hellkite");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    // -----------------------------------------------------------------------
    // ETB trigger — targeting (CR 109.5 + CR 305.6)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TargetGatherer_OffersOnlyNonbasicLandsAnOpponentControls()
    {
        var hellkite = MagmaticHellkiteFactory.Create(_alice);
        hellkite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hellkite);

        var myNonbasic = Nonbasic(_alice, "My Wasteland");          // own — excluded
        var theirNonbasic = Nonbasic(_bob, "Bojuka Bog");           // legal target
        var theirBasic = Basic("Island", CardSubtype.Island);       // basic — excluded
        theirBasic.SetOwner(_bob);
        theirBasic.SetController(_bob);
        theirBasic.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(theirBasic);

        var etb = hellkite.Abilities.OfType<TriggeredAbility>().Single();
        var request = etb.TargetRequests.Single();

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.CandidateGatherer!(ctx);

        candidates.Should().Contain(theirNonbasic, "Bob's nonbasic land is a legal target.");
        candidates.Should().NotContain(myNonbasic, "CR 109.5 — Alice's own lands aren't legal.");
        candidates.Should().NotContain(theirBasic, "CR 305.6 — a basic land isn't 'nonbasic'.");
    }

    // -----------------------------------------------------------------------
    // ETB resolution (CR 701.7 destroy + tutor basic to bf tapped + stunned)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_DestroysTargetLand_AndItsControllerTutorsBasicTappedStunned()
    {
        var hellkite = MagmaticHellkiteFactory.Create(_alice);

        // Bob controls a nonbasic land (the target) and has a basic in library.
        var target = Nonbasic(_bob, "Bojuka Bog");
        var forest = Basic("Forest", CardSubtype.Forest);
        forest.SetOwner(_bob);
        forest.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(forest);

        var etb = hellkite.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        etb.Resolve();

        // CR 701.7 — the nonbasic land is destroyed to its owner's graveyard.
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        target.Zone.Should().Be(ZoneType.Graveyard);

        // "Its controller" (Bob) tutors the basic onto the battlefield TAPPED
        // with one stun counter (CR 701.18 / CR 122.1c), then shuffles.
        _bob.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue("CR 701.18 — the basic enters tapped.");
        forest.Counters.Count(CounterType.Stun).Should().Be(1,
            "CR 122.1c — the basic enters with a stun counter on it.");
        _bob.Zones.Library.GetCards().Should().NotContain(forest);

        // The Hellkite's controller (Alice) does NOT search — the rider is the
        // land controller's search, not the Hellkite controller's.
        _alice.Zones.Battlefield.GetCards().OfType<Land>().Should().BeEmpty();
    }

    [Fact]
    public void Etb_TargetLeftBattlefield_NoOp_NoTutor()
    {
        var hellkite = MagmaticHellkiteFactory.Create(_alice);

        // Target already in graveyard (illegal at resolution).
        var target = new Land("Bojuka Bog");
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var forest = Basic("Forest", CardSubtype.Forest);
        forest.SetOwner(_bob);
        forest.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(forest);

        var etb = hellkite.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var act = () => etb.Resolve();

        act.Should().NotThrow("CR 608.2b — illegal target at resolution is a clean no-op.");
        // Single-target ability with an illegal target ⇒ the tutor rider is
        // suppressed ("its controller" has no referent).
        _bob.Zones.Battlefield.GetCards().Should().NotContain(forest);
        _bob.Zones.Library.GetCards().Should().Contain(forest);
    }

    [Fact]
    public void Etb_BasicTarget_IsIllegal_NoOp()
    {
        // CR 305.6 — a basic land is not "nonbasic"; even if somehow chosen, the
        // resolution-time supertype guard rejects it.
        var hellkite = MagmaticHellkiteFactory.Create(_alice);

        var basic = Basic("Island", CardSubtype.Island);
        basic.SetOwner(_bob);
        basic.SetController(_bob);
        basic.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(basic);

        var etb = hellkite.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { basic } });

        etb.Resolve();

        _bob.Zones.Battlefield.GetCards().Should().Contain(basic,
            "CR 608.2b — a basic land is an illegal target; nothing is destroyed.");
    }
}
