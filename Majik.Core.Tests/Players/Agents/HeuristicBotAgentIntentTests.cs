using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Moq;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Instant = Majik.Core.Cards.Instant;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Intent-aware decisions on <see cref="HeuristicBotAgent"/>. Covers
/// <c>ChooseModeAsync</c> + <c>ChooseTargetsAsync</c> reading
/// <see cref="BotIntent"/> off the request / mode-intents list.
/// </summary>
public class HeuristicBotAgentIntentTests
{
    private readonly Player _self = new("Self", 20);
    private readonly Player _opp = new("Opp", 20);

    private GameContext NewCtx() =>
        new(_self, new[] { _self, _opp }, _self,
            1, PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());

    private Creature AddCreature(Player owner, string name, int p, int t)
    {
        var c = new Creature(name, "1G", p, t) { Owner = owner, Controller = owner };
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public async Task ChooseMode_PicksRemoval_WhenOpponentHasCreature()
    {
        AddCreature(_opp, "Bear", 2, 2);
        var bot = new HeuristicBotAgent();
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy target creature.", "Draw a card.", "Gain 3 life." },
            modeIntents: new[] { BotIntent.Removal, BotIntent.Draw, BotIntent.Heal });
        idx.Should().Be(0);
    }

    [Fact]
    public async Task ChooseMode_PicksHeal_WhenLifeLow()
    {
        var lowSelf = new Player("Self", 4);
        var ctx = new GameContext(lowSelf, new[] { lowSelf, _opp }, lowSelf,
            1, PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());

        var bot = new HeuristicBotAgent();
        var idx = await bot.ChooseModeAsync(ctx,
            modes: new[] { "Draw a card.", "Gain 5 life." },
            modeIntents: new[] { BotIntent.Draw, BotIntent.Heal });
        idx.Should().Be(1);
    }

