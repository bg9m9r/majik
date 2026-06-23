using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="InsidiousRootsFactory"/>.
///
/// Insidious Roots (Duskmourn: House of Horror, {B}{G}). Enchantment. Oracle
/// text (verified against Scryfall):
///   "Creature tokens you control have "{T}: Add one mana of any color."
///    Whenever one or more creature cards leave your graveyard, create a 0/1
///    green Plant creature token, then put a +1/+1 counter on each Plant you
///    control."
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity ({B}{G} Enchantment, black/green).
/// - Group mana-grant: creature TOKENS the controller controls gain "{T}: Add
///   one mana of any color" (CR 613.1f); non-token creatures and the
///   opponent's tokens do not; the grant is revoked when Insidious Roots leaves
///   play (CR 611.2c).
/// - Leaves-graveyard trigger (CR 603.2): a creature card leaving the
///   controller's graveyard creates a 0/1 green Plant token, then puts a +1/+1
///   counter on each Plant the controller controls (including the new token).
/// </summary>
[Trait("Color", "M")]
public class InsidiousRootsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public InsidiousRootsFactoryTests()
    {
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    private System.Collections.Generic.IEnumerable<Player> AllPlayers() => new[] { _alice, _bob };

    private void PutOnBattlefield(ICard card, Player owner)
    {
        owner.Zones.Library.AddCard(card);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    private static bool ProducesColor(IManaAbility a, char wubrg)
    {
        var m = a.ManaGenerated;
        return wubrg switch
        {
            'W' => m.White == 1,
            'U' => m.Blue == 1,
            'B' => m.Black == 1,
            'R' => m.Red == 1,
            'G' => m.Green == 1,
            _ => false,
        };
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void InsidiousRoots_Identity()
    {
        var c = InsidiousRootsFactory.Create(_alice);

        c.Name.Should().Be("Insidious Roots");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeFalse();
        c.ManaCost.Should().Be("{B}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void InsidiousRoots_IsBlackGreen()
    {
        var c = InsidiousRootsFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // "Creature tokens you control have '{T}: Add one mana of any color.'"
    // -----------------------------------------------------------------------

    [Fact]
    public void GroupGrant_CreatureTokenYouControl_TapsForAnyColor()
    {
        var token = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1, Colors: new[] { ManaColor.White }),
            _alice, _zones);

        var roots = InsidiousRootsFactory.Create(_alice, _effects);
        PutOnBattlefield(roots, _alice);

        var abilities = token.Abilities.OfType<IManaAbility>().ToList();
        abilities.Should().HaveCount(5, "CR 605.1a — 'any color' = five single-colour mana abilities");
        foreach (var color in "WUBRG")
            abilities.Should().Contain(a => ProducesColor(a, color),
                $"the token should tap for {color} (CR 613.1f)");
    }

    [Fact]
    public void GroupGrant_DoesNotApplyToNontokenCreatures()
    {
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.ChangeOwner(_alice);
        bear.ChangeController(_alice);
        PutOnBattlefield(bear, _alice);

        var roots = InsidiousRootsFactory.Create(_alice, _effects);
        PutOnBattlefield(roots, _alice);
        _effects.Compute((Permanent)bear);

        bear.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "the grant scope is creature TOKENS you control, not real creatures (CR 111.4)");
    }

    [Fact]
    public void GroupGrant_DoesNotApplyToOpponentsTokens()
    {
        var bobToken = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Goblin", 1, 1, Colors: new[] { ManaColor.Red }),
            _bob, _zones);

        var roots = InsidiousRootsFactory.Create(_alice, _effects);
        PutOnBattlefield(roots, _alice);
        _effects.Compute((Permanent)bobToken);

        bobToken.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "the grant scope is tokens YOU control (CR 109.5)");
    }

    [Fact]
    public void GroupGrant_RevokedWhenRootsLeavesPlay()
    {
        var token = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Soldier", 1, 1, Colors: new[] { ManaColor.White }),
            _alice, _zones);

        var roots = InsidiousRootsFactory.Create(_alice, _effects);
        PutOnBattlefield(roots, _alice);
        token.Abilities.OfType<IManaAbility>().Should().HaveCount(5);

        _zones.MoveCard(roots, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        token.Abilities.OfType<IManaAbility>().Should().BeEmpty(
            "once the source leaves play the granted ability is lost (CR 613.6e)");
    }

    // -----------------------------------------------------------------------
    // "Whenever one or more creature cards leave your graveyard, create a 0/1
    //  green Plant creature token, then put a +1/+1 counter on each Plant you
    //  control."
    // -----------------------------------------------------------------------

    [Fact]
    public void LeavesGraveyard_CreatesGreenPlantToken_ThenCountersEachPlant()
    {
        var roots = InsidiousRootsFactory.Create(_alice);
        roots.SetOwner(_alice);
        roots.SetController(_alice);

        // Resolve the trigger directly (the create-then-counter body).
        InsidiousRootsFactory.CreatePlantThenCounterPlants(roots);

        var plants = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Plant))
            .ToList();

        plants.Should().HaveCount(1, "the trigger creates one 0/1 green Plant token");
        var plant = plants[0];
        plant.IsToken.Should().BeTrue();
        CardColors.GetColors(plant).Should().BeEquivalentTo(new[] { ManaColor.Green });
        plant.BasePower.Should().Be(0);
        plant.BaseToughness.Should().Be(1);

        // CR 122 — the just-created Plant gets a +1/+1 counter (it's a Plant
        // you control), so it's effectively 1/2.
        plant.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the new token is itself a Plant you control, so it gets a +1/+1 counter");
    }

    [Fact]
    public void LeavesGraveyard_CountersExistingPlantsToo()
    {
        var roots = InsidiousRootsFactory.Create(_alice);
        roots.SetOwner(_alice);
        roots.SetController(_alice);

        // An existing Plant the controller controls.
        var existing = new Creature("Wall of Roots", "{1}{G}", 0, 5,
            subtypes: new[] { CardSubtype.Plant, CardSubtype.Wall });
        existing.ChangeOwner(_alice);
        existing.ChangeController(_alice);
        PutOnBattlefield(existing, _alice);

        InsidiousRootsFactory.CreatePlantThenCounterPlants(roots);

        existing.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "each Plant you control gets a +1/+1 counter (CR 122)");

        // Two Plants now exist (the wall + the new token), both with a counter.
        var plantsWithCounter = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Plant)
                        && c.Counters.Count(CounterType.PlusOnePlusOne) == 1);
        plantsWithCounter.Should().Be(2);
    }

    private TriggeredAbility LeavesTrigger(Enchantment roots) =>
        roots.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    [Fact]
    public void LeavesGraveyard_FiresForCreatureLeavingYourGraveyard()
    {
        var roots = InsidiousRootsFactory.Create(_alice);
        roots.SetOwner(_alice);
        roots.SetController(_alice);

        var corpse = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        corpse.SetOwner(_alice);

        var trigger = LeavesTrigger(roots);
        var move = new CardMovedEvent(corpse, ZoneType.Graveyard, ZoneType.Hand);

        trigger.Condition.Matches(move, trigger).Should().BeTrue(
            "a creature card leaving the controller's graveyard fires the trigger (CR 603.2)");
    }

    [Fact]
    public void LeavesGraveyard_DoesNotFireForNoncreatureLeavingGraveyard()
    {
        var roots = InsidiousRootsFactory.Create(_alice);
        roots.SetOwner(_alice);
        roots.SetController(_alice);

        var instant = new Instant("Counterspell", "{U}{U}");
        instant.SetOwner(_alice);

        var trigger = LeavesTrigger(roots);
        var move = new CardMovedEvent(instant, ZoneType.Graveyard, ZoneType.Hand);

        trigger.Condition.Matches(move, trigger).Should().BeFalse(
            "only CREATURE cards leaving your graveyard fire the trigger");
    }

    [Fact]
    public void LeavesGraveyard_DoesNotFireForOpponentsGraveyard()
    {
        var roots = InsidiousRootsFactory.Create(_alice);
        roots.SetOwner(_alice);
        roots.SetController(_alice);

        var bobCorpse = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        bobCorpse.SetOwner(_bob);

        var trigger = LeavesTrigger(roots);
        var move = new CardMovedEvent(bobCorpse, ZoneType.Graveyard, ZoneType.Hand);

        trigger.Condition.Matches(move, trigger).Should().BeFalse(
            "a card leaving an OPPONENT'S graveyard does not fire 'your graveyard' (CR 109.5)");
    }
}
