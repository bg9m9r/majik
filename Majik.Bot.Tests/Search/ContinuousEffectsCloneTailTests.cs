using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Simulation;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// TDD regression suite for Phase 2A Task B2 — CloneForSim long-tail P/T effects.
///
/// Each test verifies that after <see cref="GameStateCloner.Clone"/>, the cloned
/// creature reads the BUFFED P/T (i.e. the effect was re-registered on the fresh
/// CES in the sandbox) WITHOUT any in-test re-registration hack — exactly the
/// production path.
///
/// Categories covered:
///   1. Self-pump CDA-style conditional (Kird Ape — ForestSelfPumpStaticEffect)
///   2. Bespoke anthem without allPlayersResolver (ControllerCreatureAnthemEffect /
///      Heartless Summoning shape — but since Heartless Summoning gives -1/-1 we
///      test a +1/+1 anthem via KaheeraAnthemEffect)
///   3. allPlayersResolver anthem (SliverLegionAnthemEffect)
///   4. +1/+1-counter-driven / graveyard-driven pump (KnightOfReliquary /
///      LandsInGraveyardPumpEffect) — counters and graveyard correctly value-cloned
///   5. BecomesPTEffect base-case (Layer 7b target-captured set-base)
///   6. PumpUntilEndOfTurnEffect mid-turn snapshot (ExpiresAtEndOfTurn clone)
/// </summary>
public sealed class ContinuousEffectsCloneTailTests
{
    // ── 1. Self-pump CDA-style conditional — Kird Ape ───────────────────────

    [Fact]
    public void Clone_ForestSelfPump_BuffsPresentAfterClone()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var ces = new ContinuousEffectsService();

        // Kird Ape: base 1/1, gets +1/+2 when controller controls a Forest.
        // Pass eventBus: null → lifecycle binder wires no ETB subscription,
        // so we manually register the effect below (test-board shortcut).
        var kirdApe = KirdApeFactory.Create(alice, ces, eventBus: null);
        kirdApe.ChangeOwner(alice);
        kirdApe.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(kirdApe);
        kirdApe.ActiveEffects = ces;

        // Directly register the effect (bypassing the ETB lifecycle binder
        // which requires a live IEventBus).
        ces.Register(new KirdApeFactory.ForestSelfPumpStaticEffect(kirdApe));

        // A Forest to trigger the pump.
        var forest = new Land("Forest", supertypes: null, subtypes: new[] { CardSubtype.Forest });
        forest.ChangeOwner(alice);
        forest.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(forest);
        forest.ActiveEffects = ces;

        // Live sanity: Kird Ape reads 2/3 when controller has a Forest.
        kirdApe.Power.Should().Be(2, "live: Kird Ape 1/1 + Forest pump +1/+2 = 2/3");
        kirdApe.Toughness.Should().Be(3);

        // Clone and verify the buff persists.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var clonedKirdApe = (Creature)cloned.CardMap[kirdApe.InstanceId];

