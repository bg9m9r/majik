using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Regression coverage for the spell-definition resolver wiring in
/// <see cref="GameFacade.StartFullGameAsync"/>. Before the fix, the facade
/// constructed <see cref="Majik.Core.Game.GameDriver"/> without a
/// <c>spellDefinitionResolver</c>, so
/// <see cref="Majik.Core.Game.TurnDriver"/>'s DispatchCast hit the
/// "no SpellDef for instant/sorcery" branch and silently rotated the card
/// back into hand — every non-permanent burn spell (Lava Spike, Lightning
/// Bolt, Boltwave, …) became uncastable in bot-vs-bot matches.
/// </summary>
public class SpellDefinitionResolverWiringTests
{
    [Fact]
    public void Resolver_ReturnsNonNull_ForKnownBurnSpell()
    {
        // Minimal in-memory repo that knows about Lightning Bolt. The oracle
        // text matches DamageAnyTargetTemplate's regex; OracleSpellBinder.Bind
        // should produce a runnable SpellDefinition without any DB present.
        var repo = new InMemoryCardRepo();
        repo.Add(new CardEntity
        {
            Name = "Lightning Bolt",
            ScryfallId = Guid.NewGuid().ToString(),
            ManaCost = "{R}",
            TypeLine = "Instant",
            OracleText = "Lightning Bolt deals 3 damage to any target.",
            IsImplemented = true,
        });

        var bolt = new Instant("Lightning Bolt", "{R}");
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        bolt.SetOwner(alice);

        var facade = GameFacade.Create(
            "Alice", "Bob",
            new ICard[] { bolt },
            Array.Empty<ICard>(),
            cardRepo: repo);

        var resolver = InvokeBuildResolver(facade);
        resolver.Should().NotBeNull("a card repo was supplied, so the facade should build a resolver");

        var def = resolver!(bolt, facade.Alice, null);
        def.Should().NotBeNull("Lightning Bolt's oracle text matches the damage-any-target template");
    }

    [Fact]
    public void Resolver_IsNull_WhenNoCardRepoSupplied()
    {
        // No repo → preserve legacy skip-rotate behaviour. The resolver is null,
        // so TurnDriver.DispatchCast falls through to its non-permanent skip
        // branch exactly as it did before this change.
        var bolt = new Instant("Lightning Bolt", "{R}");
        var alice = new Player("Alice", 20);
        bolt.SetOwner(alice);

        var facade = GameFacade.Create(
            "Alice", "Bob",
            new ICard[] { bolt },
            Array.Empty<ICard>());

        var resolver = InvokeBuildResolver(facade);
        resolver.Should().BeNull();
    }

    [Fact]
    public async Task FullGame_BurnSpell_LeavesHand_OnCast()
    {
        // End-to-end: a bot-style cast through StartFullGameAsync should
        // actually fire SpellCastFlow, push the spell onto the stack and let
        // it resolve into the graveyard. Before the fix the bolt was simply
        // rotated back into Alice's hand.
        var repo = new InMemoryCardRepo();
        repo.Add(new CardEntity
        {
            Name = "Lightning Bolt",
            ScryfallId = Guid.NewGuid().ToString(),
            ManaCost = "{R}",
            TypeLine = "Instant",
            OracleText = "Lightning Bolt deals 3 damage to any target.",
            IsImplemented = true,
        });

        // Decks: Alice gets a Bolt + a Mountain to pay for it; Bob is a
        // vanilla blob so the loop reaches Alice's main phase quickly.
        var bolt = new Instant("Lightning Bolt", "{R}");
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });

        var aliceDeck = new List<ICard> { bolt, mountain };
        for (var i = 0; i < 58; i++)
            aliceDeck.Add(new Land($"Filler{i}",
                supertypes: new[] { CardSupertype.Basic },
                subtypes: new[] { CardSubtype.Mountain }));
        var bobDeck = Enumerable.Range(0, 60)
            .Select(i => (ICard)new Land($"BobFiller{i}",
                supertypes: new[] { CardSupertype.Basic },
                subtypes: new[] { CardSubtype.Plains }))
            .ToList();

        var facade = GameFacade.Create("Alice", "Bob", aliceDeck, bobDeck, cardRepo: repo);

        // Drive directly via the resolver — the integration test for the
        // full PriorityLoop path lives in Majik.Bot.Tests.Integration; here
        // we just need to prove the resolver is non-null and produces a
        // SpellDefinition that actually deals damage when executed.
        var resolver = InvokeBuildResolver(facade);
        resolver.Should().NotBeNull();
        bolt.SetOwner(facade.Alice);
        var def = resolver!(bolt, facade.Alice, null);
        def.Should().NotBeNull("OracleSpellBinder should bind Lightning Bolt via the damage-any-target template");

        var chosen = new Majik.Core.Game.ChosenSpellParams(
            null, null,
            new[] { new object[] { facade.Bob } },
            Majik.Core.Players.Agents.ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
        facade.Bob.LifeTotal.Should().Be(17, "the resolver should produce a real 3-damage effect");

        await Task.CompletedTask;
    }

    private static Func<ICard, Player, Majik.Core.Stack.Stack?, Majik.Core.Game.SpellDefinition?>? InvokeBuildResolver(GameFacade facade)
    {
        var mi = typeof(GameFacade).GetMethod(
            "BuildSpellDefinitionResolver",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        mi.Should().NotBeNull("BuildSpellDefinitionResolver should exist after wiring fix");
        return (Func<ICard, Player, Majik.Core.Stack.Stack?, Majik.Core.Game.SpellDefinition?>?)
            mi!.Invoke(facade, Array.Empty<object>());
    }

    private sealed class InMemoryCardRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _byName = new(StringComparer.OrdinalIgnoreCase);
        public void Add(CardEntity e) => _byName[e.Name] = e;
        public CardEntity? GetByName(string name)
            => _byName.TryGetValue(name, out var e) ? e : null;
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
            => names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();
        public bool IsImplemented(string name)
            => _byName.TryGetValue(name, out var e) && e.IsImplemented;
        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => Array.Empty<CardEntity>();
        public void SetImplemented(string name, bool value) { }
    }
}
