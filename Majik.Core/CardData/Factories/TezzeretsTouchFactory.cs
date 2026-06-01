using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tezzeret's Touch (Aether Revolt, {1}{U}{B}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant artifact
///    Enchanted artifact is a creature with base power and toughness 5/5
///    in addition to its other types.
///    When enchanted artifact is put into a graveyard, return that card to
///    its owner's hand."
///
/// ## Shape source
/// Card identity (name, {1}{U}{B}, Enchantment — Aura) is loaded from
/// <c>Majik.Core/CardData/Cards/tezzerets-touch.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (same posture as
/// <see cref="CombatResearchFactory"/>). The animate body + LTB-return are
/// hand-wired below — the JSON ability schema expresses neither a
/// "becomes a creature with base P/T" continuous effect nor an
/// enchanted-permanent-dies trigger.
///
/// ## Implemented (v1)
/// - Enchantment — Aura at {1}{U}{B}; standard
///   <see cref="AuraSpellDefinitionBuilder"/> cast-time targeting
///   ("Enchant artifact" — CR 702.5b).
/// - <b>Animate body (CR 613)</b>: while the aura is on the battlefield AND
///   attached to an artifact, the enchanted artifact becomes a creature with
///   base power and toughness 5/5 in addition to its other types. Modeled as
///   a pair of aura-aware continuous effects gated on the aura's
///   <see cref="Permanent.AttachedTo"/> slot:
///     - <see cref="AuraAnimateArtifactEffect"/> — Layer 4 (CR 613.1c): adds
///       <see cref="CardType.Creature"/> on top of the artifact's printed
///       types ("in addition to its other types" — the Artifact type is
///       preserved). The Layer-4 Creature grant drives
///       <see cref="ContinuousEffectsService"/>'s creature-row upgrade so the
///       artifact gets a P/T row to receive the set-base below.
///     - <see cref="AuraSetBasePTEffect"/> — Layer 7b (CR 613.7b): sets the
///       enchanted artifact's base power/toughness to 5/5.
///   Both read <see cref="Permanent.AttachedTo"/> dynamically (no fixed
///   target), so a control/attachment change tracks correctly. Unlike the
///   manland cycle's animate effects, these do NOT expire at end of turn —
///   the aura's static body persists while attached (CR 613 continuous).
/// - <b>LTB return (CR 603.6b / 700.4)</b>: "When enchanted artifact is put
///   into a graveyard, return that card to its owner's hand." A
///   <see cref="TriggeredAbility"/> watches <see cref="CardMovedEvent"/> for
///   the currently/last-enchanted artifact entering the graveyard
///   (ToZone == Graveyard). On resolution the bearer card is moved from its
///   owner's graveyard to its owner's hand. The bearer reference is captured
///   via <see cref="Permanent.AttachedTo"/> at trigger time and latched in
///   <see cref="_lastBearer"/> so the return still fires after the aura
///   detaches (the artifact dying detaches the aura before/as it LTBs).
///
/// ## Deferred (v1 gaps)
/// - <b>Sorcery-speed cast restriction</b>: not enforced — same gap as every
///   other Aura factory in this repo (Auras are cast at sorcery speed by
///   CR 307.5 / 601.3e, not yet wired engine-wide).
/// - <b>Live trigger registration</b>: the LTB-return trigger is attached to
///   the aura's <see cref="Card.Abilities"/>; a live
///   <see cref="TriggerManager"/> is required for it to fire end-to-end
///   during play (same posture as <see cref="SpreadingSeasFactory"/>).
/// </summary>
[CardName("Tezzeret's Touch")]
public sealed class TezzeretsTouchFactory
{
    public const string CardName = "Tezzeret's Touch";

    /// <summary>CR 613.7b — the base power the enchanted artifact becomes.</summary>
    public const int BasePower = 5;

    /// <summary>CR 613.7b — the base toughness the enchanted artifact becomes.</summary>
    public const int BaseToughness = 5;

    public static readonly IReadOnlyList<string> OracleText = new[]
    {
        "Enchant artifact",
        "Enchanted artifact is a creature with base power and toughness 5/5 " +
            "in addition to its other types.",
        "When enchanted artifact is put into a graveyard, return that card " +
            "to its owner's hand.",
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("tezzerets-touch");

    /// <summary>
    /// Construct Tezzeret's Touch with the LTB-return trigger attached to the
    /// card shape but no live continuous effect. Suitable for shape /
    /// dispatcher / trigger tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Tezzeret's Touch.
    /// <para>When <paramref name="effects"/> is supplied, the Layer-4 animate
    /// grant + Layer-7b set-base 5/5 are registered against the service (both
    /// gated on the aura being on the battlefield AND attached to an
    /// artifact).</para>
    /// <para>The LTB-return trigger is always attached to the aura's
    /// <see cref="Card.Abilities"/>; when <paramref name="triggers"/> is
    /// supplied it is also registered so it fires during play. The return
    /// move routes through <paramref name="zoneService"/> when supplied so
    /// zone-change events fire.</para>
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // -----------------------------------------------------------------
        // Animate body — "Enchanted artifact is a creature with base power
        // and toughness 5/5 in addition to its other types." (CR 613)
        //
        // Layer 4 adds Creature (CR 613.1c — additive, Artifact preserved);
        // the Compute creature-row upgrade then provides a P/T row that the
        // Layer-7b set-base lands on (CR 613.7b).
        // -----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new AuraAnimateArtifactEffect(card));
            effects.Register(new AuraSetBasePTEffect(card, BasePower, BaseToughness));
        }

