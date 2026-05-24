using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ashiok, Dream Render (War of the Spark, {1}{U/B}{U/B}).
///
/// Legendary Planeswalker — Ashiok, starting loyalty 5.
/// Oracle text:
///   "-1: Target opponent mills four cards. Exile each card put into a
///         graveyard from anywhere this turn.
///    Players can't search libraries. (Players can still cast cards
///    requiring them to search their libraries. They just won't find
///    anything.)"
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 5, Ashiok subtype, mana cost
///   {1}{U/B}{U/B}.
/// - <b>-1</b>: target-opponent-mills-4 (CR 701.13b — via
///   <see cref="MillAction.Apply"/>) + registers an EOT-expirable
///   <see cref="GraveyardToExileReplacement"/> on the supplied
///   <see cref="ReplacementBus"/> (CR 614). The replacement rewrites
///   <em>every</em> <see cref="ZoneMoveIntent"/> whose destination is
///   <see cref="ZoneType.Graveyard"/> to <see cref="ZoneType.Exile"/> — no
///   "this way" scoping, no controller restriction; the printed rider
///   applies to cards put into a graveyard <em>from anywhere</em>, for the
///   remainder of the turn (CR 514.2 cleanup).
/// - <b>Static "Players can't search libraries"</b>: structural marker via
///   <see cref="AshiokSearchRestrictionEffect"/> registered on the supplied
///   <see cref="ContinuousEffectsService"/> while Ashiok is on the
///   battlefield. Unlike Leonin Arbiter's restriction this is unconditional
///   — no pay-to-bypass clause (CR 701.19 — players still <em>perform</em>
///   the search procedurally, but the "find" step is gated to zero hits).
///   Enforcement at the actual library-search sites is DEFERRED (same gap
///   as <see cref="LeoninArbiterSearchRestrictionEffect"/>): the engine
///   currently lacks a unified library-search surface that enforcement
///   could hook. When that surface lands, enforcement should query the
///   continuous-effects service for any active
///   <see cref="AshiokSearchRestrictionEffect"/> and short-circuit the
///   find step to an empty pick (the shuffle still occurs per the printed
///   reminder text).
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompts</b>: -1 picks the first opponent in the supplied
///   <paramref name="allPlayersResolver"/> rather than via the agent
///   (same shape as Liliana/Karn). With no resolver wired the mill body
///   no-ops and the EOT replacement is still registered.
/// - <b>"Anywhere → graveyard" coverage</b>: replacement gates on
///   destination = Graveyard only; source zone is unrestricted, matching
///   the printed text. Coverage is bounded by which call sites currently
///   route through <see cref="ZoneService.MoveCardTo"/> and consult the
///   <see cref="ReplacementBus"/> — every direct-zone-mutation path that
///   bypasses the bus also bypasses this rider, same gap as Anger of the
///   Gods / Containment Priest.
/// - <b>Library-search enforcement</b>: see static effect note above.
/// </summary>
[CardName("Ashiok, Dream Render")]
public static class AshiokDreamRenderFactory
{
    public const string CardName = "Ashiok, Dream Render";
    public const string PrintedManaCost = "{1}{U/B}{U/B}";
    public const int StartingLoyalty = 5;
    public const int MillCount = 4;

    /// <summary>
    /// Construct Ashiok, Dream Render with no resolvers / bus / continuous-
    /// effects service. Suitable for shape / dispatcher tests — the -1's
    /// mill + exile-rider body and the static search-restriction effect are
    /// silently skipped; loyalty change still applies (CR 606.3).
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, allPlayersResolver: null, replacements: null, continuousEffects: null);

    /// <summary>
    /// Construct Ashiok, Dream Render.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list at
    /// activation time. v1 picks the first non-owner as the -1 mill
    /// target. May be null — the mill body no-ops while the loyalty
    /// change still applies (CR 606.3).</param>
    /// <param name="replacements">Bus to register the EOT-expirable
    /// graveyard→exile replacement on at -1 resolution. May be null —
    /// the rider half is skipped.</param>
    /// <param name="continuousEffects">Service to register the printed
    /// static "Players can't search libraries" effect on. May be null —
    /// no static-effect lifecycle wiring is attached (suitable for
    /// shape-only / dispatcher tests).</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        ReplacementBus? replacements,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var ashiok = new Planeswalker(
            name: CardName,
            manaCost: PrintedManaCost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Ashiok });

        ashiok.SetOwner(owner);
        ashiok.SetController(owner);

        // -- -1: Target opponent mills four cards. Exile each card put into
        //        a graveyard from anywhere this turn. -----------------------
        // v1 auto-pick: the first opponent in the player list mills 4
        // (CR 701.13b). The EOT exile-rider is registered on the supplied
        // ReplacementBus regardless of whether a mill target was found
        // (matches the printed "Exile each card put into a graveyard from
        // anywhere this turn" — the rider is unconditional on the mill
        // succeeding).
        ashiok.AddAbility(new LoyaltyAbility(ashiok, -1, () =>
        {
            // ---------------- Mill half (CR 701.13b) ----------------------
            var players = allPlayersResolver?.Invoke();
            if (players != null)
            {
                foreach (var p in players)
                {
                    if (ReferenceEquals(p, owner)) continue;
                    MillAction.Apply(p, MillCount);
                    break; // CR 700.6 — "target opponent" is one player
                }
            }

            // ---------------- Exile rider (CR 614) ------------------------
            // Unconditional graveyard-bound rewrite for the rest of the
            // turn; EOT-expirable so ReplacementBus.ExpireEndOfTurn drops
            // it during cleanup (CR 514.2).
            if (replacements != null)
            {
                replacements.Register<ZoneMoveIntent>(
                    new GraveyardToExileReplacement());
            }
        }));

        // -- Static: "Players can't search libraries." --------------------
        // Structural marker on the ContinuousEffectsService — enforcement
        // at library-search sites is deferred (see xmldoc note above and
        // LeoninArbiterSearchRestrictionEffect's parallel gap).
        if (continuousEffects != null)
        {
            var staticEffect = new AshiokSearchRestrictionEffect(
                ashiok, continuousEffects);
            staticEffect.Attach();
        }

        return ashiok;
    }
}

