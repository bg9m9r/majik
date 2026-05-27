using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// End-to-end coverage for Lava Dart's Flashback (CR 702.34):
///   "Lava Dart deals 1 damage to any target.
///    Flashback—Sacrifice a Mountain."
///
/// Exercises the full alt-cost path through <see cref="SpellCastFlow"/>:
///   1. Parser pulls "Sacrifice a Mountain" off the oracle line.
///   2. Cast from graveyard with FlashbackAlternativeCost(ManaCost.Zero)
///      plus a SacrificeBasicLandCost(mountain, Mountain) rider.
///   3. Mountain leaves the battlefield as part of cost payment (CR 601.2f).
///   4. The spell resolves and deals 1 damage to the chosen target.
///   5. Post-resolution, Lava Dart is exiled — NOT sent to graveyard
///      (CR 702.34b).
/// </summary>
public class LavaDartFlashbackTests
{
    private const string LavaDartOracle =
        "Lava Dart deals 1 damage to any target.\n" +
        "Flashback—Sacrifice a Mountain. " +
        "(You may cast this card from your graveyard for its flashback cost. " +
        "Then exile it.)";

    [Fact]
    public void Parser_ExtractsSacrificeMountainFromOracle()
    {
        var fb = FlashbackOracleParser.TryParse(LavaDartOracle);

        fb.Should().NotBeNull();
        fb!.ManaCost.IsZero.Should().BeTrue("Lava Dart's flashback is non-mana");
        fb.SacrificeBasicLandSubtype.Should().Be(CardSubtype.Mountain);
    }

    [Fact]
    public void Parser_ExtractsManaCostForRegularFlashback()
    {
        // Firebolt: "Flashback {4}{R}"
        var fb = FlashbackOracleParser.TryParse(
            "Firebolt deals 2 damage to any target.\nFlashback {4}{R}.");

        fb.Should().NotBeNull();
        fb!.ManaCost.Red.Should().Be(1);
        fb.ManaCost.Generic.Should().Be(4);
        fb.SacrificeBasicLandSubtype.Should().BeNull();
    }

    [Fact]
    public void Parser_ReturnsNullWhenNoFlashbackLine()
    {
        FlashbackOracleParser.TryParse("Lightning Bolt deals 3 damage to any target.")
            .Should().BeNull();
    }

    [Fact]
    public async Task LavaDart_CastFromGraveyardViaFlashback_SacrificesMountain_DealsDamage_ThenExiled()
    {
        // ── Arrange ─────────────────────────────────────────────────────────
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Lava Dart already in Alice's graveyard.
        var lavaDart = new Instant("Lava Dart", "R") { Owner = alice };
        lavaDart.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(lavaDart);

        // A Mountain Alice can sacrifice.
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(alice);
        mountain.SetController(alice);
        mountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(mountain);

        // Parse the flashback descriptor straight from oracle to prove
        // the binder produces values the alt-cost can consume.
        var descriptor = FlashbackOracleParser.TryParse(LavaDartOracle)!;
        descriptor.SacrificeBasicLandSubtype.Should().Be(CardSubtype.Mountain);
        descriptor.ManaCost.IsZero.Should().BeTrue();

        // Build the cost objects from the descriptor.
        var altCost = new FlashbackAlternativeCost(descriptor.ManaCost);
        var sacCost = new SacrificeBasicLandCost(mountain, descriptor.SacrificeBasicLandSubtype!.Value);

        // SpellDefinition for the 1-damage effect (mirrors what
        // DamageAnyTargetTemplate binds — kept inline so this test does
        // not depend on the spell-template registry's wiring).
        var bobStartingLife = bob.LifeTotal;
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen => new IEffect[]
            {
                new Effect("Lava Dart: 1 damage", () =>
                {
                    // Single target picked by the agent below.
                    var target = chosen.Targets[0][0];
                    if (target is Player p) p.LoseLife(1);
                    else if (target is Creature c) c.TakeDamage(1);
                }),
            });

        // Scripted agent: targets Bob (any-target damage); supplies empty
        // mana payment since flashback's mana cost is zero.
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1,
            PhaseStateType.PreCombatMain, stack);

        // The DamageAnyTargetTemplate normally requests a target — supply
        // one TargetRequest so the flow prompts the agent.
        def = def with
        {
            TargetRequests = new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: new object[] { bob }),
            },
        };

        // ── Act ─────────────────────────────────────────────────────────────
        var spell = await flow.CastAsync(
            alice, lavaDart, def, agent, ctx,
            additionalCosts: new[] { (IAdditionalCost)sacCost },
            alternativeCost: altCost);

        // Mountain must already be sacrificed by the time the spell is on
        // the stack (CR 601.2f — costs paid before the spell hits the stack).
        mountain.Zone.Should().Be(ZoneType.Graveyard, "sacrifice happens before mana payment");
        alice.Zones.Battlefield.GetCards().Should().NotContain(mountain);
        alice.Zones.Graveyard.GetCards().Should().Contain(mountain);

        // Lava Dart now on the stack (no longer in graveyard).
        lavaDart.Zone.Should().Be(ZoneType.Stack);
        stack.Count.Should().Be(1);

        // ── Resolve ─────────────────────────────────────────────────────────
        spell.Resolve();

        // 1 damage dealt to Bob.
        bob.LifeTotal.Should().Be(bobStartingLife - 1);

        // Card exiled, NOT to graveyard (CR 702.34b).
        lavaDart.Zone.Should().Be(ZoneType.Exile);
        alice.Zones.Exile.GetCards().Should().Contain(lavaDart);
        alice.Zones.Graveyard.GetCards().Should().NotContain(lavaDart);
    }
}
