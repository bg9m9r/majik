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

namespace Majik.Core.Tests.Spells;

/// <summary>
/// CR 701.59 — end-to-end coverage for the Bloomburrow "Gift" mechanic
/// via its first implementor, Into the Flood Maw.
///
/// Scenarios:
///   * Cast WITHOUT gift → printed base mode: bounce a Creature an
///     opponent controls. Recipient gets no token. <c>Card.HasGiftPromised</c>
///     stays <c>false</c> across the cast.
///   * Cast WITH gift → upgraded mode: bounce ANY nonland permanent an
///     opponent controls. Recipient gets the tapped 1/1 blue Fish token.
///   * Gift delivery is cast-time (engine v1 deviation from CR 701.59
///     resolve-time delivery, documented on <see cref="Majik.Core.Spells.IGiftClause"/>):
///     the Fish lands on the recipient's battlefield BEFORE the spell
///     resolves and survives a counter spell.
///   * <see cref="Card.HasGiftPromised"/> is cleared after resolve via
///     the cleanup effect appended by <c>SpellCastFlow</c>.
/// </summary>
public class GiftMechanicTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GiftMechanicTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public async Task Cast_NoGiftPromise_BouncesCreature_NoFishDelivered()
    {
        // Bob controls a 2/2 Grizzly Bears he owns.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        // Alice casts Into the Flood Maw, declines the gift.
        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        // No QueueGiftRecipient call — ScriptedAgent defaults to "decline".
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            IntoTheFloodMawFactory.BuildDefinition(_alice, _zones, card),
            agent, ctx,
            alternativeCost: null);

        // No gift was promised — spell carries no recipient.
        spell.GiftRecipient.Should().BeNull();
        card.HasGiftPromised.Should().BeFalse();

        // Bob's board is unchanged before resolution (no token on Bob).
        _bob.Zones.Battlefield.GetCards().Should().NotContain(c => c.Name == "Fish");

        _resolver.ResolveTop(_stack);

        // Base mode: bear bounced.
        bear.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(bear);
    }

    [Fact]
    public async Task Cast_GiftPromised_DeliversTappedFish_AllowsNonlandPermanentTarget()
    {
        // Bob controls an Enchantment (a nonland, NON-creature permanent).
        // Base mode could not target it; gift mode can (CR 701.59 upgrade).
        var stax = new Enchantment("Ghostly Prison", "{2}{W}")
        { Owner = _bob, Controller = _bob };
        stax.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(stax);

        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        var agent = new ScriptedAgent();
        agent.QueueGiftRecipient(_bob);
        agent.QueueTargets(new[] { (object)stax });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            IntoTheFloodMawFactory.BuildDefinition(_alice, _zones, card),
            agent, ctx,
            alternativeCost: null);

        // Gift was promised + stamped.
        spell.GiftRecipient.Should().BeSameAs(_bob);
        card.HasGiftPromised.Should().BeTrue();

        // Fish delivered at cast time on Bob's battlefield, tapped.
        var fish = _bob.Zones.Battlefield.GetCards().OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Fish");
        fish.Should().NotBeNull("the gift Fish token is delivered at cast time");
        fish!.IsToken.Should().BeTrue();
        fish.IsTapped.Should().BeTrue("CR 701.59c — gift Fish enters tapped");
        fish.Power.Should().Be(1);
        fish.Toughness.Should().Be(1);
        CardColors.GetColors(fish).Should().Contain(ManaColor.Blue);
        fish.HasSubtype(CardSubtype.Fish).Should().BeTrue();
        fish.Owner.Should().BeSameAs(_bob);

        _resolver.ResolveTop(_stack);

        // Gift mode: enchantment bounced to Bob's hand.
        stax.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Hand.GetCards().Should().Contain(stax);

        // CR 400.7 — sentinel cleared after resolve so a re-cast / copy
        // / blink starts fresh.
        card.HasGiftPromised.Should().BeFalse(
            because: "SpellCastFlow appends a cleanup effect that clears HasGiftPromised after the printed body runs");
    }

    [Fact]
    public async Task Cast_GiftPromised_ThenCountered_RecipientKeepsFish()
    {
        // Engine v1 deviation: gift delivery is CAST-TIME, so a counter
        // does not undo the token. (See IGiftClause xmldoc.)
        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Pre-seat a placeholder creature so target selection has *something*
        // to chew on; we will not actually resolve.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var agent = new ScriptedAgent();
        agent.QueueGiftRecipient(_bob);
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var spell = await _flow.CastAsync(
            _alice, card,
            IntoTheFloodMawFactory.BuildDefinition(_alice, _zones, card),
            agent, ctx,
            alternativeCost: null);

        // Simulate a counter spell: pop the gift spell off the stack
        // without resolving (mirrors what NegateFactory's resolve does
        // via OracleSpellBinder.RemoveFromStack).
        _stack.Pop().Should().BeSameAs(spell);

        // Fish remains on Bob's battlefield because gift delivery
        // happened at cast time, before the (now-cancelled) resolution.
        var fish = _bob.Zones.Battlefield.GetCards().OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Fish");
        fish.Should().NotBeNull(
            "engine v1 delivers the gift at cast time, so it survives a counter");
        fish!.IsToken.Should().BeTrue();

        // The base spell's bounce never resolved — bear is still on Bob's board.
        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void IGiftClause_DescribedByFactory_IsTappedFishToken()
    {
        // The Description surface is what an agent UI prompts the user
        // with. Pin it so a future copy-edit doesn't drift the contract.
        var card = IntoTheFloodMawFactory.Create(_alice);
        card.Should().BeAssignableTo<Majik.Core.Spells.IGiftClause>(
            because: "Into the Flood Maw is the first Bloomburrow Gift card; the gift hook lives on the card instance");
        ((Majik.Core.Spells.IGiftClause)card).Description
            .Should().Be(IntoTheFloodMawFactory.GiftDescription);
    }

    [Fact]
    public async Task HeuristicBot_DefaultsToPromiseGift_MostAggressive()
    {
        // Bot agent defaults to promising the gift — the upgraded mode
        // is strictly better than the base mode, so the small "give
        // opponent a 1/1 Fish" cost is dominated.
        var card = IntoTheFloodMawFactory.Create(_alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);

        // Bob has BOTH a creature AND an enchantment. Base mode could
        // only see the creature. Gift mode can see either.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var stax = new Enchantment("Ghostly Prison", "{2}{W}")
        { Owner = _bob, Controller = _bob };
        stax.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(stax);

        // PLAN 01 (Slice G) — the bespoke ChooseGiftRecipientAsync is gone.
        // The gift recipient is now an optional declarative PickOne over the
        // opponent pool, handed to the single ChooseAsync sink. Drive that
        // request directly: the bot's ChooseAsync returns the first opponent
        // for a non-empty optional pick, preserving its most-aggressive
        // "promise the gift" posture.
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);
        var giftRequest = new Majik.Core.Players.Agents.ChoiceRequest(
            Majik.Core.Players.Agents.ChoiceKind.PickOne,
            IntoTheFloodMawFactory.GiftDescription, Min: 0, Max: 1,
            Candidates: new object[] { _bob },
            Intent: Majik.Core.Cards.BotIntent.None, Optional: true);
        var chosen = await bot.ChooseAsync(ctx, giftRequest);
        chosen.Should().ContainSingle().Which.Should().BeSameAs(_bob,
            because: "the bot's gift heuristic defaults to promising — the upgrade is strictly better than the base mode");
    }
}