/// <summary>
/// Replacement effect for Ashiok, Dream Render's -1 rider: every
/// <see cref="ZoneMoveIntent"/> whose destination is
/// <see cref="ZoneType.Graveyard"/> (from <em>anywhere</em>) is rewritten
/// to <see cref="ZoneType.Exile"/>. No source-zone or controller scoping —
/// matches the printed "Exile each card put into a graveyard from anywhere
/// this turn." EOT-expirable per CR 514.2.
/// </summary>
public sealed class GraveyardToExileReplacement
    : IReplacementEffect<ZoneMoveIntent>, IEndOfTurnExpirable
{
    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent.ToZone == ZoneType.Graveyard;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}

/// <summary>
/// Marker lifecycle binder for Ashiok, Dream Render's printed static
/// "Players can't search libraries" effect.
///
/// Registered on the <see cref="ContinuousEffectsService"/> as a sentinel
/// <see cref="ContinuousEffect"/> while Ashiok is on the battlefield. The
/// <em>enforcement</em> of the search restriction is DEFERRED — the engine
/// presently lacks a unified library-search surface to hook (same gap as
/// <see cref="LeoninArbiterSearchRestrictionEffect"/>). When that surface
/// lands, enforcement should query the service for any active
/// <see cref="AshiokSearchRestrictionEffect"/> and short-circuit the find
/// step to an empty pick — distinct from Leonin Arbiter, this restriction
/// is unconditional (no pay-to-bypass clause).
/// </summary>
public sealed class AshiokSearchRestrictionEffect : ContinuousEffect
{
    private readonly ICard _source;
    private readonly ContinuousEffectsService _effects;
    private bool _attached;
    private bool _currentlyActive;

    public AshiokSearchRestrictionEffect(
        ICard source,
        ContinuousEffectsService effects)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    }

    /// <summary>
    /// Register the restriction if Ashiok is already on the battlefield at
    /// attach time. Idempotent.
    /// </summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        SyncRegistration();
    }

    /// <summary>Unregister the restriction. Idempotent.</summary>
    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        if (_currentlyActive)
        {
            _effects.Unregister(this);
            _currentlyActive = false;
        }
    }

    // CR 613 layer assignment: same posture as
    // LeoninArbiterSearchRestrictionEffect — this is a cross-permanent
    // structural marker that never mutates layer characteristics. Layer 7c
    // chosen as a convenient marker slot since AppliesTo always returns
    // false.
    public override Layer Layer => Layer.PT_Modify;
    public override bool AppliesTo(Creature creature) => false;
    public override void Apply(CreatureCharacteristics chars) { }

    /// <summary>
    /// True while the restriction is registered (Ashiok on the
    /// battlefield). Enforcement code should check this before allowing
    /// any library search to "find" anything.
    /// </summary>
    public bool IsRestrictionActive => _currentlyActive;

    public override bool IsActive() => true;

    /// <summary>
    /// Re-sync registration to current zone state. Call this from card-
    /// move event handlers when the host wires them.
    /// </summary>
    public void Sync() => SyncRegistration();

    private void SyncRegistration()
    {
        var shouldBeActive = _source.Zone == ZoneType.Battlefield;

        if (shouldBeActive && !_currentlyActive)
        {
            _effects.Register(this);
            _currentlyActive = true;
        }
        else if (!shouldBeActive && _currentlyActive)
        {
            _effects.Unregister(this);
            _currentlyActive = false;
        }
    }
}
