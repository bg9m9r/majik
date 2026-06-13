using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Prod-path (GameFacade.Create) verification that the previously prod-broken
/// lands now bind through the binder chain — the AUTHORITATIVE check, since a
/// land's [CardName] factory is dead in production (the instance-swap is gated
/// on !HasType(Land)); only the binder chain runs. Mirrors
/// <see cref="ManlandBinderPipelineTests"/>: build the real seed shells through
/// <see cref="GameFacade.Create"/> and inspect the live card's
/// <see cref="ICard.Abilities"/> + <see cref="ICard.IsVanillaShell"/>.
///
/// <para>This is the same signal the pool-wide / bot-deck implementation audits
/// read, so these assertions move the named cards out of the Stub /
/// MissingTrigger backlog (or, for the off-card-effect lands, confirm WHY the
/// allowlist is correct: they are vanilla shells with provably-live off-card
/// effects).</para>
/// </summary>
public class BoundLandProdPathTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>Build the named cards' real seed shells through the production
    /// GameFacade binder chain and return them by name.</summary>
    private static IReadOnlyDictionary<string, ICard> BuildThroughProd(params string[] names)
    {
        var shells = new List<ICard>();
        foreach (var n in names)
        {
            var e = Repo.GetByName(n)!;
            var parsed = TypeLineParser.Parse(e.TypeLine);
            ICard c = parsed.Types.Contains(CardType.Land)
                ? new Land(e.Name, parsed.Supertypes, parsed.Subtypes)
                : parsed.Types.Contains(CardType.Enchantment)
                    ? new Enchantment(e.Name, e.ManaCost ?? "", parsed.Supertypes, parsed.Subtypes)
                    : new Card(e.Name, e.ManaCost ?? "", parsed.Types, parsed.Supertypes, parsed.Subtypes);
            shells.Add(c);
        }

        var facade = GameFacade.Create("A", "B", shells, System.Array.Empty<ICard>(), cardRepo: Repo);
        var byName = new Dictionary<string, ICard>(StringComparer.Ordinal);
        foreach (var c in facade.Alice.Zones.GetZone(ZoneType.Library).GetCards())
            byName.TryAdd(c.Name, c);
        return byName;
    }

    // -----------------------------------------------------------------------
    // Formerly Stub (0 abilities) — now carry their bound mana abilities.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Reflecting Pool", 6)]   // WUBRG + {C} dynamic mana abilities
    [InlineData("Boseiju, Who Shelters All", 1)] // {T}, Pay 2 life: Add {C}
    [InlineData("Sunken Citadel", 2)]    // 1 chosen-colour single + 1 restricted-double (CR 614.12)
    [InlineData("Temple of the Dragon Queen", 1)] // 1 chosen-colour single-pip (CR 614.12)
    public void FormerlyStubLand_NowHasBoundManaAbilities(string name, int expectedManaAbilities)
    {
        var card = BuildThroughProd(name)[name];

        card.IsVanillaShell.Should().BeFalse(
            $"{name} is no longer a do-nothing vanilla shell in prod");
        card.Abilities.OfType<IManaAbility>().Should().HaveCount(expectedManaAbilities);
    }

    // -----------------------------------------------------------------------
    // Formerly MissingTrigger — now carry their bound triggered ability.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Glimmervoid")]     // end-step conditional sac
    [InlineData("Abraded Bluffs")]  // ETB deal 1 to target opponent
    [InlineData("Witch's Cottage")] // enters-untapped recur
    public void FormerlyMissingTriggerLand_NowHasBoundTrigger(string name)
    {
        var card = BuildThroughProd(name)[name];

        card.Abilities.OfType<ITriggeredAbility>().Should().NotBeEmpty(
            $"{name}'s oracle trigger is bound through the prod binder chain");
    }

    // -----------------------------------------------------------------------
    // Off-card-effect lands — provably-working but carry 0 card.Abilities, so
    // the classifier flags them Stub. These confirm WHY the StubHeuristic
    // allowlist entries are correct: the behaviour lives off-card.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Urborg, Tomb of Yawgmoth")]  // additive-static (CES)
    [InlineData("Yavimaya, Cradle of Growth")] // additive-static (CES)
    [InlineData("Vesuva")]                      // enters-as-copy replacement
    public void OffCardEffectLand_BuildsWithNoCardAbilities_BehaviourLivesOffCard(string name)
    {
        var card = BuildThroughProd(name)[name];

        // Zero card.Abilities → the classifier stamps IsVanillaShell. The
        // printed behaviour runs via an off-card continuous/replacement effect
        // (verified in AdditiveLandSubtypeBinderTests / BoundLandTriggerBinderTests),
        // which is exactly why these names need the audit's StubHeuristicAllowlist.
        card.Abilities.OfType<ITriggeredAbility>().Should().BeEmpty();
        card.Abilities.OfType<IManaAbility>().Should().BeEmpty();
        card.IsVanillaShell.Should().BeTrue(
            $"{name}'s behaviour is an OFF-CARD effect — provably-working but "
            + "invisible to the card.Abilities classifier (hence the allowlist)");
    }
}
