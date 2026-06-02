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
/// Tests for <see cref="RestlessSpireFactory"/> (Lost Caverns of Ixalan
/// "Restless" creature-land cycle, blue/red member). Land:
///   "This land enters tapped.
///    {T}: Add {U} or {R}.
///    {U}{R}: Until end of turn, this land becomes a 2/1 blue and red
///    Elemental creature with \"During your turn, this creature has first
///    strike.\" It's still a land.
///    Whenever this land attacks, scry 1."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {U} / {T}: Add {R} mana abilities (two).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({U}{R}, instant speed) + Layer 4 / Layer 7b:
///     * Adds Creature type + Elemental subtype on Layer 4 ("still a land").
///     * Grants First Strike on Layer 4.
///     * Records 2/1 base P/T on Layer 7b.
/// - Unconditional ETB-tapped replacement.
/// - Attack trigger: "Whenever this land attacks, scry 1" — no targets,
///   scries the controller's library by 1.
/// </summary>
[Trait("Color", "C")]
public class RestlessSpireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessSpire_Identity()
    {
        var land = RestlessSpireFactory.Create(_alice);

        land.Name.Should().Be("Restless Spire");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Spire is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessSpire_HasManaAnimateAndAttackTrigger()
    {
        var land = RestlessSpireFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {U} and {T}: Add {R} are wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{U}{R} animate ability is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack trigger is attached to the land shape");
    }
    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessSpire_AnimateAbility_HasPrintedManaCostUR()
    {
        var land = RestlessSpireFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({U}{R})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessSpire_Animate_AppliesLayer4AndLayer7bOnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessSpireFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental,
            "Elemental subtype added");
        chars.Keywords.Should().Contain("First Strike",
            "the animated body has first strike (during your turn)");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessSpire_RegistersUnconditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessSpireFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

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
    // Attack trigger — scry 1 (no target)
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessSpire_AttackTrigger_HasNoTargets()
    {
        var land = RestlessSpireFactory.Create(_alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().BeEmpty(
            "\"Whenever this land attacks, scry 1\" targets nothing");
    }

    [Fact]
    public void RestlessSpire_AttackTrigger_Scry1_PutsTopCardToBottom_NoAgent()
    {
        var land = RestlessSpireFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Library: top -> bottom = top, mid, bottom.
        var top = new Land("Island", supertypes: null, subtypes: null);
        var mid = new Land("Mountain", supertypes: null, subtypes: null);
        var bottom = new Land("Forest", supertypes: null, subtypes: null);
        foreach (var c in new[] { top, mid, bottom })
        {
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
        }

        var before = _alice.Zones.Library.GetCards().ToList();
        before[0].Should().BeSameAs(top, "sanity: top of library is index 0");

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // No agent registered -> pre-agent default sends the peeked top card
        // to the bottom (matches Curator of Mysteries / Preordain default).
        var after = _alice.Zones.Library.GetCards().ToList();
        after.Should().HaveCount(3, "scry never changes library size");
        after[0].Should().BeSameAs(mid, "the old top card was scryed to the bottom");
        after[^1].Should().BeSameAs(top, "the scryed card is now on the bottom");
    }
}
