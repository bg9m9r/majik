using Majik.Core.Abilities;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Cards;

/// <summary>
/// Base card implementation.
/// Encapsulates card state and uses value objects.
/// </summary>
public class Card : ICard
{
    private ZoneType _zone;
    private Player? _controller;
    private readonly List<CardType> _cardTypes = new();
    private readonly List<CardSupertype> _supertypes = new();
    private readonly List<CardSubtype> _subtypes = new();
    private readonly List<IAbility> _abilities = new();
    private readonly List<ZoneType> _restrictedCastZones = new();

    // PLAN 08 — per-game deterministic id (portal's `cardId`). Reseeded from
    // the ambient DeterministicIdSource when a game scope is installed; falls
    // back to Guid.NewGuid() for scope-less direct construction (unit tests).
    public Guid InstanceId { get; } = Majik.Core.Game.DeterministicIdScope.NewId();
    public string Name { get; }
    public string ManaCost { get; }

    /// <summary>
    /// See <see cref="ICard.IsVanillaShell"/>. Stamped by
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.Create"/> after
    /// the binder chain runs, when the card carries printed oracle text
    /// but the engine produced no abilities to enforce it. Mutated through
    /// <see cref="MarkAsVanillaShell"/> so the factory has a public seam
    /// without exposing the setter for general use.
    /// </summary>
    public bool IsVanillaShell { get; private set; }

    /// <summary>
    /// Flip this card to the vanilla-shell state. Called by
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.Create"/> once it
    /// has run the full binder chain and observed that no abilities were
    /// attached for a card whose oracle text demanded them. Idempotent;
    /// re-flipping is a no-op.
    /// </summary>
    public void MarkAsVanillaShell()
    {
        IsVanillaShell = true;
    }
    
    /// <summary>
    /// The mana cost as a value object.
    /// </summary>
    public ValueObjects.ManaCost ManaCostValue { get; }

    /// <summary>
    /// The card types (cards can have multiple types).
    /// </summary>
    public IReadOnlyList<CardType> CardTypes => _cardTypes.AsReadOnly();

    /// <summary>
    /// The card supertypes.
    /// </summary>
    public IReadOnlyList<CardSupertype> Supertypes => _supertypes.AsReadOnly();

    /// <summary>
    /// The card subtypes.
    /// </summary>
    public IReadOnlyList<CardSubtype> Subtypes => _subtypes.AsReadOnly();
    
    public Player? Owner { get; internal set; }

    public Player? Controller
    {
        get => _controller;
        internal set
        {
            _controller = value;
            // CR 613 — see ChangeController: control-scoped effects re-evaluate.
            if (this is Permanent p) p.ActiveEffects?.BumpGeneration();
        }
    }

    public ZoneType Zone
    {
        get => _zone;
        internal set
        {
            _zone = value;
            // CR 613 — a permanent's battlefield presence is an input to the
            // layer system: lords/anthems/CDAs scoped to "permanents you
            // control" change as ANY permanent enters/leaves play, and an
            // effect's own IsActive() battlefield gate keys off its source's
            // zone. Because the continuous-effects service is shared across
            // every permanent in a game, bumping its generation here
            // invalidates the whole memoization cache on any zone change.
            if (this is Permanent p) p.ActiveEffects?.BumpGeneration();
        }
    }

    /// <summary>
    /// CR 110.2 — change the controller of this card. Public seam for
    /// external code (effects, commands) instead of touching the setter.
    /// </summary>
    public void ChangeController(Player? newController)
    {
        _controller = newController;
        // CR 613 — control-scoped lords/anthems ("creatures you control")
        // re-evaluate when control changes; invalidate the shared cache.
        if (this is Permanent p) p.ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// Set the owner of this card. Typically called once at card creation
    /// (CR 108.3); ownership only changes via specific effects.
    /// </summary>
    public void ChangeOwner(Player? newOwner)
    {
        Owner = newOwner;
    }

    /// <summary>
    /// CR 702.33 — runtime flashback grant ("target … gains flashback until
    /// end of turn. The flashback cost is equal to its mana cost.").
    /// When non-null, the card may be cast from the graveyard via a
    /// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/> built from
    /// this mana cost, in addition to any printed flashback. Cleared at
    /// end of turn by the granting effect's bus subscription.
    /// </summary>
    public ValueObjects.ManaCost? RuntimeFlashbackCost { get; private set; }

    /// <summary>
    /// Stamp a flashback grant on this card. Used by Snapcaster Mage and
    /// any future "until end of turn, gains flashback" effect. Idempotent —
    /// later grants overwrite earlier ones; clearing happens at EOT via the
    /// granting effect's own bookkeeping.
    /// </summary>
    public void GrantRuntimeFlashback(ValueObjects.ManaCost cost)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        RuntimeFlashbackCost = cost;
    }

    /// <summary>Clear any runtime flashback grant on this card.</summary>
    public void ClearRuntimeFlashback()
    {
        RuntimeFlashbackCost = null;
    }

    /// <summary>
    /// CR 118.9 — runtime "may cast from graveyard" grant. Used by static
    /// abilities such as Lurrus of the Dream-Den that allow casting a
    /// permanent card from the graveyard for its printed mana cost. When
    /// non-null, the card may be cast from the graveyard via a
    /// <see cref="Majik.Core.Costs.GraveyardCastAlternativeCost"/> built
    /// from this cost. Unlike <see cref="RuntimeFlashbackCost"/>, the
    /// resolved card returns to its default destination (battlefield for
    /// permanents) — there is no post-resolution exile.
    /// </summary>
    public ValueObjects.ManaCost? RuntimeGraveyardCastCost { get; private set; }

