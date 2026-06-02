using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="KarakasFactory"/> — Legendary Land with
/// {T}: Add {W} and {T}: Return target legendary creature to owner's hand.
///
/// Covers:
/// - Card identity (Legendary Land, name).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {W} mana ability.
/// - Bounce a target legendary creature → owner's hand.
/// - Bounce a non-legendary creature → no-op (CR 608.2b illegal target).
/// - Bounce a controller-owned legendary creature → works (Karakas's own
///   controller can bounce their own legendary creature; useful for
///   protect-from-removal plays).
/// </summary>
public class KarakasTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Karakas_IsLegendaryLand()
    {
        var land = KarakasFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        land.Name.Should().Be("Karakas");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Karakas()
    {
        var card = NamedCardFactory.Create("Karakas", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Karakas");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {W}
    // -----------------------------------------------------------------------

    [Fact]
    public void Karakas_HasWhiteManaAbility_AndActivationTapsLandAndProducesW()
    {
        var land = KarakasFactory.Create(_alice);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        produced.White.Should().Be(1);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Return target legendary creature to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Karakas_HasBounceActivatedAbility_WithSingleTargetRequest()
    {
        var land = KarakasFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("legendary creature");
    }

    [Fact]
    public void Karakas_Bounce_LegendaryCreature_ReturnsToOwnersHand()
    {
        // Bob controls a legendary creature; Alice taps Karakas to bounce it.
        var legendary = new Creature(
            name: "Emrakul, the Aeons Torn",
            manaCost: "{15}",
            power: 15,
            toughness: 15,
            supertypes: new[] { CardSupertype.Legendary });
        legendary.SetOwner(_bob);
        legendary.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(legendary);
        legendary.SetZone(ZoneType.Battlefield);

        var land = KarakasFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();

        // Simulate the agent picking the legendary creature target.
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { legendary },
        });

        activated.Resolve();

        // Legendary creature is now in Bob's hand.
        _bob.Zones.Hand.GetCards().Should().Contain(legendary);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(legendary);
        legendary.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Karakas_Bounce_NonLegendaryCreature_IsNotALegalTarget()
    {
        // CR 608.2b — Karakas's bounce targets only a "legendary creature".
        // The JSON return_to_hand effect declares a legendary_creature target
        // filter whose CandidateGatherer enumerates the legal picks; a plain
        // (non-legendary) creature is never offered to the activating player's
        // agent, so it can never become the chosen target.
        var bears = new Creature(
            name: "Grizzly Bears",
            manaCost: "{1}{G}",
            power: 2,
            toughness: 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var legend = new Creature(
            name: "Thalia, Guardian of Thraben",
            manaCost: "{1}{W}",
            power: 2,
            toughness: 1,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(_bob);
        legend.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(legend);
        legend.SetZone(ZoneType.Battlefield);

        var land = KarakasFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        var request = activated.TargetRequests.Should().ContainSingle().Subject;

        var ctx = new Majik.Core.Game.GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(legend, "a legendary creature is a legal Karakas target");
        candidates.Should().NotContain(bears, "a non-legendary creature is not a legal Karakas target (CR 608.2b)");
    }

    [Fact]
    public void Karakas_Bounce_OwnLegendaryCreature_Works()
    {
        // Alice can target her own legendary creature — the bounce is
        // not owner-restricted. Practical use: save a legendary creature
        // from removal by returning it to your own hand.
        var thalia = new Creature(
            name: "Thalia, Guardian of Thraben",
            manaCost: "{1}{W}",
            power: 2,
            toughness: 1,
            supertypes: new[] { CardSupertype.Legendary });
        thalia.SetOwner(_alice);
        thalia.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(thalia);
        thalia.SetZone(ZoneType.Battlefield);

        var land = KarakasFactory.Create(_alice);
        var activated = land.Abilities.OfType<ActivatedAbility>().Single();

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { thalia },
        });

        activated.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(thalia);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(thalia);
        thalia.Zone.Should().Be(ZoneType.Hand);
    }
}
