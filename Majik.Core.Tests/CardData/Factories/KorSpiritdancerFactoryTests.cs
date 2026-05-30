using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KorSpiritdancerFactory"/> (Rise of the Eldrazi /
/// reprints, {1}{W}). Creature — Kor Wizard 0/2. Oracle text (verified
/// against Scryfall):
///   "This creature gets +2/+2 for each Aura attached to it.
///    Whenever you cast an Aura spell, you may draw a card."
///
/// Covers:
/// - Identity (Kor Wizard, mana cost, base P/T 0/2, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Aura-count self-pump: 0 / 1 / 2 Auras attached -> +0/+2/+4 each.
/// - Pump ignores non-Aura attachments (Equipment).
/// - Casting an Aura spell -> draw 1.
/// - Casting a vanilla creature spell -> no draw.
/// - Opponent casting an Aura -> no draw.
/// </summary>
public class KorSpiritdancerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Enchantment NewAura(Player owner, string name = "Pacifism")
        => new(name, "{1}{W}", subtypes: new[] { CardSubtype.Aura }) { Owner = owner };

    private static Majik.Core.Spells.Spell NewAuraSpell(Player controller)
    {
        var aura = NewAura(controller, "Shape the Sands");
        return new Majik.Core.Spells.Spell(aura, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller)
    {
        var creature = new Creature("Bear", "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSpiritdancer_Identity()
    {
        var card = KorSpiritdancerFactory.Create(_alice);

        card.Name.Should().Be("Kor Spiritdancer");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KorSpiritdancer_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kor Spiritdancer", _alice);
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kor Spiritdancer");
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Part 1 — Aura-count self-pump (CR 613.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSpiritdancer_NoAuras_IsBasePT()
    {
        var effects = new ContinuousEffectsService();
        var card = KorSpiritdancerFactory.Create(_alice, null, null, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var chars = effects.Compute(card);
        chars.Power.Should().Be(0, "no Auras attached -> base 0/2");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void KorSpiritdancer_OneAura_GetsPlus2Plus2()
    {
        var effects = new ContinuousEffectsService();
        var card = KorSpiritdancerFactory.Create(_alice, null, null, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var aura = NewAura(_alice);
        _alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
        aura.AttachTo(card);

        KorSpiritdancerFactory.CountAttachedAuras(card).Should().Be(1);

        var chars = effects.Compute(card);
        chars.Power.Should().Be(2, "one Aura -> +2/+2 over base 0/2");
        chars.Toughness.Should().Be(4);
    }

    [Fact]
    public void KorSpiritdancer_TwoAuras_GetsPlus4Plus4()
    {
        var effects = new ContinuousEffectsService();
        var card = KorSpiritdancerFactory.Create(_alice, null, null, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        foreach (var n in new[] { "Aura A", "Aura B" })
        {
            var aura = NewAura(_alice, n);
            _alice.Zones.Battlefield.AddCard(aura);
            aura.SetZone(ZoneType.Battlefield);
            aura.AttachTo(card);
        }

        KorSpiritdancerFactory.CountAttachedAuras(card).Should().Be(2);

        var chars = effects.Compute(card);
        chars.Power.Should().Be(4, "two Auras -> +4/+4 over base 0/2");
        chars.Toughness.Should().Be(6);
    }

    [Fact]
    public void KorSpiritdancer_NonAuraAttachment_DoesNotPump()
    {
        var effects = new ContinuousEffectsService();
        var card = KorSpiritdancerFactory.Create(_alice, null, null, effects);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Equipment is an attachment but not an Aura -> no pump (CR 205.3g).
        var equip = new Artifact("Bone Saw", "0",
            subtypes: new[] { CardSubtype.Equipment }) { Owner = _alice };
        _alice.Zones.Battlefield.AddCard(equip);
        equip.SetZone(ZoneType.Battlefield);
        equip.AttachTo(card);

        KorSpiritdancerFactory.CountAttachedAuras(card).Should().Be(0);

        var chars = effects.Compute(card);
        chars.Power.Should().Be(0, "Equipment is not an Aura -> no pump");
        chars.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Part 2 — Aura-cast draw trigger (CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSpiritdancer_CastAuraSpell_DrawsOne()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KorSpiritdancerFactory.Create(_alice, bus, triggers, null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewAuraSpell(_alice)));

        triggers.PendingCount.Should().Be(1,
            "Aura spell fires Kor Spiritdancer's cast trigger once (CR 603.1)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().ContainSingle()
            .Which.Should().Be("Top1", "drew the top card of the library");
    }

    [Fact]
    public void KorSpiritdancer_CastVanillaCreatureSpell_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KorSpiritdancerFactory.Create(_alice, bus, triggers, null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice)));

        triggers.PendingCount.Should().Be(0,
            "vanilla creature spell is not an Aura spell");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void KorSpiritdancer_OpponentCastsAura_DoesNotDraw()
    {
        SeedLibrary(_alice, "Top1");
        var bus = new Majik.Core.Events.EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = KorSpiritdancerFactory.Create(_alice, bus, triggers, null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewAuraSpell(_bob)));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts the trigger to Kor Spiritdancer's controller");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    private static void SeedLibrary(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Instant(n, "1") { Owner = p };
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
