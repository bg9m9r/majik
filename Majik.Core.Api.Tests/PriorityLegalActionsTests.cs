using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Coverage for <see cref="PriorityLegalActions"/> — the per-object legal
/// priority actions shipped on the human seat's priority prompt
/// (<see cref="PromptDto.LegalActions"/>). Unlike the coarse
/// <see cref="PriorityKinds"/> list, these are the exact lands / cards /
/// abilities the client makes clickable, so the assertions are about
/// exclusion as much as inclusion.
/// </summary>
public class PriorityLegalActionsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PriorityLegalActionsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    private GameContext Ctx(Player active, StepStateType phase, bool landPlayAvailable) =>
        new(_alice, new[] { _alice, _bob }, activePlayer: active,
            turnNumber: 3, phase, _stack, landPlayAvailable: landPlayAvailable);

    private T InHand<T>(T card) where T : Card
    {
        card.SetOwner(_alice);
        card.SetController(_alice);
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        return card;
    }

    private T OnBattlefield<T>(T permanent) where T : Permanent
    {
        permanent.SetOwner(_alice);
        permanent.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    private Land UntappedBasic(string name, string color)
    {
        var land = OnBattlefield(new Land(name));
        land.AddAbility(new ManaAbility(land, _alice, ManaCost.Parse(color)));
        return land;
    }

    private Creature ReadyCreature(string name, bool summoningSick)
    {
        var creature = OnBattlefield(new Creature(name, "{1}{G}", 2, 2));
        creature.HasSummoningSickness = summoningSick;
        return creature;
    }

    private static ActivatedAbility TapAbility(Creature source, Player controller) =>
        new(source, controller, costs: new ICost[] { AdditionalCost.Tap(source) });

    [Fact]
    public void OwnMainPhase_EmptyStack_OneUntappedLand_ListsLandAndAffordableSpells_NotUnaffordable()
    {
        // CR 305.1 / 305.2 — own main phase, empty stack, land drop available:
        // the land in hand is playable. CR 307.1 — sorcery-speed window, so
        // creatures / sorceries are castable when payable from the one
        // untapped Mountain (CR 601.2g / 601.2h).
        var mountain = UntappedBasic("Mountain", "R");
        var landInHand = InHand(new Land("Mountain"));
        var goblin = InHand(new Creature("Goblin Guide", "{R}", 2, 2));
        var bolt = InHand(new Instant("Lightning Bolt", "{R}"));
        var giant = InHand(new Creature("Hill Giant", "{3}{R}", 3, 3));
        var drake = InHand(new Creature("Wind Drake", "{U}", 2, 2));

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: true));

        legal.PlayableLandIds.Should().Equal(landInHand.InstanceId);
        legal.CastableCardIds.Should().BeEquivalentTo(new[] { goblin.InstanceId, bolt.InstanceId });
        legal.CastableCardIds.Should().NotContain(giant.InstanceId,
            "{3}{R} is plainly unaffordable from one Mountain");
        legal.CastableCardIds.Should().NotContain(drake.InstanceId,
            "{U} cannot be paid with the only source producing {R}");
        legal.ActivatableAbilities.Should().ContainSingle(a =>
            a.PermanentInstanceId == mountain.InstanceId && a.Kind == "Mana");
    }

    [Fact]
    public void OwnMainPhase_FloatingManaCountsTowardAffordability()
    {
        // CR 106.4 — mana already in the pool pays costs. No untapped source,
        // {1}{R} floating: a {1}{R} creature is castable, {2}{R} is not.
        _alice.AddManaToPool(ManaCost.Parse("{1}{R}"));
        var bear = InHand(new Creature("Bear", "{1}{R}", 2, 2));
        var ogre = InHand(new Creature("Ogre", "{2}{R}", 3, 3));

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));

        legal.CastableCardIds.Should().Contain(bear.InstanceId);
        legal.CastableCardIds.Should().NotContain(ogre.InstanceId);
    }

    [Fact]
    public void DualSourceAssignment_IsFoundEvenWhenGreedyAutoPayMisses()
    {
        // A W/U dual listed BEFORE a Plains: the engine's greedy auto-pay
        // takes the dual for {W} and then has no {U} left, but a manual
        // payment (Plains for {W}, dual for {U}) exists, so the spell is
        // castable and must be offered.
        var dual = OnBattlefield(new Land("Azorius Dual"));
        dual.AddAbility(new ManaAbility(dual, _alice, ManaCost.Parse("W")));
        dual.AddAbility(new ManaAbility(dual, _alice, ManaCost.Parse("U")));
        UntappedBasic("Plains", "W");
        var spell = InHand(new Creature("Azorius Bear", "{W}{U}", 2, 2));

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));

        legal.CastableCardIds.Should().Contain(spell.InstanceId);
    }

    [Fact]
    public void OpponentsTurn_OnlyInstantSpeedCardsAndActivatableAbilitiesAppear()
    {
        // CR 307.1 — on the opponent's turn only instants and Flash cards may
        // be cast; CR 305.2 — no land play; CR 606.3 — no loyalty abilities.
        // Instant-speed activated abilities (CR 602.2) and mana abilities
        // (CR 605.3a) remain available.
        var mountain1 = UntappedBasic("Mountain", "R");
        UntappedBasic("Mountain", "R");
        InHand(new Land("Mountain"));
        var bolt = InHand(new Instant("Lightning Bolt", "{R}"));
        var flash = InHand(new Creature("Ambush Viper", "{1}{R}", 2, 1));
        flash.AddAbility(new KeywordAbility("Flash"));
        var vanillaCreature = InHand(new Creature("Goblin Guide", "{R}", 2, 2));
        var sorcery = InHand(new Sorcery("Lava Spike", "{R}"));

        var pinger = ReadyCreature("Prodigal Pyromancer", summoningSick: false);
        var ping = TapAbility(pinger, _alice);
        pinger.AddAbility(ping);

        var walker = OnBattlefield(new Planeswalker("Chandra", "{2}{R}{R}", 4));
        var plusOne = new LoyaltyAbility(walker, +1, () => { });
        walker.AddAbility(plusOne);

        var legal = PriorityLegalActions.Build(Ctx(_bob, StepStateType.PreCombatMain, landPlayAvailable: true));

        legal.PlayableLandIds.Should().BeEmpty("lands are played only on your own turn (CR 305.2)");
        legal.CastableCardIds.Should().BeEquivalentTo(new[] { bolt.InstanceId, flash.InstanceId });
        legal.CastableCardIds.Should().NotContain(new[] { vanillaCreature.InstanceId, sorcery.InstanceId });
        legal.ActivatableAbilities.Should().Contain(a =>
            a.PermanentInstanceId == pinger.InstanceId && a.AbilityId == ping.Id && a.Kind == "Activated");
        legal.ActivatableAbilities.Should().Contain(a =>
            a.PermanentInstanceId == mountain1.InstanceId && a.Kind == "Mana");
        legal.ActivatableAbilities.Should().NotContain(a => a.Kind == "Loyalty",
            "loyalty abilities are sorcery-speed (CR 606.3)");
    }

    [Fact]
    public void SummoningSickCreature_TapAbilityIsAbsent_NonTapAbilityIsPresent()
    {
        // CR 302.6 — a creature's {T} ability can't be activated unless it has
        // been under its controller's control continuously since their most
        // recent turn began. An ability without {T} is unaffected.
        var sick = ReadyCreature("Prodigal Pyromancer", summoningSick: true);
        var tap = TapAbility(sick, _alice);
        sick.AddAbility(tap);
        var payLife = new ActivatedAbility(sick, _alice, costs: new ICost[] { AdditionalCost.PayLife(1) });
        sick.AddAbility(payLife);

        var dork = ReadyCreature("Llanowar Elves", summoningSick: true);
        dork.AddAbility(new ManaAbility(dork, _alice, ManaCost.Parse("G")));

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));

        legal.ActivatableAbilities.Should().NotContain(a => a.AbilityId == tap.Id);
        legal.ActivatableAbilities.Should().Contain(a => a.AbilityId == payLife.Id && a.Kind == "Activated");
        legal.ActivatableAbilities.Should().NotContain(a => a.PermanentInstanceId == dork.InstanceId,
            "a summoning-sick creature's {T} mana ability is not activatable either (CR 302.6 / 605.3a)");
    }

    [Fact]
    public void TappedPermanent_TapAbilityIsAbsent()
    {
        var pinger = ReadyCreature("Prodigal Pyromancer", summoningSick: false);
        var tap = TapAbility(pinger, _alice);
        pinger.AddAbility(tap);
        pinger.Tap();

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));

        legal.ActivatableAbilities.Should().NotContain(a => a.AbilityId == tap.Id);
    }

    [Fact]
    public void ActivatedAbility_ManaCost_RequiresFloatingMana_MatchingTheEngineDispatchGate()
    {
        // The live dispatcher (TurnDriver.DispatchActivate) pays a non-mana
        // activated ability's mana cost from the FLOATING POOL only
        // (CostPayment.CanPayCosts → ManaPool.CanPay); an unfloated activation
        // is silently dropped. The list therefore mirrors that gate.
        UntappedBasic("Mountain", "R");
        var shade = ReadyCreature("Shade", summoningSick: false);
        var pump = new ActivatedAbility(shade, _alice, costs: new ICost[] { new ManaCostCost(ManaCost.Parse("{R}")) });
        shade.AddAbility(pump);

        var before = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));
        before.ActivatableAbilities.Should().NotContain(a => a.AbilityId == pump.Id);

        _alice.AddManaToPool(ManaCost.Parse("{R}"));
        var after = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));
        after.ActivatableAbilities.Should().Contain(a => a.AbilityId == pump.Id && a.Kind == "Activated");
    }

    [Fact]
    public void SorcerySpeedActivatedAbility_OnlyInSorceryWindow()
    {
        // CR 602.5d / 307.5 — "Activate only as a sorcery".
        var source = ReadyCreature("Equipment-ish", summoningSick: false);
        var sorcerySpeed = new ActivatedAbility(source, _alice, sorcerySpeed: true);
        source.AddAbility(sorcerySpeed);

        PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false))
            .ActivatableAbilities.Should().Contain(a => a.AbilityId == sorcerySpeed.Id);
        PriorityLegalActions.Build(Ctx(_alice, StepStateType.Upkeep, landPlayAvailable: false))
            .ActivatableAbilities.Should().NotContain(a => a.AbilityId == sorcerySpeed.Id);
    }

    [Fact]
    public void LoyaltyAbilities_OwnMainPhase_PayableOnly()
    {
        // CR 606.3 / 606.5 — sorcery speed, once per turn, a minus ability
        // can't take loyalty below 0.
        var walker = OnBattlefield(new Planeswalker("Grist", "{1}{B}{G}", 2));
        var plus = new LoyaltyAbility(walker, +1, () => { });
        var minus = new LoyaltyAbility(walker, -5, () => { });
        walker.AddAbility(plus);
        walker.AddAbility(minus);

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: false));

        legal.ActivatableAbilities.Should().ContainSingle(a => a.Kind == "Loyalty")
            .Which.Should().Be(new AbilityRefDto(walker.InstanceId, plus.Id, "Loyalty"));
    }

    [Fact]
    public void NonEmptyStack_NoSorcerySpeedActions()
    {
        // CR 307.1 / 305.2 — a non-empty stack closes the sorcery window even
        // on your own main phase.
        UntappedBasic("Mountain", "R");
        InHand(new Land("Mountain"));
        var creature = InHand(new Creature("Goblin Guide", "{R}", 2, 2));
        var bolt = InHand(new Instant("Lightning Bolt", "{R}"));
        var opposing = new Instant("Shock", "{R}") { Owner = _bob };
        _stack.Push(new Majik.Core.Spells.Spell(opposing, _bob));

        var legal = PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: true));

        legal.PlayableLandIds.Should().BeEmpty();
        legal.CastableCardIds.Should().Equal(bolt.InstanceId);
        legal.CastableCardIds.Should().NotContain(creature.InstanceId);
    }

    [Fact]
    public async Task PriorityPrompt_CarriesLegalActions_AndSecondLandSameTurnIsNotPlayable()
    {
        // End-to-end through RemoteAgent → GameFacade.BuildPrompt on the
        // single-round loop (own precombat main, empty stack). CR 305.2 — once
        // the land drop is used, the second land in hand is no longer listed.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var first = SeedHand(facade.Alice, new Land("Forest"));
        var second = SeedHand(facade.Alice, new Land("Mountain"));

        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);

        await facade.StartAsync();

        var initial = prompts.Last(p => p.PlayerId == facade.Alice.Id);
        initial.LegalActions.Should().NotBeNull();
        initial.LegalActions!.PlayableLandIds.Should().BeEquivalentTo(new[] { first.InstanceId, second.InstanceId });

        await facade.SubmitAsync(new PlayLandCommand(first.InstanceId) { PlayerId = facade.Alice.Id });

        var afterDrop = prompts.Last(p => p.PlayerId == facade.Alice.Id);
        afterDrop.Should().NotBeSameAs(initial);
        afterDrop.ExpectedKinds.Should().Contain(nameof(PassPriorityCommand));
        afterDrop.LegalActions.Should().NotBeNull();
        afterDrop.LegalActions!.PlayableLandIds.Should().BeEmpty(
            "only one land may be played per turn (CR 305.2)");
    }

    [Fact]
    public void NonPriorityPrompt_HasNoLegalActions()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var prompts = new List<PromptDto>();
        using var sub = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        _ = agent.ChooseXAsync(null!, new Creature("X", "{X}", 0, 0));

        prompts.Should().ContainSingle().Which.LegalActions.Should().BeNull();
        agent.Submit(new ChooseXCommand(0) { PlayerId = facade.Alice.Id });
    }

    private static T SeedHand<T>(Player player, T card) where T : Card
    {
        card.SetOwner(player);
        card.SetController(player);
        card.SetZone(ZoneType.Hand);
        player.Zones.Hand.AddCard(card);
        return card;
    }

    internal static RemoteAgent AliceAgent(GameFacade facade) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField("_aliceAgent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    // ---- CR 601.2c / 602.2b: a cast or activation is offered only when its
    // required targets exist (same pools the cast flow prompts with).

    private static Majik.Core.Players.Agents.TargetRequest Needs(string description, Func<IReadOnlyList<object>> pool, int min = 1, int? modeIndex = null) =>
        new(description, min, Math.Max(1, min), Array.Empty<object>(), CandidateGatherer: _ => pool(), ModeIndex: modeIndex);

    private static SpellDefinition Def(params Majik.Core.Players.Agents.TargetRequest[] requests) =>
        new(Array.Empty<string>(), false, requests, _ => Array.Empty<Majik.Core.Abilities.IEffect>());

    private GameContext CtxWith(Func<ICard, Player, SpellDefinition?> definitions) =>
        new(_alice, new[] { _alice, _bob }, activePlayer: _alice,
            turnNumber: 3, StepStateType.PreCombatMain, _stack, landPlayAvailable: true)
        {
            SpellDefinitions = definitions,
        };

    [Fact]
    public void ASpellWhoseRequiredTargetDoesNotExist_IsNotCastable_AndIsOnceOneExists()
    {
        UntappedBasic("Island", "U");
        UntappedBasic("Island", "U");
        var counterspell = InHand(new Instant("Counterspell", "{U}{U}"));
        var spells = new List<object>();
        var def = Def(Needs("target spell", () => spells));

        PriorityLegalActions.Build(CtxWith((_, _) => def)).CastableCardIds
            .Should().NotContain(counterspell.InstanceId, "CR 601.2c — with no spell to target it cannot be cast");

        spells.Add(new object());
        PriorityLegalActions.Build(CtxWith((_, _) => def)).CastableCardIds
            .Should().Contain(counterspell.InstanceId);
    }

    [Fact]
    public void AnUpToRequest_NeedsNoCandidate()
    {
        UntappedBasic("Mountain", "R");
        var spell = InHand(new Sorcery("Up To One", "{R}"));
        var def = Def(Needs("up to one target creature", () => Array.Empty<object>(), min: 0));

        PriorityLegalActions.Build(CtxWith((_, _) => def)).CastableCardIds.Should().Contain(spell.InstanceId);
    }

    [Fact]
    public void AModalSpell_IsCastableWhenEnoughModesHaveTargets()
    {
        UntappedBasic("Mountain", "R");
        var charm = InHand(new Instant("Two Mode Charm", "{R}"));
        var none = Array.Empty<object>();
        var some = new object[] { new object() };
        SpellDefinition Modal(IReadOnlyList<object> second, int minModes) => new(
            new[] { "first", "second" }, false,
            new[] { Needs("target artifact", () => none), Needs("target creature", () => second) },
            _ => Array.Empty<Majik.Core.Abilities.IEffect>(), MinModes: minModes, MaxModes: 2);

        PriorityLegalActions.Build(CtxWith((_, _) => Modal(some, 1))).CastableCardIds
            .Should().Contain(charm.InstanceId, "one mode with a target is enough for choose one");
        PriorityLegalActions.Build(CtxWith((_, _) => Modal(none, 1))).CastableCardIds
            .Should().NotContain(charm.InstanceId, "CR 700.2 / 601.2c — no mode can be chosen");
        PriorityLegalActions.Build(CtxWith((_, _) => Modal(some, 2))).CastableCardIds
            .Should().NotContain(charm.InstanceId, "choose two needs two modes with targets");
    }

    [Fact]
    public void AnActivatedAbilityWithNoLegalTarget_IsNotListed()
    {
        var elf = ReadyCreature("Llanowar Sniper", summoningSick: false);
        var targets = new List<object>();
        var ability = new ActivatedAbility(elf, _alice, costs: new ICost[] { AdditionalCost.Tap(elf) },
            // An artifact, so the central fallback pool (which would find the
            // Sniper itself for "target creature") is empty too.
            targetRequests: new[] { Needs("target artifact", () => targets) });
        elf.AddAbility(ability);

        PriorityLegalActions.Build(CtxWith((_, _) => null)).ActivatableAbilities
            .Should().NotContain(a => a.AbilityId == ability.Id, "CR 602.2b — no legal target");

        targets.Add(new object());
        PriorityLegalActions.Build(CtxWith((_, _) => null)).ActivatableAbilities
            .Should().Contain(a => a.AbilityId == ability.Id);
    }

    [Fact]
    public void AColorlessPip_NeedsAColorlessSource_AsThePaymentDoes()
    {
        UntappedBasic("Forest", "G");
        UntappedBasic("Forest", "G");
        var command = InHand(new Instant("Colorless Command", "{1}{C}"));

        PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: true)).CastableCardIds
            .Should().NotContain(command.InstanceId, "CR 107.4c — {C} is paid only with colorless mana");

        UntappedBasic("Wastes", "C");
        PriorityLegalActions.Build(Ctx(_alice, StepStateType.PreCombatMain, landPlayAvailable: true)).CastableCardIds
            .Should().Contain(command.InstanceId);
    }

    [Fact]
    public void AnyOtherTarget_NeedsASecondDistinctCandidate()
    {
        // CR 601.2c / 115.3 — Arc Trail's "any other target" can't be the
        // first target, so one candidate between them is not enough.
        UntappedBasic("Mountain", "R");
        var arc = InHand(new Sorcery("Arc Trail", "{R}"));
        var pool = new List<object> { new object() };
        var def = Def(
            Needs("any target", () => pool),
            Needs("any other target", () => pool) with { DistinctFromRequest = 0 });

        PriorityLegalActions.Build(CtxWith((_, _) => def)).CastableCardIds
            .Should().NotContain(arc.InstanceId, "the second target must differ from the first");

        pool.Add(new object());
        PriorityLegalActions.Build(CtxWith((_, _) => def)).CastableCardIds.Should().Contain(arc.InstanceId);
    }
}