using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coiling Rebirth (Duskmourn: House of Horror,
/// {3}{B}{B}).
///
/// Sorcery. Printed oracle text (verified against Scryfall 2026-06-24):
///   "Gift a card (You may promise an opponent a gift as you cast this
///    spell. If you do, they draw a card before its other effects.)
///    Return target creature card from your graveyard to the battlefield.
///    Then if the gift was promised and that creature isn't legendary,
///    create a token that's a copy of that creature, except it's 1/1."
///
/// ## Relationship to its analogues
/// - The Gift clause ("Gift a card" → recipient draws a card) is identical
///   to <see cref="LongRiversPullFactory"/> / <see cref="WildfireHowlFactory"/>
///   — the recipient draws one card (<see cref="Fx.DrawCards"/>) and the
///   printed body upgrades when the promise is made. Coiling Rebirth is the
///   third "Gift a card" (draw-a-card) implementor and reuses the shared
///   <see cref="Majik.Core.Spells.IGiftClause"/> cast-time delivery contract.
/// - The unconditional half — "Return target creature card from your
///   graveyard to the battlefield" — is the <see cref="UnburialRitesFactory"/>
///   reanimation body: a single 1..1 target request scoped to the caster's
///   graveyard, returned via
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (CR 701.20 / CR 110.2;
///   ETB triggers fire when a ZoneService is wired — CR 603.6a).
/// - The gift-mode rider — "create a token that's a copy of that creature,
///   except it's 1/1" — is the <see cref="CacklingCounterpartFactory"/>
///   copy-token mechanism (CR 706.2 snapshot of the source's copiable values),
///   with the printed P/T override applied (CR 706.2 / CR 706.10 — "except"
///   modifies the copiable values, so the token is created as a 1/1).
///
/// ## Implementation
/// - The base Sorcery shape (name / Sorcery type / {3}{B}{B} cost) is
///   materialised by the concrete <see cref="CoilingRebirthCard"/> subclass —
///   a <see cref="Sorcery"/> that implements
///   <see cref="Majik.Core.Spells.IGiftClause"/> so
///   <see cref="Majik.Core.Game.SpellCastFlow"/> detects the cast-time gift
///   hook (CR 701.59). Mirrors
///   <see cref="WildfireHowlFactory.WildfireHowlCard"/>.
/// - <see cref="BuildDefinition"/> exposes a single 1..1 "target creature
///   card from your graveyard" request (Intent
///   <see cref="BotIntent.Reanimate"/>) whose live candidate pool is the
///   caster's graveyard creature cards. On resolution the chosen creature is
///   re-checked (CR 608.2b — still a creature card in the caster's graveyard)
///   and returned to the caster's battlefield. THEN, only when the gift was
///   promised (<see cref="Card.HasGiftPromised"/>) AND the returned creature
///   is not legendary (CR 205.4 / CardSupertype.Legendary), a 1/1 copy token
///   of that creature is created under the caster.
///
/// ## CR 701.59 deviation — cast-time gift delivery
/// Strict CR 701.59 places gift delivery INSIDE the spell's resolution
/// ("before its other effects"). The engine v1 simplification — matching
/// <see cref="WildfireHowlFactory"/> / <see cref="LongRiversPullFactory"/>
/// and the shared <see cref="Majik.Core.Spells.IGiftClause"/> contract —
/// delivers the gift at cast time (right after the promise is recorded) so a
/// countered gift spell still leaves the promised card drawn. The
/// <see cref="Card.HasGiftPromised"/> sentinel still drives the resolve-time
/// "if the gift was promised" branch (the token-copy rider). Documented on
/// <see cref="Majik.Core.Spells.IGiftClause"/> + at the SpellCastFlow delivery
/// call site.
///
/// ## CR notes
/// - CR 701.20 — return a card from a graveyard to the battlefield.
/// - CR 110.2 — the reanimated permanent enters under the caster's control.
/// - CR 603.6a — ETB triggers on the returned creature fire (ZoneService path).
/// - CR 608.2b — illegal-on-resolution checks (target must still be a creature
///   card in the caster's graveyard).
/// - CR 205.4a — a legendary permanent has the Legendary supertype; the
///   token-copy rider is suppressed for one ("that creature isn't legendary").
/// - CR 706.2 / CR 706.10 — copy effects snapshot the source's copiable values
///   (printed name, P/T, subtypes, keyword abilities, colour identity); the
///   "except it's 1/1" clause overrides the copied P/T to 1/1.
/// - CR 707.2 — the copy token's controller is the controller of the effect
///   creating it (the caster), not the source's owner.
///
/// ## Deferred (v1 gaps — shared with the gift-card family)
/// - <b>Faithful full-copy token</b>: the v1 copy token snapshots name / P/T /
///   subtypes / keyword abilities / colour via <see cref="TokenFactory.TokenSpec"/>
///   (the same lossy snapshot every "token that's a copy" factory uses — e.g.
///   <see cref="CacklingCounterpartFactory"/>). Triggered/activated/static
///   abilities and other printed characteristics are not carried (v1 limitation).
/// - <b>Prod resolve path</b>: like every gift factory, the live cast-flow
///   resolve body for Coiling Rebirth is provided by the data-driven oracle
///   binder (the reanimation half via ReanimateToBattlefieldTemplate); the
///   gift + token-copy rider lives here and is exercised by the unit test —
///   the same posture as <see cref="WildfireHowlFactory"/> /
///   <see cref="LongRiversPullFactory"/>.
/// </summary>
[CardName("Coiling Rebirth")]
public static class CoilingRebirthFactory
{
    public const string CardName = "Coiling Rebirth";
    public const string PrintedManaCost = "{3}{B}{B}";

    /// <summary>P/T the copy token is created as ("except it's 1/1").</summary>
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>Printed oracle text, kept here so the data-driven import
    /// path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Gift a card (You may promise an opponent a gift as you cast this " +
        "spell. If you do, they draw a card before its other effects.)\n" +
        "Return target creature card from your graveyard to the battlefield. " +
        "Then if the gift was promised and that creature isn't legendary, " +
        "create a token that's a copy of that creature, except it's 1/1.";

    /// <summary>Human-readable label for the Gift clause. Surfaced by the
    /// agent-prompt UI through
    /// <see cref="Majik.Core.Spells.IGiftClause.Description"/>.</summary>
    public const string GiftDescription = "a card";

    /// <summary>
    /// Construct Coiling Rebirth as a Sorcery card that implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> (so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> detects the cast-time gift
    /// hook) with owner / controller wired. The resolve SpellDefinition is
    /// built on demand via <see cref="BuildDefinition"/> (mirrors Wildfire
    /// Howl / Long River's Pull).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new CoilingRebirthCard(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Coiling Rebirth. A single 1..1 "target
    /// creature card from your graveyard" request (the reanimation half is
    /// unconditional). The "if the gift was promised and that creature isn't
    /// legendary" token-copy rider is applied at resolution, gated on the
    /// <see cref="Card.HasGiftPromised"/> sentinel read off
    /// <paramref name="card"/>.
    /// </summary>
    /// <param name="caster">Spell controller — the graveyard whose creature
    /// card is returned ("your graveyard") and the destination battlefield
    /// (CR 110.2). Also the controller of the 1/1 copy token (CR 707.2).</param>
    /// <param name="card">Source card; read at resolve time for the
    /// gift-promised branch (<see cref="Card.HasGiftPromised"/>). Required for
    /// the token-copy rider.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move (and the token creation) route through
    /// <see cref="ZoneService"/> so ETB triggers fire (CR 603.6a).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Card card,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature card from your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate,
                    // "your graveyard" — only the caster's graveyard is a legal
                    // source (CR 608.2b re-checked at resolution).
                    CandidateGatherer: _ => caster.Zones.Graveyard.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen => new IEffect[]
            {
                new Effect(
                    $"{CardName}: reanimate target creature card; gift rider → 1/1 copy token",
                    () => Resolve(caster, chosen, card, zoneService)),
            });
    }

    /// <summary>
    /// Resolve the reanimation + gift rider. CR 608.2b — the target must still
    /// be a creature card in the caster's graveyard; otherwise the spell does
    /// nothing. After the return, when the gift was promised AND the returned
    /// creature is not legendary, create a 1/1 copy token under the caster.
    /// </summary>
    private static void Resolve(
        Player caster,
        ChosenSpellParams chosen,
        Card card,
        ZoneService? zoneService)
    {
        if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0) return;

        // CR 608.2b — illegal-on-resolution checks: must be a creature card,
        // still in the caster's graveyard ("your graveyard").
        if (chosen.Targets[0][0] is not Creature creature) return;
        if (creature.Zone != ZoneType.Graveyard) return;
        if (!ReferenceEquals(creature.Owner, caster)) return;

        // CR 701.20 — graveyard → battlefield under the caster's control
        // (CR 110.2). ZoneService-routed when supplied so ETB triggers fire
        // (CR 603.6a).
        Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);

        // "Then if the gift was promised and that creature isn't legendary,
        // create a token that's a copy of that creature, except it's 1/1."
        // CR 205.4a — a legendary permanent carries the Legendary supertype.
        if (!card.HasGiftPromised) return;
        if (creature.HasSupertype(CardSupertype.Legendary)) return;

        CreateOnePowerOneToughnessCopy(creature, caster, zoneService);
    }

    /// <summary>
    /// CR 706.2 / CR 706.10 — create a token that's a copy of
    /// <paramref name="source"/>, except its P/T is 1/1. The token snapshots
    /// the source's printed name, subtypes, keyword abilities, and colour
    /// identity (the v1 lossy copy snapshot shared with
    /// <see cref="CacklingCounterpartFactory"/>); the printed P/T is replaced
    /// by 1/1 per the "except it's 1/1" clause. CR 707.2 — the token's
    /// controller is <paramref name="caster"/>.
    /// </summary>
    public static Creature CreateOnePowerOneToughnessCopy(
        Creature source,
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(caster);

        var keywords = source.Abilities
            .OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var colours = CardColors.GetColors(source).ToList();

        var spec = new TokenFactory.TokenSpec(
            Name: source.Name,
            // "except it's 1/1" — the copy's P/T is overridden to 1/1.
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: source.Subtypes.ToArray(),
            Keywords: keywords,
            Colors: colours);

        return TokenFactory.CreateOnBattlefield(spec, caster, zoneService);
    }

    /// <summary>
    /// CR 701.59 Gift delivery — the recipient draws a card ("Gift a card").
    /// Routed through <see cref="Fx.DrawCards"/> so the engine's
    /// draw-replacement bus (CR 614) + draw triggers see the gift draw. Shared
    /// shape with <see cref="WildfireHowlFactory.DeliverCardGift"/>.
    /// </summary>
    public static void DeliverCardGift(Player recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        Fx.DrawCards(recipient, 1);
    }

    /// <summary>
    /// Concrete card class for Coiling Rebirth. Subclasses <see cref="Sorcery"/>
    /// and implements <see cref="Majik.Core.Spells.IGiftClause"/> so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> picks up the gift hook at
    /// cast time. Kept nested in the factory so the Gift wiring stays adjacent
    /// to the printed-effect implementation (mirrors
    /// <see cref="WildfireHowlFactory.WildfireHowlCard"/>).
    /// </summary>
    public sealed class CoilingRebirthCard : Sorcery, IGiftClause
    {
        public CoilingRebirthCard(string name, string manaCost) : base(name, manaCost) { }

        /// <summary>Simulation copy constructor. No extra runtime fields.</summary>
        private CoilingRebirthCard(CoilingRebirthCard src) : base(src) { }

        /// <inheritdoc cref="Majik.Core.Cards.Card.CloneForSim"/>
        internal override Majik.Core.Cards.Card CloneForSim() => new CoilingRebirthCard(this);

        /// <inheritdoc />
        public string Description => GiftDescription;

        /// <inheritdoc />
        public void DeliverTo(Player recipient, Majik.Core.Spells.Spell spell)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            DeliverCardGift(recipient);
        }
    }
}
