using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ensoul Artifact (Magic 2015, {1}{U}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant artifact
///    Enchanted artifact is a creature with base power and toughness 5/5
///    in addition to its other types."
///
/// ## Shape source
/// Card identity (name, {1}{U}, Enchantment — Aura) is loaded from
/// <c>Majik.Core/CardData/Cards/ensoul-artifact.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (same posture as
/// <see cref="TezzeretsTouchFactory"/>). The animate body is hand-wired below —
/// the JSON ability schema expresses neither a "becomes a creature with base
/// P/T" continuous effect nor cast-time Aura targeting.
///
/// Ensoul Artifact is the cast+attach+animate shell of
/// <see cref="TezzeretsTouchFactory"/> minus its LTB-return trigger, so it
/// reuses that factory's public continuous-effect classes
/// (<see cref="AuraAnimateArtifactEffect"/> + <see cref="AuraSetBasePTEffect"/>).
///
/// ## Implemented (v1)
/// - Enchantment — Aura at {1}{U}; standard
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
///   target), so a control/attachment change tracks correctly. The aura's
///   static body persists while attached (CR 613 continuous) — it does NOT
///   expire at end of turn.
///
/// ## Deferred (v1 gaps)
/// - <b>Sorcery-speed cast restriction</b>: not enforced — same gap as every
///   other Aura factory in this repo (Auras are cast at sorcery speed by
///   CR 307.5 / 601.3e, not yet wired engine-wide).
/// </summary>
[CardName("Ensoul Artifact")]
public sealed class EnsoulArtifactFactory
{
    public const string CardName = "Ensoul Artifact";

    /// <summary>CR 613.7b — the base power the enchanted artifact becomes.</summary>
    public const int BasePower = 5;

    /// <summary>CR 613.7b — the base toughness the enchanted artifact becomes.</summary>
    public const int BaseToughness = 5;

    public static readonly IReadOnlyList<string> OracleText = new[]
    {
        "Enchant artifact",
        "Enchanted artifact is a creature with base power and toughness 5/5 " +
            "in addition to its other types.",
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("ensoul-artifact");

    /// <summary>
    /// Construct Ensoul Artifact with no live continuous effect. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null);

    /// <summary>
    /// Construct Ensoul Artifact.
    /// <para>When <paramref name="effects"/> is supplied, the Layer-4 animate
    /// grant + Layer-7b set-base 5/5 are registered against the service (both
    /// gated on the aura being on the battlefield AND attached to an
    /// artifact).</para>
    /// </summary>
    public static Enchantment Create(Player owner, ContinuousEffectsService? effects)
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
        // Layer-7b set-base lands on (CR 613.7b). Reuses the public continuous
        // effects from TezzeretsTouchFactory (identical animate body).
        // -----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new AuraAnimateArtifactEffect(card));
            effects.Register(new AuraSetBasePTEffect(card, BasePower, BaseToughness));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Ensoul Artifact.
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
}