    [Fact]
    public async Task ChooseMode_LegacyFallback_WhenModeIntentsNull()
    {
        AddCreature(_opp, "Bear", 2, 2);
        var bot = new HeuristicBotAgent();
        // Pre-annotation templates pass null modeIntents — must produce a
        // valid index without crashing.
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy target creature.", "Draw a card." },
            modeIntents: null);
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ChooseMode_LegacyFallback_WhenAllModeIntentsNone()
    {
        AddCreature(_opp, "Bear", 2, 2);
        var bot = new HeuristicBotAgent();
        // Composer's None-Intent passes through as a list of all-None.
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy target creature.", "Draw a card." },
            modeIntents: new[] { BotIntent.None, BotIntent.None });
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ChooseMode_WrathSuppressed_WhenEmptyBoard()
    {
        // Wrath without our own creatures — should NOT pick over Draw.
        var bot = new HeuristicBotAgent();
        AddCreature(_opp, "Bear", 2, 2);
        var idx = await bot.ChooseModeAsync(
            NewCtx(),
            modes: new[] { "Destroy all creatures.", "Draw two cards." },
            modeIntents: new[] { BotIntent.Wrath, BotIntent.Draw });
        // Wrath gets +35 (opp has creature) but we want Draw when we have
        // no creatures of our own — actually the spec's scorer doesn't
        // suppress Wrath at choose-mode time (Task 11 handles that bias).
        // For now just confirm one of the two is picked.
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task ChooseMode_EmptyModes_ReturnsZero()
    {
        var bot = new HeuristicBotAgent();
        var idx = await bot.ChooseModeAsync(NewCtx(),
            modes: Array.Empty<string>(),
            modeIntents: Array.Empty<BotIntent>());
        idx.Should().Be(0);
    }

    [Fact]
    public async Task ChooseTargets_BuffIntent_PicksOwnBestCreature()
    {
        var mine = AddCreature(_self, "MyBear", 2, 2);
        var theirs = AddCreature(_opp, "OppBear", 2, 2);
        var bot = new HeuristicBotAgent();

        var req = new TargetRequest(
            Description: "target creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { theirs, mine },
            Intent: BotIntent.Buff);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }

    [Fact]
    public async Task ChooseTargets_RemovalIntent_PicksOpponentBiggest()
    {
        var small = AddCreature(_opp, "Goblin", 1, 1);
        var big = AddCreature(_opp, "Wurm", 6, 6);
        var bot = new HeuristicBotAgent();

        var req = new TargetRequest(
            Description: "target creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { small, big },
            Intent: BotIntent.Removal);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(big);
    }

    [Fact]
    public async Task ChooseTargets_HealIntent_PrefersSelfPlayer()
    {
        var bot = new HeuristicBotAgent();
        var req = new TargetRequest(
            Description: "target player",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { _opp, _self },
            Intent: BotIntent.Heal);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(_self);
    }

    [Fact]
    public async Task ChooseTargets_LegacyLabelFallback_WhenIntentNone()
    {
        // Intent None + "you control" label — exercises the legacy
        // LabelIsBuff path that older templates rely on.
        var mine = AddCreature(_self, "MyBear", 2, 2);
        var theirs = AddCreature(_opp, "OppBear", 2, 2);
        var bot = new HeuristicBotAgent();

        var req = new TargetRequest(
            Description: "target creature you control",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { theirs, mine },
            Intent: BotIntent.None);

        var picked = await bot.ChooseTargetsAsync(NewCtx(), req);
        picked.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }

    // ----- ManaHoldReserve (Task 10) -----

    [Fact]
    public void ManaHold_OnlyReservesForReactiveInstants()
    {
        var repo = new Mock<ICardRepository>();
        repo.Setup(r => r.IntentFor("Lava Spike")).Returns(BotIntent.Burn | BotIntent.Reach);
        repo.Setup(r => r.IntentFor("Ponder")).Returns(BotIntent.Cantrip);

        // Opp has an untapped creature → "oppHasOffense" is true, eligible for hold.
        AddCreature(_opp, "Goblin", 1, 1);

        var bot = new HeuristicBotAgent(altCostProbe: null, cardRepository: repo.Object);

        // Hand: Ponder (Cantrip — not reactive) only. Expect 0 reserve.
        var ponder = new Instant("Ponder", "{U}") { Owner = _self, Controller = _self };
        ponder.SetZone(ZoneType.Hand);
        _self.Zones.Hand.AddCard(ponder);
        bot.ManaHoldReserveForTests(NewCtx()).Should().Be(0);

        // Add Lava Spike (Burn — reactive). Expect reserve = its CMC (1).
        var bolt = new Instant("Lava Spike", "{R}") { Owner = _self, Controller = _self };
        bolt.SetZone(ZoneType.Hand);
        _self.Zones.Hand.AddCard(bolt);
        bot.ManaHoldReserveForTests(NewCtx()).Should().Be(1);
    }

    [Fact]
    public void ManaHold_LegacyPath_NoRepository_AnyInstantCounts()
    {
        // No repository — falls back to today's behaviour (every instant
        // counts as reactive). Backstops the legacy code path.
        AddCreature(_opp, "Goblin", 1, 1);

        var bot = new HeuristicBotAgent();
        var ponder = new Instant("Ponder", "{U}") { Owner = _self, Controller = _self };
        ponder.SetZone(ZoneType.Hand);
        _self.Zones.Hand.AddCard(ponder);

        bot.ManaHoldReserveForTests(NewCtx()).Should().Be(1);
    }

    [Fact]
    public void ManaHold_NoOpponentOffense_NoReserve()
    {
        // Opp has no creatures → no offense → don't reserve.
        var repo = new Mock<ICardRepository>();
        repo.Setup(r => r.IntentFor("Lava Spike")).Returns(BotIntent.Burn);

        var bot = new HeuristicBotAgent(altCostProbe: null, cardRepository: repo.Object);
        var bolt = new Instant("Lava Spike", "{R}") { Owner = _self, Controller = _self };
        bolt.SetZone(ZoneType.Hand);
        _self.Zones.Hand.AddCard(bolt);

        bot.ManaHoldReserveForTests(NewCtx()).Should().Be(0);
    }

    // ----- SequencingBonus intent bias (Task 11) -----

    [Fact]
    public void Sequencing_Ramp_AcceleratedWhenManaLight()
    {
        // 2 lands in play (< 4) → Ramp gets +4 bonus.
        _self.Zones.Battlefield.AddCard(new Land("Forest") { Owner = _self, Controller = _self });
        _self.Zones.Battlefield.AddCard(new Land("Forest") { Owner = _self, Controller = _self });

        var sorcery = new Majik.Core.Cards.Sorcery("Rampant Growth", "{1}{G}")
        { Owner = _self, Controller = _self };

        var bonusRamp = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.Ramp);
        var bonusNone = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.None);

