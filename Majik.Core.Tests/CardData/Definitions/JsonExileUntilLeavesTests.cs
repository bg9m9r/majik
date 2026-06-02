using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Engine-level coverage for the declarative <c>exile_until_leaves</c> verb —
/// the Banishing Light / Oblivion Ring / Glass Casket / Portable Hole family of
/// two LINKED abilities (CR 607 linked abilities; CR 603.6e "until" duration;
/// CR 610.3 return).
///
/// Each test builds a runtime card straight from inline JSON (the
/// <c>etb_self</c> trigger carrying an <c>exile_until_leaves</c> effect), then
/// drives the ETB / LTB triggered abilities the same way the hand-rolled
/// factory tests do — set chosen targets, execute, assert zone moves. The verb
/// must:
/// <list type="bullet">
///   <item>exile the chosen target on ETB resolution (CR 701.21),</item>
///   <item>return the SAME object to its OWNER's battlefield when the source
///   leaves (CR 610.3 / CR 110.2),</item>
///   <item>not double-return (the linked "until" return happens once),</item>
///   <item>no-op when the exiled object has since left exile (CR 603.6e),</item>
///   <item>honour "an opponent controls" (CR 109.5), "another" (exclude self),
///   and the mana-value cap (CR 202.3).</item>
/// </list>
/// </summary>
public class JsonExileUntilLeavesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>Banishing Light shape: "exile target nonland permanent an
    /// opponent controls until this leaves the battlefield."</summary>
    private const string BanishingLightJson = """
    {
      "name": "Banishing Light",
      "types": ["Enchantment"],
      "manaCost": "{2}{W}",
      "abilities": [
        {
          "kind": "triggered",
          "trigger": { "type": "etb_self" },
          "effects": [
            {
              "type": "exile_until_leaves",
              "targetFilter": "nonland_permanent",
              "opponentControlsOnly": true
            }
          ]
        }
      ]
    }
    """;

    private Enchantment BuildBanishingLight()
    {
        var def = CardDefinitionLoader.FromJson(BanishingLightJson);
        var card = (Enchantment)CardDefinitionFactory.Build(def, _alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        return card;
    }

    private static TriggeredAbility Etb(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

    private static TriggeredAbility Ltb(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 0);

    private Creature BobsCreature(string name = "Tarmogoyf", string cost = "{1}{G}")
    {
        var c = new Creature(name, cost, 0, 1);
        c.SetOwner(_bob);
        c.SetController(_bob);
        c.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void Run(TriggeredAbility ability, params object[] chosen)
    {
        // Resolve through the ability so a real ResolutionContext (carrying
        // ChosenTargets) is built — the ctx-reading ETB effect needs it. A
        // fresh ability per Run avoids the one-shot resolution-state guard.
        ability.SetChosenTargets(
            chosen.Length > 0
                ? new IReadOnlyList<object>[] { chosen }
                : System.Array.Empty<IReadOnlyList<object>>());
        ability.Resolve();
    }

    [Fact]
    public void Build_ProducesLinkedEtbAndLtbAbilities()
    {
        var light = BuildBanishingLight();

        light.Name.Should().Be("Banishing Light");
        light.HasType(CardType.Enchantment).Should().BeTrue();
        light.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "the exile_until_leaves verb attaches the linked LTB ability to the ETB ability's card");
        Etb(light).TargetRequests.Should().HaveCount(1, "the ETB declares a single target slot");
        Ltb(light).TargetRequests.Should().BeEmpty("the LTB return is untargeted");
    }

    [Fact]
    public void Etb_ExilesOpponentNonlandPermanent()
    {
        var light = BuildBanishingLight();
        var goyf = BobsCreature();

        Run(Etb(light), goyf);

        goyf.Zone.Should().Be(ZoneType.Exile, "ETB exiles the chosen nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goyf);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goyf);
    }

    [Fact]
    public void Ltb_ReturnsExiledCardToOwnersBattlefield()
    {
        var light = BuildBanishingLight();
        var goyf = BobsCreature();

        Run(Etb(light), goyf);
        goyf.Zone.Should().Be(ZoneType.Exile);

        Run(Ltb(light));

        goyf.Zone.Should().Be(ZoneType.Battlefield, "LTB returns the exiled card (CR 610.3)");
        goyf.Controller.Should().BeSameAs(_bob, "returned under its OWNER's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(goyf);
        _bob.Zones.Exile.GetCards().Should().NotContain(goyf);
    }

    [Fact]
    public void Ltb_DoesNotDoubleReturn()
    {
        var light = BuildBanishingLight();
        var goyf = BobsCreature();

        Run(Etb(light), goyf);
        Run(Ltb(light));
        goyf.Zone.Should().Be(ZoneType.Battlefield);

        // The exiled card is moved away again (e.g. it dies). A second LTB of
        // the SAME source must NOT yank it back — the linked "until" return
        // happened once (CR 603.6e).
        _bob.Zones.Battlefield.RemoveCard(goyf);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        Run(Ltb(light));

        goyf.Zone.Should().Be(ZoneType.Graveyard,
            "the linked return fires once; a second LTB must not resurrect the card");
    }

    [Fact]
    public void Ltb_NoOpWhenExiledObjectAlreadyLeftExile()
    {
        var light = BuildBanishingLight();
        var goyf = BobsCreature();

        Run(Etb(light), goyf);
        goyf.Zone.Should().Be(ZoneType.Exile);

        // The exiled card leaves exile by another effect (extraction). The LTB
        // must find nothing and no-op (CR 603.6e).
        _bob.Zones.Exile.RemoveCard(goyf);
        _bob.Zones.Graveyard.AddCard(goyf);
        goyf.SetZone(ZoneType.Graveyard);

        Run(Ltb(light));

        goyf.Zone.Should().Be(ZoneType.Graveyard, "the exiled object is gone — no return");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goyf);
    }

    [Fact]
    public void Ltb_NoOpWhenNothingExiled()
    {
        var light = BuildBanishingLight();

        Run(Ltb(light)); // no ETB run

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(light);
    }

    [Fact]
    public void Etb_RejectsLandTarget()
    {
        var light = BuildBanishingLight();
        var land = new Land("Forest");
        land.SetOwner(_bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        Run(Etb(light), land);

        land.Zone.Should().Be(ZoneType.Battlefield, "lands are excluded by the nonland filter");
    }

    [Fact]
    public void Etb_RejectsControllerSidePermanent()
    {
        var light = BuildBanishingLight();
        var aliceBird = new Creature("Bird", "{1}{W}", 1, 2);
        aliceBird.SetOwner(_alice);
        aliceBird.SetController(_alice);
        aliceBird.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBird);

        Run(Etb(light), aliceBird);

        aliceBird.Zone.Should().Be(ZoneType.Battlefield,
            "'an opponent controls' rejects controller-side permanents (CR 109.5)");
    }

    [Fact]
    public void ManaValueCap_RejectsTargetAboveCap()
    {
        // Glass Casket shape: "exile target creature an opponent controls with
        // mana value 3 or less."
        const string glassCasket = """
        {
          "name": "Glass Casket",
          "types": ["Artifact"],
          "manaCost": "{1}{W}",
          "abilities": [
            {
              "kind": "triggered",
              "trigger": { "type": "etb_self" },
              "effects": [
                {
                  "type": "exile_until_leaves",
                  "targetFilter": "creature",
                  "opponentControlsOnly": true,
                  "maxManaValue": 3
                }
              ]
            }
          ]
        }
        """;
        var card = (Artifact)CardDefinitionFactory.Build(
            CardDefinitionLoader.FromJson(glassCasket), _alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var big = BobsCreature("Titan", "{4}{G}{G}"); // mv 6 > 3
        Run(Etb(card), big);
        big.Zone.Should().Be(ZoneType.Battlefield, "mana value 6 exceeds the cap of 3");

        var small = BobsCreature("Goblin", "{R}"); // mv 1 <= 3
        Run(Etb(card), small);
        small.Zone.Should().Be(ZoneType.Exile, "mana value 1 is within the cap of 3");
    }

    [Fact]
    public void ExcludeSelf_AllowsControllerSideButNotTheSource()
    {
        // Oblivion Ring shape: "exile ANOTHER target nonland permanent" — no
        // "an opponent controls" clause, but excludes the source itself.
        const string oRing = """
        {
          "name": "Oblivion Ring",
          "types": ["Enchantment"],
          "manaCost": "{2}{W}",
          "abilities": [
            {
              "kind": "triggered",
              "trigger": { "type": "etb_self" },
              "effects": [
                {
                  "type": "exile_until_leaves",
                  "targetFilter": "nonland_permanent",
                  "opponentControlsOnly": false,
                  "excludeSelf": true
                }
              ]
            }
          ]
        }
        """;
        var ring = (Enchantment)CardDefinitionFactory.Build(
            CardDefinitionLoader.FromJson(oRing), _alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        // Targeting the ring itself is illegal ("another").
        Run(Etb(ring), ring);
        ring.Zone.Should().Be(ZoneType.Battlefield, "'another' excludes the source itself");

        // Alice's OWN creature is a legal target (no opponent restriction).
        var aliceBird = new Creature("Bird", "{1}{W}", 1, 2);
        aliceBird.SetOwner(_alice);
        aliceBird.SetController(_alice);
        aliceBird.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBird);

        Run(Etb(ring), aliceBird);
        aliceBird.Zone.Should().Be(ZoneType.Exile,
            "Oblivion Ring may exile a controller-side nonland permanent");
    }
}
