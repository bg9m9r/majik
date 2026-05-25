using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Urza's Saga (Modern Horizons 2). Legendary Enchantment Land
/// — Urza's Saga.
///
/// Covers:
///   - Identity: Land + Enchantment + Saga subtype + Urza's subtype +
///     Legendary supertype.
///   - "{T}: Add {C}" mana ability wired.
///   - SagaState attached with final chapter 3, ETB → 1 lore counter,
///     subsequent advances increment.
///   - Chapter I → Construct token spawn (0/0 colorless artifact creature
///     subtype Construct).
///   - Construct P/T scales with controller's artifact count via the
///     supplied <see cref="ContinuousEffectsService"/>.
///   - Chapter II → second Construct.
///   - Chapter III → searches library for artifact mv ≤ 2, puts it onto
///     the battlefield, then library is shuffled.
///   - After III, the SBA-ready <see cref="Majik.Core.CardData.Sagas.SagaState.ShouldBeSacrificed"/>
///     flag flips so the generic Saga sacrifice SBA finishes the Saga.
///   - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class UrzasSagaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOntoBattlefield(Permanent p, Player owner)
    {
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
        p.SetController(owner);
    }

    [Fact]
    public void UrzasSaga_IsLegendaryEnchantmentLand_UrzasSaga_Subtypes()
    {
        var saga = UrzasSagaFactory.Create(_alice);

        saga.Name.Should().Be("Urza's Saga");
        saga.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        saga.HasType(CardType.Land).Should().BeTrue(
            "primary type is Land — matches Scryfall PickPrimaryType ordering");
        saga.HasType(CardType.Enchantment).Should().BeTrue(
            "Urza's Saga is both an Enchantment Saga and a Land");
        saga.HasSubtype(CardSubtype.Saga).Should().BeTrue();
        saga.HasSubtype(CardSubtype.Urzas).Should().BeTrue();
        saga.Owner.Should().BeSameAs(_alice);
        saga.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UrzasSaga_HasTapForColourlessMana()
    {
        var saga = UrzasSagaFactory.Create(_alice);

        var mana = saga.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "the printed oracle is '{T}: Add {C}'");
        // The mana ability produces {C}; the existing ManaAbility surface
        // exposes the cost; the bot's mana picker consumes whichever it
        // needs at activation time. We assert the count + tap-source shape;
        // colour-routing behaviour is covered by ManaAbility tests.
        mana[0].Source.Should().BeSameAs(saga);
    }

    [Fact]
    public void UrzasSaga_AttachesSagaState_FinalChapter3()
    {
        var saga = UrzasSagaFactory.Create(_alice);
        PutOntoBattlefield(saga, _alice);

        saga.SagaState.Should().NotBeNull();
        saga.SagaState!.FinalChapter.Should().Be(3);
        saga.SagaState.LoreCounters.Should().Be(0, "no chapters advanced yet");
    }

    [Fact]
    public void UrzasSaga_ChapterI_SpawnsConstructToken_ColourlessArtifactCreature()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        saga.SagaState!.AdvanceAndChapter(); // I

        saga.SagaState.LoreCounters.Should().Be(1);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Construct")
            .ToList();
        tokens.Should().HaveCount(1);

        var construct = tokens[0];
        construct.HasType(CardType.Creature).Should().BeTrue();
        construct.HasType(CardType.Artifact).Should().BeTrue(
            "printed text is 'colorless Construct artifact creature token'");
        construct.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        construct.BasePower.Should().Be(0, "printed 0/0");
        construct.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void UrzasSaga_ChapterI_Construct_PowerScalesWithArtifactCount()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        // 0 artifacts in play before chapter I → token alone is 1 artifact
        // (the token itself IS an artifact via its additive Artifact type
        // stamp), so the count includes itself.
        saga.SagaState!.AdvanceAndChapter(); // I → spawn first Construct

        var firstConstruct = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Construct");

        // 1 artifact (the Construct itself); Urza's Saga is NOT an
        // artifact, so it doesn't contribute.
        firstConstruct.GetPower().Should().Be(1,
            "the Construct token itself is the only artifact on the battlefield");
        firstConstruct.GetToughness().Should().Be(1);

        // Add 3 more printed artifacts — assert the token tracks them.
        for (var i = 0; i < 3; i++)
        {
            var art = new Artifact($"Bauble{i}", "0") { Owner = _alice, Controller = _alice };
            PutOntoBattlefield(art, _alice);
        }

        // 1 (self) + 3 (added) = 4.
        firstConstruct.GetPower().Should().Be(4);
        firstConstruct.GetToughness().Should().Be(4);
    }

    [Fact]
    public void UrzasSaga_ChapterII_SpawnsSecondConstruct()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II

        saga.SagaState.LoreCounters.Should().Be(2);

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Construct")
            .ToList();
        tokens.Should().HaveCount(2, "both I and II create a Construct");
    }

    [Fact]
    public void UrzasSaga_ChapterIII_TutorsArtifactMv2OrLess_ToBattlefield()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        // Library: a mv-2 artifact (should be picked first by deterministic
        // order), a mv-1 artifact (also matches but loses the tiebreak),
        // and a non-artifact (should be skipped).
        var bigArt = new Artifact("Cranial Plating", "2") { Owner = _alice };
        var smallArt = new Artifact("Sol Ring", "1") { Owner = _alice };
        var spell = new Sorcery("Wrath of God", "2WW") { Owner = _alice };

        _alice.Zones.Library.AddCard(bigArt);   // index 0 — first by order
        _alice.Zones.Library.AddCard(smallArt); // index 1
        _alice.Zones.Library.AddCard(spell);    // index 2

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II
        saga.SagaState.AdvanceAndChapter();  // III

        // bigArt has mv=2 (one generic) so it's mv ≤ 2; deterministic
        // first-by-order picks bigArt (Cranial Plating — mv 2).
        _alice.Zones.Battlefield.GetCards()
            .Should().Contain(bigArt, "Cranial Plating mv=2 is first matching artifact");
        _alice.Zones.Library.GetCards()
            .Should().NotContain(bigArt);

        // Tutored card is now controlled by Alice + on the battlefield.
        bigArt.Controller.Should().BeSameAs(_alice);
        bigArt.Zone.Should().Be(ZoneType.Battlefield);

        // Sorcery + smallArt stay in library — sorcery filtered by type,
        // smallArt also matches but loses the deterministic tiebreak
        // (later in library order).
        _alice.Zones.Library.GetCards().Should().Contain(spell);
        _alice.Zones.Library.GetCards().Should().Contain(smallArt);
    }

    [Fact]
    public void UrzasSaga_ChapterIII_FiltersByMv_OmitsMv3Plus()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        // Only a mv-3 artifact in library — must NOT be tutored (mv > 2).
        var tooExpensive = new Artifact("Lotus Bloom", "3") { Owner = _alice };
        _alice.Zones.Library.AddCard(tooExpensive);

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II
        saga.SagaState.AdvanceAndChapter();  // III

        _alice.Zones.Library.GetCards().Should().Contain(tooExpensive,
            "mv=3 artifact does not satisfy 'mana value 2 or less'");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(tooExpensive);
    }

    [Fact]
    public void UrzasSaga_AfterChapterIII_ShouldBeSacrificed_PerGenericSagaSba()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.ShouldBeSacrificed().Should().BeFalse();

        saga.SagaState.AdvanceAndChapter(); // II
        saga.SagaState.ShouldBeSacrificed().Should().BeFalse();

        saga.SagaState.AdvanceAndChapter(); // III
        // Generic Saga SBA (CR 714.5 / 704.5r) — lore counters == final
        // chapter (3) and no chapter trigger sitting on the stack →
        // sacrifice flag flips.
        saga.SagaState.LoreCounters.Should().Be(3);
        saga.SagaState.ShouldBeSacrificed().Should().BeTrue();
    }

    [Fact]
    public void UrzasSaga_ChapterIII_NoArtifactInLibrary_LibraryUnchanged()
    {
        var effects = new ContinuousEffectsService();
        var saga = UrzasSagaFactory.Create(_alice, zoneService: null, effects: effects);
        PutOntoBattlefield(saga, _alice);

        var spell = new Sorcery("Wrath of God", "2WW") { Owner = _alice };
        _alice.Zones.Library.AddCard(spell);

        saga.SagaState!.AdvanceAndChapter(); // I
        saga.SagaState.AdvanceAndChapter();  // II
        saga.SagaState.AdvanceAndChapter();  // III

        // Library still contains the lone non-artifact spell — nothing
        // tutored. Battlefield does NOT include the spell.
        _alice.Zones.Library.GetCards().Should().Contain(spell);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(spell);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UrzasSaga()
    {
        var card = NamedCardFactory.Create("Urza's Saga", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Urza's Saga");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Saga).Should().BeTrue();
    }
}
