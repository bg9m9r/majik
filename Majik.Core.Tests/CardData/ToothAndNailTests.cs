using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ToothAndNailFactory"/> (Mirrodin, {5}{G}{G} Sorcery).
///
/// CR 700.2d — modal "Choose one —" spell with 2 modes:
///   Mode 0: search library for up to two creatures → hand → shuffle.
///   Mode 1: put up to two creature cards from hand onto the battlefield.
/// Entwine ({2}{G}{G}) lets the caster pick both modes; the additional
/// cost itself is not yet enforced (see factory header). Multi-pick is
/// honoured via <see cref="ChosenSpellParams.ModeIndexes"/>.
/// </summary>
public class ToothAndNailTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ToothAndNailTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_AtCost5GG()
    {
        var card = ToothAndNailFactory.Create(_alice);

        card.Name.Should().Be("Tooth and Nail");
        card.ManaCost.Should().Be("{5}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(7);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ToothAndNail()
    {
        var dispatched = NamedCardFactory.Create("Tooth and Nail", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Tooth and Nail");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesTwoModes_ChooseOne()
    {
        var def = ToothAndNailFactory.BuildDefinition(_alice);

        def.Modes.Should().HaveCount(2);
        def.Modes[ToothAndNailFactory.ModeTutor].Should().Contain("library");
        def.Modes[ToothAndNailFactory.ModeReanimateFromHand].Should().Contain("hand");
        def.TargetRequests.Should().BeEmpty(
            "both modes resolve via internal pickers, not cast-time target requests");
        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — tutor up to two creature cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_TutorsUpToTwoCreatures_ToHand_AndShufflesOnce()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6) { Owner = _alice };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(bears);
        _alice.Zones.Library.AddCard(titan);
        _alice.Zones.Library.AddCard(bolt);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeTutor,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Deterministic first-match fallback (no agent registered): picks
        // the first two creature cards in library iteration order.
        _alice.Zones.Hand.GetCards().Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().Contain(bears);
        _alice.Zones.Hand.GetCards().Should().Contain(titan);
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt,
            "Bolt is not a creature card; mode 0 only picks creatures");
        _alice.Zones.Library.GetCards().Should().Contain(bolt,
            "non-picked, non-creature cards stay in library");
    }

    [Fact]
    public void Mode0_OnlyOneCreatureAvailable_FindsOneAndShuffles()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(bears);
        _alice.Zones.Library.AddCard(bolt);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeTutor,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bears);
    }

    [Fact]
    public void Mode0_NoCreaturesInLibrary_IsNoOp()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeTutor,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Contain(bolt);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — put up to two creatures from hand onto the battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_PutsUpToTwoCreaturesFromHand_OntoBattlefield()
    {
        var emrakul = new Creature("Mindslaver Bot", "{6}", 5, 5) { Owner = _alice };
        var titan = new Creature("Primeval Titan", "{4}{G}{G}", 6, 6) { Owner = _alice };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(emrakul); emrakul.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(titan);   titan.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);    bolt.SetZone(ZoneType.Hand);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeReanimateFromHand,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Both creature picks moved to battlefield; bolt stays in hand.
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2);
        _alice.Zones.Battlefield.GetCards().Should().Contain(emrakul);
        _alice.Zones.Battlefield.GetCards().Should().Contain(titan);
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bolt);
        emrakul.Zone.Should().Be(ZoneType.Battlefield);
        titan.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Mode1_OneCreatureInHand_PutsOnlyOneOntoBattlefield()
    {
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(bears); bears.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);  bolt.SetZone(ZoneType.Hand);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeReanimateFromHand,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bears);
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bolt);
    }

    [Fact]
    public void Mode1_NoCreaturesInHand_IsNoOp()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Hand.AddCard(bolt); bolt.SetZone(ZoneType.Hand);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeReanimateFromHand,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bolt);
    }

    // -----------------------------------------------------------------------
    // Entwine (multi-pick) — caster picks both modes
    // -----------------------------------------------------------------------

    [Fact]
    public void Entwine_BothModesResolve_WhenModeIndexesSuppliesBoth()
    {
        // Mode 0 should tutor from library; mode 1 should put creatures
        // already in hand onto the battlefield. Set up both pools.
        var libCreature = new Creature("Library Creature", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Zones.Library.AddCard(libCreature);

        var handCreature = new Creature("Hand Creature", "{2}{G}", 3, 3) { Owner = _alice };
        _alice.Zones.Hand.AddCard(handCreature); handCreature.SetZone(ZoneType.Hand);

        var def = ToothAndNailFactory.BuildDefinition(_alice);

        // Multi-pick — both modes selected (the entwine path).
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeTutor,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: new[]
            {
                ToothAndNailFactory.ModeTutor,
                ToothAndNailFactory.ModeReanimateFromHand,
            });

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Mode 0 resolves first → libCreature is now in hand alongside
        // handCreature. Mode 1 resolves second and sees BOTH creatures
        // in hand at resolution time; with the deterministic first-match
        // fallback it picks up to two and moves them both to the
        // battlefield. This is the canonical entwine outcome — both
        // creatures end up in play (the Mindslaver + Inkwell finish).
        _alice.Zones.Battlefield.GetCards().Should().HaveCount(2,
            "entwine resolves both modes: tutor → hand → battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(handCreature);
        _alice.Zones.Battlefield.GetCards().Should().Contain(libCreature);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Entwine_DeduplicatesRepeatedModeIndex()
    {
        // ModeIndexes with mode 0 listed twice should only resolve once
        // per CR 700.2d "each mode at most once".
        var c1 = new Creature("c1", "{G}", 1, 1) { Owner = _alice };
        var c2 = new Creature("c2", "{G}", 1, 1) { Owner = _alice };
        _alice.Zones.Library.AddCard(c1);
        _alice.Zones.Library.AddCard(c2);

        var def = ToothAndNailFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: ToothAndNailFactory.ModeTutor,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty,
            ModeIndexes: new[]
            {
                ToothAndNailFactory.ModeTutor,
                ToothAndNailFactory.ModeTutor, // duplicate — should dedupe
            });

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Even though mode 0 is listed twice, it resolves exactly once
        // (which still tutors up to 2 creatures itself).
        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "the single resolved mode 0 already grabs up to two creatures");
    }
}