        clonedKirdApe.Power.Should().Be(2,
            "sandbox: Kird Ape still gets +1/+2 from re-registered ForestSelfPumpStaticEffect");
        clonedKirdApe.Toughness.Should().Be(3);
    }

    // ── 2. Bespoke anthem — KaheeraAnthemEffect ─────────────────────────────

    [Fact]
    public void Clone_KaheeraAnthem_BuffsPresentAfterClone()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var ces = new ContinuousEffectsService();

        // Kaheera: registers KaheeraAnthemEffect — "Other Cats/Elementals/Nightmares/
        // Dinosaurs/Beasts you control get +1/+1 and have Vigilance."
        var kaheera = KaheeraTheOrphanguardFactory.Create(alice, continuousEffects: ces);
        kaheera.ChangeOwner(alice);
        kaheera.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(kaheera);
        kaheera.ActiveEffects = ces;

        // A Cat creature to receive the buff.
        var catWarrior = new Creature("Cat Warrior", "{G}", 2, 2,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Warrior });
        catWarrior.ChangeOwner(alice);
        catWarrior.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(catWarrior);
        catWarrior.ActiveEffects = ces;

        // Live sanity: cat reads 3/3 (2/2 + Kaheera's +1/+1).
        catWarrior.Power.Should().Be(3, "live: Cat 2/2 + Kaheera +1/+1 = 3/3");
        catWarrior.Toughness.Should().Be(3);

        // Clone and verify.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var clonedCat = (Creature)cloned.CardMap[catWarrior.InstanceId];

        clonedCat.Power.Should().Be(3,
            "sandbox: Cat gets +1/+1 from re-registered KaheeraAnthemEffect");
        clonedCat.Toughness.Should().Be(3);
    }

    // ── 3. allPlayersResolver anthem — SliverLegionAnthemEffect ─────────────

    [Fact]
    public void Clone_SliverLegionAnthem_AllPlayersCountReboundToClone()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var ces = new ContinuousEffectsService();
        var players = new List<Player> { alice, bob };

        // Sliver Legion: "All Sliver creatures get +1/+1 for each other Sliver
        // on the battlefield."
        var legion = SliverLegionFactory.Create(alice, ces, () => players);
        legion.ChangeOwner(alice);
        legion.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(legion);
        legion.ActiveEffects = ces;

        // A second Sliver to buffer Sliver Legion and be buffed by it.
        var hatchling = new Creature("Virulent Sliver", "{G}", 1, 1,
            subtypes: new[] { CardSubtype.Sliver });
        hatchling.ChangeOwner(alice);
        hatchling.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(hatchling);
        hatchling.ActiveEffects = ces;

        // With 2 Slivers, each gets +(2-1)/+(2-1) = +1/+1.
        // Sliver Legion (7/7 base) reads 8/8; hatchling (1/1 base) reads 2/2.
        legion.Power.Should().Be(8,
            "live: Legion 7/7 + 1 other Sliver = 8/8");
        hatchling.Power.Should().Be(2,
            "live: hatchling 1/1 + 1 other Sliver = 2/2");

        // Clone — the cloner rebinds clonedPlayers to the cloned player list.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var clonedLegion    = (Creature)cloned.CardMap[legion.InstanceId];
        var clonedHatchling = (Creature)cloned.CardMap[hatchling.InstanceId];

        clonedLegion.Power.Should().Be(8,
            "sandbox: Legion re-reads cloned battlefield for Sliver count");
        clonedHatchling.Power.Should().Be(2,
            "sandbox: hatchling gets +1 from re-registered SliverLegionAnthemEffect");
    }

    // ── 4. Graveyard-driven pump — LandsInGraveyardPumpEffect ───────────────

    [Fact]
    public void Clone_KnightOfReliquaryPump_CorrectlyReadsCopiedGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var ces = new ContinuousEffectsService();

        // Knight of the Reliquary: 2/2 base, gets +N/+N for each land in GY.
        // Pass eventBus: null → lifecycle binder wires no ETB subscription,
        // so we manually register the effect below (test-board shortcut).
        var knight = KnightOfTheReliquaryFactory.Create(
            alice, effects: ces, eventBus: null, zoneService: null);
        knight.ChangeOwner(alice);
        knight.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(knight);
        knight.ActiveEffects = ces;

        // Directly register the effect (bypassing the ETB lifecycle binder).
        ces.Register(new KnightOfTheReliquaryFactory.LandsInGraveyardPumpEffect(knight));

        // Put two Land cards in Alice's graveyard to simulate fetch activations.
        var land1 = new Land("Forest");
        land1.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(land1);

        var land2 = new Land("Plains");
        land2.ChangeOwner(alice);
        alice.Zones.Graveyard.AddCard(land2);

        // Live sanity: Knight is 2+2 / 2+2 = 4/4.
        knight.Power.Should().Be(4, "live: Knight 2/2 + 2 lands in GY = 4/4");
        knight.Toughness.Should().Be(4);

        // Clone.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var clonedKnight = (Creature)cloned.CardMap[knight.InstanceId];

        clonedKnight.Power.Should().Be(4,
            "sandbox: Knight reads cloned graveyard (2 lands) via re-registered effect");
        clonedKnight.Toughness.Should().Be(4);
    }

    // ── 5. BecomesPTEffect (Layer 7b) — base case ───────────────────────────

    [Fact]
    public void Clone_BecomesPTEffect_SetsBasePtInClone()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var ces = new ContinuousEffectsService();

        // A vanilla 2/2 that gets a BecomesPTEffect making it 5/5.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.ChangeOwner(alice);
        creature.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(creature);
        creature.ActiveEffects = ces;

        // Directly register a BecomesPTEffect (simulates e.g. a copy becoming X/X).
        var effect = new BecomesPTEffect(creature, 5, 5);
        ces.Register(effect);

        // Live sanity.
        creature.Power.Should().Be(5, "live: BecomesPTEffect sets base to 5");
        creature.Toughness.Should().Be(5);

        // Clone.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var clonedCreature = (Creature)cloned.CardMap[creature.InstanceId];

        clonedCreature.Power.Should().Be(5,
            "sandbox: BecomesPTEffect re-registered on the cloned creature");
        clonedCreature.Toughness.Should().Be(5);
    }

    // ── 6. PumpUntilEndOfTurnEffect — mid-turn snapshot ──────────────────────

    [Fact]
    public void Clone_PumpUntilEot_SnapshotPreservesCurrentTurnBuff()
    {
        var alice = new Player("Alice", 20);
        var bob   = new Player("Bob",   20);

        var ces = new ContinuousEffectsService();

        // A 2/2 that has received a +3/+3 pump this turn (e.g. Giant Growth).
        var attacker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        attacker.ChangeOwner(alice);
        attacker.ChangeController(alice);
        alice.Zones.Battlefield.AddCard(attacker);
        attacker.ActiveEffects = ces;

        var pump = new PumpUntilEndOfTurnEffect(attacker, 3, 3);
        ces.Register(pump);

        // Live sanity.
        attacker.Power.Should().Be(5, "live: 2+3=5");
        attacker.Toughness.Should().Be(5);

        // Clone mid-turn — the buff should be visible in the sandbox snapshot.
        var cloned = GameStateCloner.Clone(new[] { alice, bob });
        var clonedAttacker = (Creature)cloned.CardMap[attacker.InstanceId];

        clonedAttacker.Power.Should().Be(5,
            "sandbox mid-turn snapshot: +3/+3 pump preserved via CloneForSim");
        clonedAttacker.Toughness.Should().Be(5);
    }
}
