using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShockLandCycleFactory"/> — the 10-card
/// Ravnica / Return-to-Ravnica / Gatecrash dual-land cycle.
///
/// Oracle (canonical, all 10):
/// "({T}: Add {A} or {B}.) As [Card] enters, you may pay 2 life. If you
///  don't, it enters tapped."
///
/// Covers, per cycle member:
/// - Identity (Land type, printed name, dual basic-land subtypes, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Two mana abilities producing the right coloured pair.
/// - ETB pay-2-life replacement via <see cref="ConditionalEntersTappedReplacement"/>:
///     - decline path: enters tapped, life unchanged.
///     - accept path: enters untapped, -2 life.
///     - insufficient life (CR 119.4): enters tapped, no prompt fired,
///       life unchanged.
///     - no-agent: enters tapped (default decline posture).
/// - <see cref="NamedCardFactory"/> dispatch resolves each printed name.
/// - Args validation: null owner, too few args, unknown subtype.
/// </summary>
[Trait("Color", "C")]
public class ShockLandCycleTests : IDisposable
{
    public ShockLandCycleTests()
    {
        // Tests register agents in the global AgentRegistry — clear in
        // ctor + Dispose to keep tests isolated.
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    /// <summary>
    /// All 10 shock lands with their canonical subtype + colour args.
    /// </summary>
    public static IEnumerable<object[]> AllShockLands => new[]
    {
        // cardName, subtypeA, subtypeB, colourA, colourB
        new object[] { "Sacred Foundry",    "Mountain", "Plains",   "R", "W" },
        new object[] { "Hallowed Fountain", "Plains",   "Island",   "W", "U" },
        new object[] { "Watery Grave",      "Island",   "Swamp",    "U", "B" },
        new object[] { "Overgrown Tomb",    "Swamp",    "Forest",   "B", "G" },
        new object[] { "Temple Garden",     "Forest",   "Plains",   "G", "W" },
        new object[] { "Blood Crypt",       "Swamp",    "Mountain", "B", "R" },
        new object[] { "Godless Shrine",    "Plains",   "Swamp",    "W", "B" },
        new object[] { "Breeding Pool",     "Forest",   "Island",   "G", "U" },
        new object[] { "Stomping Ground",   "Mountain", "Forest",   "R", "G" },
        new object[] { "Steam Vents",       "Island",   "Mountain", "U", "R" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_IsLand_WithCorrectName(
        string cardName, string a, string b, string ca, string cb)
    {
        var alice = new Player("Alice", 20);

        var land = ShockLandCycleFactory.Create(alice, new[] { cardName, a, b, ca, cb });

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(cardName);
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_CarriesBothBasicLandSubtypes(
        string cardName, string subtypeA, string subtypeB, string ca, string cb)
    {
        _ = ca; _ = cb;
        var alice = new Player("Alice", 20);

        var land = ShockLandCycleFactory.Create(
            alice, new[] { cardName, subtypeA, subtypeB, ca, cb });

        var typeA = Enum.Parse<CardSubtype>(subtypeA);
        var typeB = Enum.Parse<CardSubtype>(subtypeB);
        land.HasSubtype(typeA).Should().BeTrue($"{cardName} is a {subtypeA}");
        land.HasSubtype(typeB).Should().BeTrue($"{cardName} is a {subtypeB}");
    }

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_IsNotBasic_NotLegendary(
        string cardName, string a, string b, string ca, string cb)
    {
        var alice = new Player("Alice", 20);

        var land = ShockLandCycleFactory.Create(alice, new[] { cardName, a, b, ca, cb });

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "shock lands are nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }
    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_HasTwoColouredManaAbilities(
        string cardName, string a, string b, string colourA, string colourB)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var land = ShockLandCycleFactory.Create(alice, new[] { cardName, a, b, colourA, colourB });

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2,
            "one ManaAbility per produced colour (A and B)");

        var matchA = ManaCost.Parse(colourA);
        var matchB = ManaCost.Parse(colourB);
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, matchA),
            $"{cardName} produces {{{colourA}}}");
        manaAbilities.Should().Contain(m => SameCost(m.ManaGenerated, matchB),
            $"{cardName} produces {{{colourB}}}");
    }

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_HasNoActivatedOrTriggeredAbilities(
        string cardName, string a, string b, string ca, string cb)
    {
        var alice = new Player("Alice", 20);

        var land = ShockLandCycleFactory.Create(alice, new[] { cardName, a, b, ca, cb });

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "shock lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "shock lands have no triggered abilities (ETB is a replacement, CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // ETB pay-2-life replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_EntersUntapped_WhenAgentPaysTwoLife(
        string cardName, string a, string b, string ca, string cb)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = ShockLandCycleFactory.Create(
            alice, new[] { cardName, a, b, ca, cb }, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            $"{cardName} enters untapped when the controller pays 2 life");
        alice.LifeTotal.Should().Be(18,
            "paying 2 life drops Alice from 20 → 18");
    }

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_EntersTapped_WhenAgentDeclines(
        string cardName, string a, string b, string ca, string cb)
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = ShockLandCycleFactory.Create(
            alice, new[] { cardName, a, b, ca, cb }, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"{cardName} enters tapped when the controller declines to pay 2 life");
        alice.LifeTotal.Should().Be(20,
            "declining keeps Alice at 20");
    }

