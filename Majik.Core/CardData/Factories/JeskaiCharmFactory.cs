using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jeskai Charm (Khans of Tarkir, {U}{R}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Put target creature on top of its owner's library.
///     • Jeskai Charm deals 4 damage to target opponent or planeswalker.
///     • Creatures you control get +1/+1 and gain lifelink until end of turn."
///
/// CR 700.2d — modal "Choose one —" spell. The first two modes take a
/// target; the third is a team-wide static rider with no target. The bound
/// <see cref="SpellDefinition"/> exposes three <see cref="TargetRequest"/>s
/// (one per mode); only the chosen mode's slot is filled at cast time
/// (MinTargets=0 so unchosen modes don't gate the cast per CR 601.2c —
/// mirrors <see cref="BantCharmFactory"/> / <see cref="BorosCharmFactory"/>).
///
/// The card's base shape (name, single Instant type, {U}{R}{W}) is
/// materialised from the embedded JSON (<c>jeskai-charm.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The resolve-time behaviour
/// lives in <see cref="BuildDefinition"/> because a modal
/// <see cref="SpellDefinition"/> isn't expressible in the JSON schema.
///
/// Mode 0 — "Put target creature on top of its owner's library":
///   Re-checks the resolved target is still a battlefield
///   <see cref="Creature"/> (CR 608.2b), then moves it from the battlefield
///   to the TOP of its OWNER's library (CR 109.5 — "its owner's library",
///   not the controller's). Library index 0 is the top, so
///   <c>Library.InsertCardAt(0, …)</c> lands the card on top. Sibling of
///   <see cref="BantCharmFactory"/>'s bottom-of-library mode, differing only
///   in insertion index.
///
/// Mode 1 — "deals 4 damage to target opponent or planeswalker":
///   Uses <see cref="Fx.DealDamageAny"/> (same pattern as
///   <see cref="BorosCharmFactory"/>). The target must be a
///   <see cref="Player"/> other than the caster ("opponent", CR 102.1) or a
///   <see cref="Planeswalker"/>; other shapes (including the caster
///   themself) no-op per CR 608.2b.
///
/// Mode 2 — "Creatures you control get +1/+1 and gain lifelink until end of
///   turn": Enumerates the caster's battlefield creatures and, for each,
///   registers a <see cref="PumpUntilEndOfTurnEffect"/> (+1/+1, Layer 7c,
///   CR 613.7c) and a <see cref="GrantKeywordUntilEndOfTurnEffect"/> for
///   "Lifelink" (Layer 6, CR 702.15). Both expire at cleanup (CR 514.2).
///   Requires a live <see cref="ContinuousEffectsService"/>; when null the
///   mode performs no layer registration (shape-only path), same posture as
///   <see cref="BorosCharmFactory"/>'s static modes.
/// </summary>
[CardName("Jeskai Charm")]
public static class JeskaiCharmFactory
{
    public const string CardName = "Jeskai Charm";
    public const string Slug = "jeskai-charm";

    public const int ModeTopLibrary = 0;
    public const int ModeDamage      = 1;
    public const int ModePumpLifelink = 2;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    /// <summary>Damage dealt by mode 1.</summary>
    public const int DamageAmount = 4;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Put target creature on top of its owner's library.",
        $"Jeskai Charm deals {DamageAmount} damage to target opponent or planeswalker.",
        "Creatures you control get +1/+1 and gain lifelink until end of turn.",
    };

    /// <summary>Construct Jeskai Charm's base shape from the embedded JSON.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the modal "Choose one —" <see cref="SpellDefinition"/> for
    /// Jeskai Charm.
    /// </summary>
    /// <param name="caster">The player casting the spell. Used to scope the
    /// mode-2 team pump and the mode-1 "opponent" candidate filter.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// objects directly.</param>
    /// <param name="allPlayers">All players in the game (mode-1 candidate
    /// gathering).</param>
    /// <param name="continuousEffects">Optional per-turn continuous-effects
    /// service. Required for mode 2 (team +1/+1 + lifelink) to register the
    /// layer grants; when null mode 2 performs no layer registration.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IReadOnlyList<Player> allPlayers,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(allPlayers);

        // CR 601.2c — target requests for every mode that takes a target.
        // MinTargets=0 so unchosen modes don't gate the cast (mirrors
        // BantCharmFactory / BorosCharmFactory).
        var targetRequests = new[]
        {
            // Mode 0 — put target creature on top of its owner's library.
            new TargetRequest(
                Description: "target creature",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Bounce,
                CandidateGatherer: ctx => ctx.AllPlayers
                    .SelectMany(p => p.Zones.Battlefield.GetCards())
                    .Where(c => c.HasType(CardType.Creature))
                    .Cast<object>()
                    .ToList()),

            // Mode 1 — 4 damage to target opponent or planeswalker. Opponent
            // (CR 102.1) = a player other than the caster.
            new TargetRequest(
                Description: "target opponent or planeswalker",
                MinTargets: 0,
                MaxTargets: 1,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.Burn,
                CandidateGatherer: ctx =>
                {
                    var candidates = new List<object>();
                    foreach (var p in ctx.AllPlayers)
                    {
                        if (!ReferenceEquals(p, caster)) candidates.Add(p);
                        candidates.AddRange(p.Zones.Battlefield.GetCards()
                            .Where(c => c.HasType(CardType.Planeswalker))
                            .Cast<object>());
                    }
                    return candidates;
                }),

            // Mode 2 — no target (creatures you control).
            new TargetRequest(
                Description: "no target",
                MinTargets: 0,
                MaxTargets: 0,
                LegalCandidates: Array.Empty<object>(),
                Intent: BotIntent.CombatTrick),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Bounce,
                BotIntent.Burn,
                BotIntent.CombatTrick,
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
                        case ModeTopLibrary:
                            effectsOut.Add(BuildTopLibraryEffect(p, targetResolver));
                            break;
                        case ModeDamage:
                            effectsOut.Add(BuildDamageEffect(p, targetResolver, caster));
                            break;
                        case ModePumpLifelink:
                            effectsOut.Add(BuildPumpLifelinkEffect(caster, continuousEffects));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: put target creature on top of its owner's library
    // -----------------------------------------------------------------------

    private static IEffect BuildTopLibraryEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — put target creature on top of its owner's library", () =>
        {
            if (p.Targets.Count <= ModeTopLibrary) return;
            var slot = p.Targets[ModeTopLibrary];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — resolution-time legality re-check.
            if (resolved is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;

            // CR 109.5 — "its owner's library" (NOT the controller's). The
            // creature leaves the battlefield and is placed on TOP of its
            // owner's library. Library index 0 is the top, so InsertCardAt(0)
            // lands it on top — sibling of BantCharmFactory's bottom mode.
            var owner = target.Owner;
            if (owner == null) return;
            owner.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Library.InsertCardAt(0, target);
            target.SetZone(ZoneType.Library);
        });

    // -----------------------------------------------------------------------
    // Mode 1: 4 damage to target opponent or planeswalker
    // -----------------------------------------------------------------------

    private static IEffect BuildDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Player caster) =>
        new Effect($"{CardName} — deals {DamageAmount} damage to target opponent or planeswalker", () =>
        {
            if (p.Targets.Count <= ModeDamage) return;
            var slot = p.Targets[ModeDamage];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);

            // CR 608.2b — only an opponent (a Player other than the caster,
            // CR 102.1) or a Planeswalker is a legal target; other shapes —
            // including the caster themself — are a no-op.
            switch (resolved)
            {
                case Planeswalker:
                    Fx.DealDamageAny(resolved, DamageAmount);
                    break;
                case Player player when !ReferenceEquals(player, caster):
                    Fx.DealDamageAny(resolved, DamageAmount);
                    break;
            }
        });

    // -----------------------------------------------------------------------
    // Mode 2: creatures you control get +1/+1 and gain lifelink until EOT
    // -----------------------------------------------------------------------

    private static IEffect BuildPumpLifelinkEffect(
        Player caster,
        ContinuousEffectsService? continuousEffects) =>
        new Effect($"{CardName} — creatures you control get +1/+1 and gain lifelink until end of turn", () =>
        {
            if (continuousEffects == null) return;

            // CR 613.7c (Layer 7c +1/+1) + CR 702.15 / 613.1f (Layer 6
            // lifelink grant) until end of turn (CR 514.2) to every creature
            // the caster controls on the battlefield.
            foreach (var creature in caster.Zones.Battlefield
                .GetCards()
                .OfType<Creature>()
                .ToList())
            {
                continuousEffects.Register(new PumpUntilEndOfTurnEffect(creature, 1, 1));
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, "Lifelink"));
            }
        });
}
