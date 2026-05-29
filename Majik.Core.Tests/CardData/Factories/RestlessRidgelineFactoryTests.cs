using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RestlessRidgelineFactory"/> (Lost Caverns of Ixalan
/// "Restless" creature-land cycle, red/green member). Land:
///   "This land enters tapped.
///    {T}: Add {R} or {G}.
///    {2}{R}{G}: This land becomes a 3/4 red and green Dinosaur creature
///    until end of turn. It's still a land.
///    Whenever this land attacks, another target attacking creature gets
///    +2/+0 until end of turn. Untap that creature."
///
/// Mirrors <see cref="RestlessBivouacFactoryTests"/> / RestlessVinestalkTests:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {R} / {T}: Add {G} mana abilities (two).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({2}{R}{G}, instant speed) + Layer 4 / Layer 7b:
///     * Adds Creature type + Dinosaur subtype on Layer 4 ("still a land").
///     * Records 3/4 base P/T on Layer 7b.
/// - Unconditional ETB-tapped replacement.
/// - Attack trigger: a 1..1 "another target attacking creature" TargetRequest,
///   pumping the chosen creature +2/+0 until end of turn and untapping it.
/// </summary>
public class RestlessRidgelineFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessRidgeline_Identity()
    {
        var land = RestlessRidgelineFactory.Create(_alice);

        land.Name.Should().Be("Restless Ridgeline");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Ridgeline is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessRidgeline_HasManaAnimateAndAttackTrigger()
    {
        var land = RestlessRidgelineFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {R} and {T}: Add {G} are wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}{R}{G} animate ability is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack trigger is attached to the land shape");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RestlessRidgeline()
    {
        var card = NamedCardFactory.Create("Restless Ridgeline", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Restless Ridgeline");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessRidgeline_AnimateAbility_HasPrintedManaCost2RG()
    {
        var land = RestlessRidgelineFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2}{R}{G})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessRidgeline_Animate_AppliesLayer4AndLayer7bOnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessRidgelineFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Dinosaur,
            "Dinosaur subtype added");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional ("This land enters tapped.")
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessRidgeline_RegistersUnconditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessRidgelineFactory.Create(
            _alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "\"This land enters tapped\" — unconditional (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "another target attacking creature gets +2/+0 ...
    //                   Untap that creature."
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessRidgeline_AttackTrigger_RequestsOneOtherTargetCreature()
    {
        var land = RestlessRidgelineFactory.Create(_alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().HaveCount(1,
            "the attack trigger needs one 'another target attacking creature'");
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1, "the target is mandatory (not 'up to one')");
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void RestlessRidgeline_AttackTrigger_PumpsAndUntapsChosenCreature()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessRidgelineFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A tapped attacker to receive +2/+0 and an untap.
        var raptor = new Creature("Ripjaw Raptor", "{2}{G}", 4, 5);
        raptor.SetOwner(_alice);
        raptor.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(raptor);
        raptor.SetZone(ZoneType.Battlefield);
        raptor.Tap();
        raptor.IsTapped.Should().BeTrue("attacker is tapped before the trigger resolves");

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { raptor }
        });
        foreach (var e in trigger.Effects) e.Execute();

        var chars = effects.Compute(raptor);
        chars.Power.Should().Be(6, "base 4 power + 2 from the +2/+0 pump");
        chars.Toughness.Should().Be(5, "+2/+0 leaves toughness unchanged");
        raptor.IsTapped.Should().BeFalse(
            "\"Untap that creature\" untaps the pumped attacker");

        // The pump expires at end of turn (CR 514.2 cleanup); the untap does not revert.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(raptor);
        after.Power.Should().Be(4, "the +2/+0 pump expired at end of turn");
        after.Toughness.Should().Be(5);
    }

    [Fact]
    public void RestlessRidgeline_AttackTrigger_AlreadyUntappedTarget_DoesNotThrow()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessRidgelineFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A vigilance-style attacker that is NOT tapped — "Untap that creature"
        // must be a no-op rather than throwing (Permanent.Untap throws if untapped).
        var vigilant = new Creature("Watcher", "{2}{W}", 3, 3);
        vigilant.SetOwner(_alice);
        vigilant.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(vigilant);
        vigilant.SetZone(ZoneType.Battlefield);
        vigilant.IsTapped.Should().BeFalse();

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { vigilant }
        });

        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow("untapping an already-untapped creature is a no-op");

        effects.Compute(vigilant).Power.Should().Be(5, "+2/+0 still applies");
    }
}
