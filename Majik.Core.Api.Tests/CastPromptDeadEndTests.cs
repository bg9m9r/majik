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
/// CR 601.2 / 732.1 — a human casting a spell is never left at a casting
/// prompt with no legal answer, and can back out of a cast at each casting
/// prompt. Repro from the client's scripted games: DimirMidrange vs Belcher,
/// seed 1, turn 15 — Drown in the Loch offered, the mode prompt arrived with
/// every mode unchoosable and no way to answer or cancel.
/// </summary>
public class CastPromptDeadEndTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private static (GameFacade Facade, Dictionary<string, Land> Lands) Game(params (string Name, CardSubtype Sub)[] lands)
        => Game(Array.Empty<string>(), lands);

    /// <summary><paramref name="realCards"/> are built the production way
    /// (shells in Alice's deck, bound by the facade) and moved to her hand.</summary>
    private static (GameFacade Facade, Dictionary<string, Land> Lands) Game(string[] realCards, params (string Name, CardSubtype Sub)[] lands)
    {
        var deck = Enumerable.Range(0, 20).Select(_ => (ICard)new Land("Wastes"))
            .Concat(realCards.Select(n => DeckCardShellBuilder.Build(Repo.GetByName(n)!)))
            .ToList();
        var facade = GameFacade.Create("Alice", "Bob", deck, deck.Select(_ => (ICard)new Land("Wastes")).ToList(), cardRepo: Repo);
        var map = new Dictionary<string, Land>();
        var n = 0;
        foreach (var (name, sub) in lands)
        {
            var land = new Land(name, supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { sub });
            land.SetOwner(facade.Alice); land.ChangeController(facade.Alice);
            land.SetZone(ZoneType.Battlefield);
            OracleManaBinder.BindBasicLandMana(land, facade.Alice);
            facade.Alice.Zones.Battlefield.AddCard(land);
            map[$"{name}{n++}"] = land;
        }
        foreach (var name in realCards)
        {
            var built = facade.Alice.Zones.Library.GetCards().First(c => c.Name == name);
            facade.Alice.Zones.Library.RemoveCard(built);
            facade.Alice.Zones.Hand.AddCard(built);
            built.SetZone(ZoneType.Hand);
        }
        return (facade, map);
    }

    private static Card InHand(GameFacade facade, Card c)
    {
        c.SetOwner(facade.Alice); c.SetController(facade.Alice);
        facade.Alice.Zones.Hand.AddCard(c); c.SetZone(ZoneType.Hand);
        return c;
    }

    /// <summary>Runs Alice's first main phase: casts <paramref name="card"/>
    /// once, pays with every land, and hands each other Alice prompt to
    /// <paramref name="onCastPrompt"/> (null answer = pass/decline as usual).</summary>
    /// <summary>The card's zone at Alice's first priority prompt after the
    /// cast attempt (before cleanup can discard it).</summary>
    private ZoneType? _zoneAfterCast;

    private async Task<List<PromptDto>> Drive(GameFacade facade, Card card, Func<PromptDto, GameCommand?> onCastPrompt)
    {
        var seen = new List<PromptDto>();
        var cast = false;
        using var sub = facade.SubscribePrompts(p =>
        {
            GameCommand? answer = null;
            if (p.ExpectedKinds.Contains(nameof(MulliganCommand))) answer = new MulliganCommand(Keep: true);
            else if (p.PlayerId != facade.Alice.Id)
            {
                if (p.ExpectedKinds.Contains(nameof(PassPriorityCommand))) answer = new PassPriorityCommand();
            }
            else
            {
                lock (seen) seen.Add(p);
                if (p.ExpectedKinds.Contains(nameof(ChooseManaCommand)))
                    answer = new ChooseManaCommand(facade.Alice.Zones.Battlefield.GetCards().OfType<Land>().Select(l => l.InstanceId).ToList());
                else if (!cast && p.ExpectedKinds.Contains(nameof(CastSpellCommand)))
                {
                    cast = true;
                    answer = new CastSpellCommand(card.InstanceId, Array.Empty<Guid>(), null, null);
                }
                else if (p.ExpectedKinds.Contains(nameof(PassPriorityCommand)))
                {
                    if (cast) _zoneAfterCast ??= card.Zone;
                    answer = new PassPriorityCommand();
                }
                else answer = onCastPrompt(p);
            }
            if (answer != null) _ = facade.SubmitAsync(answer with { PlayerId = p.PlayerId });
        });
        await facade.StartFullGameAsync(firstPlayerSlot: 0, maxTurns: 1, rng: new GameRandom(1));
        await facade.FullGameTask!.WaitAsync(TimeSpan.FromSeconds(30));
        lock (seen) return seen.ToList();
    }

    [Fact]
    public async Task DrownInTheLoch_WithNoSpellAndNoCreature_IsNotOffered_AndAForcedCastRewindsWithoutAModePrompt()
    {
        var (facade, _) = Game(("Island", CardSubtype.Island), ("Swamp", CardSubtype.Swamp));
        var drown = InHand(facade, new Instant("Drown in the Loch", "{U}{B}"));

        var prompts = await Drive(facade, drown, _ => null);

        prompts.First(p => p.LegalActions != null).LegalActions!.CastableCardIds
            .Should().NotContain(drown.InstanceId, "CR 601.2c / 700.2 — no mode has a legal target");
        prompts.Should().NotContain(p => p.ModeOptions != null,
            "a cast whose modes can't be chosen is rewound, not prompted");
        _zoneAfterCast.Should().Be(ZoneType.Hand, "CR 732.1 — the illegal cast is reversed");
        prompts.Should().Contain(p => p.Notice != null && p.Notice.Contains("Drown in the Loch"),
            "the player is told why the cast was reversed");
    }

    [Fact]
    public async Task TheTargetsPrompt_OfACast_AcceptsCancel_AndTheCardGoesBackToHand()
    {
        var (facade, _) = Game(("Mountain", CardSubtype.Mountain));
        var bolt = InHand(facade, new Instant("Lightning Bolt", "{R}"));
        PromptDto? targets = null;

        await Drive(facade, bolt, p =>
        {
            if (!p.ExpectedKinds.Contains(nameof(ChooseTargetsCommand))) return null;
            targets ??= p;
            return new CancelCastCommand();
        });

        targets.Should().NotBeNull();
        targets!.ExpectedKinds.Should().Contain(nameof(CancelCastCommand));
        _zoneAfterCast.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public async Task TheModePrompt_OfACast_AcceptsCancel()
    {
        var (facade, _) = Game(("Island", CardSubtype.Island), ("Mountain", CardSubtype.Mountain));
        var charm = InHand(facade, new Instant("Izzet Charm", "{U}{R}"));
        PromptDto? modes = null;

        await Drive(facade, charm, p =>
        {
            if (p.ModeOptions == null) return null;
            modes ??= p;
            return new CancelCastCommand();
        });

        modes.Should().NotBeNull("Izzet Charm's draw mode is always choosable");
        modes!.ExpectedKinds.Should().Contain(nameof(CancelCastCommand));
        _zoneAfterCast.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public async Task TheXPrompt_OfACast_AcceptsCancel()
    {
        var (facade, _) = Game(("Mountain", CardSubtype.Mountain), ("Mountain", CardSubtype.Mountain));
        var banefire = InHand(facade, new Sorcery("Banefire", "{X}{R}"));
        PromptDto? x = null;

        await Drive(facade, banefire, p =>
        {
            if (!p.ExpectedKinds.Contains(nameof(ChooseXCommand))) return null;
            x ??= p;
            return new CancelCastCommand();
        });

        x.Should().NotBeNull();
        x!.ExpectedKinds.Should().Contain(nameof(CancelCastCommand));
        _zoneAfterCast.Should().Be(ZoneType.Hand);
        facade.Alice.Zones.Battlefield.GetCards().OfType<Land>().Should().OnlyContain(l => !l.IsTapped,
            "X is chosen before anything is paid");
    }

    [Fact]
    public async Task ATargetRequestWithTooFewCandidates_RewindsWithoutPromptingAPoolOnlyAgent()
    {
        var alice = new Majik.Core.Players.Player("Alice", 20);
        var bob = new Majik.Core.Players.Player("Bob", 20);
        var agent = new RemoteAgent(alice);
        var raised = 0;
        agent.PromptRequested += _ => raised++;
        var ctx = new Majik.Core.Game.GameContext(alice, new[] { alice, bob }, alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
        var request = new Majik.Core.Players.Agents.TargetRequest("two target creatures", 2, 2,
            new object[] { new Creature("Lonely Bear", "{1}{G}", 2, 2) });

        var act = () => Majik.Core.Targeting.TargetCollection.CollectAsync(
            new[] { request }, null, ctx, agent, throwOnInsufficient: true);

        await act.Should().ThrowAsync<InvalidOperationException>();
        raised.Should().Be(0, "a prompt that can't be answered legally is never raised");
    }

    [Fact]
    public async Task AModalTrigger_ShipsEachModesAvailability_LikeAModalSpell()
    {
        // CR 700.2 / 603.3c — Knight of Autumn's ETB: "destroy target artifact
        // or enchantment" can't be chosen with none on the battlefield; the
        // other two modes can.
        var (facade, _) = Game(new[] { "Knight of Autumn" },
            ("Forest", CardSubtype.Forest), ("Plains", CardSubtype.Plains), ("Forest", CardSubtype.Forest));
        var knight = (Card)facade.Alice.Zones.Hand.GetCards().First(c => c.Name == "Knight of Autumn");
        PromptDto? modes = null;

        await Drive(facade, knight, p =>
        {
            if (p.ModeOptions == null) return null;
            modes ??= p;
            return new ChooseModeCommand(2);
        });

        modes.Should().NotBeNull("the ETB trigger asks for its mode");
        modes!.ModeOptions!.Select(m => m.IsChoosable).Should().Equal(true, false, true);
        modes.ModeOptions![1].UnavailableReason.Should().Contain("artifact or enchantment");
    }
}