using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;

namespace Majik.Core.CardData;

/// <summary>
/// Reads the Scryfall <see cref="CardEntity.Keywords"/> JSON array and
/// attaches a <see cref="KeywordAbility"/> for each evergreen keyword the
/// engine knows how to act on (currently: combat-relevant + Flash + Undying
/// + Prowess). Non-evergreen / mechanically-complex keywords (Storm,
/// Suspend, Kicker, …) are ignored here and would need bespoke binders.
/// </summary>
public static class KeywordBinder
{
    /// <summary>
    /// Evergreen / runtime-handled keywords. Anything not in this set is
    /// dropped on the floor (the card still loads — just without that
    /// keyword's behavior).
    /// </summary>
    private static readonly HashSet<string> Recognized = new(StringComparer.OrdinalIgnoreCase)
    {
        "Flying", "Trample", "Vigilance", "Haste",
        "First strike", "Double strike",
        "Deathtouch", "Lifelink", "Reach", "Menace",
        "Defender", "Indestructible", "Flash",
        // Death-triggered keywords
        "Undying",
        // Cast-noncreature-spell triggered keyword (CR 702.108). Requires a
        // ContinuousEffectsService — when none is supplied the marker is
        // attached but the pump won't fire.
        "Prowess",
        // Alternative-cost keyword (CR 702.74). The KeywordAbility marker is
        // attached so downstream code (UI, action validator) can introspect
        // "this creature has evoke". The evoke alt-cost path itself is wired
        // bespoke per-card (see EvokeAlternativeCost + EvokeFactory and the
        // incarnation card factories) — Scryfall does not encode the evoke
        // cost in a way KeywordBinder can parse generically.
        "Evoke",
    };

    public static void Bind(ICard card, CardEntity entity, Player controller,
        ContinuousEffectsService? effects = null)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var keywords = ParseKeywordsArray(entity.Keywords);
        foreach (var kw in keywords)
        {
            if (!Recognized.Contains(kw)) continue;

            card.AddAbility(new KeywordAbility(kw, card, controller));

            // Keywords that require an additional triggered ability beyond the
            // marker. Each factory handles its own preconditions.
            if (kw.Equals("Undying", StringComparison.OrdinalIgnoreCase)
                && card is Creature undyingCreature)
            {
                // Set controller on the card if not already set, so the factory
                // can bind the trigger correctly.
                if (undyingCreature.Controller == null)
                    undyingCreature.SetController(controller);
                card.AddAbility(UndyingFactory.Build(undyingCreature));
            }
            else if (kw.Equals("Prowess", StringComparison.OrdinalIgnoreCase)
                && card is Creature prowessCreature
                && effects != null)
            {
                if (prowessCreature.Controller == null)
                    prowessCreature.SetController(controller);
                card.AddAbility(ProwessFactory.Build(prowessCreature, effects));
            }
            else if (kw.Equals("Evoke", StringComparison.OrdinalIgnoreCase)
                && card is Creature evokeCreature)
            {
                // CR 702.74b — every evoke creature has the printed trigger
                // "When this creature enters, if its evoke cost was paid,
                // sacrifice it." Wire it generically here. The intervening-if
                // gates on Creature.EvokeWasPaid, which is set only when the
                // EvokeAlternativeCost was used; a normal cast leaves it false
                // and the trigger is harmless.
                if (evokeCreature.Controller == null)
                    evokeCreature.SetController(controller);
                card.AddAbility(Majik.Core.Keywords.EvokeFactory.Build(evokeCreature));
            }
        }
    }

    private static IReadOnlyList<string> ParseKeywordsArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<string>();
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(json);
            return (IReadOnlyList<string>?)result ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
