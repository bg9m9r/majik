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

    public static IEnumerable<TriggeredAbility> Bind(ICard source, CardEntity entity, Player? controller = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        var ctrl = controller ?? source.Controller ?? source.Owner;
        if (ctrl == null) yield break;

        var text = entity.OracleText ?? string.Empty;

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
    }

    private static IEnumerable<IEffect> BuildEffects(string effectText, Player controller)
    {
        var m = YouGainLife.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"gain {n} life", () => controller.GainLife(n));
            yield break;
        }

        m = DrawCards.Match(effectText);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            yield return new Effect($"draw {n}", () => DrawN(controller, n));
            yield break;
        }

        // No matched effect — fall through.
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
