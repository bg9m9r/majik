using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Parity gate for <see cref="DeckCardBuilder"/>: a card built via
/// <see cref="DeckCardBuilder.Build"/> must be indistinguishable (on every
/// gameplay-relevant surface) from the card the REAL
/// <see cref="GameFacade.Create"/> deck path puts into the library. The bot's
/// determinization sampler builds sampled opponent cards through this builder,
/// so any drift here means sampled cards behave differently from live-deck
/// cards inside the search sandbox.
///
/// <para>Castability note: an instant/sorcery does NOT carry its
/// SpellDefinition on the card — TurnDriver resolves it at cast time BY NAME
/// via <see cref="ScryfallCardFactory.LookupSpellDefinition"/>. The card-side
/// castability surface is therefore: correct runtime type
/// (<see cref="Instant"/>), correct name (the resolver key), and correct mana
/// cost. The test asserts all three AND that the resolver actually yields a
/// runnable definition for the built card's name.</para>
/// </summary>
public class DeckCardBuilderParityTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    /// <summary>Build <paramref name="name"/> exactly the way a real match
    /// does: seed entity → <see cref="DeckCardShellBuilder"/> shell (the
    /// RealDeckLoader materialize step) → <see cref="GameFacade.Create"/>
    /// binder/factory chain → pluck the live card from Alice's library.</summary>
    private static ICard BuildViaFacadeDeckPath(string name)
    {
        var entity = Repo.GetByName(name);
        entity.Should().NotBeNull($"the embedded seed must contain {name}");
        var shell = DeckCardShellBuilder.Build(entity!);
        var facade = GameFacade.Create(
            "Alice", "Bob", new[] { shell }, Array.Empty<ICard>(),
            cardRepo: Repo, routeThroughNamedFactories: true);
        return facade.Alice.Zones.GetZone(ZoneType.Library).GetCards().Single();
    }

    /// <summary>Build the same card through <see cref="DeckCardBuilder.Build"/>
    /// with a scratch service set (what the determinization sampler will do).
    /// <paramref name="withOptionalServices"/> = false exercises the minimal
    /// set (triggers / zones / eventBus all null).</summary>
    private static ICard BuildViaBuilder(string name, Player owner, bool withOptionalServices)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        return DeckCardBuilder.Build(
            name, owner, Repo,
            replacements: new ReplacementBus(),
            effects: new ContinuousEffectsService(bus),
            triggers: withOptionalServices ? new TriggerManager(stack, bus) : null,
            zones: withOptionalServices ? new ZoneService(bus) : null,
            eventBus: withOptionalServices ? bus : null,
            routeThroughNamedFactories: true);
    }

    private static int Count<T>(ICard card) => card.Abilities.OfType<T>().Count();

    private static void AssertSurfaceParity(ICard built, ICard facadeCard)
    {
        built.GetType().Should().Be(facadeCard.GetType(),
            "the builder must produce the same concrete runtime type as the facade deck path");
        built.Name.Should().Be(facadeCard.Name);
        built.ManaCost.Should().Be(facadeCard.ManaCost);
        built.CardTypes.Should().BeEquivalentTo(facadeCard.CardTypes);
        built.Supertypes.Should().BeEquivalentTo(facadeCard.Supertypes);
        built.Subtypes.Should().BeEquivalentTo(facadeCard.Subtypes);
        built.IsVanillaShell.Should().Be(facadeCard.IsVanillaShell);
        Count<KeywordAbility>(built).Should().Be(Count<KeywordAbility>(facadeCard),
            "keyword-ability count must match the facade-built card");
        Count<ITriggeredAbility>(built).Should().Be(Count<ITriggeredAbility>(facadeCard),
            "triggered-ability count must match the facade-built card");
        Count<IManaAbility>(built).Should().Be(Count<IManaAbility>(facadeCard),
            "mana-ability count must match the facade-built card");
    }

    [Theory]
    [InlineData("Lightning Bolt")]
    [InlineData("Goblin Guide")]
    [InlineData("Mountain")]
    public void Build_ProducesSameCardSurface_AsFacadeDeckPath(string name)
    {
        var facadeCard = BuildViaFacadeDeckPath(name);
        var owner = new Player("Alice", 20);
        var built = BuildViaBuilder(name, owner, withOptionalServices: true);

        AssertSurfaceParity(built, facadeCard);
        built.Owner.Should().BeSameAs(owner);
    }

    /// <summary>The sampler will call Build with SCRATCH services — verify the
    /// minimal non-null set (repo + replacements + effects; triggers / zones /
    /// eventBus null) still yields a card with full surface parity.</summary>
    [Theory]
    [InlineData("Lightning Bolt")]
    [InlineData("Goblin Guide")]
    [InlineData("Mountain")]
    public void Build_WithNullOptionalServices_KeepsSurfaceParity(string name)
    {
        var facadeCard = BuildViaFacadeDeckPath(name);
        var owner = new Player("Alice", 20);
        var built = BuildViaBuilder(name, owner, withOptionalServices: false);

        AssertSurfaceParity(built, facadeCard);
    }

    [Fact]
    public void Build_Instant_CarriesTheCastabilitySurface()
    {
        var facadeCard = BuildViaFacadeDeckPath("Lightning Bolt");
        var owner = new Player("Alice", 20);
        var built = BuildViaBuilder("Lightning Bolt", owner, withOptionalServices: true);

        built.Should().BeOfType<Instant>();
        facadeCard.Should().BeOfType<Instant>();

        // Castability is resolved AT CAST TIME by name (TurnDriver's
        // spell-definition resolver → ScryfallCardFactory.LookupSpellDefinition).
        // Prove both cards' names resolve to a runnable SpellDefinition — the
        // exact surface the sampler needs sampled instants to keep.
        var resolver = new ScryfallCardFactory(Repo);
        resolver.LookupSpellDefinition(built.Name, owner, raw => raw)
            .Should().NotBeNull("the builder-built instant must be castable via the cast-time resolver");
        resolver.LookupSpellDefinition(facadeCard.Name, owner, raw => raw)
            .Should().NotBeNull();
    }

    [Fact]
    public void Build_Creature_MatchesFacadePowerToughnessAndAbilities()
    {
        var facadeCard = BuildViaFacadeDeckPath("Goblin Guide");
        var owner = new Player("Alice", 20);
        var built = BuildViaBuilder("Goblin Guide", owner, withOptionalServices: true);

        var builtCreature = built.Should().BeOfType<Creature>().Subject;
        var facadeCreature = facadeCard.Should().BeOfType<Creature>().Subject;
        builtCreature.BasePower.Should().Be(facadeCreature.BasePower);
        builtCreature.BaseToughness.Should().Be(facadeCreature.BaseToughness);

        // Goblin Guide: Haste + the attack-trigger ("defending player reveals…").
        built.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword.Equals("Haste", StringComparison.OrdinalIgnoreCase));
        Count<ITriggeredAbility>(built).Should().BeGreaterThan(0,
            "Goblin Guide's attack trigger must be attached");
    }

    [Fact]
    public void Build_Land_MatchesFacadeManaAbilities()
    {
        var facadeCard = BuildViaFacadeDeckPath("Mountain");
        var owner = new Player("Alice", 20);
        var built = BuildViaBuilder("Mountain", owner, withOptionalServices: true);

        built.Should().BeOfType<Land>();
        var builtManaAbilities = Count<IManaAbility>(built);
        builtManaAbilities.Should().Be(Count<IManaAbility>(facadeCard));
        builtManaAbilities.Should().Be(1, "a basic Mountain carries exactly one mana ability");
    }

    [Fact]
    public void Build_UnknownName_Throws()
    {
        var owner = new Player("Alice", 20);
        var act = () => DeckCardBuilder.Build(
            "Definitely Not A Real Card Name", owner, Repo,
            replacements: new ReplacementBus(),
            effects: new ContinuousEffectsService(),
            triggers: null, zones: null, eventBus: null,
            routeThroughNamedFactories: true);
        act.Should().Throw<ArgumentException>();
    }
}
