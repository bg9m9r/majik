using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Combat Research (Dominaria United, {U}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature
///    Enchanted creature has 'Whenever this creature deals combat damage
///    to a player, draw a card.'
///    As long as enchanted creature is legendary, it gets +1/+1 and has
///    ward {1}."
///
/// ## Shape source
/// Card identity (name, {U}, Enchantment — Aura, blue) is loaded from
/// <c>Majik.Core/CardData/Cards/combat-research.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The granted trigger + conditional
/// boost are attached in code below — the JSON ability schema expresses
/// neither a combat-damage-to-a-player trigger nor a conditional static, so
/// they are hand-rolled here (same posture as
/// <see cref="BorderlandRangerFactory"/>).
///
/// ## Implemented (v1)
/// - Enchantment — Aura at {U}; ETB-attach plumbing via the standard
///   <see cref="AuraSpellDefinitionBuilder"/> path (<see cref="BuildSpellDefinition"/>;
///   "Enchant creature" — CR 702.5b).
/// - <b>Granted trigger (CR 510 / CR 603.1)</b>: "Whenever this creature
///   deals combat damage to a player, draw a card." Modeled with the same
///   <see cref="EventTriggerCondition{CombatDamageDealtEvent}"/> shape the
///   Sword cycle uses (<see cref="SwordOfFireAndIceFactory"/>): matches any
///   <see cref="CombatDamageDealtEvent"/> whose <c>Source</c> is the
///   currently-enchanted creature AND whose <c>TargetPlayer != null</c> (the
///   printed text is "to a player"). On resolution it draws one card for the
///   enchanted creature's controller (CR 603.3c — the granted ability's
///   controller is the controller of the permanent it's on at the time it
///   triggers). The trigger reads <see cref="Permanent.AttachedTo"/>
///   dynamically so a control-change of the enchanted creature redirects the
///   draw correctly. Empty-library halts the draw and flags the loss-condition
///   SBA stamp (CR 704.5b / 120.3).
/// - <b>Conditional legendary boost (CR 613 Layer 7c)</b>: "As long as
///   enchanted creature is legendary, it gets +1/+1." Wired with the dynamic-N
///   constructor of <see cref="AttachedBoostEffect"/> — the power/toughness
///   closures sample <see cref="Permanent.AttachedTo"/> at each layer pass and
///   return +1/+1 only while the enchanted creature has the Legendary
///   supertype, +0/+0 otherwise. Re-evaluated continuously, so the boost
///   appears/disappears if the creature gains/loses Legendary.
///
/// ## Deferred (v1 gaps)
/// - <b>Ward {1} (CR 702.21)</b>: granted as a <b>marker keyword</b> while the
///   enchanted creature is legendary (added to the creature's computed
///   <c>Keywords</c> only when Legendary). This matches the engine-wide Ward
///   posture: <see cref="Majik.Core.Keywords.WardEffect"/> exists as a
///   stand-alone helper but the spell-resolution path does not yet consult it
///   (same gap as <see cref="KappaCannoneerFactory"/>, Reality Smasher, etc.).
///   The keyword is observable for "has ward"-matters queries; the
///   counter-unless-pay enforcement lands when the spell-resolution Ward
///   consultation does, engine-wide.
/// - <b>"You draw" attribution</b>: the granted draw goes to the enchanted
///   creature's controller via a closure reading <c>AttachedTo.Controller</c>;
///   the chosen-target plumbing is not needed (the printed ability has no
///   targets).
/// </summary>
[CardName("Combat Research")]
public static class CombatResearchFactory
{
    public const string CardName = "Combat Research";

    /// <summary>CR 613 Layer 7c — power/toughness bonus while legendary.</summary>
    public const int LegendaryPowerBoost = 1;
    public const int LegendaryToughnessBoost = 1;

    /// <summary>CR 702.21 — printed ward cost: {1}. Marker keyword only (see
    /// type-doc deferred note).</summary>
    public const string WardCost = "{1}";

    public static readonly IReadOnlyList<string> OracleText = new[]
    {
        "Enchant creature",
        "Enchanted creature has \"Whenever this creature deals combat damage " +
            "to a player, draw a card.\"",
        "As long as enchanted creature is legendary, it gets +1/+1 and has ward {1}.",
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("combat-research");

    /// <summary>
    /// Construct Combat Research with the granted combat trigger attached to
    /// the card shape but no live continuous effect. Suitable for shape /
    /// dispatcher / trigger-gating tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Combat Research. When <paramref name="continuousEffects"/> is
    /// supplied, the conditional "+1/+1 and ward {1} while legendary" boost is
    /// registered against the service (gated on the aura being on the
    /// battlefield AND attached to a legendary creature). The granted
    /// combat-damage trigger is always attached to the aura's
    /// <see cref="Card.Abilities"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Granted trigger — "Whenever this creature deals combat damage to a
        // player, draw a card." (CR 510 / CR 603.1)
        //
        // Same shape as the Sword cycle's combat-damage trigger: match any
        // CombatDamageDealtEvent whose Source is the currently-enchanted
        // creature AND TargetPlayer != null. On resolution, draw one card for
        // the enchanted creature's controller (CR 603.3c).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: enchanted creature dealt combat damage to a player — draw a card",
            () =>
            {
                // CR 603.3c — the granted ability's controller is the
                // controller of the enchanted creature. Fall back to the
                // aura's controller if (somehow) detached at resolution.
                var drawFor = card.AttachedTo?.Controller ?? card.Controller ?? owner;
                DrawOne(drawFor);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (e.TargetPlayer == null) return false;
                var enchanted = card.AttachedTo;
                if (enchanted == null) return false;
                return ReferenceEquals(e.Source, enchanted);
            }),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);

        // ----------------------------------------------------------------
        // Conditional legendary boost — "As long as enchanted creature is
        // legendary, it gets +1/+1 and has ward {1}." (CR 613 Layer 7c +
        // CR 702.21)
        //
        // Dynamic-N AttachedBoostEffect: the P/T closures and the
        // (conditionally-applied) Ward marker keyword are gated on the
        // enchanted creature having the Legendary supertype at each layer
        // pass. AttachedBoostEffect adds its keyword list unconditionally
        // while active, so a dedicated conditional effect carries the Ward
        // marker; the P/T boost likewise reads Legendary via the closures.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => IsAttachedToLegendary(card) ? LegendaryPowerBoost : 0,
                toughnessFn: () => IsAttachedToLegendary(card) ? LegendaryToughnessBoost : 0));

            continuousEffects.Register(new ConditionalWardWhileLegendaryEffect(card));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Combat Research.
    /// The printed "Enchant creature" line (CR 702.5b) makes any creature a
    /// legal target. Filters the supplied battlefield to creatures.
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
            targetDescription: "target creature",
            battlefield: battlefield,
            predicate: p => p != null && p.HasType(CardType.Creature));
    }

    /// <summary>True iff the aura is attached to a creature with the Legendary
    /// supertype (CR 205.4 — Legendary supertype gates the bonus).</summary>
    internal static bool IsAttachedToLegendary(Permanent aura)
    {
        var enchanted = aura.AttachedTo;
        return enchanted != null
            && enchanted.HasType(CardType.Creature)
            && enchanted.HasSupertype(CardSupertype.Legendary);
    }

    /// <summary>
    /// Draw a single card for <paramref name="player"/> via raw library → hand
    /// zone moves. Empty-library halts the draw and stamps
    /// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> so the SBA loop
    /// notes the loss condition (CR 704.5b / 120.3). Mirrors
    /// <see cref="SwordOfFireAndIceFactory"/>'s simple-draw shape.
    /// </summary>
    private static void DrawOne(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            player.MarkTriedToDrawFromEmptyLibrary();
            return;
        }
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}

