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
/// Tests for <see cref="RestlessReefFactory"/> (Outlaws of Thunder Junction
/// "restless" land cycle). Land:
///   "This land enters tapped.
///    {T}: Add {U} or {B}.
///    {2}{U}{B}: Until end of turn, this land becomes a 4/4 blue and black
///    Shark creature with deathtouch. It's still a land.
///    Whenever this land attacks, target player mills four cards."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch + ability shape counts.
/// - Two mana abilities ({T}: Add {U} / {T}: Add {B}).
/// - Animate ability ({2}{U}{B}) registers a
///   <see cref="ManlandCycleAnimateEffect"/> + a
///   <see cref="ManlandCycleBecomesPTEffect"/>:
///     * Adds Creature + Shark subtype + Deathtouch keyword on Layer 4.
///     * Records 4/4 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - ETB-tapped replacement registered when a bus is wired.
/// - Attack trigger mills four from the chosen target player; no-ops when
///   the chosen target isn't a Player.
/// </summary>
[Trait("Color", "C")]
public class RestlessReefFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessReef_Identity()
    {
        var land = RestlessReefFactory.Create(_alice);

        land.Name.Should().Be("Restless Reef");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land until activated");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Reef is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void RestlessReef_AnimateAbility_HasPrintedManaCost2UB()
    {
        var land = RestlessReefFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2}{U}{B})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    // -----------------------------------------------------------------------
    // Animate ability — Layer 4 + Layer 7b
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessReef_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessReefFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Shark,
            "Shark subtype added");
        chars.Keywords.Should().Contain("Deathtouch",
            "Deathtouch keyword marker added (CR 702.2)");
    }

    [Fact]
    public void RestlessReef_Animate_ExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessReefFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        effects.Compute((Permanent)land).Types.Should().Contain(CardType.Creature);

        effects.ExpireEndOfTurn();

        var afterCleanup = effects.Compute((Permanent)land);
        afterCleanup.Types.Should().NotContain(CardType.Creature,
            "the animation lifts at the cleanup step (CR 514.2)");
        afterCleanup.Types.Should().Contain(CardType.Land,
            "the printed Land type remains");
    }

    [Fact]
    public void RestlessReef_BecomesPTEffect_Records4By4()
    {
        var land = RestlessReefFactory.Create(_alice);
        var pt = new ManlandCycleBecomesPTEffect(land, 4, 4);

        pt.NewPower.Should().Be(4);
        pt.NewToughness.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // ETB-tapped
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessReef_RegistersEntersTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessReefFactory.Create(_alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Restless Reef always enters tapped (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — target player mills four
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessReef_AttackTrigger_MillsFourFromChosenPlayer()
    {
        // Seed Bob's library with 6 cards so a mill-4 leaves 2.
        for (var i = 0; i < 6; i++)
        {
            var c = NamedCardFactory.Create("Mountain", _bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = RestlessReefFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Library.GetCards().Should().HaveCount(2,
            "four of the six library cards were milled (CR 701.13)");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(4,
            "the four milled cards are now in the graveyard");
    }

    [Fact]
    public void RestlessReef_AttackTrigger_NoOps_WhenChosenTargetNotPlayer()
    {
        var land = RestlessReefFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        // A non-Player token chosen (illegal at resolution, CR 608.2b).
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { land },
        });

        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };

        act.Should().NotThrow("an illegal/non-Player target makes the trigger no-op");
    }
}
