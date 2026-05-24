using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Diagnostics;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// Regression suite for the bot's vanilla-shell graceful-degradation path
/// — see <c>feat/bot-vanilla-shell-graceful-degrade</c>. Goals:
///
/// <list type="bullet">
///   <item>Bot never crashes when offered an
///   <see cref="ICard.IsVanillaShell"/> card as a candidate (castable,
///   target, etc.).</item>
///   <item>Vanilla shells are deprioritised in EV scoring vs. implemented
///   alternatives, but not refused outright (so a deck containing only
///   shells still terminates rather than blocking on no-decision).</item>
///   <item><see cref="VanillaShellTracker"/> emits exactly one WARN log
///   line + one <see cref="UnimplementedCardEncounteredEvent"/> per
///   distinct card-name per game, regardless of how many times the bot
///   considers the card.</item>
/// </list>
/// </summary>
public class GracefulDegradeTests
{
    private static Card MakeVanillaShellCreature(string name, int power, int toughness, string manaCost = "{1}")
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.MarkAsVanillaShell();
        return c;
    }

    private static Card MakeVanillaShellSorcery(string name, string manaCost = "{2}{R}")
    {
        var c = new Sorcery(name, manaCost);
        c.MarkAsVanillaShell();
        return c;
    }

    [Fact]
    public void PriorityPolicy_DoesNotCrash_WithVanillaShellInHand()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        s.AddLandToBattlefield(s.Self, "Mountain3");
        s.AddCardToHand(s.Self, MakeVanillaShellSorcery("Pyroclasm", "{1}{R}"));

        var tracker = new VanillaShellTracker(s.Bus);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn,
            Majik.Bot.Diagnostics.NullBotDecisionSink.Instance, tracker);

        var act = pol.Pick(s.Context, s.Self);

        // Pass beats vanilla-shell sorcery: the EV penalty (-CMC) sinks
        // it below the Pass baseline, so the policy should hold rather
        // than burn 2 mana + 1 card for no observable effect.
        act.Should().BeOfType<PriorityAction.PassAction>();
    }

    [Fact]
    public void PriorityPolicy_PrefersImplementedSpell_OverVanillaShell()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");

        var implementedCrt = new Creature("Goblin Guide", manaCost: "{R}", power: 2, toughness: 2);
        var shell = MakeVanillaShellCreature("Inscrutable Beast", power: 1, toughness: 1, manaCost: "{R}");
        s.AddCardToHand(s.Self, implementedCrt);
        s.AddCardToHand(s.Self, shell);

        var tracker = new VanillaShellTracker(s.Bus);
        var pol = new PriorityPolicy(ArchetypeWeights.Burn,
            Majik.Bot.Diagnostics.NullBotDecisionSink.Instance, tracker);

        var act = pol.Pick(s.Context, s.Self);

        act.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)act).Card.Should().Be(implementedCrt);
    }

    [Fact]
    public void VanillaShellTracker_WarnsOnce_PerCardName()
    {
        var bus = new EventBus();
        var logs = new List<string>();
        var captured = new List<UnimplementedCardEncounteredEvent>();
        bus.Subscribe<UnimplementedCardEncounteredEvent>(e => captured.Add(e));

        var tracker = new VanillaShellTracker(bus, logs.Add);

        var p = new Majik.Core.Players.Player("Bot", 20);
        var shell = MakeVanillaShellSorcery("Hypothetical Spell");

        var first = tracker.Notice(shell, p, "test");
        var second = tracker.Notice(shell, p, "test");
        var thirdDifferent = tracker.Notice(
            MakeVanillaShellCreature("Other Shell", 1, 1), p, "test");

        first.Should().BeTrue();
        second.Should().BeFalse();
        thirdDifferent.Should().BeTrue();

        logs.Should().HaveCount(2);
        logs[0].Should().Contain("Hypothetical Spell")
            .And.Contain("treating as vanilla shell")
            .And.Contain("Coverage tier: Unimplemented")
            .And.Contain("EV is unreliable");

        captured.Should().HaveCount(2);
        captured.Select(e => e.CardName).Should().BeEquivalentTo(
            new[] { "Hypothetical Spell", "Other Shell" });
    }

    [Fact]
    public void VanillaShellTracker_NoOp_ForNonShellCard()
    {
        var bus = new EventBus();
        var logs = new List<string>();
        bus.Subscribe<UnimplementedCardEncounteredEvent>(_ => { });
        var tracker = new VanillaShellTracker(bus, logs.Add);

        var p = new Majik.Core.Players.Player("Bot", 20);
        var normal = new Creature("Goblin Guide", "{R}", 2, 2);
        // NOT marked vanilla shell.

        tracker.Notice(normal, p, "test").Should().BeFalse();
        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task HeuristicBotAgent_DowngradesVanillaShellBid_BelowImplementedBid()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Land1");
        s.AddLandToBattlefield(s.Self, "Land2");
        s.AddLandToBattlefield(s.Self, "Land3");
        s.AddLandToBattlefield(s.Self, "Land4");

        // Implemented small spell vs vanilla-shell big spell. Without the
        // graceful-degrade penalty, HeuristicBotAgent would prefer the
        // higher-CMC card (its bid priority is printed-CMC + sequencing).
        // With the penalty, the implemented spell wins.
        var implemented = new Creature("Tarmogoyf", "{1}{G}", 4, 5);
        var shell = MakeVanillaShellCreature("Unknown Beast", 6, 6, manaCost: "{2}{G}{G}");
        s.AddCardToHand(s.Self, implemented);
        s.AddCardToHand(s.Self, shell);

        // Give the lands green mana so they can pay both spells. Vanilla
        // Land doesn't produce mana by default in this harness — but
        // HeuristicBotAgent's TryPickManaSources only requires the source
        // to be an untapped Permanent with mana abilities. Use Mountains
        // tagged with mana production by adding actual basic Forests.
        // Simplest: replace Land1..Land4 with Basic Forests directly.
        // Strip the placeholders and rebuild with Basic Forests.
        foreach (var c in s.Self.Zones.Battlefield.GetCards().ToList())
        {
            s.Self.Zones.Battlefield.RemoveCard(c);
        }
        for (int i = 0; i < 4; i++)
        {
            var forest = new Majik.Core.Cards.Land(
                $"Forest{i}",
                supertypes: new[] { CardSupertype.Basic },
                subtypes: new[] { CardSubtype.Forest });
            // Attach a mana ability — Land+Basic doesn't auto-attach in this
            // raw constructor path; bot's mana-payment search needs at least
            // ONE EffectiveManaAbilities entry. Use the OracleManaBinder-style
            // hand-build.
            forest.ChangeOwner(s.Self);
            forest.ChangeController(s.Self);
            forest.AddAbility(new Majik.Core.Abilities.ManaAbility(
                source: forest,
                controller: s.Self,
                manaGenerated: Majik.Core.ValueObjects.ManaCost.Parse("{G}")));
            s.Self.Zones.Battlefield.AddCard(forest);
        }

        var bus = new EventBus();
        var captured = new List<UnimplementedCardEncounteredEvent>();
        bus.Subscribe<UnimplementedCardEncounteredEvent>(e => captured.Add(e));
        var tracker = new VanillaShellTracker(bus);

        var agent = new HeuristicBotAgent(
            altCostProbe: null,
            cardRepository: null,
            vanillaTracker: tracker);

        var action = await agent.ChoosePriorityActionAsync(s.Context);

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action).Card.Should().Be(implemented);

        // The bid loop iterates the shell — tracker should have noticed it.
        captured.Should().HaveCount(1);
        captured[0].CardName.Should().Be("Unknown Beast");
    }

    [Fact]
    public async Task HeuristicBotAgent_DoesNotCrash_WhenOnlyVanillaShellsInHand()
    {
        var s = new BotTestScenario();
        for (int i = 0; i < 6; i++)
        {
            var forest = new Land(
                $"Forest{i}",
                supertypes: new[] { CardSupertype.Basic },
                subtypes: new[] { CardSubtype.Forest });
            forest.ChangeOwner(s.Self);
            forest.ChangeController(s.Self);
            forest.AddAbility(new Majik.Core.Abilities.ManaAbility(
                source: forest,
                controller: s.Self,
                manaGenerated: Majik.Core.ValueObjects.ManaCost.Parse("{G}")));
            s.Self.Zones.Battlefield.AddCard(forest);
        }

        // Hand: 5 known-unimplemented vanilla shells.
        var shellNames = new[] { "Shell1", "Shell2", "Shell3", "Shell4", "Shell5" };
        foreach (var n in shellNames)
        {
            s.AddCardToHand(s.Self, MakeVanillaShellCreature(n, 2, 2, "{G}"));
        }

        var bus = new EventBus();
        var captured = new List<UnimplementedCardEncounteredEvent>();
        bus.Subscribe<UnimplementedCardEncounteredEvent>(e => captured.Add(e));
        var tracker = new VanillaShellTracker(bus);

        var agent = new HeuristicBotAgent(
            altCostProbe: null,
            cardRepository: null,
            vanillaTracker: tracker);

        // Should not crash. Action might be cast-shell-anyway (no
        // implemented alternative) or Pass — either is acceptable;
        // the contract is "no exception, terminates".
        var act = await agent.ChoosePriorityActionAsync(s.Context);
        act.Should().NotBeNull();

        // Tracker should have noticed every distinct shell name exactly
        // once during the single priority round.
        captured.Should().HaveCount(5);
        captured.Select(e => e.CardName).Should().BeEquivalentTo(shellNames);
    }

    [Fact]
    public async Task HeuristicBotAgent_TargetingVanillaShell_DoesNotCrash_AndNotices()
    {
        var s = new BotTestScenario();
        var shellCreature = MakeVanillaShellCreature("Mystery Beast", 3, 3, "{1}{G}");
        shellCreature.ChangeOwner(s.Opponent);
        shellCreature.ChangeController(s.Opponent);
        s.Opponent.Zones.Battlefield.AddCard(shellCreature);

        var bus = new EventBus();
        var captured = new List<UnimplementedCardEncounteredEvent>();
        bus.Subscribe<UnimplementedCardEncounteredEvent>(e => captured.Add(e));
        var tracker = new VanillaShellTracker(bus);

        var agent = new HeuristicBotAgent(vanillaTracker: tracker);

        var request = new TargetRequest(
            Description: "target creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: new object[] { shellCreature });

        var picked = await agent.ChooseTargetsAsync(s.Context, request);
        picked.Should().ContainSingle().Which.Should().Be(shellCreature);

        captured.Should().HaveCount(1);
        captured[0].CardName.Should().Be("Mystery Beast");
        captured[0].Context.Should().Contain("target");
    }

    [Fact]
    public void Card_IsVanillaShell_DefaultsFalse_AndCanBeFlipped()
    {
        var c = new Creature("Normal", "{1}", 1, 1);
        c.IsVanillaShell.Should().BeFalse();
        c.MarkAsVanillaShell();
        c.IsVanillaShell.Should().BeTrue();
        // Idempotent: re-flip stays true.
        c.MarkAsVanillaShell();
        c.IsVanillaShell.Should().BeTrue();
    }
}