/// <summary>
/// CR 613 Layer 6 / CR 702.21 — conditional Ward marker grant: while the aura
/// is on the battlefield AND attached to a legendary creature, the enchanted
/// creature gains the Ward marker keyword. Companion to the dynamic-N
/// <see cref="AttachedBoostEffect"/> carrying the +1/+1 (which can't gate its
/// keyword list per-pass). Marker-only — the spell-resolution Ward
/// consultation lands engine-wide in a follow-up (see
/// <see cref="CombatResearchFactory"/> deferred notes).
/// </summary>
internal sealed class ConditionalWardWhileLegendaryEffect : ContinuousEffect
{
    private readonly Permanent _aura;

    public ConditionalWardWhileLegendaryEffect(Permanent aura)
    {
        _aura = aura ?? throw new ArgumentNullException(nameof(aura));
    }

    public override Layer Layer => Layer.Abilities;

    public override Permanent? Source => _aura;

    public override bool IsActive() =>
        _aura.Zone == ZoneType.Battlefield
        && _aura.AttachedTo != null
        && _aura.AttachedTo.Zone == ZoneType.Battlefield
        && CombatResearchFactory.IsAttachedToLegendary(_aura);

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(_aura.AttachedTo, creature);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Keywords.Add("Ward");
    }
}
