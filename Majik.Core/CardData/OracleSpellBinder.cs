using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Pattern-matches an instant/sorcery's oracle text to one of a handful of
/// canonical templates, returning a runnable <see cref="SpellDefinition"/>.
/// Returns null when no template matches — caller can fall back to a
/// vanilla shell so the card still loads.
///
/// Templates handled (first match wins):
///   - "Counter target spell."                       → counter
///   - "Deals N damage to any target."               → damage (creature or player)
///   - "Deals N damage to target player [or planeswalker]." → player damage
///   - "Destroy target creature."                    → destroy creature
///   - "Destroy target artifact or enchantment."     → destroy permanent
///   - "Draw N cards."                               → draw (caster)
///   - "Target player discards N cards."             → discard
///
/// Word numerals ("one", "two", "three", …) are translated to digits.
/// </summary>
public static class OracleSpellBinder
{
    private static readonly Regex DamageAnyTarget = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+any\s+target",
        RegexOptions.IgnoreCase);
    private static readonly Regex DamagePlayer = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+target\s+player",
        RegexOptions.IgnoreCase);
    private static readonly Regex CounterSpell = new(
        @"counter\s+target\s+spell",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyCreature = new(
        @"destroy\s+target\s+(non\w+\s+)?creature",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyArtifactEnchantment = new(
        @"destroy\s+target\s+(artifact|enchantment)(\s+or\s+(artifact|enchantment))?",
        RegexOptions.IgnoreCase);
    private static readonly Regex DrawCards = new(
        @"draw\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);
    private static readonly Regex Discard = new(
        @"target\s+player\s+discards?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));

        var text = entity.OracleText ?? string.Empty;

        if (CounterSpell.IsMatch(text)) return CounterTargetSpell(resolver, stack);

        var m = DamageAnyTarget.Match(text);
        if (m.Success) return DamageAnySpell(WordToInt(m.Groups["n"].Value), resolver);

        m = DamagePlayer.Match(text);
        if (m.Success) return DamagePlayerSpell(WordToInt(m.Groups["n"].Value), resolver);

        if (DestroyCreature.IsMatch(text)) return DestroyCreatureSpell(resolver);
        if (DestroyArtifactEnchantment.IsMatch(text)) return DestroyArtifactOrEnchantmentSpell(resolver);

        m = DrawCards.Match(text);
        if (m.Success) return DrawNSpell(WordToInt(m.Groups["n"].Value), caster);

        m = Discard.Match(text);
        if (m.Success) return DiscardNSpell(WordToInt(m.Groups["n"].Value), resolver);

        return null;
    }

    // ---------- Spell templates ----------

    private static SpellDefinition DamageAnySpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n}", () => DealDamage(target, n)) };
        });

    private static SpellDefinition DamagePlayerSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n} to player", () =>
            {
                if (target is Player player) player.LoseLife(n);
            }) };
        });

    private static SpellDefinition CounterTargetSpell(Func<object, object> resolver, Majik.Core.Stack.Stack? stack) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("counter target spell", () =>
            {
                if (stack == null || target is not ISpell spell) return;
                RemoveFromStack(stack, spell);
                spell.Card.Zone = ZoneType.Graveyard;
            }) };
        });

    private static SpellDefinition DestroyCreatureSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy creature", () =>
            {
                if (target is Creature c) MoveToGraveyard(c);
            }) };
        });

    private static SpellDefinition DestroyArtifactOrEnchantmentSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target artifact or enchantment", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy artifact/enchantment", () =>
            {
                if (target is ICard card) MoveToGraveyard(card);
            }) };
        });

    private static SpellDefinition DrawNSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"draw {n}", () => DrawCards_(caster, n)) });

    private static SpellDefinition DiscardNSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"discard {n}", () =>
            {
                if (target is Player pl) DiscardCards(pl, n);
            }) };
        });

    // ---------- Primitives ----------

    private static void DealDamage(object target, int n)
    {
        switch (target)
        {
            case Player p: p.LoseLife(n); break;
            case Creature c: c.TakeDamage(n); break;
        }
    }

    private static void MoveToGraveyard(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            owner.Zones.Battlefield.RemoveCard(card);
            owner.Zones.Graveyard.AddCard(card);
        }
        card.Zone = ZoneType.Graveyard;
    }

    private static void DrawCards_(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.Zone = ZoneType.Hand;
        }
    }

    private static void DiscardCards(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Hand.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Hand.RemoveCard(top);
            player.Zones.Graveyard.AddCard(top);
            top.Zone = ZoneType.Graveyard;
        }
    }

    private static void RemoveFromStack(Majik.Core.Stack.Stack stack, IStackObject spell)
    {
        var keep = new List<IStackObject>();
        while (!stack.IsEmpty)
        {
            var top = stack.Pop()!;
            if (!ReferenceEquals(top, spell)) keep.Add(top);
        }
        for (var i = keep.Count - 1; i >= 0; i--)
        {
            stack.Push(keep[i]);
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
