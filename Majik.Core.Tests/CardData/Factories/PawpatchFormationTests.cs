using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 700.2d — modal "Choose one —" spell. Pawpatch Formation, Bloomburrow,
/// {1}{G}, three modes:
///   Mode 0: Destroy target creature with flying.
///   Mode 1: Destroy target enchantment.
///   Mode 2: Draw a card. Create a Food token.
///
/// Pawpatch Formation carries NO bespoke factory body — every mode is a
/// pattern the live spell-template registry binds from oracle text. These
/// tests therefore bind the PRINTED oracle text through the prod path
/// (<see cref="OracleSpellBinder.Bind"/> → <c>ModalChooseOneTemplate</c>),
/// exactly as <c>ScryfallCardFactory.LookupSpellDefinition</c> does at cast
/// time, then exercise each mode's resolution. Mirrors
/// <see cref="MalevolentRumbleTests"/> for the bind-from-entity shape.
/// </summary>
[Trait("Color", "G")]
[Collection(nameof(StaticRegistryCollection))]
public class PawpatchFormationTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // Printed oracle text (Scryfall, scryfallId
    // b82c20ad-0f69-4822-ae76-770832cccdf7) — the same text the embedded
    // Modern card pool supplies to the live binder at cast time.
    private static readonly CardEntity Entity = new()
    {
        Name = PawpatchFormationFactory.CardName,
        ManaCost = PawpatchFormationFactory.PrintedManaCost,
        TypeLine = "Instant",
        OracleText =
            "Choose one —\n" +
            "• Destroy target creature with flying.\n" +
            "• Destroy target enchantment.\n" +
            "• Draw a card. Create a Food token. " +
            "(It's an artifact with \"{2}, {T}, Sacrifice this token: You gain 3 life.\")",
    };

    public PawpatchFormationTests()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    private SpellDefinition Bind() =>
        OracleSpellBinder.Bind(Entity, _alice, x => x, null)
        ?? throw new InvalidOperationException("ModalChooseOne binder returned null for Pawpatch Formation");

    private static ChosenSpellParams Mode(int index, params object[] target) =>
        new(
            ModeIndex: index,
            X: null,
            Targets: new IReadOnlyList<object>[] { target },
            Mana: ManaPayment.Empty);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_MonoGreen()
    {
        var card = PawpatchFormationFactory.Create(_alice);

        card.Name.Should().Be("Pawpatch Formation");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().ContainSingle()
            .Which.Should().Be(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{G} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Modal binding shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Binder_RecognisesModalOracleText_WithThreeModes()
    {
        var def = Bind();

        def.Modes.Should().HaveCount(3, because: "Choose one — splits into 3 bullet modes");
        def.Modes[0].Should().Contain("creature with flying");
        def.Modes[1].Should().Contain("enchantment");
        def.Modes[2].Should().Contain("Food token");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy target creature with flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_DestroysTargetFlyer_MovesItToGraveyard()
    {
        var flyer = new Creature("Storm Crow", "{1}{U}", 1, 2) { Owner = _bob, Controller = _bob };
        flyer.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(flyer);

        var def = Bind();
        foreach (var e in def.EffectFactory(Mode(0, flyer))) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().NotContain(flyer,
            because: "mode 0 destroys the targeted creature");
        _bob.Zones.Graveyard.GetCards().Should().Contain(flyer);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy target enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DestroysTargetEnchantment_MovesItToGraveyard()
    {
        var aura = new Enchantment("Pacifism", "{1}{W}") { Owner = _bob, Controller = _bob };
        aura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(aura);

        var def = Bind();
        foreach (var e in def.EffectFactory(Mode(1, aura))) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().NotContain(aura,
            because: "mode 1 destroys the targeted enchantment");
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    // -----------------------------------------------------------------------
    // Mode 2 — draw a card, create a Food token
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_DrawsACard_AndCreatesOneFoodToken()
    {
        var topCard = new Instant("Opt", "{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var def = Bind();
        foreach (var e in def.EffectFactory(Mode(2))) e.Execute();

        // CR 121.1 — drew the top card into hand.
        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            because: "mode 2 draws a card");

        // CR 111 — created exactly one Food artifact token.
        var food = _alice.Zones.Battlefield.GetCards().OfType<Artifact>()
            .Where(a => a.Name == "Food").ToList();
        food.Should().ContainSingle(because: "mode 2 creates one Food token");
        food[0].IsToken.Should().BeTrue();
        food[0].HasSubtype(CardSubtype.Food).Should().BeTrue();

        // The Food token carries its "{2}, {T}, Sacrifice this token: You gain
        // 3 life." activated ability.
        food[0].Abilities.OfType<ActivatedAbility>().Should().NotBeEmpty(
            because: "Food tokens carry the gain-3-life sacrifice ability");
    }
}
