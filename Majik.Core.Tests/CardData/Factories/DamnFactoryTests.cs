using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Damn (Modern Horizons 2, {B}{B}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target creature. A creature destroyed this way can't be
///    regenerated.
///    Overload {2}{W}{W} (You may cast this spell for its overload cost. If
///    you do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Destroy each creature. A creature destroyed this way can't be
///    regenerated." — i.e. a Damnation-style board wipe.
///
/// Covers:
///   - Card identity (Sorcery, {B}{B}, Black, owner/controller) materialised
///     from the embedded JSON definition.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: single 1..1 "target creature" request.
///   - Default (not overloaded) resolve → destroys one targeted creature with
///     the "can't be regenerated" rider (CR 701.7 / 701.15).
///   - No-op if target is a non-creature or has left the battlefield
///     (CR 608.2b).
///   - Structural overloaded branch → destroys EACH creature on every
///     battlefield (CR 702.96b); non-creature permanents survive.
///
/// Overload (CR 702.96) is an alternative cost. Per <c>MODERN_COVERAGE.md</c>
/// and the <see cref="VandalblastFactory"/> / <see cref="MizziumMortarsFactory"/>
/// analogues, the <see cref="Majik.Core.Costs.OverloadAlternativeCost"/>
/// primitive is a stub not yet plumbed through
/// <see cref="Majik.Core.Services.SpellCastFlow"/>, so production casts ship
/// not-overloaded. The overloaded branch is exercised here by passing
/// <c>wasOverloaded: true</c> through the spell-definition builder directly.
/// </summary>
public class DamnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Damn_HasSorceryShape_Black_AtCostBB()
    {
        var card = DamnFactory.Create(_alice);

        card.Name.Should().Be("Damn");
        card.ManaCost.Should().Be("{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDamnShape()
    {
        var dispatched = NamedCardFactory.Create("Damn", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Damn");
        dispatched.ManaCost.Should().Be("{B}{B}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape (default / not overloaded)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetCreatureRequest()
    {
        var def = DamnFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Default (not overloaded) resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysTargetedCreature_MovesToGraveyard()
    {
        var creature = SeedCreature(_bob, "Grizzly Bears");

        Resolve(creature, wasOverloaded: false);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Damn destroys target creature (CR 701.7)");
    }

    [Fact]
    public void TargetArtifact_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact, wasOverloaded: false);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "Damn targets creatures only (CR 608.2b)");
    }

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var creature = SeedCreature(_bob, "Grizzly Bears");

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature, wasOverloaded: false);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Overloaded branch (structural — CR 702.96b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Overloaded_DestroysEachCreature_OnEveryBattlefield()
    {
        var aliceBear = SeedCreature(_alice, "Alice-Bear");
        var bobBear = SeedCreature(_bob, "Bob-Bear");
        var bobWolf = SeedCreature(_bob, "Bob-Wolf");

        // Non-creature permanents on both sides — must survive the wipe.
        var aliceArtifact = NewControlledPermanent<Artifact>(_alice, "Mind Stone", "{2}");
        var bobLand = NewControlledPermanent<Land>(_bob, "Swamp", "");

        var def = DamnFactory.BuildDefinition(
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

        aliceBear.Zone.Should().Be(ZoneType.Graveyard, "every creature is destroyed");
        bobBear.Zone.Should().Be(ZoneType.Graveyard, "every creature is destroyed");
        bobWolf.Zone.Should().Be(ZoneType.Graveyard, "every creature is destroyed");
        aliceArtifact.Zone.Should().Be(ZoneType.Battlefield, "non-creature permanents survive");
        bobLand.Zone.Should().Be(ZoneType.Battlefield, "non-creature permanents survive");
    }

    [Fact]
    public void Overloaded_EmptyBattlefields_IsCleanNoOp()
    {
        var def = DamnFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, wasOverloaded: true);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        var act = () => { foreach (var fx in def.EffectFactory(chosen)) fx.Execute(); };

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target, bool wasOverloaded)
    {
        var def = DamnFactory.BuildDefinition(
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

    private static Creature SeedCreature(Player owner, string name)
        => NewControlledPermanent<Creature>(owner, name, "{1}{G}", 2, 2);

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
        else if (typeof(T) == typeof(Land))
        {
            card = (T)(ICard)new Land(name);
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
