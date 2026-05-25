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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EldraziConscriptionFactory"/>.
///
/// Card: Eldrazi Conscription — Tribal Enchantment — Aura Eldrazi {8}
/// (Rise of the Eldrazi).
///   "Enchant creature
///    Enchanted creature gets +10/+10 and has annihilator 2 and trample."
///
/// Covers:
///   - Identity / dispatch (Tribal + Enchantment, Aura + Eldrazi).
///   - Aura subtype + Eldrazi subtype.
///   - +10/+10 boost on the attached creature via AttachedBoostEffect.
///   - Granted keywords: "Annihilator 2", "Trample".
///   - Keyword markers (discoverability) on the aura itself.
///   - Annihilator trigger fires when the bearer attacks (sacrifices 2).
///   - Trigger does NOT fire when aura is unattached.
///   - Build-spell-definition produces a target-creature aura request.
/// </summary>
public class EldraziConscriptionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziConscription_Identity()
    {
        var c = EldraziConscriptionFactory.Create(_alice);

        c.Name.Should().Be("Eldrazi Conscription");
        c.ManaCost.Should().Be("{8}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasType(CardType.Tribal).Should().BeTrue("printed Tribal Enchantment line");
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EldraziConscription()
    {
        var card = NamedCardFactory.Create("Eldrazi Conscription", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Eldrazi Conscription");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static boost — +10/+10 + Annihilator 2 + Trample
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusTenPlusTen_AppliesToAttachedCreature()
    {
        var effects = new ContinuousEffectsService();
        var aura = EldraziConscriptionFactory.Create(
            _alice, effects, triggers: null, agentSelector: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(12, "2 + 10 = 12");
        chars.Toughness.Should().Be(12, "2 + 10 = 12");
    }

    [Fact]
    public void Static_GrantsAnnihilator2AndTrampleKeywords()
    {
        var effects = new ContinuousEffectsService();
        var aura = EldraziConscriptionFactory.Create(
            _alice, effects, triggers: null, agentSelector: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("Annihilator 2");
        chars.Keywords.Should().Contain("Trample");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = EldraziConscriptionFactory.Create(
            _alice, effects, triggers: null, agentSelector: null);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // Don't attach — the boost should not fire.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Trample");
    }

    [Fact]
    public void Keyword_Markers_Present_On_Aura()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);
        var keywords = aura.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Annihilator" && k.Arg == 2,
            "discoverability marker for CR 702.86 Annihilator 2");
        keywords.Should().Contain(k => k.Keyword == "Trample",
            "discoverability marker for CR 702.19 Trample");
    }

    // -----------------------------------------------------------------------
    // Annihilator trigger — bearer's attack → defender sacrifices 2
    // -----------------------------------------------------------------------

    [Fact]
    public void Annihilator_Trigger_Attached_ToCard_Even_WithoutManager()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);

        var triggers = aura.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "the Annihilator-aura trigger is attached as a card ability for shape");
    }

    [Fact]
    public void Annihilator_Trigger_DoesNotMatch_WhenUnattached()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trig = aura.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(bear, _bob), trig)
            .Should().BeFalse(
                "the aura isn't attached to the attacker; trigger must skip");
    }

    [Fact]
    public void Annihilator_Trigger_Matches_WhenBearerAttacks()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        aura.AttachTo(bear);

        var trig = aura.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(bear, _bob), trig)
            .Should().BeTrue();
    }

    [Fact]
    public void Annihilator_Trigger_DoesNotMatch_OtherCreatureAttacking()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        aura.AttachTo(bear);

        var unenchanted = new Creature("Bear 2", "{1}{G}", 2, 2);
        unenchanted.SetOwner(_alice);
        unenchanted.SetController(_alice);
        unenchanted.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(unenchanted);

        var trig = aura.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(unenchanted, _bob), trig)
            .Should().BeFalse(
                "only the bearer's attacks fire the aura's Annihilator");
    }

    [Fact]
    public void Annihilator_Trigger_OnAttack_SacrificesTwoPermanents()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);
        aura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aura);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        aura.AttachTo(bear);

        // Bob has 3 sacrificable permanents — deterministic fallback
        // sacrifices the first two.
        var seeded = new List<Creature>();
        for (var i = 0; i < 3; i++)
        {
            var b = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            b.SetOwner(_bob);
            b.SetController(_bob);
            b.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(b);
            seeded.Add(b);
        }

        var trig = aura.Abilities.OfType<TriggeredAbility>().First();
        trig.Condition.Matches(new CreatureAttacksEvent(bear, _bob), trig)
            .Should().BeTrue();
        foreach (var e in trig.Effects) e.Execute();

        seeded[0].Zone.Should().Be(ZoneType.Graveyard);
        seeded[1].Zone.Should().Be(ZoneType.Graveyard);
        seeded[2].Zone.Should().Be(ZoneType.Battlefield,
            "Annihilator 2 — only two sacrifices");
    }

    // -----------------------------------------------------------------------
    // Spell definition — Enchant creature target
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var aura = EldraziConscriptionFactory.Create(_alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        var land = new Land("Plains");
        var enchantment = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, enchantment };
        var def = EldraziConscriptionFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(enchantment);
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
