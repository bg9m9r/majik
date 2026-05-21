using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Tokens;

/// <summary>
/// CR 111 — builds token creatures on the battlefield. The returned card
/// is marked <see cref="Permanent.IsToken"/> so SBA 704.5d removes it from
/// any zone other than the battlefield.
/// </summary>
public static class TokenFactory
{
    public sealed record TokenSpec(
        string Name,
        int Power,
        int Toughness,
        IReadOnlyList<CardSubtype>? Subtypes = null,
        IReadOnlyList<string>? Keywords = null);

    /// <summary>Create a creature token and put it onto the battlefield under
    /// the given controller. Uses <see cref="ZoneService"/> when supplied so
    /// CardMovedEvent fires (triggers Soul Warden etc.).</summary>
    public static Creature CreateOnBattlefield(
        TokenSpec spec,
        Player controller,
        ZoneService? zones = null)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var token = new Creature(
            spec.Name, manaCost: "",
            power: spec.Power, toughness: spec.Toughness,
            subtypes: spec.Subtypes ?? Array.Empty<CardSubtype>())
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
            HasSummoningSickness = true,
        };

        foreach (var kw in spec.Keywords ?? Array.Empty<string>())
        {
            token.AddAbility(new KeywordAbility(kw, token, controller));
        }

        // Tokens enter the battlefield directly (CR 111.6) — not from the library.
        token.SetZone(ZoneType.Library); // sentinel for ZoneService.MoveCard's from-check
        controller.Zones.Library.AddCard(token);

        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        return token;
    }

    /// <summary>Treasure (CR 111.10): colourless artifact token with
    /// "{T}, Sacrifice this artifact: Add one mana of any color." Bound as
    /// five ManaAbility options so the bot's mana picker can use a
    /// Treasure to satisfy any colour pip.</summary>
    public static Artifact CreateTreasure(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Treasure", "",
            subtypes: new[] { CardSubtype.Treasure })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            token.AddAbility(new ManaAbility(token, controller,
                Majik.Core.ValueObjects.ManaCost.Parse(color)));
        }
        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Clue token. Sac+draw is the canonical effect; activated
    /// ability binder will wire {2}, Sacrifice: Draw a card later.</summary>
    public static Artifact CreateClue(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Clue", "",
            subtypes: new[] { CardSubtype.Clue })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Food (CR 111.10): colorless artifact token. The
    /// "{2}, {T}, Sacrifice this artifact: You gain 3 life." activated
    /// ability is wired by the activated-ability binder; this factory
    /// produces the bare token.</summary>
    public static Artifact CreateFood(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var token = new Artifact("Food", "",
            subtypes: new[] { CardSubtype.Food })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        PutOnBattlefield(token, controller, zones);
        return token;
    }

    private static void PutOnBattlefield(Artifact token, Player controller, ZoneService? zones)
    {
        token.SetZone(ZoneType.Library); // sentinel; ZoneService validates from-zone
        controller.Zones.Library.AddCard(token);
        if (zones != null)
        {
            zones.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            // ZoneManager.MoveCard publishes CardMovedEvent for log /
            // trigger subscribers (Treasure ETB visible, downstream
            // triggers like Soul Warden fire).
            controller.Zones.MoveCard(token, ZoneType.Library, ZoneType.Battlefield);
            token.SetZone(ZoneType.Battlefield);
        }
    }
}
