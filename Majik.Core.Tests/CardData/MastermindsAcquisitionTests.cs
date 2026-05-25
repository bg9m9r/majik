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
/// Tests for <see cref="MastermindsAcquisitionFactory"/> (Rivals of Ixalan
/// {3}{B}{B} Sorcery).
///
/// CR 700.2d — modal "Choose one —" spell with 2 modes:
///   Mode 0: library tutor (search → hand → shuffle).
///   Mode 1: wishboard tutor (CR 408 — any card from outside the game).
/// </summary>
public class MastermindsAcquisitionTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public MastermindsAcquisitionTests()
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
    public void Create_HasSorceryShape_AtCost3BB()
    {
        var card = MastermindsAcquisitionFactory.Create(_alice);

        card.Name.Should().Be("Mastermind's Acquisition");
        card.ManaCost.Should().Be("{3}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(5);
        Majik.Core.Cards.CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MastermindsAcquisition()
    {
        var dispatched = NamedCardFactory.Create("Mastermind's Acquisition", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Mastermind's Acquisition");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesTwoModes_ChooseOne()
    {
        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);

        def.Modes.Should().HaveCount(2);
        def.Modes[MastermindsAcquisitionFactory.ModeLibrary].Should().Contain("library");
        def.Modes[MastermindsAcquisitionFactory.ModeWishboard].Should().Contain("outside the game");
        def.TargetRequests.Should().BeEmpty(
            "both modes resolve via internal pickers, not cast-time target requests");
        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — library tutor
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_TutorsAnyCardFromLibrary_ToHand_AndShuffles()
    {
        // Alice has three cards in library; mode 0 grabs the first one
        // via the deterministic first-pick fallback.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var counter = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);
        _alice.Zones.Library.AddCard(counter);
        _alice.Zones.Library.AddCard(bears);

        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);

        var chosen = new ChosenSpellParams(
            ModeIndex: MastermindsAcquisitionFactory.ModeLibrary,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Picked card moved to hand; library is down one.
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Mode0_EmptyLibrary_NoOp()
    {
        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);

        var chosen = new ChosenSpellParams(
            ModeIndex: MastermindsAcquisitionFactory.ModeLibrary,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 1 — wishboard tutor
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_TutorsAnyCardFromWishboard_ToHand()
    {
        // Alice's wishboard contains a non-artifact and an artifact —
        // mode 1 has no type filter, so the first card in the wishboard
        // is grabbed deterministically.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(solRing);

        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);

        var chosen = new ChosenSpellParams(
            ModeIndex: MastermindsAcquisitionFactory.ModeWishboard,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bolt,
            "first candidate is picked deterministically when no agent is registered");
        _alice.Wishboard.GetCards().Should().NotContain(bolt);
        _alice.Wishboard.GetCards().Should().Contain(solRing);
    }

    [Fact]
    public void Mode1_EmptyWishboard_NoOp()
    {
        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);

        var chosen = new ChosenSpellParams(
            ModeIndex: MastermindsAcquisitionFactory.ModeWishboard,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Mode1_AgentDeclines_NoOp()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);

        var agent = new ScriptedAgent();
        agent.QueueFromPile((ICard?)null); // decline
        AgentRegistry.Set(_alice, agent);

        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);
        var chosen = new ChosenSpellParams(
            ModeIndex: MastermindsAcquisitionFactory.ModeWishboard,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Wishboard.GetCards().Should().Contain(bolt);
    }

    // -----------------------------------------------------------------------
    // Modal cap — pick count enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public void Modal_PickCountCapped_OnlyOneModeResolves()
    {
        // Even if both indices are passed, CR 700.2d caps at PickCount = 1.
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Wishboard.AddCard(solRing);
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(bolt);

        var def = MastermindsAcquisitionFactory.BuildDefinition(_alice);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                MastermindsAcquisitionFactory.ModeLibrary,
                MastermindsAcquisitionFactory.ModeWishboard,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1, "PickCount = 1 caps the effects list");
    }
}
