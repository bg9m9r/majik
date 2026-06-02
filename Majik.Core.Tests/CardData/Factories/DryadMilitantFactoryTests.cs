using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DryadMilitantFactory"/>.
///
/// Card: Dryad Militant — Creature — Dryad Soldier {G/W} 2/1 (Return to
/// Ravnica promo / reprints). Oracle text (verified against Scryfall):
///   "({G/W} can be paid with either {G} or {W}.)"
///   "If an instant or sorcery card would be put into a graveyard from
///    anywhere, exile it instead."
///
/// Structurally the static-replacement half of <see cref="RestInPeaceFactory"/>
/// / <see cref="SanctifierEnVecFactory"/> (a CR 614 graveyard→exile rewrite,
/// gated on the source being on the battlefield, not EOT-expirable), but
/// FILTERED to instant-or-sorcery cards instead of colour. No ETB sweep —
/// Dryad Militant has no enters trigger.
/// </summary>
[Trait("Color", "GW")]
public class DryadMilitantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DryadMilitant_Identity_AndPT_AndSubtypes()
    {
        var c = DryadMilitantFactory.Create(_alice);

        c.Name.Should().Be("Dryad Militant");
        c.ManaCost.Should().Be("{G/W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dryad).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DryadMilitant()
    {
        var card = NamedCardFactory.Create("Dryad Militant", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dryad Militant");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{G/W}");
    }

    // -----------------------------------------------------------------------
    // Static graveyard rewrite — only instant or sorcery CARDS
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_RewritesInstantGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var c = DryadMilitantFactory.Create(_alice, replacements: bus);
        PlaceOnBattlefield(c, _alice);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        // "from anywhere" — resolving off the stack to a graveyard is the
        // common case.
        var intent = new ZoneMoveIntent(bolt, ZoneType.Stack, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_RewritesSorceryGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var c = DryadMilitantFactory.Create(_alice, replacements: bus);
        PlaceOnBattlefield(c, _alice);

        // Discarded from hand — "from anywhere" covers any source zone.
        var sorcery = new Sorcery("Mind Rot", "{2}{B}");
        sorcery.SetOwner(_alice);

        var intent = new ZoneMoveIntent(sorcery, ZoneType.Hand, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void Static_DoesNotAffect_CreatureGraveyardMove()
    {
        var bus = new ReplacementBus();
        var c = DryadMilitantFactory.Create(_alice, replacements: bus);
        PlaceOnBattlefield(c, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);

        var intent = new ZoneMoveIntent(bear, ZoneType.Battlefield, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "only instant or sorcery cards are exiled; permanents die normally");
    }

    [Fact]
    public void Static_DoesNotAffect_InstantNonGraveyardMove()
    {
        var bus = new ReplacementBus();
        var c = DryadMilitantFactory.Create(_alice, replacements: bus);
        PlaceOnBattlefield(c, _alice);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        // Snapcaster-style return to hand, or a flashback exile, etc. — only
        // graveyard destinations are rewritten.
        var bounce = new ZoneMoveIntent(bolt, ZoneType.Stack, ZoneType.Hand, _bob);
        bus.Apply(bounce)!.ToZone.Should().Be(ZoneType.Hand,
            "non-graveyard destinations are unaffected even for instants");
    }

    [Fact]
    public void Static_IsInert_WhileNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        var c = DryadMilitantFactory.Create(_alice, replacements: bus);
        // Not placed on the battlefield.

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        var intent = new ZoneMoveIntent(bolt, ZoneType.Stack, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "the replacement is gated on the creature's battlefield zone");
    }

    [Fact]
    public void Static_IsNotEndOfTurnExpirable()
    {
        var bus = new ReplacementBus();
        var c = DryadMilitantFactory.Create(_alice, replacements: bus);
        PlaceOnBattlefield(c, _alice);

        bus.ExpireEndOfTurn();

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        var intent = new ZoneMoveIntent(bolt, ZoneType.Stack, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacement()
    {
        var c = DryadMilitantFactory.Create(_alice);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);

        var emptyBus = new ReplacementBus();
        var intent = new ZoneMoveIntent(bolt, ZoneType.Stack, ZoneType.Graveyard, _bob);
        emptyBus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "no replacement is registered on the single-arg path");
    }

    private static void PlaceOnBattlefield(Creature c, Player owner)
    {
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }
}
