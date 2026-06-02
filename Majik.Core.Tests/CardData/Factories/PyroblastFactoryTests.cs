using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PyroblastFactory"/>.
///
/// Pyroblast (Ice Age / many reprints, {R}):
///   Instant. Oracle text (verified against Scryfall):
///     "Choose one —
///       • Counter target spell if it's blue.
///       • Destroy target permanent if it's blue."
///
///   CR 700.2d — modal "Choose one —" spell with two modes.
///   Mode 0: counter target spell if it's blue (CR 701.5; the "if it's
///     blue" is an intervening resolution check, CR 608.2c — Pyroblast can
///     target ANY spell, then does nothing if that spell isn't blue).
///   Mode 1: destroy target permanent if it's blue (CR 701.7; same
///     resolution-time blue gate).
///
/// Tests drive the per-mode bodies directly through
/// <see cref="SpellDefinition.EffectFactory"/> + <see cref="ChosenSpellParams"/>,
/// mirroring <c>IzzetCharmTests</c>.
/// </summary>
public class PyroblastFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PyroblastFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_Red_R()
    {
        var card = PyroblastFactory.Create(_alice);

        card.Name.Should().Be("Pyroblast");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCost.Should().Be("{R}");
        card.ManaCostValue.TotalValue.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsPyroblastShape()
    {
        var dispatched = NamedCardFactory.Create("Pyroblast", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Pyroblast");
        dispatched.ManaCost.Should().Be("{R}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildDefinition_ExposesTwoModes_AndTwoTargetRequests()
    {
        var def = PyroblastFactory.BuildDefinition(o => o, _stack);

        def.Modes.Should().HaveCount(2);
        def.Modes[PyroblastFactory.ModeCounter].Should().Contain("Counter");
        def.Modes[PyroblastFactory.ModeDestroy].Should().Contain("Destroy");
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(2);
        // CR 601.2c — MinTargets=0 so the unchosen mode's slot doesn't gate.
        def.TargetRequests[PyroblastFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[PyroblastFactory.ModeDestroy].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — counter target spell if it's blue (CR 701.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_CountersBlueSpell()
    {
        // Bob casts a blue spell.
        var bobCard = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = PyroblastFactory.BuildDefinition(o => o, _stack);

        var effects = def.EffectFactory(ChosenForMode(
            PyroblastFactory.ModeCounter, bobSpell));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "Pyroblast counters a blue spell (CR 701.5)");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Mode0_NonBlueSpell_NoOp()
    {
        // Bob casts a red (non-blue) spell — Pyroblast may target it but
        // does nothing on resolution (CR 608.2c "if it's blue").
        var bobCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = PyroblastFactory.BuildDefinition(o => o, _stack);

        var effects = def.EffectFactory(ChosenForMode(
            PyroblastFactory.ModeCounter, bobSpell));
        foreach (var e in effects) e.Execute();

        bobCard.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Pyroblast counters only blue spells");
        _stack.IsEmpty.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy target permanent if it's blue (CR 701.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DestroysBluePermanent()
    {
        var blueCreature = new Creature("Snapcaster Mage", "{1}{U}", 2, 1)
        { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blueCreature);
        blueCreature.SetZone(ZoneType.Battlefield);

        var def = PyroblastFactory.BuildDefinition(o => o, _stack);

        var effects = def.EffectFactory(ChosenForMode(
            PyroblastFactory.ModeDestroy, blueCreature));
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        blueCreature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Pyroblast destroys a blue permanent (CR 701.7)");
    }

    [Fact]
    public void Mode1_NonBluePermanent_NoOp()
    {
        var redCreature = new Creature("Goblin Guide", "{R}", 2, 2)
        { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(redCreature);
        redCreature.SetZone(ZoneType.Battlefield);

        var def = PyroblastFactory.BuildDefinition(o => o, _stack);

        var effects = def.EffectFactory(ChosenForMode(
            PyroblastFactory.ModeDestroy, redCreature));
        foreach (var e in effects) e.Execute();

        redCreature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Pyroblast destroys only blue permanents");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Build a <see cref="ChosenSpellParams"/> selecting <paramref name="mode"/>
    /// and placing <paramref name="target"/> in that mode's target slot.
    /// </summary>
    private ChosenSpellParams ChosenForMode(int mode, object target)
    {
        var targets = new IReadOnlyList<object>[]
        {
            mode == PyroblastFactory.ModeCounter ? new[] { target } : Array.Empty<object>(),
            mode == PyroblastFactory.ModeDestroy ? new[] { target } : Array.Empty<object>(),
        };

        return new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });
    }
}
