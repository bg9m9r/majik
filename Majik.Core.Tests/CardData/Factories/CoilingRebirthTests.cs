using System.Linq;
using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Coiling Rebirth (Duskmourn: House of Horror,
/// {3}{B}{B}).
///
/// Printed oracle (verified against Scryfall 2026-06-24):
///   "Gift a card (You may promise an opponent a gift as you cast this spell.
///    If you do, they draw a card before its other effects.)
///    Return target creature card from your graveyard to the battlefield.
///    Then if the gift was promised and that creature isn't legendary, create
///    a token that's a copy of that creature, except it's 1/1."
///
/// Unique behaviour under test (gift-conditional 1/1 copy token rider on top of
/// a graveyard-scoped reanimation):
///   * No gift → reanimate the target creature; NO token created.
///   * Gift promised + non-legendary creature → recipient draws a card at cast
///     time (CR 701.59), the creature is reanimated, AND a 1/1 copy token of it
///     is created under the caster (CR 706.10 "except it's 1/1").
///   * Gift promised + LEGENDARY creature → reanimated, but NO token ("that
///     creature isn't legendary", CR 205.4a).
///   * Identity assert (non-vanilla mana cost): Sorcery, {3}{B}{B}, black, mv 5.
/// </summary>
[Trait("Color", "B")]
public class CoilingRebirthTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CoilingRebirthTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void Create_HasSorceryShape_Black_FiveManaValue()
    {
        var card = CoilingRebirthFactory.Create(_alice);

        card.Name.Should().Be("Coiling Rebirth");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCost.Should().Be("{3}{B}{B}");
        card.ManaCostValue.TotalValue.Should().Be(5, because: "{3}{B}{B} = mana value 5");
    }

    [Fact]
    public async Task NoGift_ReanimatesTarget_NoTokenCreated()
    {
        // Alice has a non-legendary creature card in her graveyard.
        var beast = NewCreatureInGraveyard(_alice, "Wild Beast", "{2}{G}", 3, 3);

        var card = CoilingRebirthFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        // Decline the gift; target the beast in the graveyard.
        agent.QueueTargets(new[] { (object)beast });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            CoilingRebirthFactory.BuildDefinition(_alice, card, _zones),
            agent, ctx,
            alternativeCost: null);

        spell.GiftRecipient.Should().BeNull();
        card.HasGiftPromised.Should().BeFalse();

        _resolver.ResolveTop(_stack);

        // CR 701.20 — beast is reanimated under Alice's control.
        beast.Zone.Should().Be(ZoneType.Battlefield);
        beast.Controller.Should().Be(_alice);

        // No gift → no token. The battlefield holds only the reanimated beast.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().ContainSingle()
            .Which.Should().BeSameAs(beast);
    }

    [Fact]
    public async Task GiftPromised_NonLegendary_ReanimatesAndCreatesOneOneCopy()
    {
        var beast = NewCreatureInGraveyard(_alice, "Wild Beast", "{2}{G}", 3, 3);

        // Give Bob a library card so the gift draw has something to pull.
        var libCard = new Instant("Opt", "{U}") { Owner = _bob, Controller = _bob };
        libCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libCard);
        var bobHandBefore = _bob.Zones.Hand.GetCards().Count();

        var card = CoilingRebirthFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueGiftRecipient(_bob);
        agent.QueueTargets(new[] { (object)beast });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            CoilingRebirthFactory.BuildDefinition(_alice, card, _zones),
            agent, ctx,
            alternativeCost: null);

        spell.GiftRecipient.Should().BeSameAs(_bob);
        card.HasGiftPromised.Should().BeTrue();

        // CR 701.59 — "they draw a card" at cast time (engine v1 cast-time gift).
        _bob.Zones.Hand.GetCards().Count().Should().Be(bobHandBefore + 1);
        _bob.Zones.Hand.GetCards().Should().Contain(libCard);

        _resolver.ResolveTop(_stack);

        // Reanimated original is the full 3/3 Wild Beast.
        beast.Zone.Should().Be(ZoneType.Battlefield);
        beast.Controller.Should().Be(_alice);

        // CR 706.10 — gift rider creates a 1/1 copy token under Alice. The
        // battlefield now holds the original 3/3 plus the 1/1 token copy.
        var creatures = _alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        creatures.Should().HaveCount(2);

        var token = creatures.Single(c => c.IsToken);
        token.Name.Should().Be("Wild Beast", because: "the token is a copy of that creature");
        token.BasePower.Should().Be(1, because: "except it's 1/1");
        token.BaseToughness.Should().Be(1, because: "except it's 1/1");
        token.Controller.Should().Be(_alice, because: "CR 707.2 — the copy's controller is the caster");
    }

    [Fact]
    public async Task GiftPromised_LegendaryCreature_ReanimatesButNoToken()
    {
        // Legendary creature card in Alice's graveyard — the token rider is
        // suppressed ("that creature isn't legendary", CR 205.4a).
        var legend = new Creature("Tarmo, the Tireless", "{1}{G}", 4, 4,
            supertypes: new[] { CardSupertype.Legendary })
        { Owner = _alice, Controller = _alice };
        legend.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(legend);
        legend.HasSupertype(CardSupertype.Legendary).Should().BeTrue();

        var libCard = new Instant("Opt", "{U}") { Owner = _bob, Controller = _bob };
        libCard.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libCard);

        var card = CoilingRebirthFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueGiftRecipient(_bob);
        agent.QueueTargets(new[] { (object)legend });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            CoilingRebirthFactory.BuildDefinition(_alice, card, _zones),
            agent, ctx,
            alternativeCost: null);

        card.HasGiftPromised.Should().BeTrue();

        _resolver.ResolveTop(_stack);

        // Reanimated, but the legendary clause blocks the token (CR 205.4a).
        legend.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().ContainSingle()
            .Which.Should().BeSameAs(legend, because: "no token is created for a legendary creature");
    }

    private Creature NewCreatureInGraveyard(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness)
        { Owner = owner, Controller = owner };
        c.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(c);
        return c;
    }
}
