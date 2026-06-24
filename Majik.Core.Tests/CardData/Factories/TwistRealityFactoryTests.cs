using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TwistRealityFactory"/> (Instant, {1}{U}{U}).
///
/// Oracle (verified against Scryfall):
///   "Choose one —
///     • Counter target spell.
///     • Manifest dread."
///
/// CR 700.2d — modal "Choose one —" spell. Coverage (UNIQUE behaviour only;
/// the contract test covers dispatch + well-formedness):
/// - Identity: {1}{U}{U} blue Instant, mana value 3.
/// - Mode 0 — counter target spell (CR 701.5).
/// - Mode 1 — manifest dread (CR 701.59) against the caster's library.
/// - Modal pick-count cap (CR 700.2d) — only the chosen mode resolves.
/// </summary>
[Trait("Color", "U")]
public class TwistRealityFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TwistRealityFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void Create_HasBlueInstantShape()
    {
        var card = TwistRealityFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Twist Reality");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{U}{U} = mana value 3");
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_ExposesTwoModes_OnlyCounterTakesATarget()
    {
        var def = TwistRealityFactory.BuildSpellDefinition(_alice, o => o, _stack);

        def.Modes.Should().HaveCount(2);
        def.Modes[TwistRealityFactory.ModeCounter].Should().Contain("Counter");
        def.Modes[TwistRealityFactory.ModeManifestDread].Should().Contain("Manifest");

        def.TargetRequests.Should().HaveCount(2);
        // MinTargets=0 on both so the unchosen mode doesn't gate the cast.
        def.TargetRequests[TwistRealityFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[TwistRealityFactory.ModeCounter].MaxTargets.Should().Be(1);
        def.TargetRequests[TwistRealityFactory.ModeManifestDread].MaxTargets.Should().Be(0,
            "manifest dread takes no target");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — counter target spell (CR 701.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_CountersTargetSpell_SendsItToGraveyard()
    {
        // Bob has a spell on the stack.
        var bobCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = TwistRealityFactory.BuildSpellDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell },  // mode 0 target
            Array.Empty<object>(),      // mode 1 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: TwistRealityFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1, "only the chosen counter mode resolves (CR 700.2d)");
        foreach (var e in effects) e.Execute();

        bobCard.Zone.Should().Be(ZoneType.Graveyard,
            "mode 0 hard-counters the spell and sends it to the graveyard (CR 701.5)");
        _stack.IsEmpty.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Mode 1 — manifest dread (CR 701.59)
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_ManifestDread_ManifestsTopOfLibrary_SecondToGraveyard()
    {
        // Top of library = first added (Zone.AddCard appends; index 0 = top).
        var topCard = new Creature("Top Card Creature", "{1}{G}", 3, 3);
        topCard.SetOwner(_alice);
        var secondCard = new Card("Second Card", "{R}");
        secondCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var def = TwistRealityFactory.BuildSpellDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),  // mode 0 (unused)
            Array.Empty<object>(),  // mode 1 — manifest dread takes no target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: TwistRealityFactory.ModeManifestDread,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty(
            "manifest dread looks at + consumes the top two cards (CR 701.59)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(secondCard,
            "the second looked-at card goes to the graveyard");

        var wrapper = _alice.Zones.Battlefield.GetCards()
            .OfType<ManifestedCreature>().Single();
        wrapper.IsFaceDown.Should().BeTrue("manifested as a face-down 2/2 (CR 708.2)");
        wrapper.UnderlyingCard.Should().BeSameAs(topCard);
    }
}
