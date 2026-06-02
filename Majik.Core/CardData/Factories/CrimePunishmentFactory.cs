using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED split card Crime // Punishment
/// (Guildpact, {3}{W}{B} // {X}{B}{G}). Both faces are Sorceries.
///
/// ## Card text (Scryfall verified 2026-06-02)
///   Crime {3}{W}{B} — Sorcery: "Put target creature or enchantment card
///     from an opponent's graveyard onto the battlefield under your control."
///   Punishment {X}{B}{G} — Sorcery: "Destroy each artifact, creature, and
///     enchantment with mana value X."
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one
/// face to cast and only that face's mana cost / effect applies (CR 712.4a).
/// Neither face is a permanent — both halves are Sorceries here, so each
/// resolves as a one-shot effect that then heads to the graveyard.
///
/// The combined card name "Crime // Punishment" is the <c>[CardName]</c>
/// dispatch key (matching the embedded seed row), mirroring the two-face
/// posture of <see cref="WearTearFactory"/> / <see cref="BoomBustFactory"/>.
/// The card SHAPE is materialised from the embedded JSON definition
/// (<c>crime-punishment.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// behaviour is built on demand here.
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Sorcery, white+black (Crime's {3}{W}{B} pips), combined
///   card name. The combined card carries the front (Crime) face's {3}{W}{B}
///   cost — the engine's split-cast plumbing selects the per-face cost when
///   each face is cast; the printed front cost is the natural default for the
///   single combined object (same posture as <see cref="WearTearFactory"/>
///   carrying the Wear cost).
/// - <b>Crime face</b> (<see cref="BuildCrimeDefinition"/>) — single 1..1
///   <see cref="TargetRequest"/> over creature / enchantment CARDS in an
///   OPPONENT's graveyard (CR 700.6 — "from an opponent's graveyard"). On
///   resolve the chosen card is reanimated to the caster's battlefield under
///   the caster's control via
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (CR 701.20; ETB
///   triggers fire when a ZoneService is supplied, CR 603.6a). Cribs the
///   reanimate-under-control shape from <see cref="ReanimateFactory"/> /
///   <see cref="AnimateDeadFactory"/> (no life-loss rider — Crime has none).
///   CR 608.2b illegal-target re-check at resolution: a target that is no
///   longer a creature/enchantment card in an opponent's graveyard is a
///   no-op.
/// - <b>Punishment face</b> (<see cref="BuildPunishmentResolveEffect"/>) —
///   destroy each artifact, creature, AND enchantment whose mana value
///   EXACTLY equals X (CR 202.3b; printed "with mana value X", not "X or
///   less" — contrast <see cref="MeltdownFactory"/>'s "≤ X"). Each victim is
///   routed to its owner's graveyard via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> so Indestructible (CR 702.12) /
///   regeneration (CR 701.15) are honoured. Same multi-player-iteration
///   shape as <see cref="MeltdownFactory.BuildResolveEffect"/> with a wider
///   type predicate and an equality (==) mv compare.
///
/// ## Deferred (v1 gaps — shared with the rest of the split-card family)
/// - <b>Real split-cast face choice</b> (CR 712.3) — casting either named
///   half as its own spell through the cast UI. The engine has no per-face
///   split-cast surface yet, so the combined object exposes the front (Crime)
///   {3}{W}{B} cost and each half's resolve behaviour is built on demand via
///   the <c>Build*</c> helpers here (same gap as Wear // Tear / Boom // Bust).
/// - <b>Per-cast X ledger</b> — callers pass the resolved X for the
///   Punishment face directly (same v1 simplification as
///   <see cref="MeltdownFactory"/> / Pernicious Deed).
/// </summary>
[CardName("Crime // Punishment")]
public static class CrimePunishmentFactory
{
    public const string CardName = "Crime // Punishment";
    public const string Slug = "crime-punishment";

    /// <summary>CR 712 — Crime (front face) printed cost.</summary>
    public const string CrimeManaCost = "{3}{W}{B}";

    /// <summary>CR 712 — Punishment (back face) printed cost.</summary>
    public const string PunishmentManaCost = "{X}{B}{G}";

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Sorcery, white+black, combined name "Crime // Punishment"). This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to; per-face
    /// resolve behaviour is built on demand via
    /// <see cref="BuildCrimeDefinition"/> / <see cref="BuildPunishmentResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    // ── Crime face — reanimate from an opponent's graveyard ──────────────────

    /// <summary>
    /// Build the resolve-time definition for the Crime face: "Put target
    /// creature or enchantment card from an opponent's graveyard onto the
    /// battlefield under your control."
    ///
    /// Candidate gatherer enumerates creature / enchantment CARDS in the
    /// graveyards of every player who is NOT the caster (CR 700.6 — "an
    /// opponent's graveyard"). The agent picks one; an empty candidate list
    /// makes the spell illegal to cast (CR 601.2c).
    ///
    /// On resolve the chosen card is reanimated to the caster's battlefield
    /// under the caster's control (CR 701.20). CR 608.2b — illegal-target
    /// re-check: a target no longer in an opponent's graveyard, or no longer a
    /// creature / enchantment card, is a no-op.
    /// </summary>
    /// <param name="caster">Crime's controller; reanimated card enters under
    /// the caster's control, and the caster's own graveyard is excluded
    /// ("an opponent's graveyard").</param>
    /// <param name="allPlayers">All players in the game; their graveyards are
    /// scanned for opponent-owned creature / enchantment cards.</param>
    /// <param name="targetResolver">Resolves the raw chosen-target token to
    /// the live card.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire on the reanimated permanent (CR 603.6a).</param>
    public static SpellDefinition BuildCrimeDefinition(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 700.6 — only an OPPONENT's graveyard (not the caster's).
        var candidates = allPlayers
            .Where(p => p != null && !ReferenceEquals(p, caster))
            .SelectMany(p => p.Zones.Graveyard.GetCards())
            .Where(IsCreatureOrEnchantmentCard)
            .Cast<object>()
            .ToList();

        var request = new TargetRequest(
            Description: "target creature or enchantment card from an opponent's graveyard",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: candidates,
            Intent: BotIntent.Reanimate);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { request },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    // CR 608.2b — no legal target on resolution → do nothing.
                    return Array.Empty<IEffect>();
                }

                var resolved = targetResolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} (Crime) — reanimate target creature/enchantment card from an opponent's graveyard under your control",
                        () => ResolveCrime(resolved, caster, allPlayers, zoneService)),
                };
            });
    }

    /// <summary>
    /// Crime resolve helper. CR 608.2b illegal-target re-check: the chosen
    /// card must still be a creature / enchantment card in a non-caster
    /// player's graveyard. If so, reanimate it onto the caster's battlefield
    /// under the caster's control (CR 701.20).
    /// </summary>
    private static void ResolveCrime(
        object resolved,
        Player caster,
        IReadOnlyList<Player> allPlayers,
        ZoneService? zoneService)
    {
        if (resolved is not ICard card) return;
        if (!IsCreatureOrEnchantmentCard(card)) return;

        // CR 608.2b — must still be in an OPPONENT's graveyard.
        var owner = allPlayers.FirstOrDefault(p =>
            p != null
            && !ReferenceEquals(p, caster)
            && p.Zones.Graveyard.GetCards().Contains(card));
        if (owner == null) return;

        // CR 701.20 — graveyard → caster's battlefield, under caster's control.
        Fx.ReturnFromGraveyardToBattlefield(card, caster, zoneService);
    }

    /// <summary>True when <paramref name="card"/> is a creature card OR an
    /// enchantment card — the two card types Crime can reanimate.</summary>
    private static bool IsCreatureOrEnchantmentCard(ICard card)
        => card.HasType(CardType.Creature) || card.HasType(CardType.Enchantment);

    // ── Punishment face — X-gated mass destroy (mv == X) ─────────────────────

    /// <summary>
    /// Build the resolve-time effect for the Punishment face: "Destroy each
    /// artifact, creature, and enchantment with mana value X."
    ///
    /// Iterates every supplied player's battlefield and destroys each
    /// permanent that is an artifact, creature, or enchantment AND whose mana
    /// value EXACTLY equals <paramref name="x"/> (CR 202.3b; printed "with
    /// mana value X" — an equality compare, NOT the "≤ X" of
    /// <see cref="MeltdownFactory"/>). Each victim is routed to its owner's
    /// graveyard via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> so Indestructible (CR 702.12) /
    /// regeneration (CR 701.15) are honoured. A
    /// <see cref="HashSet{Card}"/> de-dupes the victim pile (a single card
    /// that is e.g. both artifact and creature is destroyed once).
    /// </summary>
    /// <param name="caster">The Punishment spell's controller. Reserved for
    /// parity with the destroy family; the printed effect has no
    /// controller-only scoping.</param>
    /// <param name="allPlayers">All players whose battlefields are swept.
    /// Typically <c>Game.Players</c>.</param>
    /// <param name="x">The resolved X value for this cast. Permanents whose
    /// <c>ManaCostValue.TotalValue == x</c> are destroyed.</param>
    public static IReadOnlyList<IEffect> BuildPunishmentResolveEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        int x)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName} (Punishment): destroy each artifact, creature, and enchantment with mana value {x}.",
                () =>
                {
                    // Snapshot every battlefield up front — MoveToGraveyard
                    // mutates the source zone in place. HashSet dedupe so a
                    // card matching multiple type clauses (e.g. artifact
                    // creature) is destroyed exactly once.
                    var victims = new HashSet<Card>(ReferenceEqualityComparer.Instance);
                    foreach (var pl in allPlayers)
                    {
                        if (pl == null) continue;
                        foreach (var c in pl.Zones.Battlefield.GetCards()
                                            .OfType<Card>()
                                            .Where(IsArtifactCreatureOrEnchantment)
                                            // CR 202.3b — mana value EXACTLY X.
                                            .Where(c => c.ManaCostValue.TotalValue == x)
                                            .ToList())
                        {
                            victims.Add(c);
                        }
                    }

                    foreach (var v in victims)
                    {
                        // CR 701.7 — destroy. Indestructible (CR 702.12) /
                        // regeneration (CR 701.15) honoured via the
                        // Destroy-reason gate.
                        OracleSpellBinder.MoveToGraveyard(v, ZoneMoveReason.Destroy);
                    }
                }),
        };
    }

    private static bool IsArtifactCreatureOrEnchantment(Card card)
        => card.HasType(CardType.Artifact)
           || card.HasType(CardType.Creature)
           || card.HasType(CardType.Enchantment);
}
