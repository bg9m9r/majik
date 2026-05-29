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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Molten Rain (Tempest / reprints, {1}{R}{R}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target land. If that land was nonbasic, Molten Rain deals 2
///    damage to the land's controller."
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}{R}, red, owner / controller).
///   - NamedCardFactory dispatch.
///   - Destroys a basic land → graveyard, NO damage (CR 701.7, CR 205.4a basic).
///   - Destroys a nonbasic land → graveyard AND 2 damage to its controller
///     (CR 701.7 + CR 119 conditional rider).
///   - No-op when target is not a land on the battlefield (CR 608.2b).
///
/// Mirrors the SmashToSmithereens shape (destroy + conditional damage to the
/// destroyed permanent's controller) and the Befoul destroy-land shape.
/// </summary>
public class MoltenRainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MoltenRain_IsSorcery_At1RR()
    {
        var card = MoltenRainFactory.Create(_alice);

        card.Name.Should().Be("Molten Rain");
        card.ManaCost.Should().Be("{1}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MoltenRain_IsRed()
    {
        var card = MoltenRainFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Red,
            "Molten Rain has {R}{R} in its mana cost (CR 105.2a)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MoltenRain()
    {
        var card = NamedCardFactory.Create("Molten Rain", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Molten Rain");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — basic land: destroy, no damage
    // -----------------------------------------------------------------------

    [Fact]
    public void MoltenRain_DestroysBasicLand_NoDamage()
    {
        var swamp = NewBasicLand(_bob, "Swamp");

        Resolve(swamp);

        swamp.Zone.Should().Be(ZoneType.Graveyard,
            "Molten Rain destroys the target land (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(swamp);
        _bob.LifeTotal.Should().Be(20,
            "the destroyed land was basic, so no damage is dealt (CR 119 conditional)");
    }

    // -----------------------------------------------------------------------
    // Resolution — nonbasic land: destroy AND 2 damage to controller
    // -----------------------------------------------------------------------

    [Fact]
    public void MoltenRain_DestroysNonbasicLand_Deals2ToController()
    {
        var nonbasic = NewNonbasicLand(_bob, "Steam Vents");

        Resolve(nonbasic);

        nonbasic.Zone.Should().Be(ZoneType.Graveyard,
            "Molten Rain destroys the target land (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(nonbasic);
        _bob.LifeTotal.Should().Be(18,
            "the destroyed land was nonbasic, so its controller is dealt 2 damage (CR 119)");
    }

    [Fact]
    public void MoltenRain_DamageGoesToLandController_NotCaster()
    {
        var nonbasic = NewNonbasicLand(_bob, "Blood Crypt");

        // Alice casts the spell; damage goes to Bob (the land's controller),
        // not Alice (CR 608.2 — "the land's controller").
        Resolve(nonbasic);

        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20, "the caster takes no damage");
    }

    // -----------------------------------------------------------------------
    // Resolution — illegal target (no-op)
    // -----------------------------------------------------------------------

    [Fact]
    public void MoltenRain_NonbasicLandTargetNotOnBattlefield_DoesNothing()
    {
        var nonbasic = NewNonbasicLand(_bob, "Steam Vents");

        // Simulate the land leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(nonbasic);
        nonbasic.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(nonbasic);

        Resolve(nonbasic);

        // CR 608.2b illegal target at resolution → entire spell no-op: no extra
        // graveyard move (already there), and crucially NO damage.
        _bob.LifeTotal.Should().Be(20,
            "illegal target → no destroy and no damage (CR 608.2b)");
    }

    [Fact]
    public void MoltenRain_NonLandTarget_DoesNothing()
    {
        var creature = new Creature("Goblin Guide", "{R}", 2, 2);
        creature.SetOwner(_bob);
        creature.SetController(_bob);
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            "Molten Rain can only target a land (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(object targetToken)
    {
        var def = MoltenRainFactory.BuildDefinition(targetResolver: t => t);
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

    private Land NewBasicLand(Player owner, string name)
    {
        var l = new Land(name, supertypes: new[] { CardSupertype.Basic });
        l.SetOwner(owner);
        l.SetController(owner);
        l.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(l);
        return l;
    }

    private Land NewNonbasicLand(Player owner, string name)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        l.SetController(owner);
        l.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(l);
        return l;
    }
}
