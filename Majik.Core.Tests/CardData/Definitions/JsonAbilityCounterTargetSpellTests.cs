using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Ability-/trigger-path coverage for the declarative <c>counter_target_spell</c>
/// verb (CR 701.5). PR #2468 added the verb + folded it into the
/// <see cref="EffectDefinition"/> union and wired it on the SPELL path
/// (<see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>); these tests
/// pin the ABILITY / TRIGGER path — a JSON <c>etb_self</c> trigger (Mystic Snake)
/// or an activated ability (Voidmage Prodigy / Glen Elendra Archmage) carrying a
/// <see cref="CounterTargetSpellEffectDef"/> must declare the stack-targeting
/// request and counter the chosen spell on resolution, reaching the live stack
/// off <see cref="ResolutionContext.Game"/>.
///
/// <para>The counter verb is path-agnostic — it reads its chosen
/// <see cref="ISpell"/> off <see cref="ResolutionContext.ChosenTargets"/> at the
/// reserved slot and the live <see cref="GameContext.Stack"/> off
/// <c>ctx.Game</c>, both of which the trigger / activated resolution context
/// supplies — so the generic <c>ToTargetRequest</c> / <c>ToResolveEffect</c>
/// ability plumbing carries it with no per-verb path code.</para>
/// </summary>
public class JsonAbilityCounterTargetSpellTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public JsonAbilityCounterTargetSpellTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // The counter resolution reaches the live stack off ctx.Game.Stack, so the
    // resolution context MUST be built over the same stack the target spell is
    // pushed onto.
    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    /// <summary>Push a spell Bob is casting onto the live stack.</summary>
    private Majik.Core.Spells.Spell BobCasts(Card spellCard)
    {
        spellCard.SetOwner(_bob);
        spellCard.SetController(_bob);
        spellCard.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(spellCard, _bob);
        _stack.Push(spell);
        return spell;
    }

    /// <summary>Build a card from a CardDef carrying one ETB-triggered
    /// counter_target_spell effect; return its single ETB triggered ability.</summary>
    private TriggeredAbility BuildEtbCounter(bool noncreature = false, bool creature = false)
    {
        var def = new CardDefinition
        {
            Name = "Mystic Snake",
            Types = new() { "Creature" },
            Subtypes = new() { "Snake" },
            ManaCost = "{1}{G}{U}{U}",
            Power = 2,
            Toughness = 2,
            Keywords = new() { "Flash" },
            Abilities = new()
            {
                new TriggeredAbilityDefinition
                {
                    Trigger = new EnterBattlefieldSelfTriggerDef(),
                    Effects = new()
                    {
                        new CounterTargetSpellEffectDef
                        {
                            Noncreature = noncreature,
                            Creature = creature,
                        },
                    },
                },
            },
        };

        var card = CardDefinitionFactory.Build(def, _alice);
        return card.Abilities.OfType<TriggeredAbility>().Single();
    }

    private async Task ResolveWith(TriggeredAbility ability, object? target)
    {
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            target is null ? System.Array.Empty<object>() : new[] { target },
        });
        await ability.ResolveAsync(agent: null, game: NewContext());
    }

    [Fact]
    public void EtbCounter_DeclaresSingleSpellTargetRequest()
    {
        var ability = BuildEtbCounter();

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("spell");
        ability.TargetRequests[0].Intent.Should().Be(Majik.Core.Cards.BotIntent.Counter);
    }

    [Fact]
    public async Task EtbCounter_CountersChosenSpell_ToGraveyard()
    {
        var ability = BuildEtbCounter();

        var bolt = new Instant("Lightning Bolt", "{R}");
        var bobSpell = BobCasts(bolt);

        await ResolveWith(ability, bobSpell);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "the countered spell goes to its owner's graveyard (CR 701.5)");
        _stack.GetAll().Should().NotContain(bobSpell);
    }

    [Fact]
    public async Task EtbCounter_IllegalTarget_FizzlesCleanly()
    {
        var ability = BuildEtbCounter();

        var bolt = new Instant("Lightning Bolt", "{R}");
        var bobSpell = BobCasts(bolt);
        // The chosen spell has already left the stack (resolved / countered
        // earlier) — illegal at resolution (CR 608.2b).
        Majik.Core.CardData.OracleSpellBinder.RemoveFromStack(_stack, bobSpell);
        bolt.SetZone(ZoneType.Graveyard);

        var before = bolt.Zone;
        await ResolveWith(ability, bobSpell);

        bolt.Zone.Should().Be(before, "an off-stack target fizzles the counter cleanly (CR 608.2b)");
    }

    [Fact]
    public async Task EtbCounter_Noncreature_DoesNotCounterCreatureSpell()
    {
        var ability = BuildEtbCounter(noncreature: true);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var bobSpell = BobCasts(bear);

        await ResolveWith(ability, bobSpell);

        // CR 608.2b — a creature spell is an illegal target for the noncreature
        // rider; the counter does nothing and the creature spell stays.
        bear.Zone.Should().NotBe(ZoneType.Graveyard, "the noncreature rider gates creature spells out");
        _stack.GetAll().Should().Contain(bobSpell);
    }

    [Fact]
    public async Task ActivatedCounter_CountersChosenSpell_ToGraveyard()
    {
        // Voidmage Prodigy / Glen Elendra shape — an ACTIVATED ability that
        // counters target spell (here unconditioned; the cost is irrelevant to
        // the resolution verb under test).
        var def = new CardDefinition
        {
            Name = "Voidmage Prodigy",
            Types = new() { "Creature" },
            Subtypes = new() { "Human", "Wizard" },
            ManaCost = "{U}{U}",
            Power = 2,
            Toughness = 1,
            Abilities = new()
            {
                new ActivatedAbilityDefinition
                {
                    Costs = new() { new ManaCostDef { Amount = "{U}{U}" } },
                    Effects = new() { new CounterTargetSpellEffectDef() },
                },
            },
        };

        var card = CardDefinitionFactory.Build(def, _alice);
        var ability = card.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].Description.Should().Contain("spell");

        var bolt = new Instant("Lightning Bolt", "{R}");
        var bobSpell = BobCasts(bolt);

        ability.SetChosenTargets(new IReadOnlyList<object>[] { new[] { (object)bobSpell } });
        await ability.ResolveAsync(agent: null, game: NewContext());

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "the activated counter sends the chosen spell to its owner's graveyard (CR 701.5)");
        _stack.GetAll().Should().NotContain(bobSpell);
    }

    [Fact]
    public async Task MysticSnakeFactory_BuildsDeclarativeEtbCounter()
    {
        // Drive the real [CardName] factory and assert its ETB counters a spell.
        var snake = Majik.Core.CardData.Factories.MysticSnakeFactory.Create(_alice);

        snake.Name.Should().Be("Mystic Snake");
        snake.Power.Should().Be(2);
        snake.Toughness.Should().Be(2);
        snake.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flash", "Mystic Snake has Flash (CR 702.8)");

        var ability = snake.Abilities.OfType<TriggeredAbility>().Single();
        ability.TargetRequests.Should().HaveCount(1);

        var bolt = new Instant("Lightning Bolt", "{R}");
        var bobSpell = BobCasts(bolt);

        await ResolveWith(ability, bobSpell);

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "Mystic Snake's ETB counters the chosen target spell (CR 701.5)");
        _stack.GetAll().Should().NotContain(bobSpell);
    }
}
