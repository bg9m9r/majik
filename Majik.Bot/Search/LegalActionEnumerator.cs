using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Search;

/// <summary>
/// Enumerates all legal priority actions for a player at a given decision
/// point. Pure enumeration — no scoring. Single source of truth shared by
/// <see cref="Majik.Bot.Heuristic.PriorityPolicy"/> (which scores these) and
/// the bot search (Task B1 / spec §10.3).
///
/// <para>
/// Sorcery-speed gating follows CR 116.2a (active player, main phase, empty
/// stack). Land drops follow CR 305.2 (one land per turn — the engine enforces
/// the hard rule; this enumerator offers the action whenever a land is in hand
/// and we are in a sorcery window, mirroring <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>).
/// Activated abilities follow CR 602.
/// </para>
/// </summary>
internal static class LegalActionEnumerator
{
    /// <summary>
    /// All legal priority actions for <paramref name="self"/> at this decision,
    /// INCLUDING Pass. Pure enumeration — no scoring. Single source of truth
    /// shared by PriorityPolicy (which scores these) and the bot search.
    /// </summary>
    public static IReadOnlyList<PriorityAction> ForPriority(GameContext ctx, Player self)
    {
        var result = new List<PriorityAction>();

        // Pass is always legal (CR 117.3).
        result.Add(PriorityAction.Pass);

        // CR 116.2a sorcery window: active player, main phase, empty stack.
        var sorceryWindow = ctx.ActivePlayer == self
            && ctx.CurrentPhase is { } phase && phase.IsMain()
            && ctx.Stack.Count == 0;

        // CR 305.2 — land play (sorcery speed). Offer the first land in hand;
        // the engine enforces the one-land-per-turn limit.
        if (sorceryWindow)
        {
            var landInHand = self.Zones.Hand.GetCards().OfType<Land>().FirstOrDefault();
            if (landInHand != null)
                result.Add(new PriorityAction.PlayLand(landInHand));
        }

        // Spell casts: any player with priority may cast a spell when timing
        // permits (CR 601.1). Instant-speed spells (Instants, Flash permanents)
        // may be cast on any player's turn. Sorcery-speed spells require the
        // caster to be the active player AND the stack to be empty (CR 116.2a).
        {
            var manaAvailable = UntappedManaSources(self);
            foreach (var card in self.Zones.Hand.GetCards())
            {
                if (card is Land) continue;

                var instantSpeed = IsInstantSpeed(card);
                // Instant-speed: legal regardless of active player.
                // Sorcery-speed: only legal in a sorcery window (active player
                //   + main phase + empty stack — checked above).
                if (!instantSpeed && !sorceryWindow) continue;

                if (ApproxCmc(card) <= manaAvailable)
                    result.Add(new PriorityAction.CastSpell(card, Array.Empty<object>()));
            }
        }

        // CR 602 — activated abilities of permanents the player controls.
        // Mana abilities are excluded (fired as part of mana payment, not here).
        foreach (var card in self.Zones.Battlefield.GetCards())
        {
            foreach (var ability in card.Abilities.OfType<IActivatedAbility>())
            {
                if (ability is IManaAbility) continue;
                if (ability.Costs.All(cost => cost.CanPay(self)))
                    result.Add(new PriorityAction.ActivateAbility(ability, Array.Empty<object>()));
            }
        }

        // CR 606.3 — loyalty abilities of planeswalkers the player controls.
        // Sorcery-speed only (active player + main phase + empty stack — the
        // same sorceryWindow predicate the land/sorcery-cast paths use), plus
        // the once-per-turn + sufficient-loyalty gate (CR 606.3/606.5).
        // LoyaltyAbility is its own ability shape (not IActivatedAbility); the
        // dispatcher builds the stack object from it on activation.
        if (sorceryWindow)
        {
            foreach (var card in self.Zones.Battlefield.GetCards())
            {
                foreach (var loyalty in card.Abilities.OfType<LoyaltyAbility>())
                {
                    if (loyalty.CanActivate())
                        result.Add(new PriorityAction.ActivateLoyaltyAbility(loyalty, Array.Empty<object>()));
                }
            }
        }

        return result;
    }

    // ── Helpers (same semantics as PriorityPolicy's private equivalents) ────────

    /// <summary>
    /// Converted mana cost via the engine's parser. Matches the convention
    /// used by <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>.
    /// </summary>
    internal static int ApproxCmc(ICard card)
        => ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;

    /// <summary>
    /// Count of untapped lands (rough mana-available proxy). Matches
    /// <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>'s UntappedManaSources.
    /// </summary>
    internal static int UntappedManaSources(Player self)
        => self.Zones.Battlefield.GetCards().OfType<Land>().Count(l => !l.IsTapped);

    /// <summary>
    /// Instant-speed cast eligibility — Instants, and Flash permanents
    /// (CR 702.8). Mirrors <see cref="Majik.Bot.Heuristic.PriorityPolicy"/>.
    /// </summary>
    internal static bool IsInstantSpeed(ICard c)
    {
        if (c.HasType(CardType.Instant)) return true;
        return c.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }
}
