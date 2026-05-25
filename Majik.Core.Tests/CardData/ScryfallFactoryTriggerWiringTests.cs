using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ScryfallFactoryTriggerWiringTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Create_SoulWardenLike_AttachesEtbTriggerFromOracleText()
    {
        var repo = new InMemRepo(new Dictionary<string, CardEntity>
        {
            ["Soul Warden"] = new CardEntity
            {
                Name = "Soul Warden",
                ManaCost = "{W}",
                TypeLine = "Creature — Human Cleric",
                Power = "1", Toughness = "1",
                OracleText = "When ~ enters the battlefield, you gain 1 life.",
            },
        });
        var factory = new ScryfallCardFactory(repo);

        var card = factory.Create("Soul Warden", _alice);

        card.Should().BeOfType<Creature>();
        card.Abilities.OfType<ITriggeredAbility>().Should().NotBeEmpty();
    }

    [Fact]
    public void Create_DiesCreatureFromDb_AttachesDeathTrigger()
    {
        var repo = new InMemRepo(new Dictionary<string, CardEntity>
        {
            ["Last Gasp Knight"] = new CardEntity
            {
                Name = "Last Gasp Knight",
                ManaCost = "{2}{B}",
                TypeLine = "Creature — Human Knight",
                Power = "2", Toughness = "2",
                OracleText = "When ~ dies, draw a card.",
            },
        });
        var factory = new ScryfallCardFactory(repo);

        var card = factory.Create("Last Gasp Knight", _alice);
        card.Abilities.OfType<ITriggeredAbility>().Should().HaveCount(1);
    }

    private sealed class InMemRepo : ICardRepository
    {
        private readonly Dictionary<string, CardEntity> _by;
        public InMemRepo(Dictionary<string, CardEntity> by) { _by = by; }
        public CardEntity? GetByName(string name) =>
            _by.TryGetValue(name, out var e) ? e : null;

        public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names) =>
            names.Select(n => GetByName(n)).OfType<CardEntity>().ToList();

        public IReadOnlyList<CardEntity> Search(string? q, bool implementedOnly, int limit,
            IReadOnlyList<string>? colors = null, IReadOnlyList<string>? types = null, IReadOnlyList<int>? cmcBuckets = null)
            => throw new NotImplementedException();

        public bool IsImplemented(string name) => throw new NotImplementedException();

        public void SetImplemented(string name, bool value) => throw new NotImplementedException();
    }
}
