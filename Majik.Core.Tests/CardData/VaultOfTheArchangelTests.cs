using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Vault of the Archangel (Dark Ascension).
///
/// Oracle:
///   "{T}: Add {C}.
///    {2}{W}{B}, {T}: Creatures you control gain deathtouch and lifelink
///    until end of turn."
///
/// Coverage:
///   * Identity — plain Land, no printed supertypes/subtypes.
///   * NamedCardFactory dispatches Vault of the Archangel to a Land.
///   * {T}: Add {C} — vanilla mana ability that taps the land for {C}.
///   * {2}{W}{B}, {T}: grant — single ActivatedAbility distinct from the
///     mana ability; on Resolve, registers a deathtouch + lifelink
///     until-end-of-turn grant on every creature the controller controls,
///     and leaves opponent creatures untouched.
/// </summary>
public class VaultOfTheArchangelTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasCorrectCardShape()
    {
        var vault = VaultOfTheArchangelFactory.Create(_alice);

        vault.Name.Should().Be("Vault of the Archangel");
        vault.HasType(CardType.Land).Should().BeTrue();
        vault.Supertypes.Should().BeEmpty(
            "Vault of the Archangel has no printed supertypes");
        vault.Subtypes.Should().BeEmpty(
            "Vault of the Archangel has no printed land subtypes");
        vault.Owner.Should().BeSameAs(_alice);
        vault.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Vault of the Archangel", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Vault of the Archangel");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void HasColorlessManaAbility_TappingProducesColorless()
    {
        var vault = VaultOfTheArchangelFactory.Create(_alice);
        var manaAbility = vault.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        produced.Generic.Should().Be(1,
            "{C} buckets as Generic +1 in ManaCost.Parse (same bucket as Karn's Bastion / Mutavault)");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        vault.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {2}{W}{B}, {T}: Creatures you control gain deathtouch and lifelink EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleGrantActivatedAbility_AlongsideManaAbility_WithColoredCost()
    {
        var vault = VaultOfTheArchangelFactory.Create(_alice);

        var activated = vault.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Effects.Should().HaveCount(1);
        activated.TargetRequests.Should().BeEmpty(
            "the grant doesn't target — it self-selects 'creatures you control'");

        // {2}{W}{B} mana cost + tap symbol.
        var manaCost = activated.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.White.Should().Be(1, "the grant costs one white");
        manaCost.Black.Should().Be(1, "the grant costs one black");
        manaCost.Generic.Should().Be(2, "the grant costs two generic");
        activated.Costs.OfType<AdditionalCost>().Should().NotBeEmpty(
            "the grant has a tap cost ({T})");
    }

    [Fact]
    public void Resolve_GrantsDeathtouchAndLifelinkToControllerCreaturesOnly()
    {
        var vault = VaultOfTheArchangelFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vault);
        vault.SetZone(ZoneType.Battlefield);

        // Alice controls a creature; it must gain deathtouch + lifelink.
        var mine = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        mine.SetOwner(_alice);
        mine.SetController(_alice);
        mine.ActiveEffects = new ContinuousEffectsService();
        _alice.Zones.Battlefield.AddCard(mine);
        mine.SetZone(ZoneType.Battlefield);

        // Bob controls a creature; it must NOT be affected.
        var theirs = new Creature("Hill Giant", "{3}{R}", 3, 3);
        theirs.SetOwner(_bob);
        theirs.SetController(_bob);
        theirs.ActiveEffects = new ContinuousEffectsService();
        _bob.Zones.Battlefield.AddCard(theirs);
        theirs.SetZone(ZoneType.Battlefield);

        var activated = vault.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        activated.Resolve();

        CombatAbilities.HasDeathtouch(mine).Should().BeTrue(
            "creatures you control gain deathtouch until end of turn");
        CombatAbilities.HasLifelink(mine).Should().BeTrue(
            "creatures you control gain lifelink until end of turn");

        CombatAbilities.HasDeathtouch(theirs).Should().BeFalse(
            "only creatures the controller controls are affected");
        CombatAbilities.HasLifelink(theirs).Should().BeFalse(
            "only creatures the controller controls are affected");
    }
}
