using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Unholy Heat (Modern Horizons 2, {R}, Instant).
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve with 0 card types in controller's graveyard → 2 damage.
///   - Resolve with 3 card types (below threshold) → 2 damage.
///   - Resolve with 4 card types (threshold met) → 4 damage.
///   - Resolve with 5 card types (threshold exceeded) → still 4 damage.
///
/// Delirium (CR 702.105) is a state check at resolution: count distinct
/// <see cref="CardType"/> values across cards in the controller's
/// graveyard. Threshold is 4.
/// </summary>
public class UnholyHeatTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public UnholyHeatTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void UnholyHeat_IsInstant_AtCostR()
    {
        var uh = UnholyHeatFactory.Create(_alice);

        uh.Name.Should().Be("Unholy Heat");
        uh.ManaCost.Should().Be("{R}");
        uh.HasType(CardType.Instant).Should().BeTrue();
        uh.Owner.Should().BeSameAs(_alice);
        uh.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UnholyHeat()
    {
        var card = NamedCardFactory.Create("Unholy Heat", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Unholy Heat");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — delirium gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UnholyHeat_NoCardTypesInGraveyard_Deals2Damage()
    {
        var bobStarting = _bob.LifeTotal;

        await CastAndResolveTargeting(_bob);

        // Empty graveyard → delirium inactive → base 2 damage.
        _bob.LifeTotal.Should().Be(bobStarting - 2);
    }

    [Fact]
    public async Task UnholyHeat_ThreeCardTypesInGraveyard_Deals2Damage()
    {
        // Below threshold (3 < 4) — still base damage.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 2);
    }

    [Fact]
    public async Task UnholyHeat_FourCardTypesInGraveyard_Deals4Damage()
    {
        // Exactly threshold (4) — delirium active → 4 damage.
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 4);
    }

    [Fact]
    public async Task UnholyHeat_FiveCardTypesInGraveyard_StillDeals4Damage()
    {
        // Above threshold (5) — delirium still active → 4 damage
        // (no scaling beyond the threshold).
        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact },
            new[] { CardType.Enchantment });

        var bobStarting = _bob.LifeTotal;
        await CastAndResolveTargeting(_bob);

        _bob.LifeTotal.Should().Be(bobStarting - 4);
    }

    [Fact]
    public void IsDeliriumActive_GraveyardTypeCounting_MatchesThreshold()
    {
        UnholyHeatFactory.IsDeliriumActive(_alice).Should().BeFalse(
            "empty graveyard has 0 distinct card types");

        SeedAliceGraveyard(
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });
        UnholyHeatFactory.IsDeliriumActive(_alice).Should().BeFalse(
            "3 distinct types is below the 4-type threshold");

        var artifactCard = new Card("Sol Ring", "1", new[] { CardType.Artifact });
        artifactCard.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(artifactCard);
        UnholyHeatFactory.IsDeliriumActive(_alice).Should().BeTrue(
            "4 distinct types satisfies delirium (CR 702.105)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Drop one card per supplied type-set into Alice's graveyard. Each
    /// inner array becomes one card's <see cref="CardType"/> set —
    /// distinctness is per-card-type across the union (mirrors the
    /// counting helper Tarmogoyf uses).
    /// </summary>
    private void SeedAliceGraveyard(params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"Seed{i++}", "0", types);
            card.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(card);
        }
    }

    /// <summary>
    /// Cast Unholy Heat from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// RiftBoltTests cast harness — direct cast/resolve, no priority
    /// loop.
    /// </summary>
    private async Task CastAndResolveTargeting(object target)
    {
        var uh = UnholyHeatFactory.Create(_alice);
        uh.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(uh);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, uh,
            UnholyHeatFactory.BuildSpellDefinition(_alice, t => t),
            agent, ctx);

        uh.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }
}
