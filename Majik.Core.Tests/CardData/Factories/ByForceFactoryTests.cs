using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for By Force (Modern Horizons, {X}{R}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Destroy X target artifacts."
///
/// Covers:
/// - Identity (name, type, cost, colour) + <see cref="NamedCardFactory"/>
///   dispatch via the embedded JSON (<c>by-force.json</c>).
/// - <see cref="SpellDefinition"/> shape: HasVariableX = true, one
///   open-cardinality target request gathering artifacts on every battlefield
///   (no "you don't control" clause — unlike Vandalblast, By Force can hit the
///   controller's own artifacts).
/// - <see cref="ByForceFactory.Resolve"/> destroys X chosen artifacts
///   (CR 701.7), across both players' battlefields.
/// - No-op for non-artifact targets (CR 608.2b — wrong type).
/// - No-op for a target that left the battlefield before resolution
///   (CR 608.2b).
/// - Clean no-op for X = 0 (empty target list).
/// </summary>
[Trait("Color", "R")]
public class ByForceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeCost_Sorcery_Red()
    {
        var card = ByForceFactory.Create(_alice);

        card.Name.Should().Be("By Force");
        card.ManaCost.Should().Be("{X}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Should().BeOfType<Sorcery>();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_HasVariableX_AndOneOpenTargetRequest()
    {
        var def = ByForceFactory.BuildSpellDefinition();

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(int.MaxValue);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    [Fact]
    public void CandidateGatherer_IncludesArtifactsFromEveryBattlefield()
    {
        // By Force has no "you don't control" clause — the controller's own
        // artifacts are legal targets too (contrast with Vandalblast).
        var aliceArtifact = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");
        var bobArtifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");
        var bobBear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var def = ByForceFactory.BuildSpellDefinition();
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(aliceArtifact);
        candidates.Should().Contain(bobArtifact);
        candidates.Should().NotContain(bobBear,
            because: "By Force targets artifacts only (CR 301)");
    }

    // -----------------------------------------------------------------------
    // Resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysChosenArtifacts_AcrossBothBattlefields()
    {
        var aliceArtifact = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");
        var bobSolRing = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");
        var bobBauble = NewControlledPermanent<Artifact>(_bob, "Mishra's Bauble", "{0}");

        // X = 2 → caller supplies 2 of the 3 artifacts; the third is untouched.
        var destroyed = ByForceFactory.Resolve(new object[] { aliceArtifact, bobSolRing });

        destroyed.Should().HaveCount(2);
        aliceArtifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "By Force can destroy the controller's own artifact (CR 701.7)");
        bobSolRing.Zone.Should().Be(ZoneType.Graveyard);
        bobBauble.Zone.Should().Be(ZoneType.Battlefield,
            because: "an artifact not chosen as a target is untouched");
    }

    [Fact]
    public void Resolve_NonArtifactTarget_Skipped()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        var destroyed = ByForceFactory.Resolve(new object[] { creature, enchantment });

        destroyed.Should().BeEmpty();
        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "By Force targets artifacts only (CR 608.2b)");
        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            because: "By Force targets artifacts only, not enchantments (CR 608.2b)");
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_Skipped()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        var destroyed = ByForceFactory.Resolve(new object[] { artifact });

        destroyed.Should().BeEmpty();
        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    [Fact]
    public void Resolve_NoTargets_CleanNoOp()
    {
        var destroyed = ByForceFactory.Resolve(Array.Empty<object>());
        destroyed.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
