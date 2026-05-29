using FluentAssertions;
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
/// Unit tests for <see cref="WitherbloomCharmFactory"/>.
///
/// Witherbloom Charm (Strixhaven, {B}{G}):
///   CR 700.2d — modal "Choose one —" instant with 3 modes.
///   Mode 0: You may sacrifice a permanent. If you do, draw two cards.
///   Mode 1: You gain 5 life.
///   Mode 2: Destroy target nonland permanent with mana value 2 or less.
///
/// Modal shape mirrors <see cref="IzzetCharmFactory"/> /
/// <see cref="ArchmagesCharmFactory"/>; the destroy-with-mana-value-gate
/// mode mirrors <see cref="AbruptDecayFactory"/>.
/// </summary>
public class WitherbloomCharmTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void WitherbloomCharm_Create_HasInstantShape_BlackGreen()
    {
        var card = WitherbloomCharmFactory.Create(_alice);

        card.Name.Should().Be("Witherbloom Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{B}{G} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WitherbloomCharm_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Witherbloom Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Witherbloom Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void WitherbloomCharm_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = WitherbloomCharmFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(3);
        def.Modes[WitherbloomCharmFactory.ModeSacrificeDraw].Should().Contain("sacrifice");
        def.Modes[WitherbloomCharmFactory.ModeGainLife].Should().Contain("gain");
        def.Modes[WitherbloomCharmFactory.ModeDestroy].Should().Contain("Destroy");

        def.TargetRequests.Should().HaveCount(3);
        // Only mode 2 (destroy) takes a target; modes 0/1 don't gate the cast.
        def.TargetRequests[WitherbloomCharmFactory.ModeSacrificeDraw].MinTargets.Should().Be(0);
        def.TargetRequests[WitherbloomCharmFactory.ModeGainLife].MinTargets.Should().Be(0);
        def.TargetRequests[WitherbloomCharmFactory.ModeDestroy].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — you may sacrifice a permanent. If you do, draw two cards.
    // -----------------------------------------------------------------------

    [Fact]
    public void WitherbloomCharm_Mode0_SacrificesPermanent_AndDrawsTwo()
    {
        // Alice controls a creature she can sacrifice, plus a stocked library.
        var victim = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
        victim.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(victim);

        var lib1 = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var lib2 = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        var lib3 = new Instant("Lava Spike", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(lib1);
        _alice.Zones.Library.AddCard(lib2);
        _alice.Zones.Library.AddCard(lib3);

        var def = WitherbloomCharmFactory.BuildDefinition(
            _alice, o => o, sacrificeCandidates: () => new[] { victim });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WitherbloomCharmFactory.ModeSacrificeDraw,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        victim.Zone.Should().Be(ZoneType.Graveyard,
            because: "the chosen permanent is sacrificed");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(victim);
        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            because: "having sacrificed a permanent, the caster draws two cards");
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            because: "library started at 3, drew 2");
    }

    [Fact]
    public void WitherbloomCharm_Mode0_NoPermanentToSacrifice_DoesNotDraw()
    {
        // "You may sacrifice a permanent. If you DO, draw two cards."
        // No sacrifice candidate → no sacrifice → no draw (CR 701.16 + the
        // "if you do" intervening condition).
        var lib1 = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(lib1);

        var def = WitherbloomCharmFactory.BuildDefinition(
            _alice, o => o, sacrificeCandidates: () => Array.Empty<Permanent>());

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WitherbloomCharmFactory.ModeSacrificeDraw,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            because: "no permanent was sacrificed, so the 'if you do' draw does not happen");
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — you gain 5 life.
    // -----------------------------------------------------------------------

    [Fact]
    public void WitherbloomCharm_Mode1_GainsFiveLife()
    {
        var def = WitherbloomCharmFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WitherbloomCharmFactory.ModeGainLife,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.LifeTotal.Should().Be(25, because: "mode 1 gains the caster 5 life");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — destroy target nonland permanent with mana value 2 or less.
    // -----------------------------------------------------------------------

    [Fact]
    public void WitherbloomCharm_Mode2_DestroysNonlandPermanentManaValue2OrLess()
    {
        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = WitherbloomCharmFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            new object[] { target },
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WitherbloomCharmFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        target.Zone.Should().Be(ZoneType.Graveyard,
            because: "a 2-mana-value nonland permanent is destroyed by mode 2");
    }

    [Fact]
    public void WitherbloomCharm_Mode2_DoesNotDestroyManaValue3()
    {
        var target = new Creature("Watchwolf", "{1}{G}{W}", 3, 3) { Owner = _bob, Controller = _bob };
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = WitherbloomCharmFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), Array.Empty<object>(), new object[] { target },
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WitherbloomCharmFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        target.Zone.Should().Be(ZoneType.Battlefield,
            because: "mana value 3 exceeds the 'mana value 2 or less' gate (CR 202.3)");
    }

    [Fact]
    public void WitherbloomCharm_Mode2_DoesNotDestroyLand()
    {
        var land = new Land("Forest") { Owner = _bob, Controller = _bob };
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var def = WitherbloomCharmFactory.BuildDefinition(_alice, o => o);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), Array.Empty<object>(), new object[] { land },
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WitherbloomCharmFactory.ModeDestroy,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        land.Zone.Should().Be(ZoneType.Battlefield,
            because: "the destroy mode targets only NONLAND permanents");
    }
}
