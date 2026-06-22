using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Karn Liberated (New Phyrexia, {7}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Karn subtype, loyalty 6,
///     mana cost {7}).
///   - Loyalty ability shape: three abilities at +4 / -3 / -14.
///   - Mechanic: +4 target opponent exiles a card from their hand
///     (auto-pick).
///   - Mechanic: -3 exile target opponent's creature (auto-pick).
///   - -14 ultimate: shape only — the loyalty ability exists at -14
///     cost and pays its loyalty even though the effect body is a
///     deferred no-op (restart-the-game is engine-foundational and not
///     shipped in this slice).
///   - NamedCardFactory dispatch.
/// </summary>
public class KarnLiberatedTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Karn_IsLegendaryPlaneswalker_Karn_6Loyalty_AtCost7()
    {
        var karn = KarnLiberatedFactory.Create(_alice);

        karn.Name.Should().Be("Karn Liberated");
        karn.ManaCost.Should().Be("{7}");
        karn.HasType(CardType.Planeswalker).Should().BeTrue();
        karn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        karn.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        karn.Loyalty.Should().Be(6);
        karn.StartingLoyalty.Should().Be(6);
        karn.Owner.Should().BeSameAs(_alice);
        karn.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Karn_HasThreeLoyaltyAbilities_Plus4_Minus3_Minus14()
    {
        var karn = KarnLiberatedFactory.Create(_alice);
        var loyaltyAbilities = karn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +4, -3, -14 });
    }

    [Fact]
    public async Task Karn_Plus4_AgentChoosesTargetPlayer_WhoExilesACardFromHand()
    {
        // PROD-PATH verification: the activating player's agent CHOOSES the
        // target player via ChoosePlayerAsync (read off the resolution
        // context), rather than the old captured-resolver first-opponent
        // shortcut. Three players so the choice is meaningful — the agent
        // picks Carol (NOT Bob, the first opponent).
        var carol = new Player("Carol", 20);

        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var carolCard = new Card("Carol spell", "G") { Owner = carol };
        carol.Zones.Hand.AddCard(carolCard);
        carolCard.SetZone(ZoneType.Hand);

        var karn = KarnLiberatedFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(karn);
        karn.SetZone(ZoneType.Battlefield);

        // The agent picks the SECOND opponent (Carol), proving the pick is
        // agent-driven, not first-eligible.
        var agent = new ScriptedAgent();
        agent.QueueChoice(candidates =>
        {
            var pick = candidates.OfType<Player>().FirstOrDefault(p => ReferenceEquals(p, carol));
            return pick is null ? System.Array.Empty<object>() : new object[] { pick };
        });

        var plus4 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +4);
        await ResolveLoyaltyAsync(plus4, agent, GameFor(_alice, _alice, _bob, carol));

        karn.Loyalty.Should().Be(10, "6 + 4 = 10");

        carol.Zones.Hand.GetCards().Should().NotContain(carolCard,
            "the agent chose Carol as the target player");
        carol.Zones.Exile.GetCards().Should().Contain(carolCard);
        carolCard.Zone.Should().Be(ZoneType.Exile);

        _bob.Zones.Hand.GetCards().Should().Contain(bobCard,
            "Bob (the FIRST opponent) was not chosen — the old shortcut would have exiled his card");
    }

    [Fact]
    public void Karn_Minus3_ExileTargetPermanent()
    {
        // Bob controls a creature; Karn's -3 exiles it.
        var victim = new Creature("Goblin", "R", 1, 1);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var karn = KarnLiberatedFactory.Create(
            _alice,
            targetResolver: () => new[] { (Permanent)victim });
        _alice.Zones.Battlefield.AddCard(karn);
        karn.SetZone(ZoneType.Battlefield);

        var minus3 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -3);
        minus3.Activate();

        karn.Loyalty.Should().Be(3, "6 - 3 = 3");

        _bob.Zones.Battlefield.GetCards().Should().NotContain(victim);
        _bob.Zones.Exile.GetCards().Should().Contain(victim);
        victim.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Karn_Minus14_Ultimate_IsDeferredNoOp_StillPaysLoyaltyCost()
    {
        // Set loyalty up so -14 can legally activate (need ≥ 14).
        var karn = KarnLiberatedFactory.Create(_alice);
        karn.AddLoyalty(8); // 6 → 14

        var ultimate = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -14);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        karn.Loyalty.Should().Be(0,
            "14 - 14 = 0; loyalty change applies even when the restart-the-game body is deferred (CR 606.3)");
    }

    [Fact]
    public void Karn_Plus4_NoLiveGameContext_LoyaltyStillTicksUp()
    {
        // Shape-only legacy sync path (ResolutionContext.Legacy — no live
        // game). The +4 effect no-ops because it can't read a player pool off
        // the context, but the loyalty change still applies (CR 606.3).
        var karn = KarnLiberatedFactory.Create(_alice);

        // Give Bob a card in hand; the context-less body should leave it alone.
        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var plus4 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +4);
        plus4.Activate();

        karn.Loyalty.Should().Be(10);
        _bob.Zones.Hand.GetCards().Should().Contain(bobCard,
            "no live game context → exile-from-hand effect is a silent no-op");
    }

    // -------------------------------------------------------------------
    // Helpers — drive a loyalty ability through the PROD async resolution
    // path (pay the loyalty cost, then run the effects against a live
    // ResolutionContext carrying the activating player's agent + game).
    // -------------------------------------------------------------------

    private static GameContext GameFor(Player self, params Player[] all) =>
        new(self, all, self, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

    private static async Task ResolveLoyaltyAsync(
        LoyaltyAbility ability, IPlayerAgent agent, GameContext game)
    {
        ability.PayLoyaltyCost();
        var ctx = ResolutionContext.For(game.Self, agent, game, chosenTargets: null);
        foreach (var e in ability.Effects)
        {
            await e.ExecuteAsync(ctx);
        }
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KarnLiberated()
    {
        var card = NamedCardFactory.Create("Karn Liberated", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Karn Liberated");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(6);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
