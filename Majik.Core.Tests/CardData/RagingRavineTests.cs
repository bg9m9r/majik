using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RagingRavineFactory"/> (Worldwake creature-land
/// cycle). Land:
///   "This land enters tapped.
///    {T}: Add {R} or {G}.
///    {2}{R}{G}: Until end of turn, this land becomes a 3/3 red and green
///    Elemental creature with \"Whenever this creature attacks, put a
///    +1/+1 counter on it.\" It's still a land."
///   (Oracle verified against Scryfall 2026-05-29.)
///
/// Same Worldwake-manland shape as <see cref="StirringWildwoodFactory"/> /
/// <see cref="NeedleSpiresFactory"/> (unconditional ETB-tapped, a two-colour
/// mana ability modelled as two <see cref="ManaAbility"/> instances, and a
/// {cost}: animate-until-EOT activated ability registering
/// <see cref="ManlandCycleAnimateEffect"/> (Layer 4) +
/// <see cref="ManlandCycleBecomesPTEffect"/> (Layer 7b)).
///
/// Distinct from the rest of the cycle: the animated body grants an
/// intrinsic triggered ability — "Whenever this creature attacks, put a
/// +1/+1 counter on it." (CR 508.1f). v1 wires this as a
/// <see cref="TriggeredAbility"/> (<see cref="Triggers.OnAttackSelf"/> →
/// add one <see cref="CounterType.PlusOnePlusOne"/> counter) registered at
/// animate-resolution against the supplied <see cref="TriggerManager"/>,
/// matching the Reckoner Bankbuster / Territorial Kavu attack-trigger shape.
/// </summary>
public class RagingRavineTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RagingRavine_Identity()
    {
        var land = RagingRavineFactory.Create(_alice);

        land.Name.Should().Be("Raging Ravine");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land until activated");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Raging Ravine is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RagingRavine()
    {
        var card = NamedCardFactory.Create("Raging Ravine", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Raging Ravine");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {R} / {T}: Add {G}");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "the {2}{R}{G} animate ability");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {R} or {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void RagingRavine_TapForR_ProducesRed()
    {
        var land = RagingRavineFactory.Create(_alice);
        var red = land.Abilities.OfType<ManaAbility>().First();

        red.CanActivate().Should().BeTrue();
        var produced = red.Activate();

        produced.Red.Should().Be(1);
        produced.Green.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void RagingRavine_TapForG_ProducesGreen()
    {
        var land = RagingRavineFactory.Create(_alice);
        var green = land.Abilities.OfType<ManaAbility>().Skip(1).First();

        green.CanActivate().Should().BeTrue();
        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.Red.Should().Be(0);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Animate ability ({2}{R}{G})
    // -----------------------------------------------------------------------

    [Fact]
    public void RagingRavine_AnimateAbility_HasPrintedManaCost2RG()
    {
        var land = RagingRavineFactory.Create(_alice);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2}{R}{G})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RagingRavine_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RagingRavineFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental,
            "Elemental subtype added");
    }

    [Fact]
    public void RagingRavine_Animate_RegistersLayer4AndLayer7b_EotExpiring()
    {
        var effects = new ContinuousEffectsService();
        var land = RagingRavineFactory.Create(_alice, effects, replacements: null, triggers: null);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.Subtypes.Should().Contain(CardSubtype.Elemental);

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == 3 && e.NewToughness == 3);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();
    }

    [Fact]
    public void RagingRavine_Animate_EndOfTurnExpiration_RevertsLand()
    {
        var effects = new ContinuousEffectsService();
        var land = RagingRavineFactory.Create(_alice, effects, replacements: null, triggers: null);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .Where(e => ReferenceEquals(e.Target, land))
            .Should().BeEmpty();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Elemental);
    }

    [Fact]
    public void RagingRavine_Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        var land = RagingRavineFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var resolve = () => AnimateOf(land).Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Granted attack trigger — "Whenever this creature attacks, put a
    // +1/+1 counter on it." (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void RagingRavine_Animate_GrantsAttackTrigger_WhenTriggerManagerWired()
    {
        var effects = new ContinuousEffectsService();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack());
        var land = RagingRavineFactory.Create(_alice, effects, replacements: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        // The land now carries the granted attack trigger.
        var attackTrigger = land.Abilities
            .OfType<TriggeredAbility>()
            .SingleOrDefault();
        attackTrigger.Should().NotBeNull(
            "animation grants the \"Whenever this creature attacks\" trigger");
    }

    [Fact]
    public void RagingRavine_AttackTrigger_PutsPlusOnePlusOneCounter()
    {
        var effects = new ContinuousEffectsService();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack());
        var land = RagingRavineFactory.Create(_alice, effects, replacements: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        AnimateOf(land).Resolve();

        var attackTrigger = land.Abilities.OfType<TriggeredAbility>().Single();
        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        // Resolve the attack trigger's effect: a +1/+1 counter lands on it.
        foreach (var fx in attackTrigger.Effects)
        {
            fx.Execute();
        }

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the attack trigger puts a +1/+1 counter on the animated land");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped (unconditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void RagingRavine_RegistersEntersTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RagingRavineFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Raging Ravine always enters tapped (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ActivatedAbility AnimateOf(Land land) =>
        land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