        // -----------------------------------------------------------------
        // LTB return — "When enchanted artifact is put into a graveyard,
        // return that card to its owner's hand." (CR 603.6b / 700.4)
        //
        // Latch the last-known bearer so the return still resolves after the
        // aura detaches: the artifact dying detaches the aura before/as it
        // LTBs, so AttachedTo can be null by the time the effect resolves.
        // -----------------------------------------------------------------
        Permanent? lastBearer = null;

        var ltbCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            var bearer = card.AttachedTo;
            if (bearer == null) return false;
            if (!ReferenceEquals(e.Card, bearer)) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            lastBearer = bearer; // latch for the resolving effect
            return true;
        });

        var ltbEffect = new Effect(
            $"{CardName} — return enchanted artifact to its owner's hand",
            () => ReturnToOwnersHand(lastBearer ?? card.AttachedTo, zoneService));

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Tezzeret's Touch.
    /// "Enchant artifact" (CR 702.5b) makes any artifact a legal target.
    /// CR 303.4f — on resolve, the aura enters the battlefield already
    /// attached to the chosen target.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target artifact",
            battlefield: battlefield,
            predicate: p => p != null && p.HasType(CardType.Artifact));
    }

    /// <summary>
    /// Return helper: move <paramref name="bearer"/> (the artifact card that
    /// was put into a graveyard) from its owner's graveyard to its owner's
    /// hand (CR 700.4 — "that card" is the physical card now in the
    /// graveyard). No-op if the bearer is unknown or no longer in a graveyard
    /// (CR 603.10c — the object may have moved before the return resolves).
    /// </summary>
    private static void ReturnToOwnersHand(Permanent? bearer, ZoneService? zoneService)
    {
        if (bearer == null) return;
        if (bearer.Zone != ZoneType.Graveyard) return;

        var graveOwner = bearer.Owner ?? bearer.Controller;
        if (graveOwner == null) return;

        if (zoneService != null)
        {
            zoneService.MoveCard(bearer, ZoneType.Graveyard, ZoneType.Hand, graveOwner);
        }
        else
        {
            graveOwner.Zones.Graveyard.RemoveCard(bearer);
            graveOwner.Zones.Hand.AddCard(bearer);
            bearer.SetZone(ZoneType.Hand);
        }
    }
}

/// <summary>
/// CR 613.1c — Layer 4 type-adding effect for Tezzeret's Touch. While the
/// aura is on the battlefield AND attached to an artifact, the enchanted
/// artifact gains <see cref="CardType.Creature"/> in addition to its other
/// types ("the Artifact type is preserved"). Reads the aura's
/// <see cref="Permanent.AttachedTo"/> slot dynamically; does NOT expire at
/// end of turn (the static body persists while attached). The Layer-4
/// Creature grant drives <see cref="ContinuousEffectsService"/>'s creature-row
/// upgrade so <see cref="AuraSetBasePTEffect"/> has a P/T row to land on.
/// </summary>
public sealed class AuraAnimateArtifactEffect : ContinuousEffect
{
    private readonly Permanent _aura;

    public AuraAnimateArtifactEffect(Permanent aura)
    {
        _aura = aura ?? throw new ArgumentNullException(nameof(aura));
    }

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _aura;

    public override bool IsActive() =>
        _aura.Zone == ZoneType.Battlefield
        && _aura.AttachedTo != null
        && _aura.AttachedTo.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        _aura.AttachedTo != null && ReferenceEquals(permanent, _aura.AttachedTo);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 613.1c — additive: Creature added on top of the printed
        // Artifact (and any other) type.
        chars.Types.Add(CardType.Creature);
    }
}

/// <summary>
/// CR 613.7b — Layer 7b set-base-P/T effect for Tezzeret's Touch. While the
/// aura is on the battlefield AND attached to an artifact, the enchanted
/// artifact's base power and toughness become the supplied values (5/5).
/// Reads the aura's <see cref="Permanent.AttachedTo"/> slot dynamically and
/// does NOT expire at end of turn. Overrides <see cref="AppliesTo(Permanent)"/>
/// so the effect is selected during the pre-upgrade <c>applicable</c> filter
/// (the artifact is not yet a creature row at that point), mirroring
/// <see cref="ManlandCycleBecomesPTEffect"/>.
/// </summary>
public sealed class AuraSetBasePTEffect : ContinuousEffect
{
    private readonly Permanent _aura;

    /// <summary>CR 613.7b — base power the enchanted artifact becomes.</summary>
    public int NewPower { get; }

    /// <summary>CR 613.7b — base toughness the enchanted artifact becomes.</summary>
    public int NewToughness { get; }

    public AuraSetBasePTEffect(Permanent aura, int power, int toughness)
    {
        _aura = aura ?? throw new ArgumentNullException(nameof(aura));
        NewPower = power;
        NewToughness = toughness;
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _aura;

    public override bool IsActive() =>
        _aura.Zone == ZoneType.Battlefield
        && _aura.AttachedTo != null
        && _aura.AttachedTo.Zone == ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        _aura.AttachedTo != null && ReferenceEquals(permanent, _aura.AttachedTo);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = NewPower;
        chars.Toughness = NewToughness;
    }
}
