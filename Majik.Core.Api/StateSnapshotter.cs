using Majik.Core.Abilities;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Pure transform from live engine state to <see cref="GameStateDto"/>.
/// Holds no state — safe to call on every "get state" request.
/// </summary>
public static class StateSnapshotter
{
    public static GameStateDto Snapshot(
        Guid gameId,
        int turnNumber,
        PhaseStateType? phase,
        Player activePlayer,
        IReadOnlyList<Player> players,
        Majik.Core.Stack.Stack stack)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        if (activePlayer == null) throw new ArgumentNullException(nameof(activePlayer));
        if (stack == null) throw new ArgumentNullException(nameof(stack));

        return new GameStateDto(
            GameId: gameId,
            TurnNumber: turnNumber,
            Phase: phase?.ToString(),
            ActivePlayerId: activePlayer.Id,
            Players: players.Select(SnapshotPlayer).ToList(),
            Stack: stack.GetAll().Select(SnapshotStackObject).ToList());
    }

    private static PlayerDto SnapshotPlayer(Player p) => new(
        Id: p.Id,
        Name: p.Name,
        Life: p.LifeTotal,
        HasLost: p.HasLost,
        Mana: SnapshotMana(p.ManaPool),
        Hand: SnapshotZone(p.Zones.Hand),
        Battlefield: SnapshotZone(p.Zones.Battlefield),
        Graveyard: SnapshotZone(p.Zones.Graveyard),
        Library: SnapshotZone(p.Zones.Library),
        Exile: SnapshotZone(p.Zones.Exile));

    private static ManaPoolDto SnapshotMana(ManaPool pool) => new(
        Generic: pool.Generic,
        White: pool.White,
        Blue: pool.Blue,
        Black: pool.Black,
        Red: pool.Red,
        Green: pool.Green,
        Colorless: 0);

    private static ZoneDto SnapshotZone(IZone zone) =>
        new(zone.GetCards().Select(SnapshotCard).ToList());

    private static CardSnapshotDto SnapshotCard(ICard card)
    {
        int? power = null;
        int? toughness = null;
        bool tapped = false;
        bool summoningSickness = false;

        if (card is Creature c)
        {
            power = c.Power;
            toughness = c.Toughness;
        }

        if (card is Permanent perm)
        {
            tapped = perm.IsTapped;
            summoningSickness = perm.HasSummoningSickness;
        }

        return new CardSnapshotDto(
            InstanceId: card.InstanceId,
            Name: card.Name,
            ManaCost: card.ManaCost,
            Types: card.CardTypes.Select(t => t.ToString()).ToList(),
            Power: power,
            Toughness: toughness,
            Tapped: tapped,
            SummoningSickness: summoningSickness,
            Abilities: card.Abilities.Select(SnapshotAbility).ToList());
    }

    private static AbilityDto SnapshotAbility(IAbility ability) => ability switch
    {
        IActivatedAbility a => new AbilityDto("Activated", a.GetType().Name),
        ITriggeredAbility => new AbilityDto("Triggered", "triggered ability"),
        IStaticAbility => new AbilityDto("Static", "static ability"),
        _ => new AbilityDto(ability.GetType().Name, ability.ToString() ?? ""),
    };

    private static StackObjectDto SnapshotStackObject(IStackObject obj) => obj switch
    {
        ISpell spell => new StackObjectDto(
            Id: spell.Id,
            Kind: "Spell",
            ControllerId: spell.Controller.Id,
            Description: spell.Card.Name),
        ITriggeredAbility t => new StackObjectDto(
            Id: t.Id,
            Kind: "TriggeredAbility",
            ControllerId: t.Controller.Id,
            Description: (t.Source as ICard)?.Name + " trigger"),
        IActivatedAbility a => new StackObjectDto(
            Id: a.Id,
            Kind: "ActivatedAbility",
            ControllerId: a.Controller.Id,
            Description: "ability"),
        _ => new StackObjectDto(obj.Id, obj.GetType().Name, null, obj.GetType().Name),
    };
}
