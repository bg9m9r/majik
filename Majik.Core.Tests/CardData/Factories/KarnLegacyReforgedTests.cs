using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Karn, Legacy Reforged (Dominaria United) — Legendary Artifact Creature.
/// Verifies the three clauses:
///   1. CDA P/T = greatest mana value among artifacts you control (Layer 7a).
///   2. Upkeep trigger adds {C} per artifact, restricted to artifact spells
///      (CR 106.4 colorless spend-restriction gate).
///   3. That mana doesn't empty as steps/phases end (CR 500.4 exception).
/// </summary>
public class KarnLegacyReforgedTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Artifact MakeArtifact(string name, string cost)
    {
        var a = new Artifact(name, cost);
        return a;
    }

    [Fact]
    public void IsAnArtifactCreature_Legendary_Golem()
    {
        var karn = KarnLegacyReforgedFactory.Create(_alice);

        karn.HasType(CardType.Creature).Should().BeTrue();
        karn.HasType(CardType.Artifact).Should().BeTrue();
        karn.Subtypes.Should().Contain(CardSubtype.Golem);
        karn.Supertypes.Should().Contain(CardSupertype.Legendary);
        karn.ManaCost.Should().Be("{5}");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Karn()
    {
        var karn = NamedCardFactory.Create("Karn, Legacy Reforged", _alice);

        karn.Should().BeOfType<Creature>();
        karn.Name.Should().Be("Karn, Legacy Reforged");
        karn.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Cda_PowerToughness_EqualsGreatestArtifactManaValue()
    {
        var bus = new EventBus();
        // Bus-wired CES — mirrors production: any game event (a permanent
        // entering the battlefield) bumps the layer-cache generation so a
        // board-derived CDA recomputes.
        var effects = new ContinuousEffectsService(bus);
        var karn = KarnLegacyReforgedFactory.Create(_alice, effects, bus, triggers: null);
        karn.ActiveEffects = effects;
        karn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(karn);
        // Drive the ETB re-Sync the way ZoneService.MoveCard would in a real
        // match — the lifecycle binder subscribes to CardMovedEvent and
        // registers the CDA when Karn enters the battlefield.
        bus.Publish(new CardMovedEvent(karn, ZoneType.Library, ZoneType.Battlefield));

        // Only Karn (mv 5) so far.
        karn.Power.Should().Be(5, "Karn itself is a mana-value-5 artifact");
        karn.Toughness.Should().Be(5);

        // Add a bigger artifact (mv 7) — its ETB event re-evaluates the CDA.
        var bigger = MakeArtifact("Big Construct", "{7}");
        bigger.SetController(_alice);
        bigger.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bigger);
        bus.Publish(new CardMovedEvent(bigger, ZoneType.Hand, ZoneType.Battlefield));

        karn.Power.Should().Be(7, "greatest mana value among controlled artifacts grows");
        karn.Toughness.Should().Be(7);
    }

    [Fact]
    public void GreatestArtifactManaValue_Helper_ZeroWhenNoArtifacts()
    {
        KarnLegacyReforgedFactory.GreatestArtifactManaValue(_alice).Should().Be(0);
    }

    [Fact]
    public void UpkeepTrigger_AddsColorlessPerArtifact_RestrictedAndDoesNotEmpty()
    {
        var karn = KarnLegacyReforgedFactory.Create(_alice);
        karn.SetController(_alice);
        karn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(karn);
        // Two more artifacts → 3 artifacts total (Karn + 2).
        foreach (var n in new[] { "Mox A", "Mox B" })
        {
            var m = MakeArtifact(n, "{0}");
            m.SetController(_alice);
            m.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(m);
        }

        // Fire the upkeep trigger effect manually.
        var trigger = karn.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var fx in trigger.Effects) fx.Execute();

        _alice.ManaPool.Generic.Should().Be(3, "add {C} for each of 3 artifacts");
        _alice.ManaProvenance.Should().HaveCount(3);
        _alice.ManaProvenance.Should().OnlyContain(s =>
            s.Color == ManaColor.Colorless && s.DoesNotEmpty && s.Restriction != null);
    }

    [Fact]
    public void KarnMana_CannotPayNonartifactSpell_ButPaysArtifactSpell()
    {
        // Float 2 of Karn's restricted {C} directly (1 artifact-mv aside —
        // we just need the restricted colorless units floating).
        var karn = KarnLegacyReforgedFactory.Create(_alice);
        karn.SetController(_alice);
        karn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(karn);
        var extra = MakeArtifact("Mox", "{0}");
        extra.SetController(_alice);
        extra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(extra);

        var trigger = karn.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var fx in trigger.Effects) fx.Execute();
        _alice.ManaPool.Generic.Should().Be(2, "Karn + Mox = 2 artifacts");

        var resolver = new ManaPaymentResolver();

        // Nonartifact spell (an instant costing {1}) — Karn's mana can't pay
        // it (CR 106.4). No producing sources in the payment; the spendable
        // pool excludes the restricted colorless, so it can't cover {1}.
        var instant = new Instant("Shock", "{1}");
        var payInstant = resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(System.Array.Empty<ICard>()),
            spentOn: instant, out _, out _);
        payInstant.Should().BeFalse("Karn's {C} can't be spent to cast a nonartifact spell");
        _alice.ManaPool.Generic.Should().Be(2, "rejected payment leaves the mana floating");

        // Artifact spell costing {1} — Karn's restricted colorless pays it.
        var artifactSpell = new Artifact("Ornithopter-ish", "{1}");
        var payArtifact = resolver.Pay(
            _alice, ManaCost.Parse("1"),
            new ManaPayment(System.Array.Empty<ICard>()),
            spentOn: artifactSpell, out _, out _);
        payArtifact.Should().BeTrue("Karn's {C} pays an artifact spell");
        _alice.ManaPool.Generic.Should().Be(1, "one colorless unit consumed");
    }

}