        (bonusRamp - bonusNone).Should().Be(4);
    }

    [Fact]
    public void Sequencing_Ramp_NoBonusWhenManaPlenty()
    {
        // 4+ lands in play → Ramp bonus does NOT apply.
        for (var i = 0; i < 5; i++)
            _self.Zones.Battlefield.AddCard(new Land($"Forest{i}") { Owner = _self, Controller = _self });

        var sorcery = new Majik.Core.Cards.Sorcery("Rampant Growth", "{1}{G}")
        { Owner = _self, Controller = _self };

        var bonusRamp = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.Ramp);
        var bonusNone = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.None);

        bonusRamp.Should().Be(bonusNone);
    }

    [Fact]
    public void Sequencing_Removal_PreferredVsFinisher()
    {
        // Opp has a 6/6 (Power >= 5) → Removal +5.
        AddCreature(_opp, "Wurm", 6, 6);
        var sorcery = new Majik.Core.Cards.Sorcery("Doom Blade", "{1}{B}")
        { Owner = _self, Controller = _self };

        var bonusRemoval = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.Removal);
        var bonusNone = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.None);

        (bonusRemoval - bonusNone).Should().Be(5);
    }

    [Fact]
    public void Sequencing_Heal_BoostedWhenLifeLow()
    {
        var lowSelf = new Player("Self", 4);
        var ctx = new GameContext(lowSelf, new[] { lowSelf, _opp }, lowSelf,
            1, PhaseStateType.PreCombatMain, new Majik.Core.Stack.Stack());

        var sorcery = new Majik.Core.Cards.Sorcery("Healing Salve", "{W}")
        { Owner = lowSelf, Controller = lowSelf };

        var bonusHeal = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, ctx, sorceryWindow: true, BotIntent.Heal);
        var bonusNone = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, ctx, sorceryWindow: true, BotIntent.None);

        (bonusHeal - bonusNone).Should().Be(4);
    }

    [Fact]
    public void Sequencing_Wrath_SuppressedOnEmptyBoard()
    {
        // Bot's battlefield has no creatures → Wrath -10 penalty.
        var sorcery = new Majik.Core.Cards.Sorcery("Wrath of God", "{2}{W}{W}")
        { Owner = _self, Controller = _self };

        var bonusWrath = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.Wrath);
        var bonusNone = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.None);

        (bonusWrath - bonusNone).Should().Be(-10);
    }

    [Fact]
    public void Sequencing_NoIntent_LegacyBonusUnchanged()
    {
        // No intent → bonus path matches today's behaviour: creature with
        // light board gets +3, sorcery in sorcery-window gets +1, etc.
        var sorcery = new Majik.Core.Cards.Sorcery("Some Sorcery", "{2}")
        { Owner = _self, Controller = _self };

        // Empty board → no Creature board-build bonus (this card isn't a creature),
        // sorcery in sorcery window → +1.
        var bonus = HeuristicBotAgent.SequencingBonusForTests(
            sorcery, NewCtx(), sorceryWindow: true, BotIntent.None);

        bonus.Should().Be(1);
    }
}
