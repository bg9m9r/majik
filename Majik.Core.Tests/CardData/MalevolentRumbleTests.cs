using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the Malevolent Rumble spell binding in
/// <see cref="OracleSpellBinder"/>.
///
/// Covers:
/// - OracleSpellBinder recognises Malevolent Rumble oracle text and returns
///   a non-null SpellDefinition.
/// - Spell has no target requests.
/// - Effect moves permanent card from top-4 to caster's hand.
/// - Effect moves non-permanent cards (instants/sorceries) to graveyard.
/// - Effect creates one Eldrazi Spawn token on the battlefield.
/// - Empty library is a no-op (no crash).
/// - All four cards non-permanent: none go to hand, all go to graveyard.
/// </summary>
public class MalevolentRumbleTests
{
    private static readonly CardEntity RumbleEntity = new()
    {
        Name = "Malevolent Rumble",
        ManaCost = "{1}{G}",
        TypeLine = "Sorcery",
        OracleText =
            "Reveal the top four cards of your library. You may put a permanent card from among them into your hand. " +
            "Put the rest into your graveyard. " +
            "Create a 0/1 colorless Eldrazi Spawn creature token with \"Sacrifice this token: Add {C}.\"",
    };

    private static readonly ChosenSpellParams NoTargets =
        new(null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);

    private static SpellDefinition Bind(Player caster) =>
        OracleSpellBinder.Bind(RumbleEntity, caster, x => x, null)
        ?? throw new InvalidOperationException("Binder returned null");

    // -----------------------------------------------------------------------
    // Binder recognition
    // -----------------------------------------------------------------------

    [Fact]
    public void MalevolentRumble_BinderRecognisesOracleText()
    {
        var alice = new Player("Alice", 20);

        var def = OracleSpellBinder.Bind(RumbleEntity, alice, x => x, null);

        def.Should().NotBeNull("Malevolent Rumble's oracle text must match the MalevolentRumblePattern regex");
    }

    [Fact]
    public void MalevolentRumble_HasNoTargetRequests()
    {
        var alice = new Player("Alice", 20);
        var def = Bind(alice);

        def.TargetRequests.Should().BeEmpty("Malevolent Rumble has no targets");
    }

    // -----------------------------------------------------------------------
    // Permanent card → hand
    // -----------------------------------------------------------------------

    [Fact]
    public void MalevolentRumble_PermanentCard_GoesToCastersHand()
    {
        var alice = new Player("Alice", 20);
        var creature = new Creature("Bear Cub", "1G", 2, 2);
        creature.SetOwner(alice);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var def = Bind(alice);
        foreach (var effect in def.EffectFactory(NoTargets)) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(creature,
            "the first permanent card in the top 4 goes to the caster's hand");
        alice.Zones.Graveyard.GetCards().Should().NotContain(creature);
        creature.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void MalevolentRumble_NonPermanentCards_GoToGraveyard()
    {
        var alice = new Player("Alice", 20);
        // Two instants (non-permanent) + one creature (permanent)
        var instant1 = new Instant("Counterspell", "UU");
        var instant2 = new Instant("Shock", "R");
        var creature = new Creature("Forest Bear", "1G", 2, 2);
        instant1.SetOwner(alice);
        instant2.SetOwner(alice);
        creature.SetOwner(alice);

        alice.Zones.Library.AddCard(instant1);
        instant1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(instant2);
        instant2.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var def = Bind(alice);
        foreach (var effect in def.EffectFactory(NoTargets)) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().Contain(instant1, "non-permanent goes to graveyard");
        alice.Zones.Graveyard.GetCards().Should().Contain(instant2, "non-permanent goes to graveyard");
        alice.Zones.Graveyard.GetCards().Should().NotContain(creature, "permanent goes to hand");
        alice.Zones.Hand.GetCards().Should().Contain(creature);
    }

    [Fact]
    public void MalevolentRumble_AllNonPermanents_AllGoToGraveyard_NoneToHand()
    {
        var alice = new Player("Alice", 20);
        var cards = new[] { "A", "B", "C", "D" }
            .Select(n => { var c = new Instant(n, ""); c.SetOwner(alice); return c; })
            .ToList();
        foreach (var c in cards) { alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library); }

        var def = Bind(alice);
        foreach (var effect in def.EffectFactory(NoTargets)) effect.Execute();

        alice.Zones.Graveyard.GetCards().Should().HaveCount(4, "all 4 non-permanents go to graveyard");
        alice.Zones.Hand.GetCards().Should().BeEmpty("no permanent was found, so nothing goes to hand");
    }

    // -----------------------------------------------------------------------
    // Eldrazi Spawn token
    // -----------------------------------------------------------------------

    [Fact]
    public void MalevolentRumble_CreatesOneEldraziSpawnToken()
    {
        var alice = new Player("Alice", 20);
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(alice);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var def = Bind(alice);
        foreach (var effect in def.EffectFactory(NoTargets)) effect.Execute();

        var spawn = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Eldrazi Spawn");

        spawn.Should().NotBeNull("Malevolent Rumble always creates an Eldrazi Spawn token");
        spawn!.Power.Should().Be(0);
        spawn.Toughness.Should().Be(1);
        spawn.IsToken.Should().BeTrue();
        spawn.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        spawn.HasSubtype(CardSubtype.Spawn).Should().BeTrue();
    }

    [Fact]
    public void MalevolentRumble_SpawnToken_HasColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var def = Bind(alice);
        foreach (var effect in def.EffectFactory(NoTargets)) effect.Execute();

        var spawn = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.Name == "Eldrazi Spawn");

        spawn.Should().NotBeNull();
        spawn!.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Generic == 1,
                "Eldrazi Spawn has 'Sacrifice: Add {C}' wired as a ManaAbility producing 1 {C}; 'C' parses as generic/colorless");
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void MalevolentRumble_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);

        var def = Bind(alice);

        var act = () =>
        {
            foreach (var effect in def.EffectFactory(NoTargets)) effect.Execute();
        };

        act.Should().NotThrow("empty library is a no-op for Malevolent Rumble");
    }
}
