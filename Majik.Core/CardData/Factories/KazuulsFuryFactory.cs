using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Kazuul's Fury // Kazuul's Cliffs (Zendikar Rising, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "As an additional cost to cast this spell, sacrifice a creature.
///    Kazuul's Fury deals damage equal to the sacrificed creature's
///    power to any target."
///
/// Back face — <see cref="KazuulsCliffsFactory"/> (Land —
/// "This land enters tapped." / "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face modelled by two independent <c>[CardName]</c>-dispatched
/// factories — same architecture as
/// <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>
/// and <see cref="ShatterskullSmashingFactory"/> /
/// <see cref="ShatterskullTheHammerPassFactory"/> (MDFC spell-front +
/// tapland-back). Casting the front face resolves "Kazuul's Fury" → this
/// factory → an <see cref="Instant"/> with the sacrifice-cost damage spell.
/// Playing the back face resolves "Kazuul's Cliffs" →
/// <see cref="KazuulsCliffsFactory"/> → a simple tapland.
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{2}{R}</c>, mono-red, owner / controller wired.
///   Card shape comes from the embedded JSON (<c>kazuuls-fury.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
///   tracker is attached after construction (front = "Kazuul's Fury",
///   back = "Kazuul's Cliffs"); starts on the front face.
/// - <b>Additional cost — sacrifice a creature (CR 601.2f)</b>:
///   <see cref="SacrificeCreatureCost"/>, which exposes the sacrificed
///   creature via <see cref="SacrificeCreatureCost.Sacrificed"/>. Same
///   "sacrifice as additional cost, then read the sacrificed permanent's
///   stats" posture as Bone Shards / the exile-cost cards (Abhorrent
///   Oculus, Hogaak). <see cref="SpellCastFlow"/> rejects the cast when
///   the cost can't be paid (CR 601.2g — no creature to sacrifice).
/// - <b>Damage equal to the sacrificed creature's power to any target</b>:
///   one 1..1 "any target" <see cref="TargetRequest"/>. On resolution
///   (<see cref="BuildSpellDefinition"/>):
///     <list type="bullet">
///       <item>The damage amount is the sacrificed creature's power
///         (CR 112.7a — last-known information: the creature has already
///         left the battlefield as part of the cost, so its
///         last-known power on the battlefield is used; for a vanilla
///         creature <see cref="Creature.Power"/> returns its base power
///         off-battlefield).</item>
///       <item>Damage is dealt to the chosen target via
///         <see cref="Fx.DealDamageAny"/> — Player → life loss
///         (CR 119.3), Creature → marked damage (CR 119.2),
///         Planeswalker → loyalty removal (CR 306.7).</item>
///       <item>CR 608.2b — illegal-at-resolution target (off battlefield,
///         wrong shape) → clean no-op.</item>
///       <item>Zero-power sacrifice → 0 damage (<see cref="Fx.DealDamageAny"/>
///         no-ops at amount ≤ 0).</item>
///     </list>
///
/// ## References
///
/// - <see cref="BoneShardsFactory"/> — same sacrifice-as-additional-cost
///   (CR 601.2f) composition into <see cref="SpellDefinition.AdditionalCosts"/>.
/// - <see cref="ShatterskullSmashingFactory"/> — companion ZNR MDFC
///   spell-front + tapland-back damage pair showing the same two-factory
///   architecture and any-target damage resolution.
/// - <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>
///   — ZNR MDFC pair with an unconditional enters-tapped back face (the
///   shape Kazuul's Cliffs mirrors).
/// </summary>
[CardName("Kazuul's Fury")]
public static class KazuulsFuryFactory
{
    public const string CardName = "Kazuul's Fury";
    public const string BackName = "Kazuul's Cliffs";
    public const string Slug = "kazuuls-fury";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>
    /// Construct the front face of Kazuul's Fury as an Instant with
    /// owner / controller wired and the <see cref="MdfcState"/> face
    /// tracker attached (starts on the front face). Card shape is built
    /// from the embedded JSON definition. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Kazuul's Cliffs) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the resolve-time "deal damage equal to the sacrificed
    /// creature's power to any target" <see cref="SpellDefinition"/>.
    ///
    /// The sacrifice-a-creature additional cost (CR 601.2f) is composed
    /// into <see cref="SpellDefinition.AdditionalCosts"/>. On resolution
    /// the damage amount is read from the sacrificed creature:
    ///   <list type="number">
    ///     <item>From <see cref="ChosenSpellParams.AdditionalCostPaymentsOrEmpty"/>
    ///       (the production path — <see cref="SpellCastFlow"/> threads the
    ///       paid cost here), OR</item>
    ///     <item>From the supplied <paramref name="sacrificeCost"/> closure
    ///       (the test / dispatcher path).</item>
    ///   </list>
    /// </summary>
    /// <param name="sacrificeCost">The sacrifice-a-creature additional
    /// cost (CR 601.2f). After payment its
    /// <see cref="SacrificeCreatureCost.Sacrificed"/> exposes the creature
    /// whose power becomes the damage amount.</param>
    /// <param name="resolver">Maps the agent-supplied raw target token to
    /// the live engine object (creature / player / planeswalker). Pass
    /// <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildSpellDefinition(
        SacrificeCreatureCost sacrificeCost,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(sacrificeCost);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                object? rawTarget = chosen.Targets.Count > 0 && chosen.Targets[0].Count > 0
                    ? chosen.Targets[0][0]
                    : null;

                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: deal damage equal to the sacrificed creature's power to any target",
                        () =>
                        {
                            // Resolve the sacrificed creature: production path
                            // threads the paid cost through ChosenSpellParams;
                            // the test / dispatcher path uses the closure cost.
                            var sacrificed = ResolveSacrificed(chosen, sacrificeCost);

                            // CR 112.7a — last-known information. The creature
                            // has left the battlefield as part of the cost, so
                            // its last-known power is used. For a vanilla
                            // creature, Creature.Power off-battlefield returns
                            // its base power.
                            var damage = sacrificed?.Power ?? 0;
                            if (damage <= 0) return; // 0-power sac → no damage.

                            if (rawTarget == null) return;
                            var target = resolver(rawTarget);

                            // CR 608.2b — resolution-time legality: the target
                            // must still be a legal "any target" (creature /
                            // player / planeswalker on the battlefield for
                            // permanents). Anything else → clean no-op.
                            if (!IsLegalTarget(target)) return;

                            // CR 119 / CR 120.3 — deal damage (Fx.DealDamageAny
                            // routes Planeswalker → loyalty, Player → life,
                            // Creature → marked damage).
                            Fx.DealDamageAny(target, damage);
                        }),
                };
            },
            AdditionalCosts: new IAdditionalCost[] { sacrificeCost });
    }

    /// <summary>
    /// Find the sacrificed creature. Prefers the
    /// <see cref="ChosenSpellParams.AdditionalCostPaymentsOrEmpty"/> entry
    /// (production path) and falls back to the closure-supplied
    /// <paramref name="sacrificeCost"/> (test / dispatcher path).
    /// </summary>
    private static Creature? ResolveSacrificed(
        ChosenSpellParams chosen,
        SacrificeCreatureCost sacrificeCost)
    {
        foreach (var paid in chosen.AdditionalCostPaymentsOrEmpty)
        {
            if (paid is SacrificeCreatureCost sc && sc.Sacrificed != null)
            {
                return sc.Sacrificed;
            }
        }
        return sacrificeCost.Sacrificed;
    }

    /// <summary>
    /// CR 115.4 — legal "any target": a player, a creature on the
    /// battlefield, or a planeswalker on the battlefield.
    /// </summary>
    private static bool IsLegalTarget(object live)
    {
        if (live is Player) return true;
        if (live is Creature c && c.Zone == ZoneType.Battlefield) return true;
        if (live is Planeswalker pw && pw.Zone == ZoneType.Battlefield) return true;
        return false;
    }
}
