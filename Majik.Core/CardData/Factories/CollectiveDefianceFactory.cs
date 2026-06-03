using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Collective Defiance (Eldritch Moon, {1}{R}{R}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Escalate {1} (Pay this cost for each mode chosen beyond the first.)
///    Choose one or more —
///     • Target player discards all the cards in their hand, then draws that
///       many cards.
///     • Collective Defiance deals 4 damage to target creature.
///     • Collective Defiance deals 3 damage to target opponent or planeswalker."
///
/// ## Mechanics
/// - <b>Choose one or more</b> (CR 700.2d) — modelled with
///   <see cref="SpellDefinition.MinModes"/> = 1,
///   <see cref="SpellDefinition.MaxModes"/> = 3 and the multi-pick prompt path
///   in <see cref="SpellCastFlow"/> (mirrors
///   <see cref="CollectiveBrutalityFactory"/>).
/// - <b>Escalate {1}</b> (CR 702.121) — a per-extra-mode MANA additional cost
///   (CR 601.2f), wired via <see cref="SpellDefinition.Escalate"/> with a fresh
///   <see cref="EscalateManaAdditionalCost"/> per extra mode. The cast flow pays
///   (modesChosen − 1) of them and rejects the cast atomically (CR 601.2g) when
///   the caster's mana pool can't cover the extra escalate mana.
///
/// ## Per-mode effects (mapped to existing primitives)
///   Mode 0 — target player wheels their hand. CR 701.16 (discard) + CR 121
///     (draw): the target's whole hand goes to the graveyard, then they draw
///     that many cards. Target slot: <see cref="ChosenSpellParams.Targets"/>[0].
///   Mode 1 — 4 damage to target creature. CR 119 via
///     <see cref="Fx.DealDamage"/>(4). Target slot: Targets[1].
///   Mode 2 — 3 damage to target opponent or planeswalker. CR 119 + CR 306.7
///     via <see cref="Fx.DealDamageAny"/>(3) (routes Player → life loss,
///     Planeswalker → loyalty removal). Target slot: Targets[2].
///
/// All three modes are targeted. Each target request carries
/// <c>MinTargets = 0</c> so an UNCHOSEN mode never gates the cast, but
/// <c>PrintedMinTargets = 1</c> so a CHOSEN targeted mode with no legal target
/// makes the whole cast illegal and rewinds (CR 601.2c) — the chosen-mode
/// MinTargets tightening this factory exercises.
/// </summary>
[CardName("Collective Defiance")]
public static class CollectiveDefianceFactory
{
    public const string CardName = "Collective Defiance";
    public const string PrintedManaCost = "{1}{R}{R}";

    public const int ModeWheel        = 0;
    public const int ModeDamageFour   = 1;
    public const int ModeDamageThree  = 2;

    /// <summary>Total number of printed modes (CR 700.2d).</summary>
    public const int TotalModes = 3;

    /// <summary>CR 119 — damage magnitude for mode 1 (target creature).</summary>
    public const int CreatureDamage = 4;

    /// <summary>CR 119 — damage magnitude for mode 2 (opponent/planeswalker).</summary>
    public const int OpponentDamage = 3;

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Target player discards all the cards in their hand, then draws that many cards.",
        "Collective Defiance deals 4 damage to target creature.",
        "Collective Defiance deals 3 damage to target opponent or planeswalker.",
    };

    /// <summary>Construct Collective Defiance as a Sorcery owned by <paramref name="owner"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Collective Defiance. "Choose one or more"
    /// (CR 700.2d) over three targeted modes, with Escalate {1} (CR 702.121)
    /// wired as the per-extra-mode mana additional cost.
    /// </summary>
    /// <param name="caster">Cast-time controller.</param>
    /// <param name="targetResolver">Resolver from the caller's GameContext
    /// (chosen target → live game object).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target request per mode. All three are targeted, so
        // MinTargets = 0 (unchosen modes don't gate) + PrintedMinTargets = 1
        // (a chosen targeted mode demands a legal target or the cast rewinds).
        var targetRequests = new[]
        {
            // Mode 0 — target player (wheel).
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Draw, PrintedMinTargets: 1),
            // Mode 1 — target creature (4 damage).
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.Burn, PrintedMinTargets: 1),
            // Mode 2 — target opponent or planeswalker (3 damage).
            new TargetRequest("target opponent or planeswalker", 0, 1, Array.Empty<object>(), BotIntent.Burn, PrintedMinTargets: 1),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Draw,
                BotIntent.Burn,
                BotIntent.Burn,
            },
            // CR 700.2d — "Choose one or more": between 1 and all 3 modes.
            MinModes: 1,
            MaxModes: TotalModes,
            // CR 702.121 — Escalate {1}. One fresh mana cost per extra mode.
            // The aggregate probe rejects a cast whose extra-mode escalate
            // bill exceeds the caster's available mana (CR 601.2g).
            Escalate: new EscalateSpec(
                Description: "{1}",
                BuildPerModeCost: _ => new EscalateManaAdditionalCost(ManaCost.Parse("{1}")),
                // CR 601.2g — the escalate mana is paid from the caster's pool
                // at cost time (before the main mana payment). Confirm the pool
                // can cover {1} × extraModes up front.
                CanPayExtra: (player, extra) =>
                    player.ManaPool.CanPay(ManaCost.Zero.AddGenericCost(extra))),
            EffectFactory: p =>
            {
                // CR 700.2d — resolve the chosen modes in PRINTED order
                // (CR 608.2c), de-duplicated, capped at the printed total.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var distinct = new SortedSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    distinct.Add(raw); // SortedSet → printed order, deduped
                }

                var effects = new List<IEffect>();
                foreach (var raw in distinct) // ascending = printed order
                {
                    switch (raw)
                    {
                        case ModeWheel:
                            effects.Add(BuildWheelEffect(p, targetResolver));
                            break;
                        case ModeDamageFour:
                            effects.Add(BuildCreatureDamageEffect(p, targetResolver));
                            break;
                        case ModeDamageThree:
                            effects.Add(BuildOpponentDamageEffect(p, targetResolver));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: target player discards all the cards in their hand, then draws
    // that many cards.
    // -----------------------------------------------------------------------
    private static IEffect BuildWheelEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Collective Defiance — target player wheels their hand", () =>
        {
            if (p.Targets.Count <= ModeWheel) return;
            var slot = p.Targets[ModeWheel];
            if (slot.Count == 0) return;
            if (resolver(slot[0]) is not Player target) return;

            // CR 701.16 — discard ALL cards in the target's hand, counting them.
            var hand = target.Zones.Hand.GetCards().ToList();
            var count = hand.Count;
            foreach (var c in hand)
            {
                target.Zones.Hand.RemoveCard(c);
                target.Zones.Graveyard.AddCard(c);
                c.SetZone(ZoneType.Graveyard);
            }

            // CR 121 — "then draws that many cards." Each top-of-library draw;
            // an empty library mid-draw flags the SBA loss (CR 704.5b).
            for (var i = 0; i < count; i++)
            {
                var top = target.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    target.MarkTriedToDrawFromEmptyLibrary();
                    break;
                }
                target.Zones.Library.RemoveCard(top);
                target.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            }
        });

    // -----------------------------------------------------------------------
    // Mode 1: Collective Defiance deals 4 damage to target creature.
    // -----------------------------------------------------------------------
    private static IEffect BuildCreatureDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"Collective Defiance — deals {CreatureDamage} damage to target creature", () =>
        {
            if (p.Targets.Count <= ModeDamageFour) return;
            var slot = p.Targets[ModeDamageFour];
            if (slot.Count == 0) return;

            // CR 608.2b — target must still be a creature on the battlefield.
            if (resolver(slot[0]) is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            Fx.DealDamage(target, CreatureDamage); // CR 119
        });

    // -----------------------------------------------------------------------
    // Mode 2: Collective Defiance deals 3 damage to target opponent or
    // planeswalker.
    // -----------------------------------------------------------------------
    private static IEffect BuildOpponentDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"Collective Defiance — deals {OpponentDamage} damage to target opponent or planeswalker", () =>
        {
            if (p.Targets.Count <= ModeDamageThree) return;
            var slot = p.Targets[ModeDamageThree];
            if (slot.Count == 0) return;

            // CR 119 + CR 306.7 — Player → life loss, Planeswalker → loyalty.
            Fx.DealDamageAny(resolver(slot[0]), OpponentDamage);
        });
}