    /// <summary>
    /// Stamp a graveyard-cast grant on this card. Used by Lurrus of the
    /// Dream-Den's static ability while it is on the battlefield, scoped
    /// to permanent cards in its controller's graveyard with mana value
    /// 2 or less. Idempotent — later grants overwrite earlier ones.
    /// </summary>
    public void GrantRuntimeGraveyardCast(ValueObjects.ManaCost cost)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        RuntimeGraveyardCastCost = cost;
    }

    /// <summary>Clear any runtime graveyard-cast grant on this card.</summary>
    public void ClearRuntimeGraveyardCast()
    {
        RuntimeGraveyardCastCost = null;
    }

    /// <summary>
    /// CR 118.9 — runtime "you may cast this card from exile until end of
    /// turn" grant. Stamped by effects such as Ragavan, Nimble Pilferer's
    /// combat-damage trigger ("Until end of turn, you may cast that card").
    /// When non-null, the named player may cast the card from the Exile
    /// zone via a <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/>
    /// built from the printed mana cost; unlike Suspend / Cascade, the
    /// allowed caster is NOT the card's owner (Ragavan exiles from the
    /// damaged player's library — the granted permission lets the
    /// Ragavan controller cast the opponent's card). Cleared at end of
    /// turn by the granting effect's bus subscription.
    /// </summary>
    public Player? RuntimeExileCastAllowedCaster { get; private set; }

    /// <summary>
    /// The mana cost the <see cref="RuntimeExileCastAllowedCaster"/> pays
    /// to cast this card from exile under the runtime grant. Typically the
    /// card's printed mana cost (per Ragavan's "you may cast that card").
    /// </summary>
    public ValueObjects.ManaCost? RuntimeExileCastCost { get; private set; }

    /// <summary>
    /// Stamp an exile-cast grant on this card. <paramref name="allowedCaster"/>
    /// is the player who may cast (need not be the owner); <paramref name="cost"/>
    /// is the mana cost they pay (typically the card's printed cost).
    /// Idempotent — later grants overwrite earlier ones. Cleared at EOT by
    /// the granting effect's bookkeeping.
    /// </summary>
    public void GrantRuntimeExileCast(Player allowedCaster, ValueObjects.ManaCost cost)
    {
        if (allowedCaster == null) throw new ArgumentNullException(nameof(allowedCaster));
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        RuntimeExileCastAllowedCaster = allowedCaster;
        RuntimeExileCastCost = cost;
    }

    /// <summary>Clear any runtime exile-cast grant on this card.</summary>
    public void ClearRuntimeExileCast()
    {
        RuntimeExileCastAllowedCaster = null;
        RuntimeExileCastCost = null;
    }

    /// <summary>
    /// CR 702.143 — runtime Escape grant. When non-null, the card has
    /// Escape while in its owner's graveyard, with this mana cost as the
    /// alt-cost mana payment and <see cref="RuntimeEscapeExileCount"/> as
    /// the "exile N other cards from your graveyard" rider. Stamped by
    /// effects such as Underworld Breach ("Each nonland card in your
    /// graveyard has escape and 'Escape—[printed mana cost], exile three
    /// other cards from your graveyard.'"). The
    /// <see cref="Majik.Core.Players.Agents.EscapeAltCostProbe.DefaultLookup"/>
    /// consults this field so a granted escape surfaces to the bot's
    /// alt-cost enumeration alongside the printed-escape ship list.
    /// </summary>
    public ValueObjects.ManaCost? RuntimeEscapeCost { get; private set; }

    /// <summary>
    /// The "exile N other cards from your graveyard" rider count tied to
    /// <see cref="RuntimeEscapeCost"/>. Underworld Breach stamps 3; future
    /// runtime-grant escape effects can pick their own count. Null when
    /// no runtime escape grant is in place.
    /// </summary>
    public int? RuntimeEscapeExileCount { get; private set; }

    /// <summary>
    /// Stamp a runtime Escape grant on this card. Used by Underworld
    /// Breach's static-while-on-battlefield grant (CR 702.143) on every
    /// nonland card in the controller's graveyard. Idempotent — later
    /// grants overwrite earlier ones; cleared by the granting effect's
    /// bookkeeping (typically when the granter leaves the battlefield).
    /// </summary>
    public void GrantRuntimeEscape(ValueObjects.ManaCost cost, int exileCount)
    {
        if (cost == null) throw new ArgumentNullException(nameof(cost));
        if (exileCount <= 0) throw new ArgumentOutOfRangeException(nameof(exileCount));
        RuntimeEscapeCost = cost;
        RuntimeEscapeExileCount = exileCount;
    }

    /// <summary>Clear any runtime Escape grant on this card.</summary>
    public void ClearRuntimeEscape()
    {
        RuntimeEscapeCost = null;
        RuntimeEscapeExileCount = null;
    }

    /// <summary>
    /// CR 305.9 / 113.6c — runtime "you may play this land card from your
    /// graveyard" permission. Stamped by static abilities such as Crucible
    /// of Worlds, Ramunap Excavator, Conduit of Worlds, and the World
    /// Breaker emblem clauses that let a player play lands from a zone
    /// other than their hand. When true, agents / the engine may surface
    /// this land as a legal target for
    /// <see cref="Majik.Core.Players.Agents.PriorityAction.PlayLand"/>
    /// even though the card is in the graveyard. The standard land-play
    /// gate (per-turn cap, main phase, stack empty, controller-on-turn —
    /// see <see cref="Majik.Core.Game.LandDropTracker.CanPlayLand"/>) still
    /// applies; only the source-zone restriction is waived.
    ///
    /// <para>
    /// The flag is set on the LAND CARD, not the player, so multiple
    /// permission sources (Crucible + Ramunap Excavator) are idempotent
    /// and don't need a per-player ledger. Once stamped, the flag stays
    /// until explicitly cleared; the agent layer is expected to also
    /// gate on the live presence of a permission source (Crucible on
    /// the battlefield) so a lingering flag after the source leaves
    /// doesn't enable a phantom play. v1 doesn't model the LTB clear
    /// — see <see cref="Majik.Core.CardData.Factories.CrucibleOfWorldsFactory"/>
    /// xmldoc.
    /// </para>
    /// </summary>
    public bool MayPlayFromGraveyard { get; private set; }

    /// <summary>
    /// Stamp the runtime "may play this land card from the graveyard" flag.
    /// Idempotent. See <see cref="MayPlayFromGraveyard"/> for semantics.
    /// </summary>
    public void GrantPlayLandFromGraveyard()
    {
        MayPlayFromGraveyard = true;
    }

    /// <summary>Clear the runtime "may play from graveyard" flag.</summary>
    public void ClearPlayLandFromGraveyard()
    {
        MayPlayFromGraveyard = false;
    }

    /// <summary>
    /// CR 702.66 — when this card was cast paying delve, the number of cards
    /// that were exiled from its caster's graveyard as part of the delve
    /// payment. Set by <see cref="Majik.Core.Game.SpellCastFlow"/> right after
    /// <see cref="Majik.Core.Costs.DelveCost.Pay"/> succeeds; read by
    /// Murktide-Regent-style ETB effects that need to know "how many cards
    /// were exiled with me" without otherwise plumbing the delve cost
    /// through to the resolving permanent. Null when the card was not cast
    /// via delve, or has already been consumed/cleared.
    /// </summary>
    public int? PendingDelveExiledCount { get; private set; }

    /// <summary>Stamp the delve-exile count on this card. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately after the
    /// delve cost is paid.</summary>
    public void SetPendingDelveExiledCount(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        PendingDelveExiledCount = count;
    }

    /// <summary>Clear the stamped delve-exile count. Called by any ETB
    /// consumer (e.g. Murktide Regent) once it has used the value.</summary>
    public void ClearPendingDelveExiledCount()
    {
        PendingDelveExiledCount = null;
    }

    /// <summary>
    /// CR 202.3b — when this card was cast as a spell with a variable {X}
    /// cost, the value chosen for X. Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> right after the caster
    /// chooses X (and before the spell hits the stack); read by ETB
    /// effects that need to know "what was X" without otherwise plumbing
    /// <c>ChosenSpellParams.X</c> through to the resolving permanent
    /// (Chalice of the Void's "enters with X charge counters", etc.).
    /// Null when the card was not cast via the X-prompt path, or has
    /// already been consumed/cleared.
    /// </summary>
    public int? PendingCastX { get; private set; }

    /// <summary>Stamp the chosen X on this card. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> right after the agent
    /// chooses X for a variable-X spell.</summary>
    public void SetPendingCastX(int x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        PendingCastX = x;
    }

    /// <summary>Clear the stamped X value. Called by any ETB consumer
    /// (e.g. Chalice of the Void) once it has used the value, so a later
    /// non-cast battlefield entry (blink, token copy) doesn't reuse it.</summary>
    public void ClearPendingCastX()
    {
        PendingCastX = null;
    }

    /// <summary>
    /// CR 702.44 — distinct colors of mana actually spent to pay this
    /// card's cast cost (colored pips + colored mana used to satisfy
    /// generic). Stamped by
    /// <see cref="Majik.Core.Game.TurnDriver"/> at cast time right after
    /// mana payment commits (parallels <see cref="PendingCastX"/>);
    /// read by Sunburst ETB effects ("enters with a +1/+1 counter / charge
    /// counter for each color of mana spent to cast it"). The set is
    /// authoritative for "colors spent" — it diffs the player's mana pool
    /// across the spend (see
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/>),
    /// so generic-mana paid with colored mana counts toward Sunburst's
    /// color-count (CR 702.44b — "if one or more colored mana was spent
    /// on its costs"). Null when the card was not cast via a colored-mana
    /// payment path, or has already been consumed/cleared. Cleared post-
    /// resolve so a later non-cast battlefield entry (blink, token copy)
    /// doesn't reuse the previous cast's colors.
    /// </summary>
    public IReadOnlyList<ManaColor>? PendingCastColors { get; private set; }

    /// <summary>
    /// Per-color multiplicity of mana actually spent to pay this card's cast
    /// cost. Where <see cref="PendingCastColors"/> records only the distinct
    /// <em>set</em> of colors (CR 702.44 / Sunburst — counters per color), this
    /// ledger preserves the <em>count</em> so an intervening-if can tell
    /// "{R}{R} was spent" from "{R}{G} was spent" (the hybrid Elemental
    /// Incarnation family — Vibrance / Wistfulness — keys off this).
    /// <para>
    /// Stamped by <see cref="Majik.Core.Game.TurnDriver"/> alongside
    /// <see cref="PendingCastColors"/>, computed by the same cross-spend pool
    /// diff in <see cref="Majik.Core.Costs.ManaPaymentResolver"/> (the
    /// per-color delta IS the count; the distinct set is just that delta
    /// collapsed to "> 0"). Colorless / generic mana is not colored and is
    /// never recorded. Only colors with a positive count appear as keys.
    /// Null when no cast has happened yet, or after the stamp is consumed /
    /// cleared. An empty (non-null) dictionary = cast but no colored mana
    /// spent.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<ManaColor, int>? PendingCastColorCounts { get; private set; }

    /// <summary>Stamp the distinct colors paid for this card's cast.
    /// Called by <see cref="Majik.Core.Game.TurnDriver"/> right after
    /// the mana resolver commits payment. Empty list = no colored mana
    /// was spent (CR 702.44b — Sunburst yields zero counters in that
    /// case). Null is reserved for "no cast has happened yet"; pass an
    /// empty list to explicitly record "cast but no colors paid".
    /// <para>
    /// Back-compat overload for callers that only know the distinct colors
    /// (Sunburst-era tests). Each distinct color back-fills a count of 1 in
    /// <see cref="PendingCastColorCounts"/> — a color appearing in the
    /// distinct set implies at least one of it was spent — so the count
    /// ledger and the predicate (<see cref="SpentAtLeast"/>) stay consistent.
    /// Callers that know exact multiplicity should prefer
    /// <see cref="SetPendingCastColorCounts"/>.
    /// </para></summary>
    public void SetPendingCastColors(IReadOnlyList<ManaColor> colors)
    {
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        PendingCastColors = colors;
        // Back-fill the count ledger: one per distinct color.
        var counts = new Dictionary<ManaColor, int>();
        foreach (var color in colors)
        {
            counts[color] = counts.TryGetValue(color, out var n) ? n + 1 : 1;
        }
        PendingCastColorCounts = counts;
    }

    /// <summary>Stamp the per-color spent-count ledger for this card's cast,
    /// and derive <see cref="PendingCastColors"/> (the distinct set, in
    /// canonical WUBRG order) from it. This is the authoritative stamp the
    /// mana resolver uses — it knows exact multiplicity. Only positive counts
    /// are retained; zero / negative entries are dropped. An empty dictionary
    /// records "cast but no colored mana spent" (both ledgers become empty,
    /// not null).</summary>
    public void SetPendingCastColorCounts(IReadOnlyDictionary<ManaColor, int> counts)
    {
        if (counts == null) throw new ArgumentNullException(nameof(counts));

        // Retain only positive counts; colorless/generic mana never reaches
        // here, but a defensive filter keeps the ledger meaningful.
        var ledger = new Dictionary<ManaColor, int>();
        foreach (var (color, n) in counts)
        {
            if (n > 0) ledger[color] = n;
        }
        PendingCastColorCounts = ledger;

        // Derive the distinct set in canonical WUBRG order (matches the
        // engine's color iteration order so Sunburst counter placement and
        // any existing consumer stay deterministic).
        var distinct = new List<ManaColor>(ledger.Count);
        foreach (var color in WubrgOrder)
        {
            if (ledger.ContainsKey(color)) distinct.Add(color);
        }
        PendingCastColors = distinct;
    }

    /// <summary>
    /// Intervening-if predicate "≥<paramref name="count"/> mana of
    /// <paramref name="color"/> was spent to cast this" — the gate the
    /// hybrid Elemental Incarnations read ("if {R}{R} was spent ...").
    /// Reads <see cref="PendingCastColorCounts"/>. Returns false when no
    /// cast has happened (null ledger). Mirrors how Sunburst ETB effects
    /// read the distinct set; designed to be wired as a
    /// <see cref="Majik.Core.Abilities.TriggeredAbility"/> intervening-if.
    /// </summary>
    /// <param name="count">Required multiplicity, must be ≥ 1 (asking for
    /// "at least 0" is degenerate and surfaces a caller mistake).</param>
    public bool SpentAtLeast(ManaColor color, int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(
                nameof(count), count, "count must be ≥ 1.");

        var ledger = PendingCastColorCounts;
        if (ledger == null) return false;
        return ledger.TryGetValue(color, out var spent) && spent >= count;
    }

    /// <summary>Canonical WUBRG color iteration order used to derive the
    /// distinct-color set from the count ledger.</summary>
    private static readonly ManaColor[] WubrgOrder =
    {
        ManaColor.White, ManaColor.Blue, ManaColor.Black,
        ManaColor.Red, ManaColor.Green,
    };

    /// <summary>Clear the stamped colors-paid ledger (both the distinct set
    /// and the count ledger). Called by any ETB consumer (e.g.
    /// SunburstFactory's ETB effect, or a conditional-ETB incarnation
    /// trigger) once it has used the value, so a later non-cast battlefield
    /// entry doesn't reuse it.</summary>
    public void ClearPendingCastColors()
    {
        PendingCastColors = null;
        PendingCastColorCounts = null;
    }

    /// <summary>
    /// CR 113.5 / CR 400.7 — persistent "this permanent was cast"
    /// marker. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> at the moment the
    /// spell is pushed onto the stack (any cost — printed, alternative,
    /// reduced, additional). Survives resolution so ETB-resident
    /// triggers ("when ~ enters, if you cast it, ...") and
    /// battlefield-entry replacements ("if a nontoken permanent would
    /// enter the battlefield and it wasn't cast, exile it instead" —
    /// Containment Priest, CR 614) can both read it.
    ///
    /// <para>Cleared when the permanent leaves the battlefield to any
    /// other zone (CR 400.7 — the card becomes a "new object" on each
    /// zone change). The flag is NOT cleared while transitioning Stack
    /// → Battlefield, so ETB triggers resolving immediately after the
    /// move still see <c>true</c>. Non-cast battlefield entries
    /// (reanimation, Show and Tell, Sneak Attack, Through the Breach,
    /// blink reappearance, token-copy ETB, Aether Vial put, …) leave
    /// the flag <c>false</c>.</para>
    ///
    /// <para>Mirrors <see cref="Majik.Core.Effects.ZoneMoveIntent.WasCast"/>,
    /// which is the in-flight intent-side mirror used by
    /// <see cref="ReplacementBus"/> consumers. The
    /// <see cref="Majik.Core.Services.ZoneService"/> populates that
    /// field from this property when building the intent for the
    /// Stack → Battlefield move, so any consumer that prefers the
    /// intent record (ContainmentPriest's replacement) and any
    /// consumer that prefers the live card field (TheOneRing's ETB
    /// gate) both see the same truth.</para>
    ///
    /// <para>Defaults to <c>false</c> so hand-built test cards without
    /// an explicit stamp are treated as non-cast.</para>
    /// </summary>
    public bool WasCast { get; private set; }

    /// <summary>Stamp the cast marker. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately before
    /// the resolving spell is pushed onto the stack (CR 601.2i).</summary>
    public void SetWasCast(bool value)
    {
        WasCast = value;
    }

    /// <summary>Clear the cast marker. Called by
    /// <see cref="Majik.Core.Services.ZoneService"/> when a permanent
    /// leaves the battlefield to any other zone (CR 400.7 — new
    /// object on each zone change). Idempotent.</summary>
    public void ClearWasCast()
    {
        WasCast = false;
    }

    /// <summary>
    /// CR 702.138b — "escaped" runtime sentinel propagated from the
    /// resolving <see cref="Majik.Core.Spells.Spell.WasCastForEscape"/>
    /// onto the card itself, so battlefield-resident triggers
    /// (Uro's "sacrifice it unless it escaped" trigger; future
    /// <em>escapes with [counters]</em> riders per CR 702.138c) can
    /// read the flag from the source card without plumbing the spell
    /// reference across the spell → permanent boundary. Stamped at
    /// cast time by <see cref="Majik.Core.Game.SpellCastFlow"/>; the
    /// flag persists across the card's current battlefield stint and
    /// is cleared when the card leaves the battlefield (any
    /// destination), mirroring CR 400.7's "new object" rule so a
    /// re-cast / blink / token copy doesn't inherit the prior
    /// escape posture.
    /// </summary>
    public bool WasCastForEscape { get; private set; }

    /// <summary>Stamp the escape sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used
    /// an <see cref="Majik.Core.Costs.EscapeAlternativeCost"/>.</summary>
    public void SetWasCastForEscape(bool value)
    {
        WasCastForEscape = value;
    }

    /// <summary>
    /// CR 702.62d / 702.62g — "cast via suspend" runtime sentinel.
    /// Stamped <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// when the cast used a <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/>
    /// with its <see cref="Majik.Core.Costs.CastFromExileAlternativeCost.IsSuspendCast"/>
    /// flag set (i.e. the "cast for free" payoff fired by
    /// <see cref="Majik.Core.Costs.SuspendedCardRegistry"/> when the last
    /// time counter is removed). Read by:
    ///   - The creature-haste rider in <see cref="Majik.Core.Game.SpellCastFlow"/>,
    ///     which registers a <see cref="Majik.Core.Effects.SuspendHasteEffect"/>
    ///     on the resolving permanent for as long as it stays on the
    ///     battlefield (CR 702.62g — "gains haste until you lose control of
    ///     the spell or the permanent it becomes").
    ///   - Future "if [card] was cast via suspend" triggers / replacements.
    ///
    /// <para>Mirrors <see cref="Majik.Core.Spells.Spell.WasCastFromSuspend"/>
    /// on the resolving stack object for resolve-body reads that don't
    /// have the spell reference handy.</para>
    ///
    /// <para>Defaults to <c>false</c>. The flag is NOT cleared on zone
    /// change — the haste-grant continuous effect carries its own
    /// LTB-revoke lifecycle via <see cref="Majik.Core.Effects.SuspendHasteEffect.IsActive"/>,
    /// and the underlying card object is replaced on the next cast (CR
    /// 400.7 — new object on each zone change) so the stale stamp can't
    /// survive a real re-cast.</para>
    /// </summary>
    public bool WasCastFromSuspend { get; private set; }

    /// <summary>Stamp the cast-from-suspend sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the cast used a
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost"/> with
    /// <see cref="Majik.Core.Costs.CastFromExileAlternativeCost.IsSuspendCast"/>
    /// set.</summary>
    public void SetWasCastFromSuspend(bool value)
    {
        WasCastFromSuspend = value;
    }

    /// <summary>
    /// CR 601.2 / CR 113.5 — "cast from hand" runtime sentinel. Stamped
    /// <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/> when the
    /// resolving spell's source zone (the zone the card was in immediately
    /// before moving to the stack) was <see cref="Majik.Core.Zones.ZoneType.Hand"/>.
    /// Read by ETB intervening-if clauses that gate on the "if you cast it
    /// from your hand" branch — Bedlam Reveler's
    /// <c>"When this creature enters, if you cast it from your hand,
    /// discard your hand, then draw three cards."</c> is the canonical
    /// consumer, distinct from The One Ring's <see cref="WasCast"/> gate
    /// which fires on any cast (flashback / suspend / from-graveyard
    /// included).
    ///
    /// <para>Survives Stack → Battlefield so ETB triggers fired off the
    /// resulting permanent's entry still observe the stamp. Cleared on
    /// battlefield exit (any destination) by
    /// <see cref="Majik.Core.Services.ZoneService"/>, matching CR 400.7
    /// — the card is a "new object" on each subsequent zone change and
    /// a re-cast / blink / token copy must not inherit the prior
    /// cast-from-hand posture.</para>
    ///
    /// <para>Mirrors <see cref="Majik.Core.Spells.Spell.WasCastFromHand"/>
    /// on the resolving stack object for resolve-body reads that don't
    /// have the spell reference handy.</para>
    ///
    /// <para>Defaults to <c>false</c> so hand-built test cards without an
    /// explicit stamp are treated as non-cast.</para>
    /// </summary>
    public bool WasCastFromHand { get; private set; }

    /// <summary>Stamp the cast-from-hand sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately before the
    /// card moves Hand → Stack (CR 601.2i).</summary>
    public void SetWasCastFromHand(bool value)
    {
        WasCastFromHand = value;
    }

    /// <summary>Clear the cast-from-hand sentinel. Called by
    /// <see cref="Majik.Core.Services.ZoneService"/> when a permanent leaves
    /// the battlefield to any other zone (CR 400.7 — new object on each
    /// zone change). Idempotent.</summary>
    public void ClearWasCastFromHand()
    {
        WasCastFromHand = false;
    }

    /// <summary>
    /// CR 601.2 / CR 113.5 — "cast from library" sentinel. Stamped
    /// <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/> when the
    /// card's source zone at cast time was
    /// <see cref="Majik.Core.Zones.ZoneType.Library"/> (Future Sight, Narset,
    /// Possibility Storm, etc.). Read by ETB intervening-if clauses that
    /// gate on the "if you cast it from your library" branch — Fblthp, the
    /// Lost's ETB draw-2 rider is the canonical consumer.
    ///
    /// <para>Survives Stack → Battlefield so ETB triggers can observe the
    /// stamp immediately after the zone move. Cleared on battlefield exit
    /// (any destination) by <see cref="Majik.Core.Services.ZoneService"/>,
    /// matching CR 400.7 — the card is a "new object" after each subsequent
    /// zone change.</para>
    ///
    /// <para>Mirrors <see cref="Majik.Core.Spells.Spell.WasCastFromLibrary"/>
    /// on the resolving stack object.</para>
    ///
    /// <para>Defaults to <c>false</c> so hand-built test cards without an
    /// explicit stamp are treated as non-library casts.</para>
    /// </summary>
    public bool WasCastFromLibrary { get; private set; }

    /// <summary>Stamp the cast-from-library sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately before the
    /// card moves Library → Stack (CR 601.2i).</summary>
    public void SetWasCastFromLibrary(bool value)
    {
        WasCastFromLibrary = value;
    }

    /// <summary>Clear the cast-from-library sentinel. Called by
    /// <see cref="Majik.Core.Services.ZoneService"/> when a permanent leaves
    /// the battlefield to any other zone (CR 400.7 — new object on each
    /// zone change). Idempotent.</summary>
    public void ClearWasCastFromLibrary()
    {
        WasCastFromLibrary = false;
    }

    /// <summary>
    /// CR 400.7 / CR 603.6a — "entered directly from library" sentinel.
    /// Stamped <c>true</c> by <see cref="Majik.Core.Services.ZoneService"/>
    /// when it observes a Library → Battlefield move where
    /// <see cref="WasCast"/> is <c>false</c> (i.e. the card was placed onto
    /// the battlefield without being cast — Glimpse of Nature style, Sneak
    /// Attack on a library-top card, etc.). Cleared on battlefield exit to
    /// any other zone (CR 400.7 — new object per zone change).
    ///
    /// <para>Pairs with <see cref="WasCastFromLibrary"/> to cover the full
    /// Fblthp, the Lost draw-2 condition: "If it entered from your library
    /// OR was cast from your library." A factory checks
    /// <c>WasCastFromLibrary || WasPlacedFromLibrary</c>.</para>
    ///
    /// <para>Defaults to <c>false</c>.</para>
    /// </summary>
    public bool WasPlacedFromLibrary { get; private set; }

    /// <summary>Stamp the placed-from-library sentinel. Called by
    /// <see cref="Majik.Core.Services.ZoneService"/> when it observes a
    /// Library → Battlefield move without a cast marker.</summary>
    public void SetWasPlacedFromLibrary(bool value)
    {
        WasPlacedFromLibrary = value;
    }

    /// <summary>Clear the placed-from-library sentinel. Called by
    /// <see cref="Majik.Core.Services.ZoneService"/> on battlefield exit.
    /// Idempotent.</summary>
    public void ClearWasPlacedFromLibrary()
    {
        WasPlacedFromLibrary = false;
    }

    /// <summary>
    /// CR 702.33b — "kicked" runtime sentinel. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Costs.KickerAdditionalCost.Pay"/> at cast
    /// announcement when the caster pays the optional kicker mana
    /// (CR 601.2b — kicker decision is locked in when the spell is
    /// cast). Read by the resolving spell's <c>EffectFactory</c> to
    /// branch the printed body on the "if [spell] was kicked" rider
    /// (Burst Lightning's deals-4-instead-of-2 toggle is the canonical
    /// consumer).
    ///
    /// <para>Cleared by <see cref="Majik.Core.Game.SpellCastFlow"/>
    /// once the spell resolves so a later re-cast / blink / token
    /// copy / on-battlefield permanent doesn't inherit the prior
    /// kicker posture (CR 400.7 — new object on each zone change).
    /// Mirrors the <see cref="Majik.Core.Spells.Spell.WasKicked"/>
    /// stamping on the resolving stack object.</para>
    ///
    /// <para>Defaults to <c>false</c> so hand-built test cards
    /// without an explicit stamp are treated as non-kicked casts.</para>
    /// </summary>
    public bool WasKicked { get; private set; }

    /// <summary>Stamp the kicker sentinel. Called by
    /// <see cref="Majik.Core.Costs.KickerAdditionalCost.Pay"/> after
    /// the kicker mana is successfully paid.</summary>
    public void SetWasKicked(bool value)
    {
        WasKicked = value;
    }

    /// <summary>Clear the kicker sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> via a cleanup
    /// effect appended after the spell's printed body so the flag
    /// doesn't leak past resolution (CR 400.7).</summary>
    public void ClearWasKicked()
    {
        WasKicked = false;
    }

    /// <summary>
    /// CR 702.169 — "Bargain" runtime sentinel. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Costs.BargainAdditionalCost.Pay"/> when the caster
    /// pays the optional bargain cost (sacrifice an artifact, enchantment, or
    /// token) as the spell is cast. Read by the resolving spell's effect body
    /// to branch on the "if this spell was bargained" rider.
    ///
    /// <para>Mirrors the established sentinel pattern (<see cref="WasKicked"/> /
    /// <see cref="WasCastForSurge"/>): cleared after the printed body resolves
    /// so a later re-cast / blink / token copy doesn't inherit the prior bargain
    /// posture (CR 400.7). Defaults to <c>false</c>.</para>
    /// </summary>
    public bool WasBargained { get; private set; }

    /// <summary>Stamp the bargain sentinel. Called by
    /// <see cref="Majik.Core.Costs.BargainAdditionalCost.Pay"/> after the
    /// sacrifice is performed.</summary>
    public void SetWasBargained(bool value)
    {
        WasBargained = value;
    }

    /// <summary>Clear the bargain sentinel (CR 400.7) — symmetric with
    /// <see cref="ClearWasKicked"/>.</summary>
    public void ClearWasBargained()
    {
        WasBargained = false;
    }

    /// <summary>
    /// CR 702.115 — "Surge" runtime sentinel. Stamped <c>true</c> by
    /// <see cref="Majik.Core.Costs.SurgeAlternativeCost"/> at cast time
    /// (during <see cref="Majik.Core.Game.SpellCastFlow"/>'s alt-cost
    /// branch) when the caster pays the surge cost rather than the printed
    /// mana cost. Read by the resolving spell's effect body to branch on
    /// the "if its surge cost was paid" rider (Reckless Bushwhacker's
    /// haste + +1/+0 swarm rider is the canonical consumer).
    ///
    /// <para>Mirrors the established sentinel pattern (<see cref="WasKicked"/> /
    /// <see cref="WasCastForEscape"/>): cleared via
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> after the printed body
    /// resolves so a later re-cast / blink / token copy / on-battlefield
    /// permanent doesn't inherit the prior surge posture (CR 400.7 — new
    /// object on each zone change). Defaults to <c>false</c> so hand-built
    /// test cards without an explicit stamp are treated as non-surge casts.</para>
    /// </summary>
    public bool WasCastForSurge { get; private set; }

    /// <summary>Stamp the surge sentinel. Called by
    /// <see cref="Majik.Core.Costs.SurgeAlternativeCost.OnResolved"/>
    /// (and mirrored at announce time by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the alt-cost is a
    /// Surge cost, so resolve-time reads see the flag).</summary>
    public void SetWasCastForSurge(bool value)
    {
        WasCastForSurge = value;
    }

    /// <summary>Clear the surge sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> via a cleanup effect
    /// appended after the spell's printed body so the flag doesn't leak
    /// past resolution (CR 400.7). Idempotent.</summary>
    public void ClearWasCastForSurge()
    {
        WasCastForSurge = false;
    }

    /// <summary>
    /// CR 601.2f — when this card is currently being cast as a spell, the
    /// set of targets the agent picked during target selection (one inner
    /// list per <see cref="Majik.Core.Game.TargetRequest"/>, mirroring
    /// <see cref="Majik.Core.Game.ChosenSpellParams.Targets"/>). Stamped by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> immediately after target
    /// collection so cost-calculation rules that depend on the chosen
    /// targets ("This spell costs {2} less to cast if it targets a blue
    /// spell." — Mystical Dispute) can read them. Null when the card is
    /// not actively being cast.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<object>>? PendingCastTargets { get; private set; }

    /// <summary>Stamp the chosen targets on this card. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> right after target
    /// collection and before cost calculation, so a
    /// <see cref="Majik.Core.Costs.CostReductionAbility"/> on the card can
    /// reference the targets that have just been picked (e.g. Mystical
    /// Dispute's "costs {2} less if it targets a blue spell").</summary>
    public void SetPendingCastTargets(IReadOnlyList<IReadOnlyList<object>> targets)
    {
        PendingCastTargets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>Clear the stamped pending targets. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> once the spell has been
    /// pushed onto the stack, so a later re-cast doesn't see stale
    /// targets.</summary>
    public void ClearPendingCastTargets()
    {
        PendingCastTargets = null;
    }

    /// <summary>
    /// CR 701.59 — "Gift" cast-time sentinel (Bloomburrow). Stamped
    /// <c>true</c> by <see cref="Majik.Core.Game.SpellCastFlow"/> when
    /// the caster opts into a gift promise during the cast flow.
    /// Resolve bodies of <see cref="Majik.Core.Spells.IGiftClause"/>
    /// implementors (e.g.
    /// <see cref="Majik.Core.CardData.Factories.IntoTheFloodMawFactory"/>)
    /// branch on this flag to apply the upgraded printed effect (Flood
    /// Maw's "instead return target nonland permanent"). Mirrors
    /// <see cref="Majik.Core.Spells.Spell.GiftRecipient"/> on the
    /// resolving spell for resolve-body reads that don't have the
    /// spell reference handy.
    ///
    /// <para>Cleared by <see cref="Majik.Core.Game.SpellCastFlow"/> via
    /// a cleanup effect appended after the spell's printed body so the
    /// flag doesn't leak past resolution (CR 400.7 — new object on
    /// each zone change). Defaults to <c>false</c> so hand-built test
    /// cards without an explicit stamp are treated as non-gifted casts.</para>
    /// </summary>
    public bool HasGiftPromised { get; private set; }

    /// <summary>Stamp the gift-promise sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> when the caster
    /// opts into a cast-time gift promise.</summary>
    public void SetHasGiftPromised(bool value)
    {
        HasGiftPromised = value;
    }

    /// <summary>Clear the gift-promise sentinel. Called by
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> via a cleanup
    /// effect appended after the spell's printed body so the flag
    /// doesn't leak past resolution (CR 400.7).</summary>
    public void ClearHasGiftPromised()
    {
        HasGiftPromised = false;
    }

    /// <summary>
    /// CR 711 — double-faced / transform card face tracker. Non-null on
    /// DFC cards; tracks which face (front / back) is currently active and
    /// exposes <see cref="Majik.Core.CardData.MDFCs.MdfcState.Transform"/>
    /// to flip between them. Null on single-faced cards.
    ///
    /// v1 is a thin attachment — flipping the state does not yet swap the
    /// runtime characteristics on the underlying Card object (full Layer 0
    /// per-face characteristic-replacement is deferred). It is the
    /// canonical observation surface for "is this card transformed?".
    /// </summary>
    public Majik.Core.CardData.MDFCs.MdfcState? MdfcState { get; set; }

    /// <summary>
    /// CR 715 — Adventure half descriptor. Non-null on adventurer cards
    /// (Bonecrusher Giant, Murderous Rider, Embereth Shieldbreaker, …);
    /// null on single-face cards. Attached by the card's factory at build
    /// time and read by <see cref="Majik.Core.Costs.AdventureAlternativeCost"/>
    /// when the caster picks the Adventure cast path.
    ///
    /// While exiled by a resolved Adventure (CR 715.3d), the printed-side
    /// "may cast from exile" permission is stamped on the card via the
    /// existing <see cref="GrantRuntimeExileCast"/> surface — same probe
    /// surface as Ragavan / Cascade — so no Adventure-specific cast-from-
    /// exile alt-cost is required.
    /// </summary>
    public Majik.Core.CardData.Adventures.AdventureSpec? AdventureSpec { get; set; }

    /// <summary>
    /// CR 105 / CR 111.4 / CR 903.4 — explicit colour identity for tokens
    /// (and any future card whose colour cannot be derived from its mana
    /// cost). Tokens have no printed mana cost so their colour is set by
    /// the effect that created them: "create a 2/2 green Cat creature
    /// token" stamps <c>{Green}</c> here. Null on normal cards (colour
    /// derives from <see cref="ManaCost"/> via
    /// <see cref="Majik.Core.Cards.CardColors.GetColors"/>); empty list is
    /// explicit "colourless" (CR 105.2c — Wurmcoil's Phyrexian Wurm
    /// tokens, Karn's Construct tokens). <see cref="CardColors.GetColors"/>
    /// honours this override when non-null; otherwise it falls back to the
    /// mana-cost pip scan.
    /// </summary>
    public IReadOnlyList<ValueObjects.ManaColor>? TokenColorsOverride { get; private set; }

    /// <summary>
    /// Stamp an explicit colour set on this card. Used by
    /// <see cref="Majik.Core.Tokens.TokenFactory.CreateOnBattlefield"/> to
    /// honour the printed colour of token-creation effects (Esika's Chariot's
    /// "green" Cats, Ocelot Pride's "white" Cats, Pact of the Titan's "red"
    /// Giant, Wurmcoil's "colourless" Wurms — empty list). Idempotent;
    /// later calls overwrite earlier ones. Should generally only be set
    /// once at token construction.
    /// </summary>
    public void SetTokenColors(IReadOnlyList<ValueObjects.ManaColor> colors)
    {
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        TokenColorsOverride = colors;
    }

    /// <summary>
    /// CR 202.2 — printed color indicator (the round dot to the left of the
    /// type line). Some cards' color is determined by a color indicator
    /// instead of (or in addition to) their mana cost: Dryad Arbor (Land
    /// Creature with empty mana cost and a green indicator), Devoid cards
    /// (e.g. Eldrazi Skyspawner — printed with a colored mana symbol but
    /// the colorless indicator overrides), the back faces of double-faced
    /// cards (Garruk Relentless // Garruk, the Veil-Cursed back-face),
    /// etc. When this list is non-null, <see cref="CardColors.GetColors"/>
    /// unions its entries with the mana-cost-pip-derived colors so the
    /// color predicate is correct for tutors and color-matters effects
    /// (Green Sun's Zenith, Summoner's Pact, Hibernation, etc.).
    /// <para>Null means "no color indicator was printed" — the default; the
    /// card's color is whatever the mana cost says (the existing behavior
    /// for every non-indicator card). An empty list is explicit "color
    /// indicator overrides to colorless" (the Devoid path — the indicator
    /// rule strips all colors regardless of mana symbols).</para>
    /// </summary>
    public IReadOnlyList<ValueObjects.ManaColor>? ColorIndicator { get; private set; }

    /// <summary>
    /// CR 702.114 — Devoid. "[This card] is colorless." When true,
    /// <see cref="Majik.Core.Cards.CardColors.GetColors"/> returns the
    /// empty set regardless of the mana-cost pips. Set by named-card
    /// factories that print the Devoid keyword (Eldrazi Skyspawner,
    /// Sowing Mycospawn, Ulamog's Reclaimer, the Battle for Zendikar /
    /// Oath of the Gatewatch Devoid Eldrazi cycle). Independent of
    /// <see cref="ColorIndicator"/> — Devoid is the printed keyword that
    /// strips colors; the color indicator is an unrelated CR 202.2c
    /// mechanism (Dryad Arbor's green indicator on an empty cost).
    /// <para>Default <c>false</c>; non-Devoid cards keep the mana-cost
    /// pip scan path.</para>
    /// </summary>
    public bool IsDevoid { get; private set; }

    /// <summary>Stamp the Devoid keyword on this card. Called by the
    /// named-card factory for any Devoid Eldrazi. Idempotent; later
    /// calls overwrite earlier ones.</summary>
    public void SetDevoid(bool value)
    {
        IsDevoid = value;
    }

    /// <summary>
    /// Stamp a printed color indicator on this card. Called by
    /// <see cref="Majik.Core.CardData.ScryfallCardFactory.Create"/> (when
    /// the seed row's <c>Colors</c> field carries colors not derivable
    /// from the mana cost — Dryad Arbor's <c>colors:["G"]</c> with empty
    /// mana cost) and by <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>
    /// (when the JSON card definition explicitly stamps a
    /// <c>colorIndicator</c>). Idempotent; later calls overwrite earlier
    /// ones.
    /// </summary>
    public void SetColorIndicator(IReadOnlyList<ValueObjects.ManaColor> colors)
    {
        if (colors == null) throw new ArgumentNullException(nameof(colors));
        ColorIndicator = colors;
    }

    public Card(string name, string manaCost = "", IEnumerable<CardType>? cardTypes = null, IEnumerable<CardSupertype>? supertypes = null, IEnumerable<CardSubtype>? subtypes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Card name cannot be null or empty", nameof(name));
        }

        Name = name;
        ManaCost = manaCost;
        ManaCostValue = ValueObjects.ManaCost.Parse(manaCost);
        _zone = ZoneType.Library;
        _controller = null;

        if (cardTypes != null)
        {
            _cardTypes.AddRange(cardTypes);
        }

        if (supertypes != null)
        {
            _supertypes.AddRange(supertypes);
        }

        if (subtypes != null)
        {
            _subtypes.AddRange(subtypes);
        }
    }

    /// <summary>
    /// CR 601.2a / CR 117.6 — zones from which this card can't be cast as
    /// a spell, baked onto the card itself (printed restriction, not an
    /// external rules attachment). The canonical consumer is Hogaak,
    /// Arisen Necropolis ("Hogaak, Arisen Necropolis can't be cast from
    /// your hand."): the factory stamps <c>ZoneType.Hand</c> here, and
    /// <see cref="Majik.Core.Rules.ActionValidator.ValidateAction"/>
    /// rejects a <see cref="Majik.Core.Rules.CastSpellAction"/> whose
    /// <see cref="Majik.Core.Rules.CastSpellAction.FromZone"/> matches.
    ///
    /// <para>Inverse of <see cref="Majik.Core.Rules.CastingRestrictions.MustCastFromHand"/>
    /// (Drannith Magistrate's "your opponents can't cast spells from
    /// anywhere other than their hands"): that restriction is keyed by
    /// the casting player and allows only Hand; this list is keyed by
    /// the card and forbids the listed zones. Both gates run
    /// independently and either can reject a cast.</para>
    ///
    /// <para>Empty for the overwhelming majority of cards; populated only
    /// by named-card factories that print a zone-of-cast restriction.</para>
    /// </summary>
    public IReadOnlyList<ZoneType> RestrictedCastZones => _restrictedCastZones.AsReadOnly();

    /// <summary>
    /// Add <paramref name="zone"/> to the list of zones this card can't be
    /// cast from. Idempotent — adding the same zone twice is a no-op.
    /// Called by named-card factories at card construction (e.g.
    /// <see cref="Majik.Core.CardData.Factories.HogaakFactory.Create"/>
    /// stamps <c>ZoneType.Hand</c>).
    /// </summary>
    public void AddRestrictedCastZone(ZoneType zone)
    {
        if (!_restrictedCastZones.Contains(zone))
        {
            _restrictedCastZones.Add(zone);
        }
    }

    /// <summary>
    /// Abilities attached to this card (activated, triggered, static, mana).
    /// </summary>
    public IReadOnlyList<IAbility> Abilities => _abilities.AsReadOnly();

    /// <summary>
    /// Attach an ability to this card. Used during card construction or by effects
    /// that grant abilities.
    /// </summary>
    public void AddAbility(IAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        _abilities.Add(ability);
        // CR 613 — the layer pipeline seeds chars.Keywords from this card's
        // KeywordAbility markers, and Layer-6 ability grants attach markers
        // here as a side effect of Compute. Invalidate so the next Compute
        // re-seeds with the freshly-attached ability (when wired).
        if (this is Permanent p) p.ActiveEffects?.BumpGeneration();
    }

    /// <summary>
    /// Remove a previously-added ability from this card. Returns true if the
    /// ability was attached and is now removed. Used by aura / equipment
    /// lifecycle binders that grant an ability on attach and revoke it when
    /// the aura leaves the battlefield or is detached (CR 303.4 / 613.1f).
    /// </summary>
    public bool RemoveAbility(IAbility ability)
    {
        if (ability == null)
        {
            throw new ArgumentNullException(nameof(ability));
        }

        var removed = _abilities.Remove(ability);
        // CR 613 — see AddAbility: revoking a granted marker must re-seed.
        if (removed && this is Permanent p) p.ActiveEffects?.BumpGeneration();
        return removed;
    }

    /// <summary>
    /// Add an extra card type after construction. Used by named-card factories
    /// that build multi-type permanents (e.g. Artifact Creature) where the
    /// primary concrete subclass only registers its own type.
    /// </summary>
    internal void AddCardType(CardType type)
    {
        if (!_cardTypes.Contains(type))
            _cardTypes.Add(type);
    }

    /// <summary>
    /// CR 205.4 — add a printed supertype after construction. Used by
    /// named-card factories / designations that grant a supertype to a card
    /// (e.g. the Ring-bearer "is legendary" clause routes through this and a
    /// <see cref="Majik.Core.Effects.GrantSupertypeEffect"/>). Idempotent —
    /// adding the same supertype twice is a no-op. Invalidates the layer-
    /// system cache so the next Compute re-seeds the effective supertype set.
    /// </summary>
    internal void AddSupertype(CardSupertype supertype)
    {
        if (!_supertypes.Contains(supertype))
        {
            _supertypes.Add(supertype);
            // CR 613.1d — the layer pipeline seeds chars.Supertypes from this
            // list; a printed-supertype mutation must re-seed.
            if (this is Permanent p) p.ActiveEffects?.BumpGeneration();
        }
    }

    public bool HasType(CardType type)
    {
        return _cardTypes.Contains(type);
    }

    public bool HasSupertype(CardSupertype supertype)
    {
        return _supertypes.Contains(supertype);
    }

    public bool HasSubtype(CardSubtype subtype)
    {
        return _subtypes.Contains(subtype);
    }

    public override string ToString()
    {
        return Name;
    }
}
