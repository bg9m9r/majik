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
/// CR 700.2e — modal "Choose two —" spell. Kolaghan's Command, Dragons of Tarkir,
/// {1}{B}{R}, four modes (return creature from graveyard / 2 damage / discard / destroy artifact).
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/> — same pattern as <see cref="CrypticCommandTests"/>.
/// </summary>
public class KolaghansCommandTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Mode 0 — Return target creature card from graveyard to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_ReturnCreatureFromGraveyard_LandsInHand()
    {
        // Stage: Alice has a creature card in her graveyard.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        var def = KolaghansCommandFactory.BuildDefinition(
            _alice,
            o => o,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { KolaghansCommandFactory.ModeReturnCreature, KolaghansCommandFactory.ModeDealDamage });

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bear }, // mode 0 — creature card target
            Array.Empty<object>(), // mode 1 — damage target (unused in this test)
            Array.Empty<object>(), // mode 2 — discard target
            Array.Empty<object>(), // mode 3 — artifact target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: KolaghansCommandFactory.ModeReturnCreature,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                KolaghansCommandFactory.ModeReturnCreature,
                KolaghansCommandFactory.ModeDealDamage,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);

        // Execute only mode 0 effect (first in list).
        effects[0].Execute();

        bear.Zone.Should().Be(ZoneType.Hand,
            because: "mode 0 returns the creature card from graveyard to its owner's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Deal 2 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DealTwoDamage_ReducesCreatureToughness()
    {
        // Stage: Bob has a 3/3 creature on the battlefield.
        var bear = new Creature("Hill Giant", "{3}{R}", 3, 3);
        bear.SetOwner(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = KolaghansCommandFactory.BuildDefinition(
            _alice,
            o => o,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { KolaghansCommandFactory.ModeReturnCreature, KolaghansCommandFactory.ModeDealDamage });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), // mode 0 — graveyard target (unused)
            new object[] { bear }, // mode 1 — 2 damage target
            Array.Empty<object>(), // mode 2 — discard target
            Array.Empty<object>(), // mode 3 — artifact target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: KolaghansCommandFactory.ModeDealDamage,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                KolaghansCommandFactory.ModeReturnCreature,
                KolaghansCommandFactory.ModeDealDamage,
            });

        var effects = def.EffectFactory(chosen);
        // Mode 1 is index 1 in the chosen list.
        effects[1].Execute();

        bear.Damage.Should().Be(2,
            because: "mode 1 deals exactly 2 damage to the targeted creature");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Target player discards a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_Discard_TargetPlayerDiscardsOneCard()
    {
        // Stage: Bob has one card in hand.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bolt);

        var def = KolaghansCommandFactory.BuildDefinition(
            _alice,
            o => o,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { KolaghansCommandFactory.ModeDiscard, KolaghansCommandFactory.ModeDestroyArtifact });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(), // mode 0 — graveyard target (unused)
            Array.Empty<object>(), // mode 1 — damage target (unused)
            new object[] { _bob }, // mode 2 — discard target player
            Array.Empty<object>(), // mode 3 — artifact target (unused)
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: KolaghansCommandFactory.ModeDiscard,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                KolaghansCommandFactory.ModeDiscard,
                KolaghansCommandFactory.ModeDestroyArtifact,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        effects[0].Execute(); // mode 2 is the first chosen

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 2 moves the discarded card from Bob's hand to his graveyard");
        _bob.Zones.Hand.GetCards().Should().NotContain(bolt);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bolt);
    }

    // -----------------------------------------------------------------------
    // Mode 3 — Destroy target artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode3_DestroyArtifact_MovesArtifactToGraveyard()
    {
        // Stage: Bob has an artifact on the battlefield.
        var needle = new Artifact("Pithing Needle", "{1}");
        needle.SetOwner(_bob);
        needle.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(needle);

        var def = KolaghansCommandFactory.BuildDefinition(
            _alice,
            o => o,
            allPlayers: new[] { _alice, _bob },
            chosenModes: new[] { KolaghansCommandFactory.ModeDealDamage, KolaghansCommandFactory.ModeDestroyArtifact });

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),   // mode 0 — graveyard target (unused)
            Array.Empty<object>(),   // mode 1 — damage target (unused)
            Array.Empty<object>(),   // mode 2 — discard target (unused)
            new object[] { needle }, // mode 3 — artifact target
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: KolaghansCommandFactory.ModeDestroyArtifact,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                KolaghansCommandFactory.ModeDealDamage,
                KolaghansCommandFactory.ModeDestroyArtifact,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(2);
        // ModeDestroyArtifact is the second chosen mode.
        effects[1].Execute();

        needle.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 3 destroys the artifact (CR 701.7) — moves it to the graveyard");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(needle);
    }

    // -----------------------------------------------------------------------
    // Shape / dispatch tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_BlackRed()
    {
        var kc = KolaghansCommandFactory.Create(_alice);

        kc.Name.Should().Be("Kolaghan's Command");
        kc.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(kc).Should().Contain(ManaColor.Black);
        CardColors.GetColors(kc).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsKolaghansCommandShape()
    {
        var dispatched = NamedCardFactory.Create("Kolaghan's Command", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Kolaghan's Command");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesFourModes_FourTargetRequests()
    {
        var def = KolaghansCommandFactory.BuildDefinition(_alice, o => o, allPlayers: null);

        def.Modes.Should().HaveCount(4);
        def.TargetRequests.Should().HaveCount(4);
        def.TargetRequests[KolaghansCommandFactory.ModeReturnCreature].MinTargets.Should().Be(0);
        def.TargetRequests[KolaghansCommandFactory.ModeDealDamage].MinTargets.Should().Be(0);
        def.TargetRequests[KolaghansCommandFactory.ModeDiscard].MinTargets.Should().Be(0);
        def.TargetRequests[KolaghansCommandFactory.ModeDestroyArtifact].MinTargets.Should().Be(0);
    }
}
