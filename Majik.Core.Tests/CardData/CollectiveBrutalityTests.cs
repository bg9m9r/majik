using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 700.2d ("choose one or more") + CR 702.121 (Escalate—Discard a card).
/// Collective Brutality (Eldritch Moon, {1}{B}, Sorcery). Three modes:
///   0 — opponent reveals hand; discard an instant/sorcery card.
///   1 — target creature gets -2/-2 until end of turn.
///   2 — target opponent loses 2 life and you gain 2 life.
///
/// Cast end-to-end through <see cref="SpellCastFlow"/> so the multi-mode
/// prompt + escalate additional-cost machinery is exercised, not just the
/// EffectFactory.
/// </summary>
public class CollectiveBrutalityTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CollectiveBrutalityTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    private SpellDefinition Def(IPlayerAgent? agent = null) =>
        CollectiveBrutalityFactory.BuildDefinition(_alice, o => o, agent, _bus);

    private Sorcery NewBrutality()
    {
        var cb = CollectiveBrutalityFactory.Create(_alice);
        cb.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cb);
        return cb;
    }

    [Fact]
    public void Create_HasSorceryShape_Black()
    {
        var cb = CollectiveBrutalityFactory.Create(_alice);

        cb.Name.Should().Be("Collective Brutality");
        cb.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(cb).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBrutalityShape()
    {
        var dispatched = NamedCardFactory.Create("Collective Brutality", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Collective Brutality");
    }

    [Fact]
    public void BuildDefinition_IsMultiMode_ChooseOneToThree_WithEscalate()
    {
        var def = Def();

        def.Modes.Should().HaveCount(3);
        def.MinModes.Should().Be(1);
        def.MaxModes.Should().Be(3);
        def.IsMultiMode.Should().BeTrue();
        def.Escalate.Should().NotBeNull();
        def.Escalate!.Description.Should().Be("Discard a card");
    }

    [Fact]
    public async Task ChooseOneMode_Free_NoEscalateDiscard()
    {
        // One mode (drain) → zero escalate discards. Alice keeps her hand.
        var cb = NewBrutality();
        var spare = new Instant("Spare Card", "{B}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(spare);

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveBrutalityFactory.ModeDrain);
        agent.QueueTargets(new object[] { _bob }); // mode-2 target opponent
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, cb, Def(agent), agent, Ctx());
        spell.Resolve();

        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(22);
        _alice.Zones.Hand.GetCards().Should().Contain(spare,
            because: "choosing a single mode pays no escalate discard");
    }

    [Fact]
    public async Task ChooseTwoModes_PayOneEscalateDiscard_BothResolveInPrintedOrder()
    {
        // Modes 1 (-2/-2) + 2 (drain). Two modes → one escalate discard.
        var cb = NewBrutality();
        var fodder = new Instant("Discard Fodder", "{B}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(fodder);

        var svc = new ContinuousEffectsService();
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.ActiveEffects = svc;

        var agent = new ScriptedAgent();
        // Announce drain first, but resolution must run -2/-2 (mode 1) before
        // drain (mode 2) — printed order, CR 608.2c.
        agent.QueueModes(CollectiveBrutalityFactory.ModeDrain,
                         CollectiveBrutalityFactory.ModeMinusTwoMinusTwo);
        // The escalate discard nominates the fodder card.
        agent.QueueFromHand(fodder);
        // Mode-aware target collection prompts only the CHOSEN modes, in
        // target-request declaration order: mode 1 (the bear), then mode 2 (Bob).
        agent.QueueTargets(new object[] { bobBear }); // mode 1 — creature
        agent.QueueTargets(new object[] { _bob });     // mode 2 — opponent
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, cb, Def(agent), agent, Ctx());

        // Escalate paid one discard BEFORE resolution.
        fodder.Zone.Should().Be(ZoneType.Graveyard,
            because: "two modes cost one escalate discard (CR 702.121)");

        spell.Resolve();

        svc.Compute(bobBear).Toughness.Should().Be(0, because: "-2/-2 reduces 2/2 to 0/0");
        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(22);
    }

    [Fact]
    public async Task ChooseThreeModes_PayTwoEscalateDiscards_AllThreeResolve()
    {
        var cb = NewBrutality();
        var fodder1 = new Instant("Fodder One", "{B}") { Owner = _alice, Zone = ZoneType.Hand };
        var fodder2 = new Instant("Fodder Two", "{B}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(fodder1);
        _alice.Zones.Hand.AddCard(fodder2);

        // Bob has an instant in hand to be discarded by mode 0.
        var bobInstant = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bobLand = new Land("Mountain") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bobInstant);
        _bob.Zones.Hand.AddCard(bobLand);

        var svc = new ContinuousEffectsService();
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.ActiveEffects = svc;

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveBrutalityFactory.ModeDiscard,
                         CollectiveBrutalityFactory.ModeMinusTwoMinusTwo,
                         CollectiveBrutalityFactory.ModeDrain);
        // Two escalate discards (Alice's two fodder cards).
        agent.QueueFromHand(fodder1);
        agent.QueueFromHand(fodder2);
        // Mode-0 pick from Bob's revealed hand (the instant, not the land).
        agent.QueueFromHand(bobInstant);
        // Targets in declaration order.
        agent.QueueTargets(new object[] { _bob });     // mode 0 — opponent
        agent.QueueTargets(new object[] { bobBear });  // mode 1 — creature
        agent.QueueTargets(new object[] { _bob });     // mode 2 — opponent
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, cb, Def(agent), agent, Ctx());

        fodder1.Zone.Should().Be(ZoneType.Graveyard);
        fodder2.Zone.Should().Be(ZoneType.Graveyard);

        spell.Resolve();

        // Mode 0 — Bob discarded the instant (land left in hand).
        bobInstant.Zone.Should().Be(ZoneType.Graveyard);
        bobLand.Zone.Should().Be(ZoneType.Hand);
        // Mode 1 — bear -2/-2.
        svc.Compute(bobBear).Toughness.Should().Be(0);
        // Mode 2 — drain.
        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(22);
    }

    [Fact]
    public async Task CannotPayEscalate_NotEnoughCardsToDiscard_CastIllegal()
    {
        // Alice picks three modes (needs two escalate discards) but has no
        // spare cards in hand (only Collective Brutality, which is on the
        // stack by pay time). CR 601.2g — the cast is illegal and throws.
        var cb = NewBrutality(); // only card in hand

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveBrutalityFactory.ModeDrain,
                         CollectiveBrutalityFactory.ModeMinusTwoMinusTwo,
                         CollectiveBrutalityFactory.ModeDiscard);

        Func<Task> act = async () => await _flow.CastAsync(_alice, cb, Def(agent), agent, Ctx());

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*Escalate*");
    }

    [Fact]
    public void EffectFactory_DuplicateModeIndices_DeDuplicated()
    {
        // CR 700.2d — the same mode can't be chosen more than once.
        var def = Def();
        var chosen = new ChosenSpellParams(
            ModeIndex: CollectiveBrutalityFactory.ModeDrain,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                System.Array.Empty<object>(),
                System.Array.Empty<object>(),
                new object[] { _bob },
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                CollectiveBrutalityFactory.ModeDrain,
                CollectiveBrutalityFactory.ModeDrain, // duplicate
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1, because: "duplicates collapse per CR 700.2d");
    }

    [Fact]
    public void EffectFactory_ResolvesInPrintedOrder_RegardlessOfAnnounceOrder()
    {
        // Announce mode 2 then mode 0; effects come back in printed order
        // (0 then 2) per CR 608.2c.
        var def = Def();
        var chosen = new ChosenSpellParams(
            ModeIndex: CollectiveBrutalityFactory.ModeDrain,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                new object[] { _bob },
                System.Array.Empty<object>(),
                new object[] { _bob },
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                CollectiveBrutalityFactory.ModeDrain,    // announced first
                CollectiveBrutalityFactory.ModeDiscard,  // announced second
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        effects[0].Description.Should().Contain("reveals hand",
            because: "mode 0 (discard) is printed before mode 2 (drain)");
        effects[1].Description.Should().Contain("loses 2 life");
    }
}
