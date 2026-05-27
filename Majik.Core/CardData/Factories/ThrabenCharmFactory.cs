using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thraben Charm (Shadows Over Innistrad Remastered, {1}{W}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • Thraben Charm deals damage equal to twice the number of creatures
///       you control to target creature.
///     • Destroy target enchantment.
///     • Exile any number of target players' graveyards."
///
/// CR 700.2d — modal "Choose one —" spell. Three <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast, CR 601.2c).
///
/// Mode 0 — "damage equal to twice creatures you control to target creature":
///   Counts the caster's creatures on the battlefield at resolve time × 2,
///   then calls <see cref="Fx.DealDamage"/> on the target creature.
///   CR 608.2b — if the target is no longer a Creature at resolution, no-op.
///   CR 119.2 — non-combat damage recorded via <see cref="Creature.TakeDamage"/>.
///
/// Mode 1 — "destroy target enchantment":
///   Validates the target is still a legal enchantment on the battlefield
///   (CR 608.2b), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7). Indestructible
///   (CR 702.12b) and regeneration (CR 701.15) gates apply.
///
/// Mode 2 — "exile any number of target players' graveyards":
///   v1 targets one player (MinTargets=0, MaxTargets=1 — "any number" MVP).
///   Snapshots and exiles all cards in the target player's graveyard to that
///   player's exile zone. Empty graveyard is a clean no-op (CR 608.2b).
///   Mirrors <see cref="TormodsCryptFactory"/> graveyard-exile model.
///
/// Pattern mirrors <see cref="BorosCharmFactory"/> / <see cref="IzzetCharmFactory"/>
/// for the choose-one modal shape.
/// </summary>
[CardName("Thraben Charm")]
public static class ThrabenCharmFactory
{
    public const string CardName = "Thraben Charm";
    public const string PrintedManaCost = "{1}{W}";

    public const int ModeDynamicDamage       = 0;
    public const int ModeDestroyEnchantment  = 1;
    public const int ModeExileGraveyard      = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Thraben Charm deals damage equal to twice the number of creatures you control to target creature.",
        "Destroy target enchantment.",
        "Exile any number of target players' graveyards.",
    };

    /// <summary>Construct Thraben Charm as an Instant owned by <paramref name="owner"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Thraben Charm.
    /// All three modes are wired.
    /// </summary>
    /// <param name="caster">The player casting the spell.</param>
    /// <param name="targetResolver">Resolver from the caller's GameContext.</param>
    /// <param name="allPlayers">All players in the game.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — target requests per mode. MinTargets=0 so unchosen
        // modes don't gate the cast (mirrors IzzetCharmFactory /
        // BorosCharmFactory / ArchmagesCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — target creature for dynamic damage.
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.Burn),
            // Mode 1 — target enchantment to destroy.
            new TargetRequest("target enchantment", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 2 — target player whose graveyard is exiled.
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Removal),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Burn,
                BotIntent.Removal,
                BotIntent.Removal,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeDynamicDamage:
                            effectsOut.Add(BuildDynamicDamageEffect(caster, p, targetResolver));
                            break;
                        case ModeDestroyEnchantment:
                            effectsOut.Add(BuildDestroyEnchantmentEffect(p, targetResolver));
                            break;
                        case ModeExileGraveyard:
                            effectsOut.Add(BuildExileGraveyardEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: deal damage equal to twice the number of creatures you control
    // -----------------------------------------------------------------------

    private static IEffect BuildDynamicDamageEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Thraben Charm — deals 2× creature count damage to target creature", () =>
        {
            if (p.Targets.Count <= ModeDynamicDamage) return;
            var slot = p.Targets[ModeDynamicDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be a Creature at resolution.
            if (resolved is not Creature targetCreature) return;

            // Count the caster's creatures on the battlefield at resolve time
            // (CR 608.2b — dynamic count evaluated when the effect resolves,
            // not when the spell was cast).
            var creatureCount = caster.Zones.Battlefield
                .GetCards()
                .OfType<Creature>()
                .Count();

            var damage = creatureCount * 2;

            // CR 119.2 — non-combat damage; Fx.DealDamage no-ops when damage ≤ 0.
            Fx.DealDamage(targetCreature, damage);
        });

    // -----------------------------------------------------------------------
    // Mode 1: destroy target enchantment
    // -----------------------------------------------------------------------

    private static IEffect BuildDestroyEnchantmentEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Thraben Charm — destroy target enchantment", () =>
        {
            if (p.Targets.Count <= ModeDestroyEnchantment) return;
            var slot = p.Targets[ModeDestroyEnchantment];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must still be an enchantment on the battlefield.
            if (resolved is not ICard card) return;
            if (!card.HasType(CardType.Enchantment)) return;
            if (card.Zone != ZoneType.Battlefield) return;

            // CR 701.7 — destroy. Indestructible (CR 702.12b) and regeneration
            // (CR 701.15) gates are handled by MoveToGraveyard(Destroy).
            OracleSpellBinder.MoveToGraveyard(card, ZoneMoveReason.Destroy);
        });

    // -----------------------------------------------------------------------
    // Mode 2: exile any number of target players' graveyards (v1: 1 player)
    // -----------------------------------------------------------------------

    private static IEffect BuildExileGraveyardEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Thraben Charm — exile target player's graveyard", () =>
        {
            if (p.Targets.Count <= ModeExileGraveyard) return;
            var slot = p.Targets[ModeExileGraveyard];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — target must be a Player.
            if (resolved is not Player targetPlayer) return;

            // Snapshot the graveyard before mutating it. CR 608.2b —
            // an empty graveyard is a clean no-op (the ability still
            // resolves, no cards move). Mirrors TormodsCryptFactory.
            var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
            foreach (var card in graveyardCards)
            {
                targetPlayer.Zones.Graveyard.RemoveCard(card);
                targetPlayer.Zones.Exile.AddCard(card);
                card.SetZone(ZoneType.Exile);
            }
        });
}
