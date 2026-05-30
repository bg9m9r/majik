using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for All That Glitters (Throne of Eldraine, {1}{W}).
///
/// Enchantment — Aura. Oracle text (verified against Scryfall 2026-05-29):
///   "Enchant creature"
///   "Enchanted creature gets +1/+1 for each artifact and/or enchantment
///    you control."
///
/// ## Implementation
///
/// Combines two existing analogue shapes:
///   - <b>Aura wiring + plain "Enchant creature" targeting</b> — same as
///     <see cref="DeadWeightFactory"/>: <see cref="AuraSpellDefinitionBuilder"/>
///     with the any-creature predicate (CR 702.5b — "Enchant creature"
///     makes every creature on the battlefield a legal target). CR 303.4f —
///     on resolution the aura enters already attached to the chosen target.
///   - <b>Dynamic +N/+N where N = controller's live count of permanents
///     that are an artifact and/or an enchantment</b> — same closure
///     pattern as <see cref="CranialPlatingFactory"/>: the dynamic-N
///     <see cref="AttachedBoostEffect"/> overload (Layer 7c, CR 613.3c)
///     samples <see cref="CountArtifactsAndEnchantments"/> at each layer
///     pass. The aura reads its CURRENT controller dynamically so a
///     control-change effect re-targets the count correctly, and the
///     boost transfers automatically if the aura is re-attached.
///
/// ## Self-counting
///
/// All That Glitters is itself an enchantment you control, so it counts
/// toward its own boost — the printed text has no "other" carve-out (CR
/// 109.2 / 700.5). With the aura the only relevant permanent, the boost is
/// +1/+1. This mirrors Cranial Plating counting itself among "artifacts
/// you control".
///
/// ## "and/or" — count once
///
/// A single permanent that is BOTH an artifact and an enchantment (an
/// artifact-enchantment) satisfies "artifact and/or enchantment" but is
/// counted only once — CR 700.5: a permanent matching multiple parts of an
/// "and/or" clause is still a single permanent. <see cref="CountArtifactsAndEnchantments"/>
/// counts each qualifying permanent once via a single
/// <c>HasType(Artifact) || HasType(Enchantment)</c> predicate.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied the boost is
/// registered immediately; its <c>IsActive</c> gates on the aura being on
/// the battlefield AND attached to a battlefield permanent, so an
/// unattached / off-battlefield aura contributes nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests.
/// </summary>
[CardName("All That Glitters")]
public static class AllThatGlittersFactory
{
    public const string CardName = "All That Glitters";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Constructs an All That Glitters with card identity only (no live
    /// continuous effect). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, continuousEffects: null);

    /// <summary>
    /// Constructs an All That Glitters. When
    /// <paramref name="continuousEffects"/> is supplied, the dynamic
    /// +N/+N boost (Layer 7c, CR 613.3c) is registered against the
    /// service; the effect gates on the aura being on the battlefield AND
    /// attached to a battlefield permanent.
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
            // CR 613.3c — Layer 7c P/T modification. Dynamic-N closures
            // sample the controller's live artifact/enchantment count at
            // each layer pass (same posture as Cranial Plating).
            continuousEffects.Register(new AttachedBoostEffect(
                source: card,
                powerFn: () => CountArtifactsAndEnchantments(card),
                toughnessFn: () => CountArtifactsAndEnchantments(card)));
        }

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for All That
    /// Glitters. "Enchant creature" — any creature on the supplied
    /// battlefield is a legal target (CR 702.5b). CR 303.4f — on resolve
    /// the aura enters the battlefield already attached to the chosen
    /// target.
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
            predicate: static p => p.HasType(CardType.Creature),
            intent: BotIntent.Buff);
    }

    /// <summary>
    /// Live count of permanents on the aura's CURRENT controller's
    /// battlefield that are an artifact and/or an enchantment. Each
    /// qualifying permanent is counted exactly once (CR 700.5 — an
    /// artifact-enchantment is a single permanent, not two). Reads the
    /// controller dynamically (not at factory-construction time) so a
    /// control-change effect re-targets the count correctly. Defaults to
    /// 0 when the aura has no live controller (off-battlefield / orphaned)
    /// so the boost gates cleanly via <see cref="AttachedBoostEffect.IsActive"/>.
    /// </summary>
    public static int CountArtifactsAndEnchantments(Permanent aura)
    {
        var ctrl = aura.Controller ?? aura.Owner;
        if (ctrl == null) return 0;
        return ctrl.Zones.Battlefield.GetCards()
            .Count(c => c.HasType(CardType.Artifact)
                     || c.HasType(CardType.Enchantment));
    }
}
