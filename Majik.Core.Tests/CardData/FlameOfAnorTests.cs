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
/// Unit tests for <see cref="FlameOfAnorFactory"/>.
///
/// Flame of Anor (The Lord of the Rings: Tales of Middle-earth, {1}{U}{R}):
///   Oracle text:
///     "Choose one. If you control a Wizard as you cast this spell, you may
///      choose two instead.
///       • Target player draws two cards.
///       • Destroy target artifact.
///       • Flame of Anor deals 5 damage to target creature."
///
/// CR 700.2d/700.2e — modal "Choose one (or two)" spell. The conditional
/// pick count ("you may choose two instead" iff the caster controls a Wizard
/// as the spell is cast) is decided at BuildDefinition time by inspecting the
/// caster's battlefield (CR 601.2b — modes are chosen as the spell is cast).
/// </summary>
public class FlameOfAnorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameOfAnor_Create_HasInstantShape_BlueRed()
    {
        var card = FlameOfAnorFactory.Create(_alice);

        card.Name.Should().Be("Flame of Anor");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{U}{R} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlameOfAnor_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Flame of Anor", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Flame of Anor");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void FlameOfAnor_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = FlameOfAnorFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(3);
        def.Modes[FlameOfAnorFactory.ModeDraw].Should().Contain("draws two");
        def.Modes[FlameOfAnorFactory.ModeDestroyArtifact].Should().Contain("Destroy");
        def.Modes[FlameOfAnorFactory.ModeDamage].Should().Contain("5 damage");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[FlameOfAnorFactory.ModeDraw].MinTargets.Should().Be(0);
        def.TargetRequests[FlameOfAnorFactory.ModeDestroyArtifact].MinTargets.Should().Be(0);
        def.TargetRequests[FlameOfAnorFactory.ModeDamage].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Conditional pick count: "you may choose two instead" iff control Wizard
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameOfAnor_NoWizard_PickCountIsOne()
    {
        FlameOfAnorFactory.PickCount(_alice).Should().Be(1,
            because: "no Wizard on the battlefield → choose one");
    }

    [Fact]
    public void FlameOfAnor_ControlsWizard_PickCountIsTwo()
    {
        var wiz = new Creature("Snapcaster Mage", "{1}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Wizard })
        {
            Owner = _alice,
            Controller = _alice,
        };
        wiz.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wiz);

        FlameOfAnorFactory.PickCount(_alice).Should().Be(2,
            because: "controlling a Wizard as you cast lets you choose two instead (CR 601.2b)");
    }

    [Fact]
    public void FlameOfAnor_OpponentWizard_DoesNotRaisePickCount()
    {
        // Bob's Wizard does not count — must be a Wizard YOU control.
        var wiz = new Creature("Snapcaster Mage", "{1}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Wizard })
        {
            Owner = _bob,
            Controller = _bob,
        };
        wiz.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wiz);

        FlameOfAnorFactory.PickCount(_alice).Should().Be(1,
            because: "only a Wizard YOU control unlocks the second mode");
    }

    // -----------------------------------------------------------------------
    // Mode: Target player draws two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameOfAnor_DrawMode_TargetPlayerDrawsTwo()
    {
        var lib1 = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        var lib2 = new Instant("Counterspell", "{U}{U}") { Owner = _bob };
        var lib3 = new Instant("Lava Spike", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(lib1);
        _bob.Zones.Library.AddCard(lib2);
        _bob.Zones.Library.AddCard(lib3);

        var def = FlameOfAnorFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { _bob }, // mode 0 — target player
            System.Array.Empty<object>(),
            System.Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: FlameOfAnorFactory.ModeDraw,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2, because: "target player draws two cards");
        _bob.Zones.Library.GetCards().Should().HaveCount(1, because: "library started at 3, drew 2");
    }

    // -----------------------------------------------------------------------
    // Mode: Destroy target artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameOfAnor_DestroyArtifactMode_DestroysArtifact()
    {
        var artifact = new Artifact("Pithing Needle", "{1}") { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var def = FlameOfAnorFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            System.Array.Empty<object>(),
            new object[] { artifact }, // mode 1 — target artifact
            System.Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: FlameOfAnorFactory.ModeDestroyArtifact,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Destroy target artifact moves it to the graveyard (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // Mode: Deals 5 damage to target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameOfAnor_DamageMode_Deals5ToCreature()
    {
        var creature = new Creature("Tarmogoyf", "{1}{G}", 4, 5) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = FlameOfAnorFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            System.Array.Empty<object>(),
            System.Array.Empty<object>(),
            new object[] { creature }, // mode 2 — target creature
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: FlameOfAnorFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        creature.Damage.Should().Be(5, because: "Flame of Anor deals 5 damage to target creature");
    }

    // -----------------------------------------------------------------------
    // Choose two (with Wizard): both chosen modes resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void FlameOfAnor_ChooseTwo_BothModesResolve()
    {
        // Alice controls a Wizard, so she may choose two modes (CR 601.2b).
        var wiz = new Creature("Snapcaster Mage", "{1}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Wizard })
        {
            Owner = _alice,
            Controller = _alice,
        };
        wiz.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wiz);

        var artifact = new Artifact("Pithing Needle", "{1}") { Owner = _bob, Controller = _bob };
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var creature = new Creature("Tarmogoyf", "{1}{G}", 4, 5) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = FlameOfAnorFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            System.Array.Empty<object>(),
            new object[] { artifact }, // mode 1
            new object[] { creature }, // mode 2
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: FlameOfAnorFactory.ModeDestroyArtifact,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[] { FlameOfAnorFactory.ModeDestroyArtifact, FlameOfAnorFactory.ModeDamage });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2, because: "two distinct modes were chosen");
        foreach (var e in effects) e.Execute();

        artifact.Zone.Should().Be(ZoneType.Graveyard);
        creature.Damage.Should().Be(5);
    }
}
