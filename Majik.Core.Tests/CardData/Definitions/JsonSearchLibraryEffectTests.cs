using System.Text.Json;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Tests for the declarative <c>search_library</c> tutor effect verb
/// (<see cref="SearchLibraryEffectDef"/>, CR 701.19 / CR 701.20a) — "search
/// your library for a [filtered] card, put it [destination], then shuffle".
/// Exercises the shared <see cref="CardDefRuntime.BuildJsonEffect"/> build path
/// against a live library, the JSON polymorphic round-trip, and the
/// declarative panorama-land assembly (<c>sacrifice_self</c> +
/// <c>search_library</c> to the battlefield tapped).
/// </summary>
public class JsonSearchLibraryEffectTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    private async Task SearchAsync(ICard source, SearchLibraryEffectDef def)
    {
        var effect = CardDefRuntime.BuildJsonEffect(
            def, card: source, controller: _alice, replacements: null);
        // No agent registered → LibrarySearch falls back to the deterministic
        // first-candidate pick, so the filter is exercised by the candidate set.
        var ctx = ResolutionContext.For(_alice, agent: null, game: null, chosenTargets: null);
        await effect.ExecuteAsync(ctx);
    }

    private Land BasicLand(string name, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype })
        {
            Owner = _alice,
        };
        land.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(land);
        return land;
    }

    private Land FetchSource()
    {
        var l = new Land("Fetcher") { Owner = _alice, Controller = _alice };
        l.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(l);
        return l;
    }

    [Fact]
    public void SearchLibrary_RoundTripsThroughJsonUnion()
    {
        const string json = """
            { "type": "search_library", "subtypes": ["Forest", "Plains", "Island"],
              "destination": "battlefield_tapped" }
            """;
        // Mirror the production loader's case-insensitive options
        // (CardDefinitionLoader) so camelCase JSON binds to the PascalCase props.
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var def = JsonSerializer.Deserialize<EffectDefinition>(json, opts);
        def.Should().BeOfType<SearchLibraryEffectDef>();
        var search = (SearchLibraryEffectDef)def!;
        search.Subtypes.Should().BeEquivalentTo(new[] { "Forest", "Plains", "Island" });
        search.Destination.Should().Be("battlefield_tapped");
        search.Shuffle.Should().BeTrue("the shuffle rider defaults on (CR 701.20a)");
    }

    [Fact]
    public void DestroyTarget_ThenControllerMaySearch_RoundTripsThroughJsonUnion()
    {
        // Boseiju, Who Endures — the destroy verb's nested "that player may
        // search" rider binds as a concrete SearchLibraryEffectDef property (no
        // own "type" discriminator — it is a typed child, not a union member).
        const string json = """
            { "type": "destroy_target",
              "targetFilter": "artifact_enchantment_nonbasic_land",
              "thenControllerMaySearch": {
                "subtypes": ["Plains", "Island", "Swamp", "Mountain", "Forest"],
                "destination": "battlefield" } }
            """;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var def = JsonSerializer.Deserialize<EffectDefinition>(json, opts);
        def.Should().BeOfType<DestroyTargetEffectDef>();
        var destroy = (DestroyTargetEffectDef)def!;
        destroy.TargetFilter.Should().Be("artifact_enchantment_nonbasic_land");
        destroy.ThenControllerMaySearch.Should().NotBeNull();
        destroy.ThenControllerMaySearch!.Subtypes.Should().BeEquivalentTo(
            new[] { "Plains", "Island", "Swamp", "Mountain", "Forest" });
        destroy.ThenControllerMaySearch.Destination.Should().Be("battlefield");
        destroy.ThenControllerMaySearch.BasicLand.Should().BeFalse(
            "a land card WITH a basic land type need not have the Basic supertype (CR 205.4a)");
    }

    [Fact]
    public async Task SearchLibrary_BasicSubtype_ToBattlefieldTapped_EntersTapped()
    {
        var forest = BasicLand("Forest", CardSubtype.Forest);

        var source = FetchSource();
        await SearchAsync(source, new SearchLibraryEffectDef
        {
            Subtypes = new() { "Forest", "Plains", "Island" },
            Destination = "battlefield_tapped",
        });

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest,
            "CR 701.19 — the chosen basic is put onto the battlefield");
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        forest.IsTapped.Should().BeTrue("the printed rider puts it onto the battlefield TAPPED");
    }

    [Fact]
    public async Task SearchLibrary_OnlyMatchesFilteredBasics()
    {
        // Swamp is the ONLY card in the library and is NOT in the GWU filter, so
        // the deterministic first-candidate pick finds nothing and Swamp stays.
        var swamp = BasicLand("Swamp", CardSubtype.Swamp);

        var source = FetchSource();
        await SearchAsync(source, new SearchLibraryEffectDef
        {
            Subtypes = new() { "Forest", "Plains", "Island" },
            Destination = "battlefield_tapped",
        });

        _alice.Zones.Library.GetCards().Should().Contain(swamp,
            "CR 701.19a — a basic Swamp is not a legal find for a Forest/Plains/Island filter");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(swamp);
    }

    [Fact]
    public async Task SearchLibrary_CardType_ToHand()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(bear);

        var source = FetchSource();
        await SearchAsync(source, new SearchLibraryEffectDef
        {
            CardType = "Creature",
            Destination = "hand",
        });

        _alice.Zones.Hand.GetCards().Should().Contain(bear,
            "destination=hand moves the chosen creature card to hand");
        _alice.Zones.Library.GetCards().Should().NotContain(bear);
    }
}
