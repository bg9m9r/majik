using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Spellstutter Sprite (Lorwyn, {1}{U}).
///
/// Covers:
///   - Identity (name, type, Faerie + Wizard subtypes, 1/1, mana cost).
///   - NamedCardFactory dispatch.
///   - Flash + Flying keyword markers + ETB triggered ability.
///   - ETB counters a 1-drop with just the Sprite as the only Faerie.
///   - ETB counters a higher-mv spell when more Faeries are out.
///   - ETB no-ops on an illegal target (mv exceeds Faerie count).
///   - Countered card lands in its owner's graveyard (CR 701.5).
/// </summary>
public class SpellstutterSpriteTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellstutterSpriteTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void SpellstutterSprite_Identity()
    {
        var c = SpellstutterSpriteFactory.Create(_alice);

        c.Name.Should().Be("Spellstutter Sprite");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB counter-target-spell trigger");
    }

    [Fact]
    public void SpellstutterSprite_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Spellstutter Sprite", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Spellstutter Sprite");
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void SpellstutterSprite_Etb_TargetRequestShape()
    {
        var c = SpellstutterSpriteFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("Faeries you control");
    }

    [Fact]
    public void SpellstutterSprite_Etb_CountersOneDrop_WithSelfAsOnlyFaerie()
    {
        // The Sprite itself counts as 1 Faerie controlled — so it should
        // be able to counter any mv-1 spell on the stack right after it
        // enters.
        var sprite = SpellstutterSpriteFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(sprite);
        sprite.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(boltSpell);

        bolt.ManaCostValue.TotalValue.Should().Be(1);

        var etb = sprite.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });

        foreach (var e in etb.Effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.5 — countered spell goes to its owner's graveyard");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bolt);
        _stack.GetAll().Should().NotContain(boltSpell);
    }

    [Fact]
    public void SpellstutterSprite_Etb_CountersThreeDrop_WithThreeFaeries()
    {
        // Two other Faeries already on Alice's battlefield + the Sprite =
        // 3 Faeries; can counter a mv-3 spell.
        var other1 = new Creature("Faerie Mechanist", "{2}{U}", 2, 2,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Artificer });
        other1.SetOwner(_alice); other1.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(other1);
        other1.SetZone(ZoneType.Battlefield);

        var other2 = new Creature("Faerie Conclave Token", "{U}", 2, 1,
            subtypes: new[] { CardSubtype.Faerie });
        other2.SetOwner(_alice); other2.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(other2);
        other2.SetZone(ZoneType.Battlefield);

        var sprite = SpellstutterSpriteFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(sprite);
        sprite.SetZone(ZoneType.Battlefield);

        var threeDrop = new Sorcery("Cryptic Command Lite", "{1}{U}{U}");
        threeDrop.SetOwner(_bob);
        threeDrop.SetController(_bob);
        threeDrop.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(threeDrop, _bob);
        _stack.Push(spell);

        threeDrop.ManaCostValue.TotalValue.Should().Be(3);

        var etb = sprite.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });

        foreach (var e in etb.Effects) e.Execute();

        threeDrop.Zone.Should().Be(ZoneType.Graveyard,
            "3 Faeries on battlefield → can counter mv-3 spell");
        _stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public void SpellstutterSprite_Etb_IllegalTarget_MvExceedsFaerieCount()
    {
        // Only the Sprite as the lone Faerie — mv-2 spell exceeds the
        // 1-Faerie count, so the effect no-ops (CR 608.2b).
        var sprite = SpellstutterSpriteFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(sprite);
        sprite.SetZone(ZoneType.Battlefield);

        var twoDrop = new Instant("Mana Leak", "{1}{U}");
        twoDrop.SetOwner(_bob);
        twoDrop.SetController(_bob);
        twoDrop.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(twoDrop, _bob);
        _stack.Push(spell);

        twoDrop.ManaCostValue.TotalValue.Should().Be(2);

        var etb = sprite.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });

        foreach (var e in etb.Effects) e.Execute();

        twoDrop.Zone.Should().Be(ZoneType.Stack,
            "mv exceeds Faerie count — effect no-ops, spell stays on stack");
        _stack.GetAll().Should().Contain(spell);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(twoDrop);
    }
}
