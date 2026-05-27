using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Mistbind Clique (Lorwyn, {3}{U}{U}).
///
/// Covers:
///   - Identity (name, type, Faerie + Wizard subtypes, 4/4, mana cost).
///   - NamedCardFactory dispatch.
///   - Flash + Flying keyword markers + ETB triggered ability.
///   - ETB target request shape ("target player").
///   - ETB taps every Land target player controls (CR 701.20).
///   - Already-tapped lands stay tapped (no-op guard).
///   - Lands of OTHER players are untouched.
///   - Non-Land permanents are untouched.
///
/// Champion-a-Faerie (CR 702.71) is documented as DEFERRED in the
/// factory — no engine primitive yet. The card still counts as a Faerie
/// for Spellstutter Sprite / Scion of Oona purposes.
/// </summary>
public class MistbindCliqueTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MistbindClique_Identity()
    {
        var c = MistbindCliqueFactory.Create(_alice);

        c.Name.Should().Be("Mistbind Clique");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB tap-all-lands trigger (Champion deferred — no engine primitive yet).");
    }

    [Fact]
    public void MistbindClique_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mistbind Clique", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Mistbind Clique");
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void MistbindClique_Etb_TargetRequestShape()
    {
        var c = MistbindCliqueFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target player");
    }

    [Fact]
    public void MistbindClique_Etb_TapsAllLands_OfTargetPlayer()
    {
        // Bob has three untapped Islands. Alice resolves Mistbind targeting
        // Bob — all of Bob's lands tap.
        var island1 = new Land("Island");
        island1.SetOwner(_bob); island1.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(island1);
        island1.SetZone(ZoneType.Battlefield);

        var island2 = new Land("Island");
        island2.SetOwner(_bob); island2.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(island2);
        island2.SetZone(ZoneType.Battlefield);

        var island3 = new Land("Island");
        island3.SetOwner(_bob); island3.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(island3);
        island3.SetZone(ZoneType.Battlefield);

        var mistbind = MistbindCliqueFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mistbind);
        mistbind.SetZone(ZoneType.Battlefield);

        var etb = mistbind.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in etb.Effects) e.Execute();

        island1.IsTapped.Should().BeTrue();
        island2.IsTapped.Should().BeTrue();
        island3.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void MistbindClique_Etb_AlreadyTappedLand_StaysTapped_NoOp()
    {
        // Pre-tapped land — Tap() guard prevents a redundant call, and the
        // land stays tapped.
        var island = new Land("Island");
        island.SetOwner(_bob); island.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(island);
        island.SetZone(ZoneType.Battlefield);
        island.Tap();
        island.IsTapped.Should().BeTrue();

        var mistbind = MistbindCliqueFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mistbind);
        mistbind.SetZone(ZoneType.Battlefield);

        var etb = mistbind.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in etb.Effects) e.Execute();

        island.IsTapped.Should().BeTrue("already tapped — guard no-ops, land stays tapped.");
    }

    [Fact]
    public void MistbindClique_Etb_DoesNotTapOtherPlayersLands()
    {
        // Alice's own Islands stay untapped — only the targeted player's
        // lands tap (CR 109.5 — "target player" is scoped).
        var aliceLand = new Land("Island");
        aliceLand.SetOwner(_alice); aliceLand.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(aliceLand);
        aliceLand.SetZone(ZoneType.Battlefield);

        var bobLand = new Land("Island");
        bobLand.SetOwner(_bob); bobLand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobLand);
        bobLand.SetZone(ZoneType.Battlefield);

        var mistbind = MistbindCliqueFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mistbind);
        mistbind.SetZone(ZoneType.Battlefield);

        var etb = mistbind.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in etb.Effects) e.Execute();

        bobLand.IsTapped.Should().BeTrue("target player's land taps.");
        aliceLand.IsTapped.Should().BeFalse("non-targeted player's lands untouched.");
    }

    [Fact]
    public void MistbindClique_Etb_DoesNotTapNonLandPermanents()
    {
        // Bob's creature is on the battlefield alongside a land. Only the
        // land taps.
        var bobLand = new Land("Island");
        bobLand.SetOwner(_bob); bobLand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobLand);
        bobLand.SetZone(ZoneType.Battlefield);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bobBear.SetOwner(_bob); bobBear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        var mistbind = MistbindCliqueFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mistbind);
        mistbind.SetZone(ZoneType.Battlefield);

        var etb = mistbind.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in etb.Effects) e.Execute();

        bobLand.IsTapped.Should().BeTrue("land taps.");
        bobBear.IsTapped.Should().BeFalse("non-land permanents untouched — 'all lands' is type-scoped.");
    }
}
