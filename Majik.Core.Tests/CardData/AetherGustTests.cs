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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Aether Gust (Core Set 2020, {1}{U}).
/// Oracle: "Choose target spell or permanent that's red or green. Its owner
/// puts it on the top or bottom of their library."
///
/// Coverage:
///   * Card shape + dispatch by name.
///   * Target a red spell on the stack with topChooser=false → spell removed
///     and card moved to the bottom of its owner's library.
///   * Target a green permanent on the battlefield with topChooser=true →
///     permanent removed from battlefield and placed on top of its owner's
///     library.
///   * Target a blue spell → illegal at resolution (CR 608.2b), no-op.
///   * Target a white (non-creature, non-spell) permanent → illegal at
///     resolution (CR 608.2b), no-op.
/// </summary>
public class AetherGustTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AetherGustTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var gust = AetherGustFactory.Create(_alice);

        gust.Name.Should().Be("Aether Gust");
        gust.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(gust).Should().Contain(ManaColor.Blue);
        gust.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAetherGustShape()
    {
        var dispatched = NamedCardFactory.Create("Aether Gust", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Aether Gust");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public async Task TargetingRedSpellOnStack_ToBottom_MovesCardToLibraryBottom()
    {
        // Aether Gust in Alice's hand.
        var gust = AetherGustFactory.Create(_alice);
        gust.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gust);

        // Bob has a couple cards in his library so we can verify Lightning
        // Bolt ends up on the BOTTOM (not the top).
        var bobLibraryTopper = new Card("Filler A", "");
        bobLibraryTopper.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobLibraryTopper);

        // Bob casts Lightning Bolt {R} — a red spell on the stack.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        bobBolt.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, gust,
            AetherGustFactory.BuildDefinition(o => o, _stack, topChooser: _ => false),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Stack cleared of the Lightning Bolt.
        _stack.Count.Should().Be(0);

        // Bolt now in Bob's library, at the BOTTOM.
        bobBolt.Zone.Should().Be(ZoneType.Library);
        var library = _bob.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(2);
        library[0].Should().BeSameAs(bobLibraryTopper, because: "the pre-existing card stays on top");
        library[^1].Should().BeSameAs(bobBolt, because: "Aether Gust placed Bolt on the bottom");
    }

    [Fact]
    public async Task TargetingGreenPermanentOnBattlefield_ToTop_MovesPermanentToLibraryTop()
    {
        var gust = AetherGustFactory.Create(_alice);
        gust.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gust);

        // Bob already has one card in library — we want Tarmogoyf to land
        // ABOVE it (index 0).
        var bobLibraryFloor = new Card("Filler B", "");
        bobLibraryFloor.SetOwner(_bob);
        _bob.Zones.Library.AddCard(bobLibraryFloor);

        // Bob controls a green Tarmogoyf on the battlefield.
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob);
        goyf.SetController(_bob);
        goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)goyf });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, gust,
            AetherGustFactory.BuildDefinition(o => o, _stack, topChooser: _ => true),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Goyf gone from the battlefield, now in Bob's library, on TOP.
        _bob.Zones.Battlefield.ContainsCard(goyf).Should().BeFalse();
        goyf.Zone.Should().Be(ZoneType.Library);

        var library = _bob.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(2);
        library[0].Should().BeSameAs(goyf, because: "Aether Gust placed Tarmogoyf on top");
        library[1].Should().BeSameAs(bobLibraryFloor);
    }

    [Fact]
    public async Task TargetingBlueSpellOnStack_IsNoOp_AtResolution()
    {
        var gust = AetherGustFactory.Create(_alice);
        gust.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gust);

        // Bob casts Counterspell {U}{U} — a blue spell. Aether Gust requires
        // a red OR green target; blue is illegal at resolution time.
        var bobCounterspell = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        bobCounterspell.SetZone(ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bobCounterspell, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, gust,
            AetherGustFactory.BuildDefinition(o => o, _stack, topChooser: _ => false),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // CR 608.2b — illegal target (blue, not red/green) → effect does
        // nothing. The Counterspell remains where it was; Aether Gust does
        // NOT move it to Bob's library.
        bobCounterspell.Zone.Should().NotBe(ZoneType.Library);
        _bob.Zones.Library.ContainsCard(bobCounterspell).Should().BeFalse();
    }

    [Fact]
    public async Task TargetingWhitePermanent_IsNoOp_AtResolution()
    {
        var gust = AetherGustFactory.Create(_alice);
        gust.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(gust);

        // Bob controls a white non-creature, non-spell permanent (an
        // enchantment): Sigil of the Empty Throne {3}{W}{W}. White is
        // neither red nor green — illegal target at resolution.
        var sigil = new Enchantment("Sigil of the Empty Throne", "{3}{W}{W}");
        sigil.SetOwner(_bob);
        sigil.SetController(_bob);
        sigil.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(sigil);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)sigil });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, gust,
            AetherGustFactory.BuildDefinition(o => o, _stack, topChooser: _ => true),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        // Sigil stays on the battlefield; nothing in Bob's library.
        _bob.Zones.Battlefield.ContainsCard(sigil).Should().BeTrue();
        sigil.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Library.Count.Should().Be(0);
    }

    [Fact]
    public void BuildDefinition_DefaultsToBottom_WhenNoChooserSupplied()
    {
        // Shape-only assertion: the SpellDefinition carries exactly one
        // 1..1 target request, no modes, no variable X. This mirrors the
        // SpellSnare shape test.
        var def = AetherGustFactory.BuildDefinition(o => o, _stack);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }
}
