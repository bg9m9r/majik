using FluentAssertions;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 601.2c — the live priority prompt only offers a spell whose required
/// targets exist. The turn driver's priority contexts carry the cast
/// dispatcher's definition lookup, so on a full game a Counterspell with
/// nothing on the stack is not castable while an untargeted spell of the same
/// cost is.
/// </summary>
public class LegalActionsTargetFeasibilityFacadeTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    [Fact]
    public async Task Counterspell_WithAnEmptyStack_IsNotOffered()
    {
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Forest")).ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.Select(_ => (ICard)new Land("Forest")).ToList(), cardRepo: Repo);
        var alice = facade.Alice;
        for (var i = 0; i < 2; i++)
        {
            var island = new Land("Island", supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { CardSubtype.Island });
            island.SetOwner(alice); island.ChangeController(alice);
            island.SetZone(ZoneType.Battlefield);
            OracleManaBinder.BindBasicLandMana(island, alice);
            alice.Zones.Battlefield.AddCard(island);
        }
        Card InHand(Card c) { c.SetOwner(alice); c.SetController(alice); alice.Zones.Hand.AddCard(c); c.SetZone(ZoneType.Hand); return c; }
        var counterspell = InHand(new Instant("Counterspell", "{U}{U}"));
        var think = InHand(new Instant("Think Twice", "{1}{U}"));

        LegalActionsDto? legal = null;
        using var sub = facade.SubscribePrompts(p =>
        {
            GameCommand answer;
            if (p.ExpectedKinds.Contains(nameof(MulliganCommand))) answer = new MulliganCommand(Keep: true);
            else if (p.ExpectedKinds.Contains(nameof(PassPriorityCommand)))
            {
                if (p.PlayerId == alice.Id && p.LegalActions != null) legal ??= p.LegalActions;
                answer = new PassPriorityCommand();
            }
            else return;
            _ = facade.SubmitAsync(answer with { PlayerId = p.PlayerId });
        });

        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 1, rng: new GameRandom(1));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(30));

        legal.Should().NotBeNull();
        legal!.CastableCardIds.Should().Contain(think.InstanceId, "the mana is there and it needs no target");
        legal.CastableCardIds.Should().NotContain(counterspell.InstanceId,
            "CR 601.2c — there is no spell for Counterspell to target");
    }
}