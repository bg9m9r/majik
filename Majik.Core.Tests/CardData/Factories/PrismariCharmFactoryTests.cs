using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PrismariCharmFactory"/>.
///
/// Card: Prismari Charm — Instant {U}{R} (Strixhaven: School of Mages).
///   CR 700.2d — modal "Choose one —" spell with 3 modes.
///   Mode 0 — Surveil 2, then draw a card.
///   Mode 1 — Prismari Charm deals 1 damage to each of one or two targets.
///   Mode 2 — Return target nonland permanent to its owner's hand.
///
/// Covers:
///   - Identity: name, Instant type, Blue+Red colours, mana value 2.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: 3 modes, 3 TargetRequests (all MinTargets=0;
///     mode 1 MaxTargets=2).
///   - Mode 0 resolve: surveil 2 (default all-to-graveyard) then draws 1.
///   - Mode 1 resolve: 1 damage to a single target.
///   - Mode 1 resolve: 1 damage EACH to two targets (Player + Creature).
///   - Mode 2 resolve: bounce target nonland permanent (Creature) to hand.
///   - Mode 2 resolve: land is NOT a legal target — no-op.
/// </summary>
public class PrismariCharmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismariCharm_Create_HasInstantShape_BlueRed()
    {
        var card = PrismariCharmFactory.Create(_alice);

        card.Name.Should().Be("Prismari Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{U}{R} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PrismariCharm_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Prismari Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Prismari Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismariCharm_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = PrismariCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        def.Modes.Should().HaveCount(3);
        def.Modes[PrismariCharmFactory.ModeSurveilDraw].Should().Contain("Surveil 2");
        def.Modes[PrismariCharmFactory.ModeSurveilDraw].Should().Contain("draw a card");
        def.Modes[PrismariCharmFactory.ModeDamage].Should().Contain("1 damage");
        def.Modes[PrismariCharmFactory.ModeDamage].Should().Contain("one or two targets");
        def.Modes[PrismariCharmFactory.ModeBounce].Should().Contain("nonland permanent");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[PrismariCharmFactory.ModeSurveilDraw].MinTargets.Should().Be(0,
            because: "mode 0 has no target");
        def.TargetRequests[PrismariCharmFactory.ModeSurveilDraw].MaxTargets.Should().Be(0);
        def.TargetRequests[PrismariCharmFactory.ModeDamage].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[PrismariCharmFactory.ModeDamage].MaxTargets.Should().Be(2,
            because: "mode 1 takes one OR two targets");
        def.TargetRequests[PrismariCharmFactory.ModeBounce].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[PrismariCharmFactory.ModeBounce].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 0: surveil 2, then draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismariCharm_Mode0_Surveils2_AndDraws1()
    {
        // Library has 4 cards. Default (no agent registered) all-to-graveyard:
        // top 2 → graveyard, then draw the 3rd into hand.
        var top1 = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var top2 = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        var top3 = new Instant("Lava Spike", "{R}") { Owner = _alice };
        var top4 = new Instant("Brainstorm", "{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top1);
        _alice.Zones.Library.AddCard(top2);
        _alice.Zones.Library.AddCard(top3);
        _alice.Zones.Library.AddCard(top4);

        // Ensure no agent is registered so we get the deterministic
        // all-to-graveyard fallback path (mirrors LibrarySpellFactory).
        AgentRegistry.Clear();

        var def = PrismariCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), // mode 0 — no target
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: PrismariCharmFactory.ModeSurveilDraw,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // top1 + top2 surveilled to graveyard (all-to-grave fallback).
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { top1, top2 },
            because: "surveil 2 with no agent moves both peeked cards to the graveyard");
        // top3 drawn into hand.
        _alice.Zones.Hand.GetCards().Should().Contain(top3,
            because: "after surveil 2, the next card is drawn into hand");
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        // top4 remains in library.
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(top4);
    }

    // -----------------------------------------------------------------------
    // Mode 1: 1 damage to one target
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismariCharm_Mode1_OneTarget_Deals1Damage()
    {
        var def = PrismariCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _bob },   // mode 1 — single target
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: PrismariCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, because: "mode 1 deals 1 damage to the chosen target");
    }

    // -----------------------------------------------------------------------
    // Mode 1: 1 damage to EACH of two targets (Player + Creature)
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismariCharm_Mode1_TwoTargets_Deals1ToEach()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = PrismariCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _bob, bear },   // mode 1 — two targets
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: PrismariCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // 1 to each — NOT divided; each takes the full 1.
        _bob.LifeTotal.Should().Be(19,
            because: "mode 1 deals 1 damage to EACH chosen target (not divided)");
        bear.Damage.Should().Be(1,
            because: "mode 1 deals 1 damage to EACH chosen target (not divided)");
    }

    // -----------------------------------------------------------------------
    // Mode 2: bounce target nonland permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismariCharm_Mode2_BouncesNonlandPermanent_ToOwnersHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = PrismariCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bear },  // mode 2 — target nonland permanent
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: PrismariCharmFactory.ModeBounce,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand,
            because: "mode 2 returns the target nonland permanent to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void PrismariCharm_Mode2_LandIsNotLegalTarget_NoOp()
    {
        var land = new Land("Mountain") { Owner = _bob, Controller = _bob };
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var def = PrismariCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { land },  // raw-target a land — should no-op
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: PrismariCharmFactory.ModeBounce,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Disperse-style: lands are NOT legal targets — clean no-op.
        land.Zone.Should().Be(ZoneType.Battlefield,
            because: "Prismari Charm mode 2 cannot bounce a land (nonland restriction)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
        _bob.Zones.Hand.GetCards().Should().NotContain(land);
    }
}