    [Theory]
    [MemberData(nameof(AllShockLands))]
    public void ShockLand_EntersTapped_WhenControllerCannotPayTwoLife(
        string cardName, string a, string b, string ca, string cb)
    {
        // CR 119.4 — you can't pay life you don't have. With life = 1
        // the agent is never even prompted; the land enters tapped.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(19); // life = 1
        // No QueueYesNo — if the predicate (incorrectly) prompted, the
        // ScriptedAgent would throw InvalidOperationException, exposing the
        // CR 119.4 gate bug.
        var agent = new ScriptedAgent();
        AgentRegistry.Set(alice, agent);

        var land = ShockLandCycleFactory.Create(
            alice, new[] { cardName, a, b, ca, cb }, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            $"{cardName} enters tapped when controller can't pay 2 life (life={alice.LifeTotal})");
        alice.LifeTotal.Should().Be(1,
            "life is unchanged — no payment took place");
        // ScriptedAgent would throw if prompted (empty queue) — surviving
        // this far means the predicate never asked. CR 119.4 honoured.
    }

    [Fact]
    public void ShockLand_EntersUntapped_AtExactlyTwoLife()
    {
        // CR 119.4 carve-out — life payments may bring you to 0. At
        // exactly 2 life paying is legal: drop to 0, then SBAs handle
        // the loss of game outside this replacement.
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.LoseLife(18); // life = 2
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = ShockLandCycleFactory.Create(
            alice,
            new[] { "Sacred Foundry", "Mountain", "Plains", "R", "W" },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "at exactly 2 life the payment is legal — enters untapped");
        alice.LifeTotal.Should().Be(0,
            "paying 2 life from 2 drops to 0; SBAs run later");
    }

    [Fact]
    public void ShockLand_EntersTapped_WhenNoAgentRegistered()
    {
        // No agent → default to declining the optional payment (matches
        // the shape-only posture of the single-arg dispatcher path).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        // intentionally no AgentRegistry.Set

        var land = ShockLandCycleFactory.Create(
            alice,
            new[] { "Watery Grave", "Island", "Swamp", "U", "B" },
            replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent → default decline → enters tapped");
        alice.LifeTotal.Should().Be(20);
    }
    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ShockLand_Create_ThrowsOnNullOwner()
    {
        var act = () => ShockLandCycleFactory.Create(
            null!,
            new[] { "Sacred Foundry", "Mountain", "Plains", "R", "W" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ShockLand_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => ShockLandCycleFactory.Create(
            alice,
            new[] { "Sacred Foundry", "Mountain", "Plains", "R" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ShockLandCycleFactory needs args*");
    }

    [Fact]
    public void ShockLand_Create_ThrowsOnUnknownSubtype()
    {
        var alice = new Player("Alice", 20);

        var act = () => ShockLandCycleFactory.Create(
            alice,
            new[] { "Sacred Foundry", "NotASubtype", "Plains", "R", "W" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*unknown basic subtype*");
    }

    [Fact]
    public void ShockLand_FallbackOverload_BuildsSacredFoundry()
    {
        var alice = new Player("Alice", 20);

        var land = ShockLandCycleFactory.Create(alice);

        land.Name.Should().Be("Sacred Foundry");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        land.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
        land.HasSubtype(CardSubtype.Plains).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool SameCost(ManaCost a, ManaCost b) =>
        a.White == b.White &&
        a.Blue == b.Blue &&
        a.Black == b.Black &&
        a.Red == b.Red &&
        a.Green == b.Green &&
        a.Generic == b.Generic;
}
