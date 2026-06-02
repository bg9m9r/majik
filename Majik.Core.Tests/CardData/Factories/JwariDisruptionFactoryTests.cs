using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="JwariDisruptionFactory"/> and
/// <see cref="JwariRuinsFactory"/> — the front + back faces of the Zendikar
/// Rising modal double-faced card Jwari Disruption // Jwari Ruins.
///
/// Front face (Jwari Disruption, {1}{U}):
///   Instant. "Counter target spell unless its controller pays {1}."
///
/// Back face (Jwari Ruins):
///   Land. "This land enters tapped." "{T}: Add {U}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: SpellDefinition shape (1 target spell request).
/// - Front: counter when controller cannot pay {1} → spell to graveyard (CR 701.5).
/// - Front: no-op when controller auto-pays {1} (CR 118.4).
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {U} mana ability.
/// - Back: unconditional enters-tapped replacement.
/// </summary>
[Trait("Color", "U")]
public class JwariDisruptionFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public JwariDisruptionFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void JwariDisruption_Identity_1U_Instant()
    {
        var card = JwariDisruptionFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Jwari Disruption");
        card.ManaCost.Should().Be("{1}{U}");
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void JwariDisruption_IsBlue()
    {
        var card = JwariDisruptionFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue, "the {U} pip makes it blue");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Black);
    }

    [Fact]
    public void JwariDisruption_CarriesMdfcState_FrontFace()
    {
        var card = JwariDisruptionFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Jwari Disruption is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Jwari Disruption");
        card.MdfcState!.BackFaceName.Should().Be("Jwari Ruins");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Jwari Disruption");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = JwariDisruptionFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    // =========================================================================
    // Front face — resolve counter
    // =========================================================================

    [Fact]
    public async Task Counters_TargetSpell_WhenControllerCannotPayOne()
    {
        var jwari = JwariDisruptionFactory.Create(_alice);
        jwari.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(jwari);

        // Bob has 0 mana → cannot pay {1} → Jwari Disruption counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, jwari,
            JwariDisruptionFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {1} so Jwari Disruption counters his spell");
    }

    [Fact]
    public async Task DoesNotCounter_WhenControllerPaysOne()
    {
        var jwari = JwariDisruptionFactory.Create(_alice);
        jwari.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(jwari);

        // Bob has {1} available in his mana pool — he auto-pays the rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, jwari,
            JwariDisruptionFactory.BuildSpellDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {1} so Jwari Disruption is countered into a no-op (CR 118.4)");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void JwariRuins_Identity_Land()
    {
        var land = JwariRuinsFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Jwari Ruins");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Jwari Ruins is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void JwariRuins_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = JwariRuinsFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Jwari Ruins is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Jwari Disruption");
        land.MdfcState!.BackFaceName.Should().Be("Jwari Ruins");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Jwari Ruins");
    }

    [Fact]
    public void JwariRuins_HasSingleManaAbility_AddingBlue()
    {
        var land = JwariRuinsFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {U} ability");
        manaAbilities[0].ManaGenerated.Blue.Should().BeGreaterThan(0, "produces blue mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Black.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    [Fact]
    public void JwariRuins_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = JwariRuinsFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Jwari Ruins always enters tapped (CR 614.1c) — no opt-out");
    }
}
