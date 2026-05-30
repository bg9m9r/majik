using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WhirlerRogueFactory"/> (Kaladesh).
///
/// Oracle (verified against Scryfall):
///   "When this creature enters, create two 1/1 colorless Thopter artifact
///    creature tokens with flying.
///    Tap two untapped artifacts you control: Target creature can't be
///    blocked this turn."
///
/// Covers:
/// - Identity ({2}{U}{U}, 2/2, Human Rogue Artificer, NOT an artifact).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB trigger mints TWO 1/1 flying Thopter artifact creature tokens.
/// - "Tap two untapped artifacts" activated ability shape
///   (<see cref="TapTwoUntappedArtifactsCost"/>, single target).
/// - Cost can't be paid with fewer than two untapped artifacts.
/// - Resolution grants the target creature CR 509.1c "can't be blocked"
///   until end of turn (CR 514.2 EOT expiry).
/// - Shape-only / illegal-target paths no-op.
/// </summary>
public class WhirlerRogueFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerRogue_Identity_HumanRogueArtificer()
    {
        var card = WhirlerRogueFactory.Create(_alice);

        card.Name.Should().Be("Whirler Rogue");
        card.ManaCost.ToString().Should().Be("{2}{U}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeFalse(
            "Whirler Rogue is a plain blue creature, not an artifact");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WhirlerRogue_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Whirler Rogue", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Whirler Rogue");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — create two flying Thopters
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerRogue_HasExactlyOneEtbTrigger()
    {
        var card = WhirlerRogueFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB \"create two Thopters\" trigger");
    }

    [Fact]
    public void WhirlerRogue_EtbEffect_MintsTwoFlyingThopterArtifactTokens()
    {
        var alice = new Player("Alice", 20);
        var card = WhirlerRogueFactory.Create(alice);
        // Put Whirler on the battlefield so the ETB source-zone check
        // (CR 603.6c) passes.
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var thopters = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .ToList();

        thopters.Should().HaveCount(2, "ETB creates TWO Thopter tokens");
        thopters.Should().AllSatisfy(t =>
        {
            t.IsToken.Should().BeTrue("CR 111.1 — minted as a token");
            t.BasePower.Should().Be(1);
            t.BaseToughness.Should().Be(1);
            t.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
            t.HasType(CardType.Creature).Should().BeTrue();
            t.HasType(CardType.Artifact).Should().BeTrue(
                "Thopter token is an Artifact Creature (CR 111.1)");
            t.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Flying",
                    "the printed Thopter token has flying (CR 702.9)");
        });
    }

    [Fact]
    public void WhirlerRogue_EtbEffect_NoOpWhenNotOnBattlefield()
    {
        // CR 603.6c — the closure short-circuits when zone != Battlefield.
        var alice = new Player("Alice", 20);
        var card = WhirlerRogueFactory.Create(alice);
        card.SetZone(ZoneType.Hand);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Thopter")
            .Should().BeEmpty("no tokens when Whirler isn't on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Activated ability — tap two artifacts: unblockable
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerRogue_HasExactlyOneActivatedAbility()
    {
        var card = WhirlerRogueFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the tap-two-artifacts: unblockable activation");
    }

    [Fact]
    public void WhirlerRogue_ActivatedAbility_HasTapTwoArtifactsCost()
    {
        var card = WhirlerRogueFactory.Create(_alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        var cost = activated.Costs.OfType<TapTwoUntappedArtifactsCost>().Single();
        cost.Count.Should().Be(2, "Tap two untapped artifacts you control");
    }

    [Fact]
    public void WhirlerRogue_ActivatedAbility_HasSingleCreatureTarget()
    {
        var card = WhirlerRogueFactory.Create(_alice);
        var activated = card.Abilities.OfType<ActivatedAbility>().Single();

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // TapTwoUntappedArtifactsCost behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void TapTwoArtifactsCost_CannotPay_WithOneArtifact()
    {
        var alice = new Player("Alice", 20);
        var artifact = MakeArtifact("Ornithopter", alice);
        alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var cost = new TapTwoUntappedArtifactsCost(2);
        cost.CanPay(alice).Should().BeFalse(
            "only one untapped artifact — can't pay tap-two");
    }

    [Fact]
    public void TapTwoArtifactsCost_CanPay_WithTwoArtifacts_AndTapsBoth()
    {
        var alice = new Player("Alice", 20);
        var a1 = MakeArtifact("Ornithopter", alice);
        var a2 = MakeArtifact("Memnite", alice);
        foreach (var a in new[] { a1, a2 })
        {
            alice.Zones.Battlefield.AddCard(a);
            a.SetZone(ZoneType.Battlefield);
        }

        var cost = new TapTwoUntappedArtifactsCost(2);
        cost.CanPay(alice).Should().BeTrue();

        cost.Pay(alice);

        a1.IsTapped.Should().BeTrue("paying the cost taps the chosen artifacts");
        a2.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TapTwoArtifactsCost_TappedArtifactsAreNotEligible()
    {
        var alice = new Player("Alice", 20);
        var a1 = MakeArtifact("Ornithopter", alice);
        var a2 = MakeArtifact("Memnite", alice);
        foreach (var a in new[] { a1, a2 })
        {
            alice.Zones.Battlefield.AddCard(a);
            a.SetZone(ZoneType.Battlefield);
        }
        a2.Tap(); // only one untapped left

        var cost = new TapTwoUntappedArtifactsCost(2);
        cost.CanPay(alice).Should().BeFalse(
            "a tapped artifact is not an eligible untapped artifact");
    }

    // -----------------------------------------------------------------------
    // Unblockable grant on resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void WhirlerRogue_Activate_AgainstCreature_GrantsCantBeBlockedUntilEot()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var card = WhirlerRogueFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();

        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue("the ability grants the bear unblockable this turn");

        effects.ExpireEndOfTurn();

        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked)
            .Should().BeFalse("the grant is only \"this turn\" (CR 514.2 EOT expiry)");
    }

    [Fact]
    public void WhirlerRogue_Activate_IllegalTarget_NoRestrictionRegistered()
    {
        // A Player is not a Creature → CR 608.2b no-op.
        var effects = new ContinuousEffectsService();
        var card = WhirlerRogueFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        var resolve = () => { foreach (var e in ability.Effects) e.Execute(); };
        resolve.Should().NotThrow();

        var dummy = new Creature("Dummy", "{G}", 1, 1);
        effects.HasRestriction(dummy, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    [Fact]
    public void WhirlerRogue_Activate_NoEffectsService_NoOp()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var card = WhirlerRogueFactory.Create(_alice); // effects = null
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var ability = card.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        var resolve = () => { foreach (var e in ability.Effects) e.Execute(); };
        resolve.Should().NotThrow("the shape-only path silently skips the grant");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Artifact MakeArtifact(string name, Player owner)
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(owner);
        a.SetController(owner);
        return a;
    }
}
