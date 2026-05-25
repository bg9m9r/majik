using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 700.2d — modal "Choose two —" spell. Cryptic Command, Lorwyn,
/// {1}{U}{U}{U}, four modes (counter / bounce / mass-tap-opponents / draw).
///
/// The tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/> — at v1 <see cref="SpellCastFlow"/> only
/// collects a scalar <c>ModeIndex</c>, so callers wanting multi-mode
/// invoke the bound effects against a hand-built params object. This
/// mirrors the production path the modal runtime exposes via
/// <c>ChosenSpellParams.ModeIndexes</c>.
/// </summary>
public class CrypticCommandTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CrypticCommandTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var cc = CrypticCommandFactory.Create(_alice);

        cc.Name.Should().Be("Cryptic Command");
        cc.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(cc).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsCrypticCommandShape()
    {
        var dispatched = NamedCardFactory.Create("Cryptic Command", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Cryptic Command");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_RequiredModeCount_IsTwo()
    {
        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        def.RequiredModeCount.Should().Be(2,
            because: "CR 700.2d — Cryptic Command's printed pick is \"Choose two —\"");
        def.RequiredModeCount.Should().Be(CrypticCommandFactory.PickCount);
    }

    [Fact]
    public void BuildDefinition_ExposesFourModes_WithPerModeIntents()
    {
        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        def.Modes.Should().HaveCount(4);
        def.Modes[0].Should().Contain("Counter");
        def.Modes[1].Should().Contain("Return target permanent");
        def.Modes[2].Should().Contain("Tap all creatures");
        def.Modes[3].Should().Be("Draw a card.");
        def.ModeIntentsOrEmpty.Should().HaveCount(4);
        def.ModeIntentsOrEmpty[CrypticCommandFactory.ModeCounter]
            .Should().Be(BotIntent.Counter);
        def.ModeIntentsOrEmpty[CrypticCommandFactory.ModeDraw]
            .Should().Be(BotIntent.Draw);
    }

    [Fact]
    public void ChooseTwo_CounterAndDraw_RunsBothModes()
    {
        // Stage: Bob has a spell on the stack to counter; Alice has a
        // card on top of her library to draw.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var aliceTopCard = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(aliceTopCard);

        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        // Two target slots (mode 0, mode 1). Caster only fills mode 0's
        // slot because mode 1 (bounce) was NOT picked; the bounce slot
        // stays empty.
        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell }, // mode 0 — counter
            Array.Empty<object>(),     // mode 1 — bounce (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: CrypticCommandFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                CrypticCommandFactory.ModeCounter,
                CrypticCommandFactory.ModeDraw,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        foreach (var eff in effects) eff.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "counter mode moves the targeted spell to the graveyard");
        aliceTopCard.Zone.Should().Be(ZoneType.Hand,
            because: "draw mode pulls the top card of Alice's library into her hand");
        _alice.Zones.Hand.GetCards().Should().Contain(aliceTopCard);
    }

    [Fact]
    public void ChooseTwo_BounceAndTapOpponents_RunsBothModes()
    {
        // Stage: Bob has a tapped/untapped creature mix; Alice targets
        // Bob's enchantment for bounce (an enchantment, not a creature,
        // so it survives the mass tap and is the bounce target).
        var bobEnch = new Enchantment("Sigil of Sleep", "{U}") { Owner = _bob, Controller = _bob };
        bobEnch.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobEnch);

        var bobBear1 = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear1.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear1);

        var bobBear2 = new Creature("Runeclaw Bear", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear2.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear2);

        // Alice's own creature must NOT get tapped — mass tap is
        // opponents-only (CR 700.2d, mode body).
        var aliceCreature = new Creature("Hill Giant", "{3}{R}", 3, 3) { Owner = _alice, Controller = _alice };
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),     // mode 0 — counter (unused)
            new object[] { bobEnch },  // mode 1 — bounce target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: CrypticCommandFactory.ModeBounce,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                CrypticCommandFactory.ModeBounce,
                CrypticCommandFactory.ModeTapOpponents,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        foreach (var eff in effects) eff.Execute();

        bobEnch.Zone.Should().Be(ZoneType.Hand,
            because: "bounce mode returns the enchantment to Bob's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(bobEnch);

        bobBear1.IsTapped.Should().BeTrue();
        bobBear2.IsTapped.Should().BeTrue();
        aliceCreature.IsTapped.Should().BeFalse(
            because: "the mass tap only hits creatures Alice's OPPONENTS control");
    }

    [Fact]
    public void ChooseTwo_RespectsPickCount_ExtraModesIgnored()
    {
        // CR 700.2d — pick count is enforced. Caller submits all four
        // indices; runtime caps at PickCount (2) and silently drops the
        // overflow.
        var aliceCard1 = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        var aliceCard2 = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(aliceCard1);
        _alice.Zones.Library.AddCard(aliceCard2);

        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: CrypticCommandFactory.ModeDraw,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            // All four modes requested — only the first two should fire.
            ModeIndexes: new[]
            {
                CrypticCommandFactory.ModeDraw,
                CrypticCommandFactory.ModeTapOpponents,
                CrypticCommandFactory.ModeBounce,
                CrypticCommandFactory.ModeCounter,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(CrypticCommandFactory.PickCount);

        foreach (var eff in effects) eff.Execute();

        // First mode (draw) pulled exactly one card.
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void ChooseTwo_DuplicateModeIndices_DeDuplicated()
    {
        // CR 700.2d — "the same mode can't be chosen more than once".
        // Two identical Draw entries collapse to a single draw effect;
        // the runtime then refuses to expand to a second effect on the
        // duplicate.
        var topCard = new Instant("Counterspell", "{U}{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topCard);

        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: CrypticCommandFactory.ModeDraw,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                CrypticCommandFactory.ModeDraw,
                CrypticCommandFactory.ModeDraw, // duplicate
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1, because: "duplicates are dropped per CR 700.2d");
        foreach (var eff in effects) eff.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void TapOpponents_LeavesAlreadyTappedCreaturesAlone()
    {
        // Mass tap is a no-op for already-tapped creatures. Idempotent.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.Tap(); // pre-tapped

        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: CrypticCommandFactory.ModeTapOpponents,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                CrypticCommandFactory.ModeTapOpponents,
                CrypticCommandFactory.ModeDraw,
            });

        var effects = def.EffectFactory(chosen);
        // Mass-tap effect should still fire even though no untap state
        // changed — assertion is "no crash, already-tapped stays tapped".
        Action act = () =>
        {
            foreach (var eff in effects) eff.Execute();
        };
        act.Should().NotThrow();
        bear.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesTwoTargetRequests_BothOptional()
    {
        // The target requests must be MinTargets=0 so a chosen spell that
        // skips a targeted mode doesn't fail at cast time when no target
        // is provided for that mode's slot.
        var def = CrypticCommandFactory.BuildDefinition(_alice, o => o, _stack);

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[CrypticCommandFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[CrypticCommandFactory.ModeBounce].MinTargets.Should().Be(0);
        def.TargetRequests[CrypticCommandFactory.ModeCounter].Intent.Should().Be(BotIntent.Counter);
        def.TargetRequests[CrypticCommandFactory.ModeBounce].Intent.Should().Be(BotIntent.Bounce);
    }
}
