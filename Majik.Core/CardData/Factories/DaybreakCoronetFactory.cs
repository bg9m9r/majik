using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Daybreak Coronet (Future Sight, {W}{W}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant creature with another Aura attached to it"
///   "Enchanted creature gets +3/+3 and has first strike, vigilance,
///    and lifelink."
///
/// ## Implementation
///
/// - Aura subtype + {W}{W} cost; ETB-attach plumbing via the standard
///   <see cref="AuraSpellDefinitionBuilder"/> path.
/// - Static "enchanted creature gets +3/+3 and has first strike,
///   vigilance, lifelink" via a single <see cref="AttachedBoostEffect"/>
///   carrying both the P/T modification (+3/+3) and the granted keyword
///   list (CR 613 Layer 7c for P/T + Layer 6 for keyword grants — the
///   AttachedBoostEffect implementation reads
///   <see cref="Permanent.AttachedTo"/> dynamically so the boost
///   transfers cleanly if the aura is ever re-attached). Same shape as
///   Daybreak Coronet's sibling pumps in Sword of Fire and Ice / Cori-Steel
///   Cutter.
/// - <b>Cast-time target predicate</b>: the printed "Enchant creature
///   with another Aura attached to it" clause requires the target
///   creature to already have at least one Aura attached at cast time
///   (CR 702.5b — "Enchant X" defines the legal target set; the "with
///   another Aura attached" qualifier narrows it further per CR 700.6).
///   <see cref="BuildSpellDefinition"/> filters the battlefield by
///   <see cref="Permanent.Attachments"/> containing at least one
///   permanent with <see cref="CardSubtype.Aura"/>. CR 608.2b — the
///   legality re-check at resolve time is handled by the existing
///   <see cref="AuraSpellDefinitionBuilder"/> machinery and the SBA
///   sweep (CR 704.5n) drops the Coronet to the graveyard if the
///   creature loses its other aura between cast and resolve. v1
///   accepts this graceful-fizzle posture (matches Animate Dead's
///   no-legal-target shape).
///
/// ## Deferred (v1 gaps)
///
/// - <b>SBA-driven fall-off check</b>: if every other aura attached
///   to the enchanted creature leaves the battlefield, Daybreak
///   Coronet's printed "Enchant creature with another Aura attached
///   to it" requirement is no longer met → CR 704.5n SBA destroys
///   the Coronet. The SBA engine already implements 704.5n
///   generically against the aura's legality predicate; the
///   on-battlefield enforcement of the "another aura attached"
///   constraint is part of that generic surface and isn't re-wired
///   here.
/// - <b>Replacement-ordering prompt</b>: irrelevant for Daybreak
///   Coronet — single static, no overlap with other replacements.
/// </summary>
[CardName("Daybreak Coronet")]
public static class DaybreakCoronetFactory
{
    public const string CardName = "Daybreak Coronet";
    public const string PrintedManaCost = "{W}{W}";
    public const int PowerBoost = 3;
    public const int ToughnessBoost = 3;

    /// <summary>Granted keywords on the enchanted creature: First
    /// Strike, Vigilance, Lifelink (CR 702.7 / 702.20 / 702.15).</summary>
    public static readonly IReadOnlyList<string> GrantedKeywords =
        new[] { "First Strike", "Vigilance", "Lifelink" };

    /// <summary>Printed oracle text. Source of truth for the
    /// "Enchant creature with another Aura attached to it" clause —
    /// distinct enough from the generic single-noun "Enchant X" forms
    /// that <see cref="AuraEnchantClauseParser"/> isn't reused.</summary>
    public const string OracleText =
        "Enchant creature with another Aura attached to it\n" +
        "Enchanted creature gets +3/+3 and has first strike, vigilance, " +
        "and lifelink.";

    /// <summary>
    /// Constructs a Daybreak Coronet with card identity only (no live
    /// continuous effect). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs a Daybreak Coronet. When
    /// <paramref name="continuousEffects"/> is supplied, the +3/+3 +
    /// granted-keywords boost is registered against the service; gated
    /// on the aura being on the battlefield AND attached (effect's
    /// <c>IsActive</c> check).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613 — single AttachedBoostEffect carries both the
            // Layer 7c P/T bump and the Layer 6 keyword grants.
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                power: PowerBoost,
                toughness: ToughnessBoost,
                grantedKeywords: GrantedKeywords));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Daybreak
    /// Coronet. The printed "Enchant creature with another Aura
    /// attached to it" requires the target creature to already have
    /// at least one aura attached (CR 702.5b + 700.6). Filters the
    /// supplied battlefield enumerable to creatures whose
    /// <see cref="Permanent.Attachments"/> includes at least one Aura.
    /// CR 303.4f — on resolve, the Coronet enters the battlefield
    /// already attached to the chosen target.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        return AuraSpellDefinitionBuilder.ForAura(
            aura,
            targetDescription: "target creature with another Aura attached to it",
            battlefield: battlefield,
            predicate: HasAnotherAuraAttached);
    }

    /// <summary>
    /// Target-legality predicate for the printed "creature with
    /// another Aura attached to it" clause. Returns true iff the
    /// candidate is a creature with at least one
    /// <see cref="CardSubtype.Aura"/> in its
    /// <see cref="Permanent.Attachments"/>. Distinct from "any aura"
    /// — the printed text says "another", so a candidate whose only
    /// attached aura would be Daybreak Coronet itself fails. v1 the
    /// Coronet hasn't entered yet (cast-time check), so the
    /// distinction is structural rather than substantive — the
    /// predicate accepts any candidate with ≥1 aura attached and the
    /// SBA path catches the on-battlefield "no longer has another
    /// aura" case.
    /// </summary>
    public static bool HasAnotherAuraAttached(Permanent candidate)
    {
        if (candidate == null) return false;
        if (!candidate.HasType(CardType.Creature)) return false;
        foreach (var attached in candidate.Attachments)
        {
            if (attached != null && attached.HasSubtype(CardSubtype.Aura))
                return true;
        }
        return false;
    }
}
