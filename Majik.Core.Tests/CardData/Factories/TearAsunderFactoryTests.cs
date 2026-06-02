using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tear Asunder (Commander Legends, {1}{G}, Instant).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Kicker {1}{B} (You may pay an additional {1}{B} as you cast this spell.)
///    Exile target artifact or enchantment. If this spell was kicked, exile
///    target nonland permanent instead."
///
/// Covers:
///   - Card identity (Instant, {1}{G}, Green, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 target, no modes, no variable X,
///     BotIntent.Removal. Unkicked targets artifact-or-enchantment; kicked
///     targets nonland permanent.
///   - Resolve unkicked: exiles a target artifact (CR 701.21).
///   - Resolve unkicked: exiles a target enchantment (CR 701.21).
///   - Resolve unkicked: a creature survives (wrong type, CR 608.2b).
///   - Resolve kicked: exiles a creature (kicker widens to nonland permanent,
///     CR 702.33b).
///   - Resolve kicked: a land survives (still "nonland", CR 608.2b).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
///   - Kicker rider builds a real KickerAdditionalCost (CR 702.33).
/// </summary>
[Trait("Color", "G")]
public class TearAsunderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TearAsunder_IsInstant_Green_AtCost1G()
    {
        var card = TearAsunderFactory.Create(_alice);

        card.Name.Should().Be("Tear Asunder");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Definition_Unkicked_HasSingleArtifactOrEnchantmentTarget()
    {
        var def = TearAsunderFactory.BuildSpellDefinition(wasKicked: false, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().ContainAny("artifact", "enchantment");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    [Fact]
    public void Definition_Kicked_HasSingleNonlandPermanentTarget()
    {
        var def = TearAsunderFactory.BuildSpellDefinition(wasKicked: true, o => o);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("nonland permanent");
    }

    // -----------------------------------------------------------------------
    // Resolve — unkicked (artifact or enchantment)
    // -----------------------------------------------------------------------

    [Fact]
    public void Unkicked_ExilesArtifact()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact, wasKicked: false);

        artifact.Zone.Should().Be(ZoneType.Exile,
            "unkicked exiles a target artifact (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void Unkicked_ExilesEnchantment()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        Resolve(enchantment, wasKicked: false);

        enchantment.Zone.Should().Be(ZoneType.Exile,
            "unkicked exiles a target enchantment (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(enchantment);
    }

    [Fact]
    public void Unkicked_CreatureTarget_DoesNothing()
    {
        // A creature is not a legal target for the unkicked spell. If somehow
        // resolved against one, CR 608.2b → no-op.
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature, wasKicked: false);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "unkicked targets artifact or enchantment only (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Resolve — kicked (any nonland permanent)
    // -----------------------------------------------------------------------

    [Fact]
    public void Kicked_ExilesCreature()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Tarmogoyf", "{1}{G}", 4, 5);

        Resolve(creature, wasKicked: true);

        creature.Zone.Should().Be(ZoneType.Exile,
            "kicked widens the target to any nonland permanent (CR 702.33b)");
        _bob.Zones.Exile.GetCards().Should().Contain(creature);
    }

    [Fact]
    public void Kicked_ExilesArtifact()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact, wasKicked: true);

        artifact.Zone.Should().Be(ZoneType.Exile,
            "kicked still exiles artifacts (still a nonland permanent, CR 701.21)");
    }

    [Fact]
    public void Kicked_LandTarget_DoesNothing()
    {
        var land = new Land("Forest");
        land.SetOwner(_bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        Resolve(land, wasKicked: true);

        land.Zone.Should().Be(ZoneType.Battlefield,
            "kicked targets a nonland permanent — a land is not legal (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal target (left battlefield)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact, wasKicked: false);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Kicker rider
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAdditionalCost_ProducesKickerRider()
    {
        var card = TearAsunderFactory.Create(_alice);

        var cost = TearAsunderFactory.BuildAdditionalCost(card);

        cost.Should().BeOfType<KickerAdditionalCost>();
        cost.Description.Should().Contain("Kicker");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken, bool wasKicked)
    {
        var def = TearAsunderFactory.BuildSpellDefinition(wasKicked, targetResolver: t => t);
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

    private static T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
