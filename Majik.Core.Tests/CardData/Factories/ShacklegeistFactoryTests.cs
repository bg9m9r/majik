using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShacklegeistFactory"/> — Creature — Spirit {1}{U} 2/2:
///   "Flying
///    This creature can block only creatures with flying.
///    Tap two untapped Spirits you control: Tap target creature you don't control."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller)
///     materialised from the embedded JSON definition.
///   - Flying keyword presence (CR 702.9).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ShacklegeistTapAbility"/> attached, costed by the
///     tap-two-Spirits cost (no mana, no self-tap symbol).
///   - CanActivate / cost gate: false with fewer than two untapped Spirits.
///   - No summoning-sickness gate on the cost (CR 302.6 N/A — the cost is the
///     word "Tap", not a {T} symbol).
///   - Resolution taps the chosen opponent creature (CR 701.21), re-checking
///     legality at resolution (CR 608.2b).
///   - The opponent-scoped candidate gatherer excludes the controller's own
///     creatures (CR 109.5 — "you don't control").
///
/// The printed "This creature can block only creatures with flying" rider is
/// deferred — see <see cref="ShacklegeistFactory"/> XML doc (no "can only block
/// X" combat primitive yet, identical to Brazen Borrower).
/// </summary>
[Trait("Color", "U")]
public class ShacklegeistFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>Add an untapped Spirit to <paramref name="owner"/>'s
    /// battlefield (summoning-sick by default, mirroring Permanent's default
    /// state).</summary>
    private static Creature AddSpirit(Player owner, string name)
    {
        var spirit = new Creature(name, "{U}", 1, 1, subtypes: new[] { CardSubtype.Spirit });
        spirit.SetOwner(owner);
        spirit.SetController(owner);
        spirit.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(spirit);
        return spirit;
    }

    private static Creature AddVanilla(Player owner, string name)
    {
        var c = new Creature(name, "{1}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Shacklegeist_IsSpirit_2_2_AtCost1U()
    {
        var c = ShacklegeistFactory.Create(_alice);

        c.Name.Should().Be("Shacklegeist");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Shacklegeist_HasFlying()
    {
        var c = ShacklegeistFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Flying");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Shacklegeist()
    {
        var card = NamedCardFactory.Create("Shacklegeist", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Shacklegeist");
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Abilities.OfType<ShacklegeistTapAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Shacklegeist_HasSingleTapAbility()
    {
        var c = ShacklegeistFactory.Create(_alice);

        c.Abilities.OfType<ShacklegeistTapAbility>().Should().HaveCount(1,
            "Shacklegeist prints one activated ability: "
            + "\"Tap two untapped Spirits you control: Tap target creature you don't control.\"");
    }

    // -----------------------------------------------------------------------
    // Cost gate (CR 602.2b — tap two untapped Spirits you control)
    // -----------------------------------------------------------------------

    [Fact]
    public void TapCost_CannotPay_WithFewerThanTwoUntappedSpirits()
    {
        var geist = ShacklegeistFactory.Create(_alice);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        // Only Shacklegeist itself is a Spirit (one untapped Spirit total).

        var ability = geist.Abilities.OfType<ShacklegeistTapAbility>().Single();
        ability.TapChoice.CanPay(_alice).Should().BeFalse(
            "the cost requires two untapped Spirits; only Shacklegeist is one.");
    }

    [Fact]
    public void TapCost_CanPay_WithShacklegeistPlusOneSpirit_DespiteSummoningSickness()
    {
        // CR 302.6 only restricts a creature tapping ITSELF via a {T} symbol in
        // an activation cost. Shacklegeist's cost is the word "Tap" on a set of
        // Spirits — so summoning-sick Spirits are still eligible bodies.
        var geist = ShacklegeistFactory.Create(_alice);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        AddSpirit(_alice, "Sick Spirit"); // summoning-sick by default

        var ability = geist.Abilities.OfType<ShacklegeistTapAbility>().Single();
        ability.TapChoice.CanPay(_alice).Should().BeTrue(
            "two untapped Spirits (Shacklegeist + one) exist; the cost is not gated on summoning sickness.");
    }

    [Fact]
    public void TapCost_Pay_TapsTwoChosenSpirits()
    {
        var geist = ShacklegeistFactory.Create(_alice);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        var s1 = AddSpirit(_alice, "Spirit A");
        var s2 = AddSpirit(_alice, "Spirit B");

        var ability = geist.Abilities.OfType<ShacklegeistTapAbility>().Single();
        ability.TapChoice.Targets = new[] { s1, s2 };
        ability.TapChoice.Pay(_alice);

        s1.IsTapped.Should().BeTrue();
        s2.IsTapped.Should().BeTrue();
        geist.IsTapped.Should().BeFalse(
            "Shacklegeist has no {T} in its cost — it stays untapped when not one of the two chosen.");
    }

    // -----------------------------------------------------------------------
    // Targeting + resolution (CR 109.5 / 608.2b / 701.21)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetGatherer_OffersOnlyCreaturesYouDontControl()
    {
        var geist = ShacklegeistFactory.Create(_alice);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);

        var myCreature = AddVanilla(_alice, "My Bear");
        var theirCreature = AddVanilla(_bob, "Their Bear");

        var ability = geist.Abilities.OfType<ShacklegeistTapAbility>().Single();
        var request = ability.TargetRequests.Single();

        var ctx = new Majik.Core.Game.GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var candidates = request.CandidateGatherer!(ctx);

        candidates.Should().Contain(theirCreature, "Bob's creature is one Alice doesn't control.");
        candidates.Should().NotContain(myCreature, "CR 109.5 — Alice's own creatures aren't targetable.");
        candidates.Should().NotContain(geist, "Shacklegeist is controlled by Alice — not a legal target.");
    }

    [Fact]
    public void Resolve_TapsTheChosenOpponentCreature()
    {
        var geist = ShacklegeistFactory.Create(_alice);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);

        var theirCreature = AddVanilla(_bob, "Their Bear");
        theirCreature.IsTapped.Should().BeFalse();

        var ability = geist.Abilities.OfType<ShacklegeistTapAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { theirCreature } });

        ability.Resolve();

        theirCreature.IsTapped.Should().BeTrue("CR 701.21 — the chosen opponent creature is tapped.");
    }

    [Fact]
    public void Resolve_DoesNothing_WhenTargetIsControllersOwnCreature()
    {
        // Defensive CR 109.5 re-check at resolution: a target that is (now)
        // controlled by the ability's controller is left untouched.
        var geist = ShacklegeistFactory.Create(_alice);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);

        var myCreature = AddVanilla(_alice, "My Bear");

        var ability = geist.Abilities.OfType<ShacklegeistTapAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { myCreature } });

        ability.Resolve();

        myCreature.IsTapped.Should().BeFalse(
            "CR 109.5 — \"you don't control\" excludes the controller's own creatures.");
    }
}
