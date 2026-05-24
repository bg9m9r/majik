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
        Majik.Core.Stack.Stack stack,
        Player? viewer = null)
    {
        if (players == null) throw new ArgumentNullException(nameof(players));
        if (activePlayer == null) throw new ArgumentNullException(nameof(activePlayer));
        if (stack == null) throw new ArgumentNullException(nameof(stack));

        return new GameStateDto(
            GameId: gameId,
            TurnNumber: turnNumber,
            Phase: phase?.ToString(),
            ActivePlayerId: activePlayer.Id,
            Players: players.Select(p => SnapshotPlayer(p, viewer)).ToList(),
            Stack: stack.GetAll().Select(SnapshotStackObject).ToList());
    }

    private static PlayerDto SnapshotPlayer(Player p, Player? viewer)
    {
        // CR 706 — opponent hand + library are hidden information.
        // Viewer == p (or null = spectator-all-revealed) sees everything.
        var hideHidden = viewer != null && !ReferenceEquals(p, viewer);
        return new PlayerDto(
            Id: p.Id,
            Name: p.Name,
            Life: p.LifeTotal,
            HasLost: p.HasLost,
            Mana: SnapshotMana(p.ManaPool),
            Hand: hideHidden ? HiddenZone(p.Zones.Hand) : SnapshotZone(p.Zones.Hand),
            Battlefield: SnapshotZone(p.Zones.Battlefield),
            Graveyard: SnapshotZone(p.Zones.Graveyard),
            Library: HiddenZone(p.Zones.Library),       // always hidden, even to owner
            Exile: SnapshotZone(p.Zones.Exile));
    }

    /// <summary>Zone whose contents are hidden — DTO carries only the count.</summary>
    private static ZoneDto HiddenZone(IZone zone)
    {
        var n = zone.GetCards().Count();
        var placeholders = Enumerable.Range(0, n).Select(_ => new CardSnapshotDto(
            InstanceId: Guid.Empty,
            Name: "(hidden)",
            ManaCost: "",
            Types: System.Array.Empty<string>(),
            Power: null,
            Toughness: null,
            Tapped: false,
            SummoningSickness: false,
            Abilities: System.Array.Empty<AbilityDto>())).ToList();
        return new ZoneDto(placeholders);
    }

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
            Abilities: card.Abilities.Select(SnapshotAbility).ToList(),
            ProducedManaColors: ComputeProducedManaColors(card));
    }

    /// <summary>
    /// CR 605 — derive the WUBRG/C colour string from the card's actual
    /// <see cref="IManaAbility"/> instances so the client can render a
    /// "tap for mana" affordance without round-tripping oracle text.
    /// Order is fixed WUBRG then C. Hybrid / generic / X / Snow are
    /// excluded from v1; only the five colours plus pure {C} are emitted.
    /// </summary>
    private static string ComputeProducedManaColors(ICard card)
    {
        var w = false; var u = false; var b = false; var r = false; var g = false; var c = false;
        foreach (var ma in card.Abilities.OfType<IManaAbility>())
        {
            var mc = ma.ManaGenerated;
            if (mc == null) continue;
            if (mc.White > 0) w = true;
            if (mc.Blue > 0) u = true;
            if (mc.Black > 0) b = true;
            if (mc.Red > 0) r = true;
            if (mc.Green > 0) g = true;
            // {C} is parsed into Generic with no colour pips set.
            if (mc.Generic > 0 && mc.White == 0 && mc.Blue == 0
                && mc.Black == 0 && mc.Red == 0 && mc.Green == 0)
            {
                c = true;
            }
        }
        var sb = new System.Text.StringBuilder(6);
        if (w) sb.Append('W');
        if (u) sb.Append('U');
        if (b) sb.Append('B');
        if (r) sb.Append('R');
        if (g) sb.Append('G');
        if (c) sb.Append('C');
        return sb.ToString();
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
