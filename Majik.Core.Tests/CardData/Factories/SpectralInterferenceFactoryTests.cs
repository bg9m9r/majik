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
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Spectral Interference (Modern Horizons 3, {1}{U}).
/// Oracle (verified against Scryfall):
///   "Counter target artifact or creature spell unless its controller pays {4}."
///
/// The "soft counter unless pays" rider of Miscalculation combined with the
/// type-restricted target of Exclude/Annul ("artifact or creature spell").
///
/// Coverage (unique behaviour only — dispatch + well-formedness are asserted
/// for every implemented card by CardFactoryContractTests):
///   * Identity: Instant {1}{U}, blue.
///   * SpellDefinition shape (1 target, "artifact or creature").
///   * Counters a creature spell when controller can't pay {4} (CR 701.5).
///   * Counters an artifact spell when controller can't pay {4}.
///   * Controller pays {4} → no counter (CR 118.4 "unless" cost).
///   * Non-artifact/-creature spell target → no-op at resolution (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class SpectralInterferenceFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpectralInterferenceFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Blue_OneU()
    {
        var card = SpectralInterferenceFactory.Create(_alice);

        card.Name.Should().Be("Spectral Interference");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCost.Should().Be("{1}{U}");
        card.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleArtifactOrCreatureTargetRequest()
    {
        var def = SpectralInterferenceFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    // -----------------------------------------------------------------------
    // Counter unless pay {4}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountersCreatureSpell_WhenControllerCannotPayFour()
    {
        var card = SpectralInterferenceFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob has 0 mana → cannot pay {4}.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        await CastAtAsync(card, bobSpell);
        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {4} so the creature spell is countered");
    }

    [Fact]
    public async Task CountersArtifactSpell_WhenControllerCannotPayFour()
    {
        var card = SpectralInterferenceFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var bobArtifact = new Artifact("Sol Ring", "{1}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobArtifact, _bob);
        _stack.Push(bobSpell);

        await CastAtAsync(card, bobSpell);
        _resolver.ResolveTop(_stack);

        bobArtifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {4} so the artifact spell is countered");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysFour()
    {
        var card = SpectralInterferenceFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(4));

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        await CastAtAsync(card, bobSpell);
        _resolver.ResolveTop(_stack);

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {4} so Spectral Interference no-ops (CR 118.4)");
    }

    [Fact]
    public async Task DoesNotCounter_NonArtifactNonCreatureSpell()
    {
        var card = SpectralInterferenceFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob casts an instant — neither artifact nor creature, even with 0 mana.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        await CastAtAsync(card, bobSpell);
        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "an instant is not an artifact or creature spell — no-op (CR 608.2b)");
    }

    private async Task CastAtAsync(Instant card, Majik.Core.Spells.Spell target)
    {
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)target });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, card,
            SpectralInterferenceFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);
    }
}
