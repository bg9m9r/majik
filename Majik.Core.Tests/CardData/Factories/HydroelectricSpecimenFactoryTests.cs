using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Spell = Majik.Core.Spells.Spell;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HydroelectricSpecimenFactory"/> and
/// <see cref="HydroelectricLaboratoryFactory"/> — the front + back faces of
/// the Modern Horizons 3 modal double-faced card
/// Hydroelectric Specimen // Hydroelectric Laboratory.
///
/// Front face (Hydroelectric Specimen, {2}{U}):
///   Creature — Weird 1/4.
///   "Flash
///    When this creature enters, you may change the target of target instant
///    or sorcery spell with a single target to this creature."
///
/// Back face (Hydroelectric Laboratory):
///   Land. "As this land enters, you may pay 3 life. If you don't, it enters
///   tapped." "{T}: Add {U}."
///
/// Covers:
/// - Front identity (Weird 1/4 {2}{U}, blue) + Flash + dispatch + MdfcState.
/// - Front ETB: redirects a single-target instant/sorcery spell to itself.
/// - Front ETB: no-op when no target chosen / multi-target spell / non-spell /
///   permanent (creature) spell.
/// - Back identity (Land, non-basic, {T}: Add {U}) + dispatch + MdfcState.
/// - Back ETB: pay 3 life → untapped; decline → tapped; can't afford → tapped.
/// </summary>
[Trait("Color", "U")]
public class HydroelectricSpecimenFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public HydroelectricSpecimenFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void Specimen_Identity_CreatureWeird_1_4_Blue2U()
    {
        var c = HydroelectricSpecimenFactory.Create(_alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Hydroelectric Specimen");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Land).Should().BeFalse();
        c.ManaCost.Should().Be("{2}{U}");
        c.ManaCostValue.TotalValue.Should().Be(3);
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(4);
        c.Subtypes.Should().Contain(CardSubtype.Weird);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Specimen_IsBlue()
    {
        var c = HydroelectricSpecimenFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "the {U} pip makes it blue");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Black);
        colors.Should().NotContain(ManaColor.Green);
    }

    [Fact]
    public void Specimen_HasFlashKeyword()
    {
        var c = HydroelectricSpecimenFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Hydroelectric Specimen has Flash (CR 702.8)");
    }

    [Fact]
    public void Specimen_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Hydroelectric Specimen", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Hydroelectric Specimen");
    }

    [Fact]
    public void Specimen_HasMdfcState_WithCastableLandBackFace()
    {
        var c = HydroelectricSpecimenFactory.Create(_alice);

        c.MdfcState.Should().NotBeNull();
        c.MdfcState!.FrontFaceName.Should().Be("Hydroelectric Specimen");
        c.MdfcState.BackFaceName.Should().Be("Hydroelectric Laboratory");
        c.MdfcState.IsBackFace.Should().BeFalse("the creature is the front face");
        c.MdfcState.CastableBackFace.Should().NotBeNull();
        c.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        c.MdfcState.CastableBackFace.Name.Should().Be("Hydroelectric Laboratory");
    }

    [Fact]
    public void Specimen_HasSingleEtbTrigger_BattlefieldActive_OptionalSingleTarget()
    {
        var c = HydroelectricSpecimenFactory.Create(_alice);

        var trig = c.Abilities.OfType<TriggeredAbility>().Single();
        trig.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trig.TargetRequests.Should().ContainSingle();
        // "you may" → 0..1 target.
        trig.TargetRequests[0].MinTargets.Should().Be(0);
        trig.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // =========================================================================
    // Front face — ETB redirect
    // =========================================================================

    private static Spell SingleTargetInstant(Player controller, object originalTarget)
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = controller, Controller = controller };
        bolt.SetZone(ZoneType.Stack);
        var spell = new Spell(bolt, controller);
        spell.ChosenTargets.Add(originalTarget);
        return spell;
    }

    [Fact]
    public void Etb_RedirectsSingleTargetInstant_ToThisCreature()
    {
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var specimen = HydroelectricSpecimenFactory.Create(_alice, stack);
        var etb = specimen.Abilities.OfType<TriggeredAbility>().Single();

        // Bob's bolt currently targets Alice; it's on the stack.
        var spell = SingleTargetInstant(_bob, originalTarget: _alice);
        stack.Push(spell);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });
        foreach (var eff in etb.Effects) eff.Execute();

        // CR 114.6 — the bolt's single chosen target is now the Specimen.
        spell.ChosenTargets.Should().ContainSingle();
        spell.ChosenTargets[0].Should().BeSameAs(specimen,
            "the ETB changed the spell's target to Hydroelectric Specimen");
    }

    [Fact]
    public void Etb_RedirectsSingleTargetSorcery_ToThisCreature()
    {
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var specimen = HydroelectricSpecimenFactory.Create(_alice, stack);
        var etb = specimen.Abilities.OfType<TriggeredAbility>().Single();

        var sorcery = new Sorcery("Doom Blade", "{1}{B}") { Owner = _bob, Controller = _bob };
        sorcery.SetZone(ZoneType.Stack);
        var spell = new Spell(sorcery, _bob);
        spell.ChosenTargets.Add(_alice);
        stack.Push(spell);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });
        foreach (var eff in etb.Effects) eff.Execute();

        spell.ChosenTargets[0].Should().BeSameAs(specimen,
            "a single-target sorcery is redirected to the Specimen too");
    }

    [Fact]
    public void Etb_NoTargetChosen_IsCleanNoOp()
    {
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var specimen = HydroelectricSpecimenFactory.Create(_alice, stack);
        var etb = specimen.Abilities.OfType<TriggeredAbility>().Single();

        var spell = SingleTargetInstant(_bob, originalTarget: _alice);
        stack.Push(spell);

        // "you may" — chose no target.
        etb.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());

        var act = () => { foreach (var eff in etb.Effects) eff.Execute(); };
        act.Should().NotThrow();
        spell.ChosenTargets[0].Should().BeSameAs(_alice,
            "no target chosen → the spell's original target is untouched");
    }

    [Fact]
    public void Etb_MultiTargetSpell_IsNotRedirected()
    {
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var specimen = HydroelectricSpecimenFactory.Create(_alice, stack);
        var etb = specimen.Abilities.OfType<TriggeredAbility>().Single();

        // A spell with TWO chosen targets — "with a single target" excludes it.
        var twin = new Instant("Electrolyze", "{1}{U}{R}") { Owner = _bob, Controller = _bob };
        twin.SetZone(ZoneType.Stack);
        var spell = new Spell(twin, _bob);
        spell.ChosenTargets.Add(_alice);
        spell.ChosenTargets.Add(specimen);
        stack.Push(spell);

        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });
        foreach (var eff in etb.Effects) eff.Execute();

        spell.ChosenTargets.Should().HaveCount(2,
            "a multi-target spell is not 'a spell with a single target' (CR 608.2b)");
        spell.ChosenTargets[0].Should().BeSameAs(_alice);
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void Laboratory_Identity_Land_TapsForBlue_BackFace()
    {
        var lab = HydroelectricLaboratoryFactory.Create(_alice);

        lab.Should().BeOfType<Land>();
        lab.Name.Should().Be("Hydroelectric Laboratory");
        lab.HasType(CardType.Land).Should().BeTrue();
        lab.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Hydroelectric Laboratory is a non-Basic land");
        lab.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        lab.Owner.Should().BeSameAs(_alice);
        lab.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        lab.MdfcState.Should().NotBeNull();
        lab.MdfcState!.IsBackFace.Should().BeTrue();
        lab.MdfcState.ActiveFaceName.Should().Be("Hydroelectric Laboratory");
        lab.MdfcState.FrontFaceName.Should().Be("Hydroelectric Specimen");

        // {T}: Add {U} — single mana ability producing one blue.
        var mana = lab.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Blue.Should().Be(1);
        mana.ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Laboratory_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Hydroelectric Laboratory", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hydroelectric Laboratory");
    }

    // =========================================================================
    // Back face — pay-3-life-or-tapped ETB (CR 614.1c)
    // =========================================================================

    private static ZoneMoveIntent EtbIntent(ICard land, Player controller) =>
        new(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

    [Fact]
    public void Laboratory_EntersUntapped_WhenAgentPaysThreeLife()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(_alice, agent);

        var lab = HydroelectricLaboratoryFactory.Create(_alice, bus);

        var after = bus.Apply(EtbIntent(lab, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Hydroelectric Laboratory enters untapped when the controller pays 3 life");
        _alice.LifeTotal.Should().Be(17, "paying 3 life drops Alice from 20 → 17");
    }

    [Fact]
    public void Laboratory_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(_alice, agent);

        var lab = HydroelectricLaboratoryFactory.Create(_alice, bus);

        var after = bus.Apply(EtbIntent(lab, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Hydroelectric Laboratory enters tapped when the controller declines (CR 614.1c)");
        _alice.LifeTotal.Should().Be(20, "declining keeps Alice at 20");
    }

    [Fact]
    public void Laboratory_EntersTapped_WhenControllerCannotPayThreeLife()
    {
        var bus = new ReplacementBus();
        var poor = new Player("Poor", 20);
        poor.LoseLife(18); // life = 2 < 3
        // No QueueYesNo — if the predicate (incorrectly) prompted, the
        // ScriptedAgent would throw on an empty queue, exposing the CR 119.4 bug.
        var agent = new ScriptedAgent();
        AgentRegistry.Set(poor, agent);

        var lab = HydroelectricLaboratoryFactory.Create(poor, bus);

        var after = bus.Apply(EtbIntent(lab, poor));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "can't pay 3 life with only 2 → enters tapped (CR 119.4)");
        poor.LifeTotal.Should().Be(2, "no life is paid when the controller can't afford it");
    }

    [Fact]
    public void Laboratory_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var lab = HydroelectricLaboratoryFactory.Create(_alice, bus);

        var after = bus.Apply(EtbIntent(lab, _alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent → default decline → enters tapped");
        _alice.LifeTotal.Should().Be(20);
    }
}
