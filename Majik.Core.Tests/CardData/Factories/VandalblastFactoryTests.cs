using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Vandalblast (Return to Ravnica / Modern Masters 2017, {R},
/// Sorcery).
///
/// Oracle text:
///   "Destroy target artifact you don't control.
///    Overload {4}{R} (You may cast this spell for its overload cost. If you
///    do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Destroy each artifact you don't control."
///
/// Covers:
///   - Card identity (Sorcery, {R}, Red, owner/controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: single 1..1 "target artifact you don't control"
///     request; candidate gatherer excludes the controller's own artifacts
///     (CR 109.5 — "you" = the spell's controller).
///   - Default (not overloaded) resolve → destroys one targeted artifact the
///     controller does NOT control (CR 701.7).
///   - No-op if target is a creature/enchantment (wrong type — CR 608.2b).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
///   - Structural overloaded branch → destroys EACH artifact the controller
///     does NOT control; the controller's own artifacts + non-artifacts are
///     untouched (CR 702.96b).
///
/// Overload (CR 702.96) is an alternative cost. Per <c>MODERN_COVERAGE.md</c>
/// and the <see cref="MizziumMortarsFactory"/> analogue, the
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive is a stub
/// not yet plumbed through <see cref="Majik.Core.Services.SpellCastFlow"/>, so
/// production casts ship not-overloaded. The overloaded branch is exercised
/// here by passing <c>wasOverloaded: true</c> through the spell-definition
/// builder directly (same posture as Mizzium Mortars).
/// </summary>
[Trait("Color", "R")]
public class VandalblastFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Vandalblast_HasSorceryShape_Red_AtCostR()
    {
        var card = VandalblastFactory.Create(_alice);

        card.Name.Should().Be("Vandalblast");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape (default / not overloaded)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetArtifactRequest()
    {
        var def = VandalblastFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    [Fact]
    public void CandidateGatherer_ExcludesControllersOwnArtifacts()
    {
        // CR 109.5 / oracle "you don't control": only opponents' artifacts
        // are legal candidates.
        var bobArtifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");
        var aliceArtifact = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");

        var def = VandalblastFactory.BuildDefinition(_alice, o => o);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobArtifact);
        candidates.Should().NotContain(aliceArtifact,
            because: "Vandalblast targets an artifact you DON'T control (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Default (not overloaded) resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysTargetedOpponentArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact, wasOverloaded: false);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Vandalblast destroys target artifact you don't control (CR 701.7)");
    }

    [Fact]
    public void DoesNotDestroy_ControllersOwnArtifact()
    {
        // Even if (illegally) resolved against the controller's own artifact,
        // the "you don't control" clause is re-checked at resolution.
        var aliceArtifact = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");

        Resolve(aliceArtifact, wasOverloaded: false);

        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "Vandalblast cannot destroy an artifact you control (CR 109.5)");
    }

    [Fact]
    public void TargetCreature_DoesNothing()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature, wasOverloaded: false);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Vandalblast targets artifacts only (CR 608.2b)");
    }

    [Fact]
    public void TargetEnchantment_DoesNothing()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        Resolve(enchantment, wasOverloaded: false);

        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            because: "Vandalblast targets artifacts only, not enchantments (CR 608.2b)");
    }

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact, wasOverloaded: false);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Overloaded branch (structural — CR 702.96b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Overloaded_DestroysEachArtifact_YouDontControl()
    {
        // Bob (opponent) artifacts — all destroyed.
        var bobSolRing = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");
        var bobBauble = NewControlledPermanent<Artifact>(_bob, "Mishra's Bauble", "{0}");

        // Alice (controller) artifacts — spared (CR 702.96b "each artifact you
        // don't control"; the controller is the "you" per CR 109.5).
        var aliceMindStone = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");

        // Non-artifact permanent on opponent's side — must not be hit.
        var bobBear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var def = VandalblastFactory.BuildDefinition(
            controller: _alice,
            targetResolver: o => o,
            allPlayers: new[] { _alice, _bob },
            wasOverloaded: true);

        // No targets — overloaded branch carries no TargetRequests
        // (CR 702.96b — "target" is rewritten to "each").
        def.TargetRequests.Count.Should().Be(0);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bobSolRing.Zone.Should().Be(ZoneType.Graveyard, "opponent artifacts are destroyed");
        bobBauble.Zone.Should().Be(ZoneType.Graveyard, "opponent artifacts are destroyed");
        aliceMindStone.Zone.Should().Be(ZoneType.Battlefield,
            "the controller's own artifacts are spared (CR 109.5)");
        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "non-artifact permanents are untouched");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target, bool wasOverloaded)
    {
        var def = VandalblastFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, wasOverloaded);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

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
