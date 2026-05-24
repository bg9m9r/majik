using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="IzzetCharmFactory"/> and
/// <see cref="IzzetStaticasterFactory"/>.
///
/// Izzet Charm (Return to Ravnica, {U}{R}):
///   CR 700.2d — modal "Choose one —" spell with 3 modes.
///   Mode 0: counter noncreature spell unless its controller pays {2}.
///   Mode 1: 2 damage to any target.
///   Mode 2: draw two cards, then discard two cards.
///
/// Izzet Staticaster (Return to Ravnica, {1}{U}{R} 0/3):
///   Flash. {T}: 1 damage to target creature and each other creature with
///   the same name.
/// </summary>
public class IzzetCharmTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    public IzzetCharmTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetCharm_Create_HasInstantShape_BlueRed()
    {
        var card = IzzetCharmFactory.Create(_alice);

        card.Name.Should().Be("Izzet Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{U}{R} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IzzetCharm_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Izzet Charm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Izzet Charm");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void IzzetCharm_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = IzzetCharmFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, _stack);

        def.Modes.Should().HaveCount(3);
        def.Modes[IzzetCharmFactory.ModeCounter].Should().Contain("Counter");
        def.Modes[IzzetCharmFactory.ModeDamage].Should().Contain("damage");
        def.Modes[IzzetCharmFactory.ModeLoot].Should().Contain("Draw");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[IzzetCharmFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[IzzetCharmFactory.ModeDamage].MinTargets.Should().Be(0);
        def.TargetRequests[IzzetCharmFactory.ModeLoot].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Izzet Charm — Mode 0: counter noncreature spell unless pay {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetCharm_Mode0_CountersNoncreatureSpell()
    {
        // Bob has an instant on the stack with no mana to pay {2}.
        var bobCard = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = IzzetCharmFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell }, // mode 0 target
            Array.Empty<object>(),     // mode 1 (unused)
            Array.Empty<object>(),     // mode 2 (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: IzzetCharmFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 counters the noncreature spell and sends it to the graveyard");
        _stack.IsEmpty.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Izzet Charm — Mode 1: deal 2 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetCharm_Mode1_Deals2DamageToPlayer()
    {
        var def = IzzetCharmFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { _bob },    // mode 1 target — player
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: IzzetCharmFactory.ModeDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(18, because: "mode 1 deals 2 damage to any target");
    }

    // -----------------------------------------------------------------------
    // Izzet Charm — Mode 2: draw two, discard two
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetCharm_Mode2_DrawsTwoDiscardsTwoForCaster()
    {
        // Alice has 3 cards in library and an empty hand.
        var lib1 = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var lib2 = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        var lib3 = new Instant("Lava Spike", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(lib1);
        _alice.Zones.Library.AddCard(lib2);
        _alice.Zones.Library.AddCard(lib3);

        var def = IzzetCharmFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(), // mode 2 — no target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: IzzetCharmFactory.ModeLoot,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        // Drew 2, discarded 2 → net 0 hand gain; library -2.
        _alice.Zones.Hand.GetCards().Should().HaveCount(0,
            because: "draw 2 then discard 2 returns the hand to 0 (started empty)");
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2,
            because: "the two drawn cards are discarded to the graveyard");
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            because: "library started at 3, drew 2");
    }

    // -----------------------------------------------------------------------
    // Izzet Staticaster — identity
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetStaticaster_Create_HasCorrectShape()
    {
        var card = IzzetStaticasterFactory.Create(_alice);

        card.Name.Should().Be("Izzet Staticaster");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{U}{R} = mana value 3");
        ((Creature)card).BasePower.Should().Be(0);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IzzetStaticaster_HasFlashKeyword()
    {
        var card = IzzetStaticasterFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(a => a.Keyword == "Flash",
            because: "CR 702.8 — Flash allows casting at instant speed");
    }

    [Fact]
    public void IzzetStaticaster_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Izzet Staticaster", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Izzet Staticaster");
    }

    // -----------------------------------------------------------------------
    // Izzet Staticaster — activated ability
    // -----------------------------------------------------------------------

    [Fact]
    public void IzzetStaticaster_ActivatedAbility_Deals1DamageToTargetAndSameNamedCreatures()
    {
        // Bob controls three 1/1 goblins with the same name, plus one bear.
        var goblin1 = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        goblin1.SetZone(ZoneType.Battlefield);
        var goblin2 = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        goblin2.SetZone(ZoneType.Battlefield);
        var goblin3 = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        goblin3.SetZone(ZoneType.Battlefield);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);

        // Alice's allCreaturesResolver returns all four creatures.
        var allCreatures = new List<Creature> { goblin1, goblin2, goblin3, bear };

        var staticaster = IzzetStaticasterFactory.Create(
            _alice,
            allCreaturesResolver: () => allCreatures);
        staticaster.SetZone(ZoneType.Battlefield);

        // Get the activated ability and supply the target (goblin1).
        var ability = staticaster.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { goblin1 },
        });

        ability.Resolve();

        // goblin1 is the primary target — takes 1 damage.
        goblin1.Damage.Should().Be(1,
            because: "the primary target takes 1 damage from Izzet Staticaster");

        // goblin2 and goblin3 share the name "Goblin Guide" — each takes 1 damage.
        goblin2.Damage.Should().Be(1,
            because: "goblin2 has the same name as the target — swept for 1 damage");
        goblin3.Damage.Should().Be(1,
            because: "goblin3 has the same name as the target — swept for 1 damage");

        // The bear has a different name — no damage.
        bear.Damage.Should().Be(0,
            because: "'Grizzly Bears' does not match 'Goblin Guide'");
    }

    [Fact]
    public void IzzetStaticaster_SingleArgCreate_OnlyDamagesPrimaryTarget()
    {
        // Without allCreaturesResolver the name-sweep is a no-op; only
        // the primary target takes 1 damage.
        var goblin1 = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        goblin1.SetZone(ZoneType.Battlefield);
        var goblin2 = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _bob, Controller = _bob };
        goblin2.SetZone(ZoneType.Battlefield);

        var staticaster = IzzetStaticasterFactory.Create(_alice);
        staticaster.SetZone(ZoneType.Battlefield);

        var ability = staticaster.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { goblin1 },
        });

        ability.Resolve();

        goblin1.Damage.Should().Be(1, because: "primary target always takes 1 damage");
        goblin2.Damage.Should().Be(0,
            because: "without allCreaturesResolver the name-sweep does not fire");
    }
}
