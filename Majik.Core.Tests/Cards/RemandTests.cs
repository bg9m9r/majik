using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for <see cref="RemandFactory"/> and <see cref="ManaLeakFactory"/>.
///
/// Remand (Ravnica: City of Guilds, {1}{U}):
///   "Counter target spell. If that spell is countered this way, put it into
///    its owner's hand instead of into that player's graveyard. Draw a card."
///
/// Mana Leak (Stronghold, {1}{U}):
///   "Counter target spell unless its controller pays {3}."
///
/// Covers:
///   Remand:
///     - Card shape + dispatch.
///     - Resolve: target spell countered → card goes to owner's hand; caster draws.
///     - Resolve: target no longer on stack → no-op (no draw, no hand return).
///   Mana Leak:
///     - Card shape + dispatch.
///     - Resolve: controller can't pay {3} → spell countered.
///     - Resolve: controller has {3} → spell NOT countered (they auto-pay).
/// </summary>
public class RemandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // =========================================================================
    // Remand — identity
    // =========================================================================

    [Fact]
    public void Remand_Identity()
    {
        var card = RemandFactory.Create(_alice);

        card.Name.Should().Be("Remand");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Remand()
    {
        var card = NamedCardFactory.Create("Remand", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Remand");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // =========================================================================
    // Remand — counter target spell → goes to owner's hand; caster draws
    // =========================================================================

    [Fact]
    public void Remand_Resolve_CountersTargetSpell_CardGoesToOwnersHand_CasterDraws()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's spell on the stack — the target for Remand.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        // Give Alice a card on top of her library so she can draw.
        var libraryCard = new Card("Island", "") { Owner = _alice };
        libraryCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(libraryCard);

        var def = RemandFactory.BuildDefinition(
            _alice, targetResolver: o => o, stack: stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Bob's bolt is no longer on the stack.
        stack.Count.Should().Be(0, "Remand removed the spell from the stack");

        // The card goes to Bob's hand, NOT to his graveyard (CR 608.2b).
        bobBolt.Zone.Should().Be(ZoneType.Hand,
            "Remand redirects the countered card to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bobBolt,
            "Remand adds the countered card to its owner's hand zone collection");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobBolt,
            "Remand explicitly prevents the card from going to the graveyard");

        // Alice drew a card.
        _alice.Zones.Hand.GetCards().Should().Contain(libraryCard,
            "Remand causes the caster to draw a card");
        _alice.Zones.Library.GetCards().Should().NotContain(libraryCard,
            "the drawn card was removed from the library");
    }

    // =========================================================================
    // Remand — illegal target (spell no longer on stack) → no-op, no draw
    // =========================================================================

    [Fact]
    public void Remand_Resolve_TargetNotOnStack_NoOp_NoDraw()
    {
        // CR 608.2b — if the target spell is no longer on the stack at
        // resolution, the entire effect (counter + hand-return + draw) is skipped.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's spell is NOT on the stack — it has already been countered
        // by another spell or resolved before Remand.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        // Deliberately NOT pushing bobSpell to the stack.

        var libraryCard = new Card("Island", "") { Owner = _alice };
        libraryCard.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(libraryCard);

        var def = RemandFactory.BuildDefinition(
            _alice, targetResolver: o => o, stack: stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Nothing moved — Alice did NOT draw.
        _alice.Zones.Hand.GetCards().Should().NotContain(libraryCard,
            "no draw fires when Remand's target is no longer on the stack (CR 608.2b)");
        _alice.Zones.Library.GetCards().Should().Contain(libraryCard,
            "library is unchanged when Remand fizzles");

        // Bob's card zone is unchanged.
        bobBolt.Zone.Should().NotBe(ZoneType.Hand,
            "Remand did nothing to the card since the target was not on the stack");
    }

    // =========================================================================
    // Mana Leak — identity
    // =========================================================================

    [Fact]
    public void ManaLeak_Identity()
    {
        var card = ManaLeakFactory.Create(_alice);

        card.Name.Should().Be("Mana Leak");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ManaLeak()
    {
        var card = NamedCardFactory.Create("Mana Leak", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Mana Leak");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // =========================================================================
    // Mana Leak — controller can't pay {3} → spell countered
    // =========================================================================

    [Fact]
    public void ManaLeak_Resolve_ControllerCannotPayThree_SpellCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's spell on the stack; Bob has no mana.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = ManaLeakFactory.BuildDefinition(targetResolver: o => o, stack: stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Spell removed from stack and card goes to graveyard.
        stack.Count.Should().Be(0, "Mana Leak countered the spell (Bob couldn't pay {3})");
        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            "countered spell's card goes to the graveyard (CR 701.5)");
    }

    // =========================================================================
    // Mana Leak — controller pays {3} → spell resolves normally (not countered)
    // =========================================================================

    [Fact]
    public void ManaLeak_Resolve_ControllerPaysThree_SpellNotCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's spell on the stack; Bob has {3} available to pay.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        // Pre-stage Bob's mana pool with exactly {3} so the unless-pay
        // rider succeeds and the counter no-ops.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        var def = ManaLeakFactory.BuildDefinition(targetResolver: o => o, stack: stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Bob paid {3}; his spell was NOT countered and remains on the stack.
        stack.Count.Should().Be(1,
            "Bob paid {3}; Mana Leak's counter no-ops and the spell stays on the stack");
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            "the spell was not countered since its controller paid {3}");
    }
}
