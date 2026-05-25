using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EldraziMimicFactory"/> (Oath of the Gatewatch, {2}).
///
/// Covers:
/// - Identity (Creature — Eldrazi, {2}, 2/1, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Trigger predicate: colourless creature, mv >= 4, controller match, not self.
/// - Trigger resolution registers a <see cref="BecomesPTUntilEndOfTurnEffect"/>
///   with the source creature's current P/T at resolve time.
/// - Negative cases: coloured creature, mv-3 creature, opponent-controlled
///   creature, Mimic itself entering all reject the trigger.
/// </summary>
public class EldraziMimicFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void EldraziMimic_Identity()
    {
        var mimic = EldraziMimicFactory.Create(_alice);

        mimic.Name.Should().Be("Eldrazi Mimic");
        mimic.ManaCost.Should().Be("{2}");
        mimic.HasType(CardType.Creature).Should().BeTrue();
        mimic.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        mimic.BasePower.Should().Be(2);
        mimic.BaseToughness.Should().Be(1);
        mimic.Owner.Should().BeSameAs(_alice);
        mimic.Controller.Should().BeSameAs(_alice);

        // Mimic itself is colourless (no coloured pips in {2}).
        CardColors.GetColors(mimic).Should().BeEmpty(
            "Eldrazi Mimic's printed cost {2} has no coloured pips (CR 105)");
    }

    [Fact]
    public void EldraziMimic_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Eldrazi Mimic", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Eldrazi Mimic");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void EldraziMimic_AttachesEtbOtherTrigger()
    {
        var mimic = EldraziMimicFactory.Create(_alice);

        mimic.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Eldrazi Mimic has exactly one printed triggered ability");
    }

    [Fact]
    public void EldraziMimic_Trigger_FiresForColorlessMv4CreatureUnderControl()
    {
        var mimic = EldraziMimicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mimic);
        mimic.SetZone(ZoneType.Battlefield);

        // A 5/5 colourless mv-4 Eldrazi (Reality Smasher-shaped, but cost
        // here is the simplest colourless mv-4 — {4}).
        var other = new Creature("Other Eldrazi", "{4}", 5, 5,
            subtypes: new[] { CardSubtype.Eldrazi });
        other.SetOwner(_alice);
        other.SetController(_alice);
        other.SetZone(ZoneType.Battlefield);

        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(other, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeTrue(
            "another colourless creature with mana value 4 entered under Mimic's controller");
    }

    [Fact]
    public void EldraziMimic_Trigger_RejectsColoredCreature()
    {
        var mimic = EldraziMimicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mimic);
        mimic.SetZone(ZoneType.Battlefield);

        // A coloured 5/5 mv-4 creature (Tarmogoyf-shaped at {1}{G}).
        var coloured = new Creature("Coloured Beast", "{2}{G}{G}", 5, 5);
        coloured.SetOwner(_alice);
        coloured.SetController(_alice);
        coloured.SetZone(ZoneType.Battlefield);

        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(coloured, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeFalse(
            "coloured creature — Mimic's printed text requires colourless");
    }

    [Fact]
    public void EldraziMimic_Trigger_RejectsManaValue3Creature()
    {
        var mimic = EldraziMimicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mimic);
        mimic.SetZone(ZoneType.Battlefield);

        var mv3 = new Creature("Small Eldrazi", "{3}", 3, 3,
            subtypes: new[] { CardSubtype.Eldrazi });
        mv3.SetOwner(_alice);
        mv3.SetController(_alice);
        mv3.SetZone(ZoneType.Battlefield);

        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(mv3, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeFalse(
            "mana value 3 — Mimic's printed text requires mv >= 4");
    }

    [Fact]
    public void EldraziMimic_Trigger_RejectsOpponentControlledCreature()
    {
        var mimic = EldraziMimicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mimic);
        mimic.SetZone(ZoneType.Battlefield);

        var opp = new Creature("Opponent's Eldrazi", "{4}", 5, 5,
            subtypes: new[] { CardSubtype.Eldrazi });
        opp.SetOwner(_bob);
        opp.SetController(_bob);
        opp.SetZone(ZoneType.Battlefield);

        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(opp, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeFalse(
            "Mimic's printed text reads 'under your control' (CR 109.3) — opponent's enters do not trigger");
    }

    [Fact]
    public void EldraziMimic_Trigger_RejectsSelfEntering()
    {
        var mimic = EldraziMimicFactory.Create(_alice);
        // The 'another' clause: Mimic's own ETB never triggers itself.
        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();

        var ev = new CardMovedEvent(mimic, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeFalse(
            "'another' clause (CR 603.1) — Mimic's own entry does not trigger");
    }

    [Fact]
    public void EldraziMimic_Resolve_CopiesSourcePT_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var mimic = EldraziMimicFactory.Create(_alice, effects, triggers: null);
        mimic.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(mimic);
        mimic.SetZone(ZoneType.Battlefield);

        // A 5/5 colourless mv-4 source. Mimic should become 5/5 after the
        // trigger resolves.
        var src = new Creature("Reality Smasher Clone", "{4}", 5, 5,
            subtypes: new[] { CardSubtype.Eldrazi });
        src.SetOwner(_alice);
        src.SetController(_alice);
        src.SetZone(ZoneType.Battlefield);

        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(src, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeTrue();

        // Resolve the effect — the predicate's closure captured 'src';
        // executing the effect should register a Layer 7b set-base P/T.
        foreach (var e in trigger.Effects) e.Execute();

        mimic.Power.Should().Be(5,
            "Layer 7b set-base P/T copies the source's power (CR 613.7b)");
        mimic.Toughness.Should().Be(5,
            "Layer 7b set-base P/T copies the source's toughness");

        // EOT expiry — sweep expirable effects and Mimic snaps back to 2/1.
        effects.ExpireEndOfTurn();
        mimic.Power.Should().Be(2, "base P/T restored after EOT sweep (CR 514.2)");
        mimic.Toughness.Should().Be(1, "base P/T restored after EOT sweep");
    }

    [Fact]
    public void EldraziMimic_Resolve_ShapeOnlyPath_NoEffectsService_IsNoOp()
    {
        // Shape-only Create — effects null, so the resolve closure should
        // no-op cleanly rather than throw.
        var mimic = EldraziMimicFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mimic);
        mimic.SetZone(ZoneType.Battlefield);

        var src = new Creature("Eldrazi mv-4", "{4}", 5, 5,
            subtypes: new[] { CardSubtype.Eldrazi });
        src.SetOwner(_alice);
        src.SetController(_alice);
        src.SetZone(ZoneType.Battlefield);

        var trigger = mimic.Abilities.OfType<TriggeredAbility>().Single();
        var ev = new CardMovedEvent(src, ZoneType.Stack, ZoneType.Battlefield);
        trigger.IsTriggered(ev).Should().BeTrue();

        // Execute — no effects service wired, so no continuous effect is
        // registered. Mimic stays at base P/T.
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        mimic.BasePower.Should().Be(2);
        mimic.BaseToughness.Should().Be(1);
    }
}
