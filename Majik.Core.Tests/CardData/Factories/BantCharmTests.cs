using FluentAssertions;
using Majik.Core.CardData;
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
/// CR 700.2d — modal "Choose one —" spell. Bant Charm, Shards of Alara,
/// {G}{W}{U}, three modes:
///   Mode 0: Destroy target artifact.
///   Mode 1: Put target creature on the bottom of its owner's library.
///   Mode 2: Counter target instant spell.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring ArchmagesCharmTests /
/// IzzetCharmTests.
/// </summary>
public class BantCharmTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BantCharmTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_BantColors()
    {
        var card = BantCharmFactory.Create(_alice);

        card.Name.Should().Be("Bant Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().Contain(ManaColor.White);
        colors.Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{G}{W}{U} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBantCharmShape()
    {
        var dispatched = NamedCardFactory.Create("Bant Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Bant Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesThreeModes_WithPerModeIntents()
    {
        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        def.Modes.Should().HaveCount(3);
        def.Modes[BantCharmFactory.ModeDestroyArtifact].Should().Contain("Destroy");
        def.Modes[BantCharmFactory.ModeBottomCreature].Should().Contain("bottom");
        def.Modes[BantCharmFactory.ModeCounterInstant].Should().Contain("Counter");

        def.ModeIntentsOrEmpty.Should().HaveCount(3);
        def.ModeIntentsOrEmpty[BantCharmFactory.ModeDestroyArtifact].Should().Be(BotIntent.Removal);
        def.ModeIntentsOrEmpty[BantCharmFactory.ModeBottomCreature].Should().Be(BotIntent.Bounce);
        def.ModeIntentsOrEmpty[BantCharmFactory.ModeCounterInstant].Should().Be(BotIntent.Counter);

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[BantCharmFactory.ModeDestroyArtifact].MinTargets.Should().Be(0);
        def.TargetRequests[BantCharmFactory.ModeBottomCreature].MinTargets.Should().Be(0);
        def.TargetRequests[BantCharmFactory.ModeCounterInstant].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy target artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_DestroyArtifact_MovesArtifactToGraveyard()
    {
        var bobBauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _bob, Controller = _bob };
        bobBauble.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBauble);

        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBauble },
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeDestroyArtifact,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobBauble.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 destroys the target artifact");
    }

    [Fact]
    public void Mode0_DestroyArtifact_IgnoresNonArtifactTarget()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobBear }, // not an artifact — CR 608.2b gate
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeDestroyArtifact,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            because: "the destroy-artifact mode no-ops on a non-artifact target");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — put target creature on the bottom of its owner's library
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_BottomCreature_MovesCreatureToBottomOfOwnersLibrary()
    {
        // Bob owns + controls the creature; its existing library has one card
        // so we can confirm the creature lands on the BOTTOM (after it).
        var existingTop = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(existingTop);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobBear },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeBottomCreature,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobBear);

        var library = _bob.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(2);
        // Index 0 is the top; the bear must be on the BOTTOM (last).
        library[0].Should().BeSameAs(existingTop, because: "the pre-existing card stays on top");
        library[^1].Should().BeSameAs(bobBear,
            because: "the creature is placed on the bottom of its owner's library");
    }

    [Fact]
    public void Mode1_BottomCreature_UsesOwnersLibrary_NotControllers()
    {
        // Alice controls a creature that Bob owns. It must return to BOB's
        // library (owner), not Alice's (controller). CR 109.5.
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _alice };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobBear },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeBottomCreature,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Library.GetCards().Should().Contain(bobBear,
            because: "'its owner's library' = Bob's, even though Alice controlled it");
        _alice.Zones.Library.GetCards().Should().NotContain(bobBear);
    }

    // -----------------------------------------------------------------------
    // Mode 2 — counter target instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_CounterInstant_RemovesInstantFromStackToGraveyard()
    {
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bobSpell },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeCounterInstant,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "counter mode sends the targeted instant to the graveyard");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Mode2_CounterInstant_IgnoresNonInstantSpell()
    {
        // A sorcery on the stack — Bant Charm can only counter instants.
        var bobSorcery = new Sorcery("Divination", "{2}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobSorcery, _bob);
        _stack.Push(bobSpell);

        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { bobSpell },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeCounterInstant,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobSorcery.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "the counter-instant mode no-ops on a sorcery (CR 608.2b)");
        _stack.IsEmpty.Should().BeFalse(because: "the sorcery stays on the stack");
    }

    // -----------------------------------------------------------------------
    // Choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        var def = BantCharmFactory.BuildDefinition(o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: BantCharmFactory.ModeDestroyArtifact,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                BantCharmFactory.ModeDestroyArtifact,
                BantCharmFactory.ModeCounterInstant, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(BantCharmFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
