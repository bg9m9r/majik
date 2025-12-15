using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Tests.Helpers;

/// <summary>
/// Test data builders for creating test objects.
/// Follows the Builder pattern for clean test setup.
/// </summary>
public static class TestDataBuilder
{
    public static PlayerBuilder Player() => new();
    public static CardBuilder Card() => new();
    public static ManaCostBuilder ManaCost() => new();
}

public class PlayerBuilder
{
    private string _name = "Test Player";
    private int _lifeTotal = 20;
    private ManaPool _manaPool = ManaPool.Empty;

    public PlayerBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public PlayerBuilder WithLifeTotal(int life)
    {
        _lifeTotal = life;
        return this;
    }

    public PlayerBuilder WithManaPool(ManaPool pool)
    {
        _manaPool = pool;
        return this;
    }

    public Player Build()
    {
        var player = new Player(_name, _lifeTotal);
        // Note: ManaPool is read-only, so we'd need to add mana through methods
        // This is a simplified builder - extend as needed
        return player;
    }
}

public class CardBuilder
{
    private string _name = "Test Card";
    private string _manaCost = "";
    private Player? _owner;
    private Player? _controller;

    public CardBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public CardBuilder WithManaCost(string manaCost)
    {
        _manaCost = manaCost;
        return this;
    }

    public CardBuilder WithOwner(Player owner)
    {
        _owner = owner;
        return this;
    }

    public CardBuilder WithController(Player controller)
    {
        _controller = controller;
        return this;
    }

    public Instant BuildInstant()
    {
        var card = new Instant(_name, _manaCost);
        if (_owner != null) card.Owner = _owner;
        if (_controller != null) card.Controller = _controller;
        return card;
    }

    public Creature BuildCreature(int power, int toughness)
    {
        var card = new Creature(_name, _manaCost, power, toughness);
        if (_owner != null) card.Owner = _owner;
        if (_controller != null) card.Controller = _controller;
        return card;
    }
}

public class ManaCostBuilder
{
    private string _manaCost = "";

    public ManaCostBuilder WithCost(string cost)
    {
        _manaCost = cost;
        return this;
    }

    public Majik.Core.ValueObjects.ManaCost Build()
    {
        return Majik.Core.ValueObjects.ManaCost.Parse(_manaCost);
    }
}
