using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Mortify (Guildpact, {1}{W}{B}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Destroy target creature or enchantment."
///
/// Mortify is the white-black "destroy target creature or enchantment"
/// instant — structurally the creature-or-X destroy twin of
/// Hero's Downfall / Dreadbore (creature or planeswalker), only the second
/// allowed type differs (Enchantment rather than Planeswalker). The test set
/// mirrors DreadboreTests.
///
/// Covers:
///   - Card identity (Instant, {1}{W}{B}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 creature-or-enchantment target
///     request, no modes, no variable X, BotIntent.Removal.
///   - Resolve: destroys a creature (CR 701.7).
///   - Resolve: destroys an enchantment (CR 701.7).
///   - Resolve: artifact target (not creature, not enchantment) is illegal at
///     resolution → no-op (CR 608.2b).
///   - Resolve: off-battlefield target → no-op (CR 608.2b).
/// </summary>
public class MortifyTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortify_IsInstant_AtCost1WB()
    {
        var card = MortifyFactory.Create(_alice);

        card.Name.Should().Be("Mortify");
        card.ManaCost.Should().Be("{1}{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Mortify()
    {
        var card = NamedCardFactory.Create("Mortify", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Mortify");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortify_Definition_HasSingleCreatureOrEnchantmentTarget()
    {
        var def = MortifyFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or enchantment");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroys creature / enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortify_DestroysCreature()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Mortify destroys the targeted creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void Mortify_DestroysEnchantment()
    {
        var enchantment = new Enchantment("Oblivion Ring", "{2}{W}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(enchantment);
        enchantment.SetZone(ZoneType.Battlefield);

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            "Mortify destroys the targeted enchantment (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(enchantment);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(enchantment);
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets
    // -----------------------------------------------------------------------

    [Fact]
    public void Mortify_ArtifactTarget_DoesNothing()
    {
        // Pure artifact (not creature, not enchantment) — illegal at resolution.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Mortify can only destroy creatures or enchantments (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void Mortify_TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged by the resolve — CR 608.2b illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = MortifyFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
