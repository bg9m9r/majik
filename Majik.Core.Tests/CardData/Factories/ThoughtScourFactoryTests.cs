using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ThoughtScourFactory"/> (Innistrad, {U}).
///
/// Covers:
/// - Identity (Instant, {U}, Blue, mana value 1, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="ThoughtScourFactory.BuildDefinition"/> shape (single
///   "target player" TargetRequest 1..1, no modes, no X).
/// - CandidateGatherer includes the caster (self-mill is legal) AND opponents.
/// - Resolving on an opponent: mills 2 from opponent, draws 1 for caster.
/// - Resolving on self: mills 2 from caster's own library, draws 1 for caster.
/// - Short library fully mills without throwing (CR 701.13a).
/// - Illegal target (resolver returns non-Player) → no-op (CR 608.2b).
/// </summary>
public class ThoughtScourFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_Identity_InstantOneBlueCost()
    {
        var card = ThoughtScourFactory.Create(_alice);

        card.Name.Should().Be("Thought Scour");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.ManaCostValue.TotalValue.Should().Be(1);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThoughtScour_DispatchesViaNamedCardFactory()
    {
        var dispatched = NamedCardFactory.Create("Thought Scour", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Thought Scour");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{U}");
    }

    // -----------------------------------------------------------------------
    // BuildDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_BuildDefinition_Shape()
    {
        var def = ThoughtScourFactory.BuildDefinition(_alice, raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target player");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CandidateGatherer — both caster and opponents are legal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_CandidateGatherer_IncludesCasterAndOpponent()
    {
        var def = ThoughtScourFactory.BuildDefinition(_alice, raw => raw);
        var tr  = def.TargetRequests[0];

        // Build a minimal GameContext with both players.
        var stack = new Majik.Core.Stack.Stack();
        var ctx   = new GameContext(
            _alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, stack);

        var candidates = tr.ResolveCandidates(ctx);

        candidates.Should().Contain(_alice,
            "controller may choose themselves as the target (self-mill)");
        candidates.Should().Contain(_bob,
            "controller may choose an opponent as the target");
    }

    // -----------------------------------------------------------------------
    // Resolve — target opponent: mill 2 opponent, draw 1 caster (CR 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_Resolve_MillsTwoFromOpponent_DrawsOneForCaster()
    {
        // Populate Bob's library with 5 cards.
        for (int i = 0; i < 5; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // Give Alice 1 card in library so the draw has something to take.
        var drawCard = new Instant("DrawMe", "{U}");
        drawCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(drawCard);
        drawCard.SetZone(ZoneType.Library);

        var def     = ThoughtScourFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.Count.Should().Be(ThoughtScourFactory.MillCount,
            "2 cards move from Bob's library to his graveyard (CR 701.13)");
        _bob.Zones.Library.Count.Should().Be(3,
            "3 cards remain in Bob's library");

        _alice.Zones.Hand.Count.Should().Be(1,
            "Alice draws 1 card");
        _alice.Zones.Library.Count.Should().Be(0,
            "Alice's drawn card left her library");
    }

    // -----------------------------------------------------------------------
    // Resolve — target self: mill 2 from own library, draw 1 (self-mill mode)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_Resolve_TargetSelf_MillsTwoFromSelf_DrawsOne()
    {
        // Populate Alice's library with 5 cards (2 mill + 1 draw = 3 consumed).
        for (int i = 0; i < 5; i++)
        {
            var c = new Instant($"AliceCard{i}", "{U}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def     = ThoughtScourFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _alice } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Graveyard.Count.Should().Be(ThoughtScourFactory.MillCount,
            "2 cards milled from Alice's own library to her graveyard");
        _alice.Zones.Library.Count.Should().Be(2,
            "3 cards left library (2 milled + 1 drawn); 2 remain");
        _alice.Zones.Hand.Count.Should().Be(1,
            "Alice draws 1 card after milling herself");
    }

    // -----------------------------------------------------------------------
    // Short library — mill does not throw (CR 701.13a)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_Resolve_ShortLibrary_MillsAllRemaining()
    {
        // Only 1 card in Bob's library — fewer than MillCount=2.
        var c = new Instant("Junk0", "{U}");
        c.SetOwner(_bob);
        _bob.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);

        // Alice has a card to draw.
        var drawCard = new Instant("DrawMe", "{U}");
        drawCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(drawCard);
        drawCard.SetZone(ZoneType.Library);

        var def     = ThoughtScourFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 701.13a — milling more than library has just mills all remaining");
        _bob.Zones.Library.Count.Should().Be(0, "all remaining cards milled");
        _bob.Zones.Graveyard.Count.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Illegal target → no-op (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThoughtScour_Resolve_IllegalTarget_NoOps()
    {
        // Resolver returns a non-Player.
        var stale = new Instant("Stale", "{U}");
        var def     = ThoughtScourFactory.BuildDefinition(_alice, _ => stale);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 608.2b — illegal target at resolution is a clean no-op");
        _bob.Zones.Graveyard.Count.Should().Be(0);
        _alice.Zones.Hand.Count.Should().Be(0,
            "draw does not fire when mill target is illegal");
    }
}
