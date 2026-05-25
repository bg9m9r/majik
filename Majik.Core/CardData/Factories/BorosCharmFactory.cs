using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boros Charm (Gatecrash, {R}{W}).
///
/// Instant. Oracle text:
///   "Choose one —
///     • Boros Charm deals 4 damage to target player.
///     • Permanents you control gain indestructible until end of turn.
///     • Target creature gets +1/+0 and gains double strike until end of turn."
///
/// CR 700.2d — choose-one modal spell. Same shape as
/// <see cref="IzzetCharmFactory"/> — three modes, pick exactly 1.
///
/// Targets are addressed by index into <see cref="ChosenSpellParams.Targets"/>:
///   Targets[0] — chosen player (mode 0 — 4 damage), if mode 0 picked.
///   Targets[1] — unused (mode 1 — mass indestructible; non-targeted).
///   Targets[2] — chosen creature (mode 2 — pump + double strike), if mode 2 picked.
///
/// Default mode when none provided: 0 (the BR-aggro burn finisher line).
///
/// ## "Permanents you control gain indestructible until end of turn"
/// CR 702.12 — Indestructible. v1 wires this as one
/// <see cref="GrantAbilityEffect"/> per controlled permanent, with each
/// effect's <see cref="GrantAbilityEffect.Source"/> set to the permanent
/// itself + <c>expiresAtEndOfTurn = true</c>. After registration we call
/// <see cref="GrantAbilityEffect.Sync"/> directly so the
/// <see cref="KeywordAbility"/>("Indestructible") marker is attached to
/// the bearer's <see cref="Card.Abilities"/> list immediately — without
/// waiting for a layer-compute pass to trigger the auto-sync via
/// <c>ContinuousEffectsService.SyncAbilityGrants</c>. At cleanup the
/// service's <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> hook
/// revokes every EOT grant (CR 514.2).
///
/// Why per-permanent self-source: Boros Charm is an Instant, not a
/// permanent — there's no on-battlefield source to pin the grants to. By
/// making each permanent its own grant source, the grant's
/// <see cref="GrantAbilityEffect.IsActive"/> check
/// (<c>source.Zone == Battlefield</c>) auto-suspends when the bearer
/// LTB's (e.g. someone bounces a granted permanent — the grant becomes
/// inactive instead of dangling).
/// </summary>
[CardName("Boros Charm")]
public static class BorosCharmFactory
{
    public const string CardName = "Boros Charm";
    public const string PrintedManaCost = "{R}{W}";

    public const int ModeDealDamage     = 0;
    public const int ModeIndestructible = 1;
    public const int ModePumpDoubleStrike = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 3;

    public const int DamageAmount = 4;
    public const int PumpPower = 1;
    public const int PumpToughness = 0;

