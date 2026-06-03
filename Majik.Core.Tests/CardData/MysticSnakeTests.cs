using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Mystic Snake (Onslaught, {1}{G}{U}{U}).
///
/// Oracle text:
///   "Flash
///    When this creature enters, counter target spell."
///
/// Mirrors <see cref="SpellstutterSpriteTests"/> — a Flash creature whose ETB
/// counters a target spell — with the mana-value ceiling removed (Mystic Snake
/// counters ANY spell, CR 701.5).
///
/// The factory is now a declarative JSON shell (mystic-snake.json) carrying the
/// <c>counter_target_spell</c> verb on its <c>etb_self</c> trigger, so the ETB
/// counter resolves through the shared <see cref="Majik.Core.Primitives.Fx.Counter"/>
/// primitive — reaching the live stack off the resolution context's game
/// (threaded by <see cref="TriggeredAbility.ResolveAsync"/>).
///
/// Covers:
///   - Identity (name, type, Snake subtype, 2/2, mana cost).
///   - NamedCardFactory dispatch.
///   - Flash keyword marker + a single ETB triggered ability.
///   - ETB counters any target spell regardless of mana value.
///   - Countered card lands in its owner's graveyard (CR 701.5).
///   - ETB no-ops on an illegal target (target no longer on the stack).
/// </summary>
public class MysticSnakeTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MysticSnakeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // The counter resolution reaches the live stack off ctx.Game.Stack, so the
    // resolution context MUST be built over the same stack the target spell is
    // pushed onto.
    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    [Fact]
    public void MysticSnake_Identity()
    {
        var c = MysticSnakeFactory.Create(_alice);

        c.Name.Should().Be("Mystic Snake");
        c.ManaCost.Should().Be("{1}{G}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB counter-target-spell trigger");
    }

    [Fact]
    public void MysticSnake_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mystic Snake", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Mystic Snake");
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
    }

    [Fact]
    public void MysticSnake_Etb_TargetRequestShape()
    {
        var c = MysticSnakeFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("spell");
    }

    [Fact]
    public async Task MysticSnake_Etb_CountersAnySpell_RegardlessOfManaValue()
    {
        // Mystic Snake has no mana-value cap — it can counter an expensive
        // spell that Spellstutter Sprite could not.
        var snake = MysticSnakeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(snake);
        snake.SetZone(ZoneType.Battlefield);

        var bigSpell = new Sorcery("Expensive Sorcery", "{5}{U}{U}");
        bigSpell.SetOwner(_bob);
        bigSpell.SetController(_bob);
        bigSpell.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bigSpell, _bob);
        _stack.Push(spell);

        bigSpell.ManaCostValue.TotalValue.Should().Be(7);

        var etb = snake.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });

        await etb.ResolveAsync(agent: null, game: NewContext());

        bigSpell.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.5 — countered spell goes to its owner's graveyard");
        _stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public async Task MysticSnake_Etb_IllegalTarget_SpellNotOnStack_NoOps()
    {
        // CR 608.2b — if the targeted spell is no longer on the stack at
        // resolution, the counter does nothing.
        var snake = MysticSnakeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(snake);
        snake.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        // Not on the stack — already resolved/in graveyard.
        bolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bolt);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);

        var etb = snake.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });

        await etb.ResolveAsync(agent: null, game: NewContext());

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "target was not on the stack — counter no-ops");
    }
}
