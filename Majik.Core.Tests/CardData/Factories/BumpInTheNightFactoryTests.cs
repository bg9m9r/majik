using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BumpInTheNightFactory"/> (Innistrad, {B}).
///
/// Scryfall oracle (verbatim):
///   "Target opponent loses 3 life.
///    Flashback {5}{R} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Covers:
/// - Identity ({B} Sorcery).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "target opponent".
/// - Resolve body makes the target opponent LOSE 3 life via
///   <see cref="Primitives.Fx.LoseLife"/> (CR 119 — life loss, NOT damage).
/// - Flashback cost matches the printed {5}{R} (CR 702.34) and exiles the
///   card after a graveyard cast (CR 702.34b), exercised end-to-end through
///   <see cref="SpellCastFlow"/>.
/// </summary>
public class BumpInTheNightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BumpInTheNight_Identity_SorceryAtB()
    {
        var bump = BumpInTheNightFactory.Create(_alice);

        bump.Name.Should().Be("Bump in the Night");
        bump.HasType(CardType.Sorcery).Should().BeTrue();
        bump.ManaCost.ToString().Should().Be("{B}");
        bump.Owner.Should().BeSameAs(_alice);
        bump.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BumpInTheNight_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Bump in the Night", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Bump in the Night");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Spell definition — single "target opponent" request
    // -----------------------------------------------------------------------

    [Fact]
    public void BumpInTheNight_SpellDefinition_HasSingleTargetOpponentRequest()
    {
        var def = BumpInTheNightFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target opponent");
        def.HasVariableX.Should().BeFalse();
    }

    /// <summary>
    /// Resolve body makes the target opponent lose exactly 3 life. CR 119 —
    /// "loses 3 life" is life loss, not damage; routed through
    /// <see cref="Primitives.Fx.LoseLife"/> (NOT <c>DealDamage</c>), so
    /// damage-prevention / lifelink-style replacements never apply.
    /// </summary>
    [Fact]
    public void BumpInTheNight_Resolve_TargetOpponentLosesThreeLife()
    {
        var def = BumpInTheNightFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(17, "Bump in the Night: target opponent loses 3 life");
    }

    // -----------------------------------------------------------------------
    // Flashback cost shape — CR 702.34
    // -----------------------------------------------------------------------

    [Fact]
    public void BumpInTheNight_FlashbackCost_IsFiveGenericPlusRed()
    {
        var cost = BumpInTheNightFactory.BuildFlashbackCost();

        // FlashbackOracleParser.TryParse runs the printed text through
        // ManaCost.Parse("{5}{R}"); the resulting cost round-trips to the
        // parser's canonical "5R" rendering. Compare against the same
        // ManaCost.Parse to stay independent of ToString() formatting and
        // assert the cost is non-trivial (5 generic + 1 red = 6 total).
        cost.AlternativeManaCost.IsZero.Should().BeFalse();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{5}{R}"),
            "printed flashback cost is {5}{R} (CR 702.34)");
    }

    // -----------------------------------------------------------------------
    // End-to-end flashback cast — full SpellCastFlow
    // -----------------------------------------------------------------------

    /// <summary>
    /// End-to-end: cast Bump in the Night from Alice's graveyard via its
    /// {5}{R} flashback cost; on resolution Bob loses 3 life, and the card
    /// is exiled post-resolution (CR 702.34b).
    /// </summary>
    [Fact]
    public async Task BumpInTheNight_FlashbackCast_FullPath_OpponentLosesThree_ThenExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        // Bump in the Night in Alice's graveyard.
        var bump = BumpInTheNightFactory.Create(_alice);
        bump.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bump);

        var def = BumpInTheNightFactory.BuildSpellDefinition(resolver: x => x);
        var altCost = BumpInTheNightFactory.BuildFlashbackCost();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, stack);

        var spell = await flow.CastAsync(
            _alice, bump, def, agent, ctx,
            alternativeCost: altCost);

        // Bump on the stack now (flashback move out of graveyard).
        bump.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Target opponent lost 3 life.
        _bob.LifeTotal.Should().Be(17);

        // CR 702.34b — flashback exiles the card after resolution, NOT graveyard.
        bump.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bump);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bump);
    }
}