    /// <summary>Granted keyword for mode 2 — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>Granted keyword for mode 1 — CR 702.12 Indestructible.</summary>
    public const string GrantedIndestructible = "Indestructible";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Boros Charm deals 4 damage to target player.",
        "Permanents you control gain indestructible until end of turn.",
        "Target creature gets +1/+0 and gains double strike until end of turn.",
    };

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Boros Charm is cast.
    /// The caller resolves targets through <paramref name="targetResolver"/>
    /// and supplies the optional <see cref="ContinuousEffectsService"/> used
    /// by mode 1's mass-indestructible grant (CR 702.12) and mode 2's
    /// double-strike grant (CR 702.4).
    /// </summary>
    /// <param name="caster">The casting player — read for mode 1's
    /// "permanents you control" scoping.</param>
    /// <param name="targetResolver">Chosen-target resolver (typically a
    /// <c>StackResolver</c>).</param>
    /// <param name="continuousEffects">Layer-system service. When supplied,
    /// mode 1 registers one EOT-expirable <see cref="GrantAbilityEffect"/>
    /// per controlled permanent; mode 2 registers a
    /// <see cref="GrantKeywordUntilEndOfTurnEffect"/> for Double strike on
    /// the targeted creature via <see cref="Creature.ActiveEffects"/>
    /// (also reads from the same service — see <see cref="Creature.ActiveEffects"/>).
    /// Null = those grants silently no-op (shape-only path).</param>
    /// <param name="chosenMode">Override the picked mode. Defaults to
    /// <see cref="ModeDealDamage"/> (the BR-aggro burn line) when null.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ContinuousEffectsService? continuousEffects = null,
        int? chosenMode = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests for every targeted mode, regardless of
        // pick at declare time. MinTargets=0 so unchosen modes don't block.
        var targetRequests = new[]
        {
            // Mode 0 — target player.
            new TargetRequest("target player", 0, 1, Array.Empty<object>(), BotIntent.Burn),
            // Mode 2 — target creature.
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.CombatTrick),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Burn,        // mode 0 — 4 damage to player
                BotIntent.Protection,  // mode 1 — mass indestructible (combat protection / wrath shield)
                BotIntent.CombatTrick, // mode 2 — pump + double strike
            },
            EffectFactory: p =>
            {
                // Boros Charm is choose-one. Honour ModeIndexes[0] when
                // supplied, else legacy scalar ModeIndex, else default.
                int pick;
                if (p.ModeIndexes is { Count: > 0 } list)
                {
                    pick = list[0];
                }
                else if (p.ModeIndex.HasValue)
                {
                    pick = p.ModeIndex.Value;
                }
                else
                {
                    pick = chosenMode ?? ModeDealDamage;
                }

                if (pick < 0 || pick >= TotalModes) return Array.Empty<IEffect>();

                return pick switch
                {
                    ModeDealDamage       => new IEffect[] { BuildDealDamageEffect(p, targetResolver) },
                    ModeIndestructible   => new IEffect[] { BuildIndestructibleEffect(caster, continuousEffects) },
                    ModePumpDoubleStrike => new IEffect[] { BuildPumpDoubleStrikeEffect(p, targetResolver) },
                    _ => Array.Empty<IEffect>(),
                };
            });
    }

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    private static IEffect BuildDealDamageEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName}: deal {DamageAmount} damage to target player", () =>
        {
            // Targets layout: index 0 = mode-0's player slot.
            if (p.Targets.Count == 0) return;
            var slot = p.Targets[0];
            if (slot.Count == 0) return;
            var target = resolver(slot[0]);
            if (target is not Player) return;
            SearingBlazeFactory.DealDamageWithPlaneswalker(target, DamageAmount);
        });

    private static IEffect BuildIndestructibleEffect(
        Player caster,
        ContinuousEffectsService? continuousEffects) =>
        new Effect(
            $"{CardName}: permanents you control gain indestructible until end of turn",
            () =>
            {
                if (continuousEffects == null) return;

                // CR 702.12 — Indestructible. Snapshot the bearer set at
                // resolve time (CR 609.2 — modal effect resolves against
                // the battlefield as it is when the spell resolves). One
                // EOT-expirable GrantAbilityEffect per permanent.
                //
                // ToList() to avoid mutation surprises if some
                // KeywordAbility.AddAbility call indirectly perturbs the
                // battlefield zone enumeration.
                var bearers = caster.Zones.Battlefield.GetCards()
                    .OfType<Permanent>()
                    .ToList();

                foreach (var bearer in bearers)
                {
                    // KeywordAbility marker — both the SBA path
                    // (CombatAbilities.HasIndestructible for creatures via
                    // the ContinuousEffectsService keyword-seed) and the
                    // destroy-effect gate (OracleSpellBinder.HasIndestructible
                    // for non-creatures via the printed-KeywordAbility
                    // fallback) read this same shape.
                    var keyword = new KeywordAbility(
                        GrantedIndestructible,
                        bearer,
                        bearer.Controller ?? caster);

                    var grant = new GrantAbilityEffect(
                        source: bearer,
                        target: bearer,
                        ability: keyword,
                        expiresAtEndOfTurn: true);

                    continuousEffects.Register(grant);

                    // Sync now — the layer system's auto-sync runs lazily
                    // on the next ContinuousEffectsService.Compute call,
                    // but the OracleSpellBinder destroy-effect gate reads
                    // KeywordAbility directly off the bearer's Abilities
                    // list (it never calls Compute for non-creatures). The
                    // explicit Sync attaches the marker eagerly so the next
                    // Wrath / removal spell in the same resolution sees it.
                    grant.Sync();
                }
            });

    private static IEffect BuildPumpDoubleStrikeEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect(
            $"{CardName}: target creature gets +{PumpPower}/+{PumpToughness} and gains double strike until end of turn",
            () =>
            {
                // Targets layout: index 1 = mode-2's creature slot (the
                // mode-0 player slot at index 0 is empty when mode 2 is
                // picked, since SpellCastFlow emits both target requests).
                if (p.Targets.Count <= 1) return;
                var slot = p.Targets[1];
                if (slot.Count == 0) return;
                var target = resolver(slot[0]);
                if (target is not Creature creature) return;
                if (creature.ActiveEffects == null) return;

                // CR 613.1c Layer 7c — +1/+0 EOT pump.
                creature.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(creature, PumpPower, PumpToughness));
                // CR 613.1c Layer 6 — Double strike keyword grant EOT.
                creature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, GrantedDoubleStrike));
            });
}
