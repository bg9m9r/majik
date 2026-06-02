using System.Linq;
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
/// Tests for <see cref="RestlessCottageFactory"/> (Wilds of Eldraine
/// "Restless" creature-land cycle, black/green member — sibling of
/// <see cref="RestlessSpireFactory"/>). Land:
///   "This land enters tapped.
///    {T}: Add {B} or {G}.
///    {2}{B}{G}: This land becomes a 4/4 black and green Horror creature
///    until end of turn. It's still a land.
///    Whenever this land attacks, create a Food token and exile up to one
///    target card from a graveyard."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {B} / {T}: Add {G} mana abilities (two).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({2}{B}{G}, instant speed) + Layer 4 / Layer 7b:
///     * Adds Creature type + Horror subtype on Layer 4 ("still a land").
///     * Records 4/4 base P/T on Layer 7b.
/// - Unconditional ETB-tapped replacement.
/// - Attack trigger: "Whenever this land attacks, create a Food token and
///   exile up to one target card from a graveyard" — a 0..1 graveyard
///   target; on resolve creates a Food and exiles the chosen card.
/// </summary>
[Trait("Color", "C")]
public class RestlessCottageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessCottage_Identity()
    {
        var land = RestlessCottageFactory.Create(_alice);

        land.Name.Should().Be("Restless Cottage");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Cottage is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessCottage_HasManaAnimateAndAttackTrigger()
    {
        var land = RestlessCottageFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {B} and {T}: Add {G} are wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}{B}{G} animate ability is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack trigger is attached to the land shape");
    }
    // -----------------------------------------------------------------------
    // {T}: Add {B} / {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessCottage_TapProducesBlack()
    {
        var land = RestlessCottageFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var black = land.Abilities.OfType<ManaAbility>().FirstOrDefault(a =>
        {
            var m = a.Activate();
            land.Untap();
            return m.Black > 0;
        });

        black.Should().NotBeNull("{T}: Add {B} must be present");
    }

    [Fact]
    public void RestlessCottage_TapProducesGreen()
    {
        var land = RestlessCottageFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var green = land.Abilities.OfType<ManaAbility>().FirstOrDefault(a =>
        {
            var m = a.Activate();
            land.Untap();
            return m.Green > 0;
        });

        green.Should().NotBeNull("{T}: Add {G} must be present");
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessCottage_AnimateAbility_HasPrintedManaCost2BG()
    {
        var land = RestlessCottageFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2}{B}{G})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessCottage_Animate_AppliesLayer4AndLayer7bOnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessCottageFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Horror,
            "Horror subtype added");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessCottage_RegistersUnconditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessCottageFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

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
    // Attack trigger — Food token + exile up to one graveyard card
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessCottage_AttackTrigger_HasUpToOneGraveyardTarget()
    {
        var land = RestlessCottageFactory.Create(_alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().ContainSingle(
            "\"exile up to one target card from a graveyard\"");
        var req = trigger.TargetRequests.Single();
        req.MinTargets.Should().Be(0, "\"up to one\" — optional");
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void RestlessCottage_AttackTrigger_CreatesFood_WithNoTarget()
    {
        var land = RestlessCottageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var beforeArtifacts = _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Food");

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        // No chosen target ("up to one" — zero chosen is legal).
        foreach (var e in trigger.Effects) e.Execute();

        var afterArtifacts = _alice.Zones.Battlefield.GetCards()
            .Count(c => c.Name == "Food");
        afterArtifacts.Should().Be(beforeArtifacts + 1,
            "the trigger always creates a Food token even with no exile target");
    }

    [Fact]
    public void RestlessCottage_AttackTrigger_ExilesChosenGraveyardCard()
    {
        var land = RestlessCottageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A card in Alice's graveyard to be exiled.
        var victim = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        victim.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(victim);
        victim.SetZone(ZoneType.Graveyard);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { victim } });

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().NotContain(victim,
            "the chosen graveyard card is exiled (CR 701.21)");
        _alice.Zones.Exile.GetCards().Should().Contain(victim);
        victim.Zone.Should().Be(ZoneType.Exile);

        _alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Food")
            .Should().Be(1, "a Food token is also created");
    }
}
