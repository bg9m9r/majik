using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// The shared cast-time spell-definition resolver factory used by BOTH
/// <c>GameFacade</c> (live games) and <c>SandboxGame</c> (bot-search
/// sandboxes). Pins the two contract edges: null repo → null resolver
/// (callers keep TurnDriver's skip-rotate default), and a known
/// instant resolving to a runnable <see cref="SpellDefinition"/> that
/// actually deals its damage.
/// </summary>
public class SpellDefinitionResolverFactoryTests
{
    [Fact]
    public void Create_NullRepo_ReturnsNullResolver()
    {
        SpellDefinitionResolverFactory.Create(cardRepo: null).Should().BeNull(
            "no repo means callers must preserve the no-resolver default");
    }

    [Fact]
    public void Create_WithRepo_ResolvesKnownBurnSpell_AndEffectDealsDamage()
    {
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

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);

        var resolver = SpellDefinitionResolverFactory.Create(repo);
        resolver.Should().NotBeNull();

        var def = resolver!(bolt, alice, null);
        def.Should().NotBeNull(
            "Lightning Bolt's oracle text matches the damage-any-target template");

        var chosen = new ChosenSpellParams(
            null, null,
            new[] { new object[] { bob } },
            Majik.Core.Players.Agents.ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
        bob.LifeTotal.Should().Be(17, "the resolved definition is a real 3-damage effect");
    }

    [Fact]
    public void Create_WithRepo_UnknownCard_ResolvesToNullDefinition()
    {
        var repo = new InMemoryCardRepo(); // knows nothing
        var alice = new Player("Alice", 20);
        var mystery = new Instant("Totally Unknown Spell", "{1}");
        mystery.SetOwner(alice);

        var resolver = SpellDefinitionResolverFactory.Create(repo);
        resolver.Should().NotBeNull();
        resolver!(mystery, alice, null).Should().BeNull(
            "an unknown name has no entity, so the lookup yields no definition");
    }

    private sealed class InMemoryCardRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _byName = new(StringComparer.OrdinalIgnoreCase);
        public void Add(CardEntity e) => _byName[e.Name] = e;
        public CardEntity? GetByName(string name)
            => _byName.TryGetValue(name, out var e) ? e : null;
        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
            => names.Select(GetByName).OfType<CardEntity>().ToList();
        public bool IsImplemented(string name)
            => _byName.TryGetValue(name, out var e) && e.IsImplemented;
        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => Array.Empty<CardEntity>();
        public void SetImplemented(string name, bool value) { }
    }
}
