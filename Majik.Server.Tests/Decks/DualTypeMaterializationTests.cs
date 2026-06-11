using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Server.Tests.Decks;

/// <summary>
/// Prod-path regression for the dual-type materialization bug (CR 205.1b — a
/// card can have more than one card type). The production deck loader
/// (<c>RealDeckLoader</c>) materializes each deck card via
/// <see cref="DeckCardShellBuilder.Build"/>; <see cref="GameFacade.Create"/>
/// then runs the same binder/factory chain production uses. Before the fix,
/// the materializer picked a single primary type and dropped the secondary —
/// so an artifact land was NOT an Artifact, an enchantment land was NOT an
/// Enchantment, and Esper Sentinel was NOT an Artifact, silently breaking
/// every artifact-/enchantment-matters interaction (Affinity, Mox Opal
/// metalcraft, Cranial Plating, Stoneforge, the card's own identity).
///
/// <para>These tests build through the REAL <see cref="EmbeddedCardRepository"/>
/// and the REAL <see cref="GameFacade"/> so they exercise the exact path a
/// live match takes.</para>
/// </summary>
public class DualTypeMaterializationTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>
    /// Build <paramref name="cardNames"/> the way prod does — materialize the
    /// shells through <see cref="DeckCardShellBuilder"/> (RealDeckLoader's
    /// path), then run them through <see cref="GameFacade.Create"/> with the
    /// real card repo (the binder/factory chain) — and return the resulting
    /// live cards keyed by name. Pads to a legal-ish library with basic lands.
    /// </summary>
    private static IReadOnlyDictionary<string, ICard> BuildLive(params string[] cardNames)
    {
        var shells = new List<ICard>();
        foreach (var name in cardNames)
        {
            var entity = Repo.GetByName(name);
            entity.Should().NotBeNull($"'{name}' must exist in the embedded seed");
            shells.Add(DeckCardShellBuilder.Build(entity!));
        }
        // Pad to a sane library size with basic Forests so facade build is happy.
        while (shells.Count < 40)
        {
            shells.Add(new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest }));
        }

        var facade = GameFacade.Create(
            aliceName: "A", bobName: "B",
            aliceDeck: shells, bobDeck: Array.Empty<ICard>(),
            cardRepo: Repo);

        var byName = new Dictionary<string, ICard>(StringComparer.Ordinal);
        foreach (var card in facade.Alice.Zones.GetZone(ZoneType.Library).GetCards())
        {
            byName[card.Name] = card;
        }
        return byName;
    }

    [Fact]
    public void DarksteelCitadel_BuiltThroughProdPath_IsBothLandAndArtifact()
    {
        var live = BuildLive("Darksteel Citadel");
        var citadel = live["Darksteel Citadel"];

        citadel.HasType(CardType.Land).Should().BeTrue("Darksteel Citadel is a Land");
        citadel.HasType(CardType.Artifact).Should().BeTrue(
            "Darksteel Citadel is an Artifact Land (CR 205.1b) — the secondary " +
            "Artifact type must survive materialization for affinity / metalcraft / Plating");
    }

    [Fact]
    public void BridgeLand_BuiltThroughProdPath_IsBothLandAndArtifact()
    {
        var live = BuildLive("Razortide Bridge");
        var bridge = live["Razortide Bridge"];

        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue(
            "the MOM 'Bridge' cycle are Artifact Lands (CR 205.1b)");
    }

    [Fact]
    public void UrzasSaga_BuiltThroughProdPath_IsBothLandAndEnchantment()
    {
        var live = BuildLive("Urza's Saga");
        var saga = live["Urza's Saga"];

        saga.HasType(CardType.Land).Should().BeTrue("Urza's Saga is a Land");
        saga.HasType(CardType.Enchantment).Should().BeTrue(
            "Urza's Saga is an Enchantment Land (CR 205.1b)");
    }

    [Fact]
    public void EsperSentinel_BuiltThroughProdPath_IsBothCreatureAndArtifact()
    {
        var live = BuildLive("Esper Sentinel");
        var sentinel = live["Esper Sentinel"];

        sentinel.HasType(CardType.Creature).Should().BeTrue("Esper Sentinel is a Creature");
        sentinel.HasType(CardType.Artifact).Should().BeTrue(
            "Esper Sentinel is an Artifact Creature (CR 205.1b) — its own 'artifact' " +
            "identity and affinity/metalcraft accounting depend on the Artifact type");
    }

    [Fact]
    public void TreasureVault_BuiltThroughProdPath_IsBothLandAndArtifact()
    {
        var live = BuildLive("Treasure Vault");
        var vault = live["Treasure Vault"];

        vault.HasType(CardType.Land).Should().BeTrue();
        vault.HasType(CardType.Artifact).Should().BeTrue(
            "Treasure Vault is an Artifact Land (CR 205.1b)");
    }

    [Fact]
    public void PlainBasicLand_BuiltThroughProdPath_IsLandOnly_NotArtifact()
    {
        var live = BuildLive("Forest");
        var forest = live["Forest"];

        forest.HasType(CardType.Land).Should().BeTrue();
        forest.HasType(CardType.Artifact).Should().BeFalse(
            "a plain basic land must NOT gain a spurious Artifact type");
        forest.HasType(CardType.Enchantment).Should().BeFalse();
        forest.HasType(CardType.Creature).Should().BeFalse();
    }

    [Fact]
    public void PlainCreature_BuiltThroughProdPath_IsCreatureOnly_NotArtifact()
    {
        // Grizzly Bears-style vanilla — a non-artifact creature must stay so.
        var live = BuildLive("Llanowar Elves");
        var elf = live["Llanowar Elves"];

        elf.HasType(CardType.Creature).Should().BeTrue();
        elf.HasType(CardType.Artifact).Should().BeFalse(
            "a plain creature must NOT gain a spurious Artifact type");
    }
}
