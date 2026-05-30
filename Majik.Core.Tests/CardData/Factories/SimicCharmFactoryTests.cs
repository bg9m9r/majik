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
/// Unit tests for <see cref="SimicCharmFactory"/>.
///
/// Card: Simic Charm — Instant {G}{U} (Gatecrash).
///   CR 700.2d — modal "Choose one —" spell with 3 modes.
///   Mode 0: "Target creature gets +3/+3 until end of turn."
///   Mode 1: "Permanents you control gain hexproof until end of turn."
///   Mode 2: "Return target creature to its owner's hand."
///
/// Covers:
///   - Identity: name, Instant type, Green+Blue colours, mana value 2.
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape: 3 modes, 3 TargetRequests (all MinTargets=0).
///   - Mode 0 resolve: +3/+3 until EOT via PumpUntilEndOfTurnEffect; expires.
///   - Mode 1 resolve: grants Hexproof-until-EOT to every creature the caster
///     controls (asserted via ContinuousEffectsService); enemy creatures are
///     unaffected; the grant expires at end of turn.
///   - Mode 2 resolve: bounce target creature to its owner's hand.
/// </summary>
public class SimicCharmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SimicCharm_Create_HasInstantShape_GreenBlue()
    {
        var card = SimicCharmFactory.Create(_alice);

        card.Name.Should().Be("Simic Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{G}{U} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SimicCharm_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Simic Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Simic Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SimicCharm_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = SimicCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        def.Modes.Should().HaveCount(3);
        def.Modes[SimicCharmFactory.ModePump].Should().Contain("+3/+3");
        def.Modes[SimicCharmFactory.ModeHexproof].Should().Contain("hexproof");
        def.Modes[SimicCharmFactory.ModeBounce].Should().Contain("owner's hand");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[SimicCharmFactory.ModePump].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[SimicCharmFactory.ModePump].MaxTargets.Should().Be(1);
        def.TargetRequests[SimicCharmFactory.ModeHexproof].MinTargets.Should().Be(0,
            because: "mode 1 has no target — MinTargets must be 0");
        def.TargetRequests[SimicCharmFactory.ModeHexproof].MaxTargets.Should().Be(0);
        def.TargetRequests[SimicCharmFactory.ModeBounce].MinTargets.Should().Be(0,
            because: "CR 700.2d / 601.2c — unchosen mode slots must not gate the cast");
        def.TargetRequests[SimicCharmFactory.ModeBounce].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 0: target creature gets +3/+3 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void SimicCharm_Mode0_TargetCreatureGetsPlus3Plus3_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ActiveEffects = svc;

        var def = SimicCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bear },     // mode 0 — creature target
            Array.Empty<object>(),     // mode 1 (unused)
            Array.Empty<object>(),     // mode 2 (unused)
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: SimicCharmFactory.ModePump,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        var pumped = svc.Compute(bear);
        pumped.Power.Should().Be(5, because: "2/2 + 3 = 5 power until EOT");
        pumped.Toughness.Should().Be(5, because: "2/2 + 3 = 5 toughness until EOT");

        // CR 514.2 — the pump expires in the cleanup step.
        svc.ExpireEndOfTurn();
        var afterCleanup = svc.Compute(bear);
        afterCleanup.Power.Should().Be(2, because: "the +3/+3 grant expires at end of turn (CR 514.2)");
        afterCleanup.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Mode 1: permanents you control gain hexproof until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void SimicCharm_Mode1_GrantsHexproofToControllersCreatures()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var enemy = new Creature("Goblin", "{R}", 1, 1);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);
        enemy.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.ActiveEffects = svc;

        var def = SimicCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: SimicCharmFactory.ModeHexproof,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        svc.Compute(ally).Keywords.Should().Contain("Hexproof",
            because: "mode 1 grants hexproof to all permanents Alice controls");
        svc.Compute(enemy).Keywords.Should().NotContain("Hexproof",
            because: "mode 1 only affects permanents the caster controls");
    }

    [Fact]
    public void SimicCharm_Mode1_HexproofExpires_EndOfTurn()
    {
        var svc = new ContinuousEffectsService();

        var ally = new Creature("Bear", "{1}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        ally.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ally);
        ally.ActiveEffects = svc;

        var def = SimicCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, svc);

        var chosen = new ChosenSpellParams(
            ModeIndex: SimicCharmFactory.ModeHexproof,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
        svc.Compute(ally).Keywords.Should().Contain("Hexproof");

        // CR 514.2 — EOT cleanup expires the grant.
        svc.ExpireEndOfTurn();

        svc.Compute(ally).Keywords.Should().NotContain("Hexproof",
            because: "Hexproof grant expires at end of turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Mode 2: return target creature to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void SimicCharm_Mode2_BouncesTargetCreature_ToOwnersHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = SimicCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bear },  // mode 2 — target creature
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: SimicCharmFactory.ModeBounce,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand,
            because: "mode 2 returns the target creature to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void SimicCharm_Mode2_NonCreatureTarget_NoOp()
    {
        var land = new Land("Forest") { Owner = _bob, Controller = _bob };
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var def = SimicCharmFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { land },  // raw-target a land — should no-op
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: SimicCharmFactory.ModeBounce,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Simic Charm mode 2 only targets creatures — a land no-ops.
        land.Zone.Should().Be(ZoneType.Battlefield,
            because: "Simic Charm mode 2 can only bounce a creature");
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
        _bob.Zones.Hand.GetCards().Should().NotContain(land);
    }
}
