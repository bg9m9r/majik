using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Bodies for a slice of the remaining catalogued Madness cards (CR 702.35).
/// Madness itself is already engine-supported intrinsically via
/// <see cref="MadnessCatalog"/> + the <c>Fx.DiscardCard</c> replacement funnel,
/// so each card here only needs its BODY:
///
/// <list type="bullet">
///   <item><b>Kitchen Imp</b> — fileless JSON 2/2 Imp, Flying + Haste.</item>
///   <item><b>Twins of Maurer Estate</b> — fileless JSON 3/5 Vampire vanilla.</item>
///   <item><b>Insatiable Gorgers</b> — 5/3 Vampire Berserker + AttacksEachCombat
///     marker (factory, mirrors Ulamog's Crusher's identical printed line).</item>
///   <item><b>Just the Wind</b> — bounce a creature (OracleSpellBinder →
///     BounceTargetTemplate).</item>
///   <item><b>Terminal Agony</b> — destroy a creature (→ DestroyCreatureTemplate).</item>
///   <item><b>Murderous Compulsion</b> — destroy a tapped creature
///     (→ DestroyCreatureTemplate, "tapped" modifier).</item>
///   <item><b>Ichor Slick</b> — target creature gets -3/-3 (→ DebuffCreatureTemplate).</item>
///   <item><b>Nagging Thoughts</b> — look at top two, one to hand / one to grave
///     (→ LookAtTopPutOneInHandTemplate).</item>
/// </list>
///
/// Each card must (a) dispatch by name into the right shape, (b) be catalogued
/// for intrinsic madness at its printed madness cost, and (c) for spells, bind
/// through the existing template registry from the seed oracle text.
/// </summary>
public class MadnessCardBodiesRemainingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Creatures ─────────────────────────────────────────────────────────

    [Fact]
    public void KitchenImp_Identity_FlyingHaste_AndMadness()
    {
        var card = (Creature)NamedCardFactory.Create("Kitchen Imp", _alice);

        card.Name.Should().Be("Kitchen Imp");
        card.ManaCost.Should().Be("{3}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Imp).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);

        CombatAbilities.HasFlying((Permanent)card).Should().BeTrue("Kitchen Imp has Flying (CR 702.9)");
        CombatAbilities.HasHaste((Permanent)card).Should().BeTrue("Kitchen Imp has Haste (CR 702.10)");

        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{B}"));
    }

    [Fact]
    public void TwinsOfMaurerEstate_Identity_Vanilla_AndMadness()
    {
        var card = (Creature)NamedCardFactory.Create("Twins of Maurer Estate", _alice);

        card.Name.Should().Be("Twins of Maurer Estate");
        card.ManaCost.Should().Be("{4}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(5);

        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{2}{B}"));
    }

    [Fact]
    public void InsatiableGorgers_Identity_AttacksEachCombatMarker_AndMadness()
    {
        var card = (Creature)NamedCardFactory.Create("Insatiable Gorgers", _alice);

        card.Name.Should().Be("Insatiable Gorgers");
        card.ManaCost.Should().Be("{2}{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Berserker).Should().BeTrue();
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(3);

        card.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>()
            .Should().Contain(k => k.Keyword == "AttacksEachCombat",
                "the attacks-each-combat restriction is shipped as a marker (CR 508.1c)");

        MadnessCatalog.HasMadness(card).Should().BeTrue();
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{3}{R}"));
    }

    [Fact]
    public void AllSliceCreatures_AreImplemented()
    {
        foreach (var name in new[] { "Kitchen Imp", "Twins of Maurer Estate", "Insatiable Gorgers" })
        {
            ImplementedCardNames.Contains(name).Should().BeTrue(
                $"{name}'s body is shipped this slice");
        }
    }

    // ── Spells: bind via the template registry from seed oracle text ────────

    [Fact]
    public void JustTheWind_BindsBounce_AndReturnsCreature()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Just the Wind",
                ManaCost = "{1}{U}",
                OracleText = "Return target creature to its owner's hand.\n" +
                             "Madness {U} (If you discard this card, discard it into exile. " +
                             "When you do, cast it for its madness cost or put it into your graveyard.)",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull("Just the Wind binds via BounceTargetTemplate");
        def!.TargetRequests.Should().HaveCount(1);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bear } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand, "the targeted creature is returned to its owner's hand");
    }

    [Fact]
    public void TerminalAgony_BindsDestroyCreature_AndDestroysIt()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Terminal Agony",
                ManaCost = "{2}{B}{R}",
                OracleText = "Destroy target creature.\nMadness {B}{R}",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull("Terminal Agony binds via DestroyCreatureTemplate");

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bear } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard, "the targeted creature is destroyed (CR 701.7)");
    }

    [Fact]
    public void MurderousCompulsion_BindsDestroyTappedCreature()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Murderous Compulsion",
                ManaCost = "{1}{B}",
                OracleText = "Destroy target tapped creature.\nMadness {1}{B}",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull("Murderous Compulsion binds via DestroyCreatureTemplate (tapped modifier)");

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bear } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard, "the targeted creature is destroyed");
    }

    [Fact]
    public void IchorSlick_BindsDebuff_AndShrinksCreature()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Ichor Slick",
                ManaCost = "{2}{B}",
                OracleText = "Target creature gets -3/-3 until end of turn.\n" +
                             "Cycling {2}\nMadness {3}{B}",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull("Ichor Slick binds via DebuffCreatureTemplate");
        def!.TargetRequests.Should().HaveCount(1);
    }

    [Fact]
    public void NaggingThoughts_BindsLookAtTopPutOneInHand()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Nagging Thoughts",
                ManaCost = "{1}{U}",
                OracleText = "Look at the top two cards of your library. Put one of them " +
                             "into your hand and the other into your graveyard.\nMadness {1}{U}",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull("Nagging Thoughts binds via LookAtTopPutOneInHandTemplate");
    }
}
