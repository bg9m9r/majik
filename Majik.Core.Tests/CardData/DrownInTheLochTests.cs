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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 700.2d — modal "Choose one" spell. Drown in the Loch, Throne of
/// Eldraine, {U}{B}, two modes (counter target spell mv ≤ X / destroy
/// target creature mv ≤ X) where X = largest mana value among cards in
/// opponents' graveyards.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring ArchmagesCharmTests.
/// </summary>
public class DrownInTheLochTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public DrownInTheLochTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void Create_HasInstantShape_ManaValueTwo()
    {
        var card = DrownInTheLochFactory.Create(_alice);

        card.Name.Should().Be("Drown in the Loch");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{U}{B} = mana value 2");

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDrownInTheLochShape()
    {
        var dispatched = NamedCardFactory.Create("Drown in the Loch", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Drown in the Loch");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesTwoModes_WithPerModeIntents()
    {
        var def = DrownInTheLochFactory.BuildDefinition(_alice, o => o, _stack);

        def.Modes.Should().HaveCount(2);
        def.Modes[DrownInTheLochFactory.ModeCounter].Should().Contain("Counter");
        def.Modes[DrownInTheLochFactory.ModeDestroy].Should().Contain("Destroy");

        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.ModeIntentsOrEmpty[DrownInTheLochFactory.ModeCounter]
            .Should().Be(BotIntent.Counter);
        def.ModeIntentsOrEmpty[DrownInTheLochFactory.ModeDestroy]
            .Should().Be(BotIntent.Removal);

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[DrownInTheLochFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[DrownInTheLochFactory.ModeDestroy].MinTargets.Should().Be(0);
    }

    [Fact]
    public void ComputeX_TakesLargestManaValueAcrossOpponentGraveyards()
    {
        // Bob's graveyard: {R} bolt (mv 1), {2}{U}{U} command (mv 4), and a
        // basic Mountain (mv 0). Alice's own graveyard is ignored.
        _bob.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}") { Owner = _bob });
        _bob.Zones.Graveyard.AddCard(new Instant("Cryptic Command", "{1}{U}{U}{U}") { Owner = _bob });
        _bob.Zones.Graveyard.AddCard(new Land("Mountain") { Owner = _bob });
        _alice.Zones.Graveyard.AddCard(new Sorcery("Ancestral Recall", "{U}") { Owner = _alice });

        var x = DrownInTheLochFactory.ComputeX(_alice, new[] { _alice, _bob });

        x.Should().Be(4, because: "Cryptic Command has mv 4 — the largest in Bob's graveyard");
    }

    [Fact]
    public void ComputeX_EmptyOpponentGraveyards_ReturnsZero()
    {
        var x = DrownInTheLochFactory.ComputeX(_alice, new[] { _alice, _bob });
        x.Should().Be(0);
    }

    [Fact]
    public void Mode0_Counter_RemovesTargetSpellFromStack_WhenMvLeqX()
    {
        // X = 3 (Bob has a {3}-cost card in graveyard). Bob casts Lightning
        // Bolt ({R}, mv 1) — Alice counters it.
        _bob.Zones.Graveyard.AddCard(new Creature("Tarmogoyf", "{1}{G}", 0, 1) { Owner = _bob });
        _bob.Zones.Graveyard.AddCard(new Sorcery("Hymn to Tourach", "{1}{B}{B}") { Owner = _bob });

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = DrownInTheLochFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell }, // mode 0 — counter
            Array.Empty<object>(),     // mode 1 — destroy (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: DrownInTheLochFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var eff in effects) eff.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "counter mode sends the targeted spell to the graveyard when mv ≤ X");
    }

    [Fact]
    public void Mode0_Counter_NoOps_WhenTargetSpellManaValueExceedsX()
    {
        // X = 1 (only a {R} bolt in Bob's graveyard). Bob casts Cryptic
        // Command (mv 4) — the gate rejects it (CR 608.2b).
        _bob.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}") { Owner = _bob });

        var bobCommand = new Instant("Cryptic Command", "{1}{U}{U}{U}")
        {
            Owner = _bob, Controller = _bob,
        };
        var bobSpell = new Majik.Core.Spells.Spell(bobCommand, _bob);
        _stack.Push(bobSpell);

        var def = DrownInTheLochFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: DrownInTheLochFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bobCommand.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Cryptic Command's mv 4 exceeds X = 1, so the counter no-ops");
        _stack.IsEmpty.Should().BeFalse(because: "the over-cost target is left on the stack");
    }

    [Fact]
    public void Mode1_Destroy_SendsTargetCreatureToGraveyard_WhenMvLeqX()
    {
        // X = 3 (Bob has {1}{B}{B} Hymn in graveyard, mv 3). Bob controls
        // Grizzly Bears ({1}{G}, mv 2) — Alice destroys it.
        _bob.Zones.Graveyard.AddCard(new Sorcery("Hymn to Tourach", "{1}{B}{B}") { Owner = _bob });

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob, Controller = _bob,
        };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = DrownInTheLochFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobBear },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: DrownInTheLochFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "destroy mode sends the creature to its owner's graveyard when mv ≤ X");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobBear);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobBear);
    }

    [Fact]
    public void Mode1_Destroy_NoOps_WhenTargetCreatureManaValueExceedsX()
    {
        // X = 2 (Bob's biggest grave card is {1}{G} Goyf, mv 2). Bob
        // controls a Cryptic Command-sized 5/5 (mv 5) — gate rejects.
        _bob.Zones.Graveyard.AddCard(new Creature("Tarmogoyf", "{1}{G}", 0, 1) { Owner = _bob });

        var bobBig = new Creature("Primeval Titan", "{4}{G}", 6, 6)
        {
            Owner = _bob, Controller = _bob,
        };
        bobBig.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBig);

        var def = DrownInTheLochFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobBig },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: DrownInTheLochFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bobBig.Zone.Should().Be(ZoneType.Battlefield,
            because: "Primeval Titan's mv 5 exceeds X = 2, so the destroy no-ops");
    }

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        // CR 700.2d — pick count = 1. Caller submits both modes; runtime
        // caps at PickCount (1) and drops the overflow.
        _bob.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}") { Owner = _bob });

        var def = DrownInTheLochFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: DrownInTheLochFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                DrownInTheLochFactory.ModeCounter,
                DrownInTheLochFactory.ModeDestroy, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(DrownInTheLochFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
