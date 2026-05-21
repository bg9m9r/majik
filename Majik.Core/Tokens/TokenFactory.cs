using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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

    /// <summary>Clue token (CR 111.10): colourless artifact with
    /// "{2}, Sacrifice this artifact: Draw a card."</summary>
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

        // {2}, Sacrifice this artifact: Draw a card.
        token.AddAbility(BuildClueDrawAbility(token, controller));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Food (CR 111.10): colorless artifact token with
    /// "{2}, {T}, Sacrifice this artifact: You gain 3 life."</summary>
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

        // {2}, {T}, Sacrifice this artifact: You gain 3 life.
        token.AddAbility(BuildFoodGainLifeAbility(token, controller));

        PutOnBattlefield(token, controller, zones);
        return token;
    }

    /// <summary>Eldrazi Spawn (CR 111.10): colorless creature token, 0/1, with
    /// "Sacrifice this token: Add {C}." mana ability.
    /// v1: ManaAbility produces {C} without enforcing the sacrifice cost — the
    /// sacrifice restriction is documented but deferred pending a sac-cost ManaAbility
    /// cost extension.</summary>
    public static Creature CreateEldraziSpawn(Player controller, ZoneService? zones = null)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var token = new Creature("Eldrazi Spawn", manaCost: "",
            power: 0, toughness: 1,
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Spawn })
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
            HasSummoningSickness = true,
        };

        // "Sacrifice this token: Add {C}."
        // v1: wired as a plain ManaAbility that produces {C}.
        // Sacrifice cost enforcement is deferred until ManaAbility supports
        // additional costs (same gap as Treasure/Food token sac cost).
        token.AddAbility(new ManaAbility(token, controller,
            Majik.Core.ValueObjects.ManaCost.Parse("C")));

        // Put the token onto the battlefield using the sentinel-library pattern
        // shared by CreateTreasure / CreateFood so CardMovedEvent fires correctly.
        token.SetZone(ZoneType.Library);
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

    /// <summary>"{2}, Sacrifice this artifact: Draw a card." — Clue ability.</summary>
    private static ActivatedAbility BuildClueDrawAbility(Artifact source, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ValueObjects.ManaCost.Parse("2")),
            AdditionalCost.Sacrifice(source),
        };
        var effects = new IEffect[]
        {
            new Effect("draw 1 from Clue", () => DrawOneCard(controller)),
        };
        return new ActivatedAbility(source, controller, costs: costs, effects: effects);
    }

    /// <summary>"{2}, {T}, Sacrifice this artifact: You gain 3 life." — Food ability.</summary>
    private static ActivatedAbility BuildFoodGainLifeAbility(Artifact source, Player controller)
    {
        var costs = new ICost[]
        {
            new ManaCostCost(ValueObjects.ManaCost.Parse("2")),
            AdditionalCost.Tap(source),
            AdditionalCost.Sacrifice(source),
        };
        var effects = new IEffect[]
        {
            new Effect("Food: gain 3 life", () => controller.GainLife(3)),
        };
        return new ActivatedAbility(source, controller, costs: costs, effects: effects);
    }

    /// <summary>Move the top card of <paramref name="player"/>'s library to
    /// their hand (CR 121.2). No-ops silently if the library is empty
    /// (empty-library state-loss is handled by SBAs, not here).</summary>
    private static void DrawOneCard(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return;
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
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
