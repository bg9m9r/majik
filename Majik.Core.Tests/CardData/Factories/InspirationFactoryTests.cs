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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Inspiration (Tempest / various, {3}{U}).
///
/// Instant. Oracle text:
///   "Target player draws two cards."
///
/// Coverage:
///   * Card shape: Instant, mana cost {3}{U}, blue, mana value 4.
///   * Dispatch by name via NamedCardFactory.
///   * SpellDefinition: 1 target request (target player, min 1 / max 1).
///   * Resolve effect: the TARGET player draws exactly 2 cards (library −2,
///     hand +2). CR 121.1 — top-of-library draw.
///   * No life-loss side effect (Inspiration has no life rider unlike
///     Sign in Blood).
///   * Caster's life unchanged after resolve.
/// </summary>
public class InspirationFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Shape / identity ────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_ThreeBlue()
    {
        var card = InspirationFactory.Create(_alice);

        card.Name.Should().Be("Inspiration");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}");
        card.ManaCostValue.TotalValue.Should().Be(4, because: "{3}{U} = mana value 4");
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsInspirationInstant()
    {
        var dispatched = NamedCardFactory.Create("Inspiration", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Inspiration");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── SpellDefinition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_ExposesOneTargetPlayerRequest()
    {
        var def = InspirationFactory.BuildSpellDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("player");
    }

    // ── Resolve effect: target player draws 2 ───────────────────────────────

    [Fact]
    public void TargetPlayerDrawsExactlyTwoCards_FromTheirLibrary()
    {
        // Bob is the target player; Alice is the caster.
        var bobTop1 = new Instant("Counterspell", "{U}{U}") { Owner = _bob };
        var bobTop2 = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobTop1);
        _bob.Zones.Library.AddCard(bobTop2);

        var def = InspirationFactory.BuildSpellDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2,
            because: "Inspiration draws exactly two cards for the target player");
        _bob.Zones.Library.GetCards().Should().BeEmpty(
            because: "both library cards moved to hand");
    }

    [Fact]
    public void CasterCanTargetSelf_DrawsTwoFromOwnLibrary()
    {
        var aliceTop1 = new Instant("Opt", "{U}") { Owner = _alice };
        var aliceTop2 = new Instant("Brainstorm", "{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(aliceTop1);
        _alice.Zones.Library.AddCard(aliceTop2);

        var def = InspirationFactory.BuildSpellDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _alice },
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var eff in effects) eff.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            because: "caster targeting self draws two from their own library");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void NoLifeLoss_CasterLifeUnchanged()
    {
        // Inspiration has no life-loss rider. Alice casts it targeting Bob;
        // both players should retain 20 life after resolution.
        var bobTop1 = new Instant("A", "{0}") { Owner = _bob };
        var bobTop2 = new Instant("B", "{0}") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobTop1);
        _bob.Zones.Library.AddCard(bobTop2);

        var def = InspirationFactory.BuildSpellDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var eff in effects) eff.Execute();

        _alice.LifeTotal.Should().Be(20, because: "Inspiration has no life-loss clause");
        _bob.LifeTotal.Should().Be(20, because: "Inspiration has no life-loss clause");
    }

    [Fact]
    public void EmptyLibrary_MarksTargetForDrawLoss_ShortCircuits()
    {
        // CR 704.5b — target player with empty library trying to draw loses.
        // The factory must mark the player and stop (no crash).
        var def = InspirationFactory.BuildSpellDefinition(o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _bob },  // Bob has 0 cards in library
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        var act = () => { foreach (var eff in effects) eff.Execute(); };

        act.Should().NotThrow(because: "empty-library draw must not crash");
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "CR 704.5b — drawing from empty library sets the SBA flag");
    }
}
