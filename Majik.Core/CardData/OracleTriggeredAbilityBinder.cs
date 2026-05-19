using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Scans oracle text for common triggered-ability phrasings and synthesizes
/// <see cref="TriggeredAbility"/> instances attached to a permanent.
///
/// Templates handled (first match per line wins):
///   "When [ ~ | this creature ] enters the battlefield, ..."  → ETB
///   "When ~ dies, ..."                                       → death trigger
///   "Whenever ~ deals combat damage to a player, ..."        → combat
///
/// Effect tail is fed back through <see cref="OracleSpellBinder"/>-style
/// simple templates: "you gain N life", "draw N cards", "deals N damage
/// to any target" (caster-side; targets deferred — phase 21.5 will route
/// through agent prompts).
/// </summary>
public static class OracleTriggeredAbilityBinder
{
    private static readonly Regex EtbLine = new(
        @"When(ever)?\s+(?<ref>~|this creature|this artifact|this enchantment|this permanent)\s+enters(\s+the\s+battlefield)?\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex DiesLine = new(
        @"When\s+(?<ref>~|this creature)\s+dies\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);
    private static readonly Regex CombatDamagePlayer = new(
        @"Whenever\s+(?<ref>~|this creature)\s+deals\s+combat\s+damage\s+to\s+a\s+player\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);

    private static readonly Regex YouGainLife = new(
        @"you\s+gain\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+li(?:fe|ves)",
        RegexOptions.IgnoreCase);
    private static readonly Regex DrawCards = new(
        @"draw\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);
    private static readonly Regex DealDamageOpponent = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+damage\s+to\s+(that\s+player|any\s+opponent)",
        RegexOptions.IgnoreCase);
    private static readonly Regex CreateTreasure = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+treasure\s+tokens?",
        RegexOptions.IgnoreCase);
    private static readonly Regex CreateClue = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+clue\s+tokens?",
        RegexOptions.IgnoreCase);
    private static readonly Regex GetEnergy = new(
        @"you get\s+((?:\{E\}\s*)+)",
        RegexOptions.IgnoreCase);
    private static readonly Regex AnotherCreatureEnters = new(
        @"whenever another creature you control enters\s*,\s*(?<effect>[^.]+)\.",
        RegexOptions.IgnoreCase);

    public static IEnumerable<TriggeredAbility> Bind(ICard source, CardEntity entity, Player? controller = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        var ctrl = controller ?? source.Controller ?? source.Owner;
        if (ctrl == null) yield break;

        // Scryfall's oracle text uses the card's literal name (e.g. "Ragavan
        // deals combat damage…"); our regexes use `~` as the self-reference
        // placeholder. Normalise by replacing every occurrence of the
        // card's full name AND the short-name fragment before the comma
        // (e.g. "Ragavan, Nimble Pilferer" → match both "Ragavan" and the
        // full name) with `~`.
        var text = entity.OracleText ?? string.Empty;
        if (!string.IsNullOrEmpty(entity.Name))
        {
            text = text.Replace(entity.Name, "~");
            var commaIdx = entity.Name.IndexOf(',');
            if (commaIdx > 0)
            {
                var shortName = entity.Name[..commaIdx];
                text = text.Replace(shortName, "~");
            }
        }

        foreach (Match m in EtbLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnEnterBattlefieldSelf(source),
                effects: effects);
        }

        foreach (Match m in DiesLine.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnDies(source),
                effects: effects);
        }

        foreach (Match m in CombatDamagePlayer.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                    ReferenceEquals(e.Source, source) && e.TargetPlayer != null),
                effects: effects);
        }

        // "Whenever another creature you control enters, ..." — Soul Warden,
        // Guide of Souls, Soul Attendant pattern.
        foreach (Match m in AnotherCreatureEnters.Matches(text))
        {
            var effects = BuildEffects(m.Groups["effect"].Value, ctrl).ToList();
            if (effects.Count == 0) continue;
            yield return new TriggeredAbility(
                source, ctrl,
                Triggers.OnAnyCreatureEntersBattlefield(),
                effects: effects);
        }
    }

    private static IEnumerable<IEffect> BuildEffects(string effectText, Player controller)
    {
        var m = YouGainLife.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"gain {n} life", () => controller.GainLife(n));
        }

        m = DrawCards.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"draw {n}", () => DrawN(controller, n));
        }

        m = CreateTreasure.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"create {n} treasure", () =>
            {
                for (var i = 0; i < n; i++)
                {
                    Majik.Core.Tokens.TokenFactory.CreateTreasure(controller);
                }
            });
        }

        m = CreateClue.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"create {n} clue", () =>
            {
                for (var i = 0; i < n; i++)
                {
                    Majik.Core.Tokens.TokenFactory.CreateClue(controller);
                }
            });
        }

        m = GetEnergy.Match(effectText);
        if (m.Success)
        {
            var n = System.Text.RegularExpressions.Regex.Matches(
                m.Value, @"\{E\}", RegexOptions.IgnoreCase).Count;
            yield return new Effect($"get {n}E", () => controller.GainEnergy(n));
        }
    }

    private static void DrawN(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.TriedToDrawFromEmptyLibrary = true;
                return;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.Zone = ZoneType.Hand;
        }
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
