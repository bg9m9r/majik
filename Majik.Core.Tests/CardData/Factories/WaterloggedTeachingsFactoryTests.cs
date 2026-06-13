using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WaterloggedTeachingsFactory"/> and
/// <see cref="InundatedArchiveFactory"/> — the front + back faces of the
/// Modern Horizons 3 modal double-faced card
/// Waterlogged Teachings // Inundated Archive.
///
/// Front face (Waterlogged Teachings, {3}{U/B}):
///   Instant. "Search your library for an instant card or a card with flash,
///   reveal it, put it into your hand, then shuffle."
///
/// Back face (Inundated Archive):
///   Land. "This land enters tapped." "{T}: Add {U} or {B}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: tutor an instant card → hand, then shuffle (CR 701.19a / 701.20a).
/// - Front: tutor a card with flash → hand.
/// - Front: does not tutor a card that is neither instant nor flash.
/// - Back: Land type, non-basic, no subtypes.
/// - Back: MDFC state pre-flipped to back face.
/// - Back: {T}: Add {U} or {B} (two mana abilities).
/// - Back: unconditional enters-tapped replacement (CR 614.1c).
/// - NamedCardFactory dispatch for both faces (prod source-gen path).
/// </summary>
[Trait("Color", "M")]
public class WaterloggedTeachingsFactoryTests
{
    private static ChosenSpellParams EmptyChoices() =>
        new(ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

    private static void Resolve(SpellDefinition spell)
    {
        foreach (var fx in spell.EffectFactory(EmptyChoices()))
        {
            fx.Execute();
        }
    }

    private readonly Player _alice = new("Alice", 20);

    // =========================================================================
    // Front face — identity + dispatch
    // =========================================================================

    [Fact]
    public void WaterloggedTeachings_Identity_3UB_Instant()
    {
        var card = WaterloggedTeachingsFactory.Create(_alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Waterlogged Teachings");
        card.ManaCost.Should().Be("{3}{U/B}");
        card.ManaCostValue.TotalValue.Should().Be(4);
        card.HasType(CardType.Instant).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WaterloggedTeachings()
    {
        var card = NamedCardFactory.Create("Waterlogged Teachings", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Waterlogged Teachings");
        card.ManaCost.Should().Be("{3}{U/B}");
    }

    [Fact]
    public void WaterloggedTeachings_CarriesMdfcState_FrontFace()
    {
        var card = WaterloggedTeachingsFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull(
            "Waterlogged Teachings is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Waterlogged Teachings");
        card.MdfcState!.BackFaceName.Should().Be("Inundated Archive");
        card.MdfcState!.IsBackFace.Should().BeFalse(
            "the front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Waterlogged Teachings");
        card.MdfcState!.CanCastEitherFace.Should().BeTrue(
            "the front face carries a castable back-land descriptor (CR 712.3)");
        card.MdfcState!.CastableBackFace!.IsLand.Should().BeTrue(
            "the back face Inundated Archive is a land");
    }

    // =========================================================================
    // Front face — tutor resolution
    // =========================================================================

    [Fact]
    public void Resolve_TutorsInstantCard_IntoHand_ThenShuffles()
    {
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(_alice); forest.SetController(_alice);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice); bolt.SetController(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        // Library order puts non-instant first so the deterministic agent's
        // first-of-filtered-candidates pick is the instant (the predicate
        // filters before the agent sees it).
        _alice.Zones.Library.AddCard(forest);
        _alice.Zones.Library.AddCard(bolt);
        _alice.Zones.Library.AddCard(bear);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Resolve(WaterloggedTeachingsFactory.BuildSpellDefinition(_alice));

        // The instant card is now in hand.
        _alice.Zones.Hand.GetCards().Select(c => c.Name)
            .Should().ContainSingle().Which.Should().Be("Lightning Bolt");
        _alice.Zones.Library.GetCards().Should().NotContain(bolt);
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Resolve_TutorsCardWithFlash_IntoHand()
    {
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(_alice); forest.SetController(_alice);
        // A creature with flash (e.g. Snapcaster-style) — not an instant by
        // type, but qualifies via the "card with flash" clause.
        var flashCreature = new Creature("Flash Bird", "{1}{U}", 2, 1);
        flashCreature.SetOwner(_alice); flashCreature.SetController(_alice);
        flashCreature.AddAbility(new KeywordAbility("Flash", flashCreature, _alice));
        _alice.Zones.Library.AddCard(forest);
        _alice.Zones.Library.AddCard(flashCreature);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Resolve(WaterloggedTeachingsFactory.BuildSpellDefinition(_alice));

        _alice.Zones.Hand.GetCards().Select(c => c.Name)
            .Should().ContainSingle().Which.Should().Be("Flash Bird");
    }

    [Fact]
    public void Resolve_DoesNotTutor_NonInstantNonFlashCard()
    {
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(_alice); forest.SetController(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        _alice.Zones.Library.AddCard(forest);
        _alice.Zones.Library.AddCard(bear);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        Resolve(WaterloggedTeachingsFactory.BuildSpellDefinition(_alice));

        // No instant / flash card to find — hand stays empty (CR 701.19a).
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
    }

    // =========================================================================
    // Back face — Inundated Archive
    // =========================================================================

    [Fact]
    public void InundatedArchive_Identity_NonBasicLand()
    {
        var land = InundatedArchiveFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Inundated Archive");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_InundatedArchive()
    {
        var card = NamedCardFactory.Create("Inundated Archive", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Inundated Archive");
    }

    [Fact]
    public void InundatedArchive_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = InundatedArchiveFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull(
            "Inundated Archive is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Waterlogged Teachings");
        land.MdfcState!.BackFaceName.Should().Be("Inundated Archive");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Inundated Archive");
    }

    [Fact]
    public void InundatedArchive_HasTwoManaAbilities_AddingBlueOrBlack()
    {
        var land = InundatedArchiveFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(2, "{T}: Add {U} or {B} — one ability per colour");
        manaAbilities.Should().Contain(a => a.ManaGenerated.Blue > 0,
            "one ability produces blue");
        manaAbilities.Should().Contain(a => a.ManaGenerated.Black > 0,
            "one ability produces black");
        manaAbilities.Should().NotContain(a => a.ManaGenerated.Red > 0);
        manaAbilities.Should().NotContain(a => a.ManaGenerated.Green > 0);
        manaAbilities.Should().NotContain(a => a.ManaGenerated.White > 0);
    }

    [Fact]
    public void InundatedArchive_EntersTapped_Unconditionally()
    {
        var bus = new ReplacementBus();
        var land = InundatedArchiveFactory.Create(_alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Inundated Archive always enters tapped (CR 614.1c) — no opt-out");
    }

    // =========================================================================
    // Back-land play via the #2623 cast-either-face enumeration
    // =========================================================================

    [Fact]
    public void FrontFace_ExposesCastableBackLand_ForBackLandEnumeration()
    {
        // CR 305 / 712.3 — the front-face card carries a castable back-land
        // descriptor so the bot's LegalActionEnumerator (#2623) can surface
        // the Inundated Archive land play even at 0 mana.
        var card = WaterloggedTeachingsFactory.Create(_alice);

        card.MdfcState!.CanCastEitherFace.Should().BeTrue();
        var backFace = card.MdfcState!.CastableBackFace!;
        backFace.IsLand.Should().BeTrue();
        backFace.Name.Should().Be("Inundated Archive");

        // The descriptor materializes a fully-wired back-land instance.
        var built = backFace.BuildCard(_alice, replacements: null);
        built.Should().BeOfType<Land>();
        built.Name.Should().Be("Inundated Archive");
    }
}
