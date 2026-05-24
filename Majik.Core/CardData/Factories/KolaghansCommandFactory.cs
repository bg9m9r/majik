using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kolaghan's Command (Dragons of Tarkir, {1}{B}{R}).
///
/// Instant. Oracle text:
///   "Choose two —
///     • Return target creature card from your graveyard to your hand.
///     • Kolaghan's Command deals 2 damage to any target.
///     • Target player discards a card.
///     • Destroy target artifact."
///
/// CR 700.2e — modal spells choose N distinct modes. This factory is the
/// same shape as <see cref="CrypticCommandFactory"/> — four modes, pick 2.
///
/// Targets are addressed by index into <see cref="ChosenSpellParams.Targets"/>:
///   Targets[0] — chosen creature card in a graveyard (mode 0), if mode 0 was picked.
///   Targets[1] — chosen any-target (mode 1 — 2 damage), if mode 1 was picked.
///   Targets[2] — chosen player (mode 2 — discard), if mode 2 was picked.
///   Targets[3] — chosen artifact (mode 3 — destroy), if mode 3 was picked.
///
/// v1 defaults to modes 0+1 chosen when no explicit mode selectors provided.
/// </summary>
[CardName("Kolaghan's Command")]
public static class KolaghansCommandFactory
{
    public const string CardName = "Kolaghan's Command";

    public const int ModeReturnCreature = 0;
    public const int ModeDealDamage     = 1;
    public const int ModeDiscard        = 2;
    public const int ModeDestroyArtifact = 3;

    /// <summary>
    /// Number of modes to pick on cast (CR 700.2e — "Choose two —").
    /// </summary>
    public const int PickCount = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 4;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{1}{B}{R}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// The printed mode labels, in oracle order.
    /// </summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Return target creature card from your graveyard to your hand.",
        "Kolaghan's Command deals 2 damage to any target.",
        "Target player discards a card.",
        "Destroy target artifact.",
    };

    /// <summary>
    /// Build the SpellDefinition for Kolaghan's Command.
    /// </summary>
    /// <param name="caster">The casting player.</param>
    /// <param name="targetResolver">Resolves targets at effect time.</param>
    /// <param name="allPlayers">All players (for multi-player discard mode).</param>
    /// <param name="stack">Not used by this spell — present for signature parity
    /// with <see cref="CrypticCommandFactory.BuildDefinition"/>.</param>
    /// <param name="chosenModes">Defaults to <c>new[]{0,1}</c> when null.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player>? allPlayers,
        Majik.Core.Stack.Stack? stack = null,
        IReadOnlyList<int>? chosenModes = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that takes
        // a target, regardless of whether that mode was chosen at declare
        // time. MinTargets=0 so unchosen modes' slots don't block casting.
        var targetRequests = new[]
        {
            // Mode 0 — creature card in any graveyard.
            new TargetRequest("target creature card in a graveyard", 0, 1, Array.Empty<object>(), BotIntent.Reanimate),
            // Mode 1 — any target (Player / Creature / Planeswalker).
            new TargetRequest("any target", 0, 1, Array.Empty<object>(), BotIntent.Removal),
            // Mode 2 — target player.
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Discard),
            // Mode 3 — target artifact.
            new TargetRequest("target artifact", 0, 1, Array.Empty<object>(), BotIntent.Removal),
        };

        var defaultModes = chosenModes ?? new[] { ModeReturnCreature, ModeDealDamage };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Reanimate,
                BotIntent.Removal,
                BotIntent.Discard,
                BotIntent.Removal,
            },
            EffectFactory: p =>
            {
                // Prefer ModeIndexes; fall back to legacy scalar ModeIndex;
                // finally fall back to defaultModes.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : defaultModes);

                var effects = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;     // CR 700.2e — no duplicates
                    if (seen.Count > PickCount) break; // honour printed pick count

                    switch (raw)
                    {
                        case ModeReturnCreature:
                            effects.Add(BuildReturnCreatureEffect(caster, p, targetResolver));
                            break;
                        case ModeDealDamage:
                            effects.Add(BuildDealDamageEffect(p, targetResolver));
                            break;
                        case ModeDiscard:
                            effects.Add(BuildDiscardEffect(p, targetResolver));
                            break;
                        case ModeDestroyArtifact:
                            effects.Add(BuildDestroyArtifactEffect(p, targetResolver));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    private static IEffect BuildReturnCreatureEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Kolaghan's Command — return creature card from graveyard to hand", () =>
        {
            // v1: use declared target if present; otherwise auto-pick first
            // creature card in caster's graveyard.
            ICard? card = null;

            if (p.Targets.Count > ModeReturnCreature)
            {
                var slot = p.Targets[ModeReturnCreature];
                if (slot.Count > 0)
                {
                    var resolved = resolver(slot[0]);
                    card = resolved as ICard;
                }
            }

            // Auto-pick fallback: first creature card in controller's graveyard.
            if (card == null)
            {
                card = caster.Zones.Graveyard.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault();
            }

            if (card == null) return;

            var owner = card.Owner ?? caster;
            owner.Zones.Graveyard.RemoveCard(card);
            owner.Zones.Hand.AddCard(card);
            card.SetZone(ZoneType.Hand);
        });

    private static IEffect BuildDealDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Kolaghan's Command — deal 2 damage to any target", () =>
        {
            if (p.Targets.Count <= ModeDealDamage) return;
            var slot = p.Targets[ModeDealDamage];
            if (slot.Count == 0) return;
            var target = resolver(slot[0]);
            SearingBlazeFactory.DealDamageWithPlaneswalker(target, 2);
        });

    private static IEffect BuildDiscardEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Kolaghan's Command — target player discards a card", () =>
        {
            Player? target = null;

            if (p.Targets.Count > ModeDiscard)
            {
                var slot = p.Targets[ModeDiscard];
                if (slot.Count > 0)
                {
                    target = resolver(slot[0]) as Player;
                }
            }

            // v1 fallback: first non-null player in AllPlayers (typically the opponent).
            if (target == null && p.AllPlayers is { Count: > 0 })
            {
                target = p.AllPlayers.FirstOrDefault();
            }

            if (target == null) return;

            // v1 deterministic first-card-in-hand pick (matches Liliana of the Veil).
            var pick = target.Zones.Hand.GetCards().FirstOrDefault();
            if (pick == null) return;
            target.Zones.Hand.RemoveCard(pick);
            target.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        });

    private static IEffect BuildDestroyArtifactEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Kolaghan's Command — destroy target artifact", () =>
        {
            if (p.Targets.Count <= ModeDestroyArtifact) return;
            var slot = p.Targets[ModeDestroyArtifact];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ICard card) return;
            // CR 701.7 — "destroy". Indestructible rider deferred.
            OracleSpellBinder.MoveToGraveyard(card);
        });
}
