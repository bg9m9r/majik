using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pay-down for the <c>twins-of-maurer-estate-kitchen-imp-vanilla-madness-bodies</c>
/// deferral — the vanilla Madness card BODIES whose madness line already works
/// intrinsically (CR 702.35 via <see cref="MadnessCatalog"/> + the
/// <c>Fx.DiscardCard</c> funnel). Each card is a JSON def / CardDef-DSL shape +
/// thin <c>[CardName]</c> factory mapping onto an already-shipped primitive:
///
///   - Twins of Maurer Estate — vanilla 3/5 Vampire (no abilities).
///   - Kitchen Imp — 2/2 Imp with Flying + Haste.
///   - Ichor Slick — Sorcery, "target creature gets -3/-3 until end of turn".
///   - Just the Wind — Instant, "return target creature to its owner's hand".
///   - Terminal Agony — Sorcery, "destroy target creature".
///   - Nagging Thoughts — Sorcery, look at top two, one to hand, the other to GY.
///
/// (Murderous Compulsion — the seventh unblocked card — was already implemented;
/// not re-tested here.) Madness fires the instant the factory ships, so each
/// card is also confirmed present in <see cref="MadnessCatalog"/>.
/// </summary>
public class TwinsKitchenImpVanillaMadnessBodiesTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TwinsKitchenImpVanillaMadnessBodiesTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Madness intrinsic — every card stays catalogued (CR 702.35)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Twins of Maurer Estate", "{2}{B}")]
    [InlineData("Kitchen Imp", "{B}")]
    [InlineData("Ichor Slick", "{3}{B}")]
    [InlineData("Just the Wind", "{U}")]
    [InlineData("Terminal Agony", "{B}{R}")]
    [InlineData("Nagging Thoughts", "{1}{U}")]
    public void MadnessCatalog_Contains_EachUnblockedCard(string name, string madnessCost)
    {
        var card = NamedCardFactory.Create(name, _alice);
        MadnessCatalog.HasMadness(card).Should().BeTrue(
            $"{name} is catalogued — madness fires intrinsically (CR 702.35)");
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse(madnessCost),
            $"{name}'s catalogued madness cost is {madnessCost}");
    }

    // -----------------------------------------------------------------------
    // Twins of Maurer Estate — vanilla 3/5 Vampire
    // -----------------------------------------------------------------------

    [Fact]
    public void TwinsOfMaurerEstate_Identity_Vanilla()
    {
        var c = (Creature)NamedCardFactory.Create("Twins of Maurer Estate", _alice);

        c.Name.Should().Be("Twins of Maurer Estate");
        c.ManaCost.Should().Be("{4}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty("vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Kitchen Imp — 2/2 Imp, Flying + Haste
    // -----------------------------------------------------------------------

    [Fact]
    public void KitchenImp_Identity_FlyingHaste()
    {
        var c = (Creature)NamedCardFactory.Create("Kitchen Imp", _alice);

        c.Name.Should().Be("Kitchen Imp");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Imp).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying", "CR 702.9");
        keywords.Should().Contain("Haste", "CR 702.10");

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Ichor Slick — Sorcery, target creature gets -3/-3 until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void IchorSlick_Identity()
    {
        var c = NamedCardFactory.Create("Ichor Slick", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Ichor Slick");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void IchorSlick_AppliesMinus3Minus3_UntilEndOfTurn()
    {
        // 4/4 → 1/1 after -3/-3 (CR 613 Layer 7c, CR 514.2).
        var target = new Creature("Serra Angel", "{3}{W}{W}", 4, 4)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var def = IchorSlickFactory.BuildDefinition();
        ResolveSpellOn(def, target);

        target.Power.Should().Be(1, "Serra Angel 4/4 with -3/-3 → 1/1");
        target.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Just the Wind — Instant, return target creature to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void JustTheWind_Identity()
    {
        var c = NamedCardFactory.Create("Just the Wind", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Just the Wind");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public async Task JustTheWind_ReturnsTargetCreatureToOwnersHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var card = JustTheWindFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, card, JustTheWindFactory.BuildDefinition(_zones), agent, ctx, alternativeCost: null);
        _resolver.ResolveTop(_stack);

        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Terminal Agony — Sorcery, destroy target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void TerminalAgony_Identity()
    {
        var c = NamedCardFactory.Create("Terminal Agony", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Terminal Agony");
        c.ManaCost.Should().Be("{2}{B}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public async Task TerminalAgony_DestroysTargetCreature()
    {
        var ogre = new Creature("Hill Giant", "{4}{R}", 3, 3) { Owner = _bob, Controller = _bob };
        ogre.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ogre);

        var card = TerminalAgonyFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)ogre });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, card, TerminalAgonyFactory.BuildDefinition(), agent, ctx, alternativeCost: null);
        _resolver.ResolveTop(_stack);

        ogre.Zone.Should().Be(ZoneType.Graveyard, "Terminal Agony destroys the target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(ogre);
    }

    // -----------------------------------------------------------------------
    // Nagging Thoughts — look at top two, one to hand, the other to graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void NaggingThoughts_Identity()
    {
        var c = NamedCardFactory.Create("Nagging Thoughts", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Nagging Thoughts");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public async Task NaggingThoughts_OneToHand_OtherToGraveyard()
    {
        var top1 = new Sorcery("Lava Spike", "{R}") { Owner = _alice, Controller = _alice };
        var top2 = new Sorcery("Rift Bolt", "{2}{R}") { Owner = _alice, Controller = _alice };
        top1.SetZone(ZoneType.Library);
        top2.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(top1);
        _alice.Zones.Library.AddCard(top2);

        var card = NaggingThoughtsFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, card, NaggingThoughtsFactory.BuildDefinition(), agent, ctx, alternativeCost: null);
        _resolver.ResolveTop(_stack);

        // Exactly one of the looked-at cards is in hand, the other in graveyard
        // (CR 116.1b — controller's choice; agentless harness auto-picks the
        // first eligible card). Net: hand +1, graveyard +1, none left on library.
        var inHand = new[] { top1, top2 }.Count(c => c.Zone == ZoneType.Hand);
        var inGrave = new[] { top1, top2 }.Count(c => c.Zone == ZoneType.Graveyard);
        inHand.Should().Be(1, "exactly one of the top two goes to hand");
        inGrave.Should().Be(1, "the other goes to the graveyard");
        _alice.Zones.Library.GetCards().Should().NotContain(top1).And.NotContain(top2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve a non-zone-moving target spell (the -3/-3 pump) directly: build
    /// the SpellDefinition's effects for a hand-supplied creature target and run
    /// them. Mirrors LastGaspTests.Resolve.
    /// </summary>
    private static void ResolveSpellOn(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }
    }
}
