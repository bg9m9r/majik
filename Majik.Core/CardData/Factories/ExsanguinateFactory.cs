using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Exsanguinate (Worldwake / reprints, {X}{B}{B}).
///
/// Sorcery. Oracle text (verified against the embedded Scryfall seed
/// 2026-06-24):
///   "Each opponent loses X life. You gain life equal to the life lost this
///    way."
///
/// ## Implementation
///
/// Card shape (name, Sorcery, {X}{B}{B}) is materialised from the embedded
/// JSON definition (<c>exsanguinate.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The {X}{B}{B} cost makes the
/// card's <see cref="SpellDefinition.HasVariableX"/> true so the cast flow
/// prompts for X (CR 601.2b) and stamps it onto
/// <see cref="ChosenSpellParams.X"/>.
///
/// The on-resolve drain is built via <see cref="BuildSpellDefinition"/>, the
/// single source of truth shared by:
///   * the production cast path — <see cref="OracleSpellBinder"/> binds the
///     seed oracle text to this definition through
///     <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.ExsanguinateDrainTemplate"/>
///     (the named-factory <see cref="BuildSpellDefinition"/> is NOT itself in
///     the prod path — cards are resolved AT CAST TIME BY NAME via the binder
///     registry, so the template delegates here to keep one implementation), and
///   * the unit test, which exercises the resolve body directly.
///
/// ## Resolve semantics (CR notes)
/// - <b>X</b> is read from <see cref="ChosenSpellParams.X"/> (CR 601.2b /
///   CR 107.3 — the announced value chosen at cast time).
/// - <b>"Each opponent loses X life"</b> — CR 109.5 (no targets; "each
///   opponent" is a global set evaluated on resolution). Every player in
///   <see cref="ChosenSpellParams.AllPlayers"/> except the caster (and any who
///   have already lost — CR 800.4a) loses X life via
///   <see cref="Player.LoseLife"/> (CR 119.3).
/// - <b>"You gain life equal to the life lost this way"</b> — CR 119.3: a
///   SEPARATE life-change event. The caster gains the TOTAL life lost across
///   all opponents. v1 totals <c>X × opponentCount</c>, exact whenever every
///   opponent simply loses X. Same primitive-level posture as the rest of the
///   lose-life/gain-life drain family (Gray Merchant of Asphodel / The Meathook
///   Massacre): <see cref="Player.LoseLife"/> reports no committed-loss amount,
///   so a "can't lose life" / loss-replacement effect on a given opponent is
///   not yet subtracted from the lifegain.
/// - <b>X = 0</b> — CR 119.4: losing 0 life is not losing life; the resolve
///   body is a clean no-op (no life-loss, no lifegain).
/// </summary>
[CardName("Exsanguinate")]
public static class ExsanguinateFactory
{
    public const string CardName = "Exsanguinate";
    public const string Slug = "exsanguinate";
    public const string PrintedManaCost = "{X}{B}{B}";

    /// <summary>Build the card shape from the embedded JSON definition. This
    /// is the overload <see cref="NamedCardFactory"/> dispatches to.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Exsanguinate — a variable-X
    /// spell with no modes and no targets ("each opponent" is global, not a
    /// chosen target — CR 109.5). The resolve body reads the chosen X and the
    /// player roster off the <see cref="ChosenSpellParams"/> stamped by the
    /// cast flow.
    /// </summary>
    /// <param name="caster">Spell controller — the life-gain recipient
    /// (CR 119.3) and the player excluded from "each opponent".</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: chosen => BuildResolveEffect(caster, chosen.X ?? 0, chosen.AllPlayers));
    }

    /// <summary>
    /// Build Exsanguinate's resolve effect — every player in
    /// <paramref name="allPlayers"/> except <paramref name="caster"/> loses
    /// <paramref name="x"/> life, then the caster gains the total life lost.
    /// Exposed for direct unit testing of the unique drain behaviour.
    /// </summary>
    /// <param name="caster">Life-gain recipient; excluded from the drain.</param>
    /// <param name="x">Chosen X — life each opponent loses.</param>
    /// <param name="allPlayers">Every player in the game. When null (a legacy
    /// caller that didn't plumb the roster) the effect is a no-op rather than
    /// throwing — same posture as
    /// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Damage.DamageSpellFactory.EachOpponentLosesLifeSpell"/>.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, int x, IReadOnlyList<Player>? allPlayers)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: each opponent loses {x} life; you gain that much life.",
                () =>
                {
                    // CR 119.4 — losing 0 life is not losing life. No loss, no
                    // lifegain.
                    if (x <= 0) return;
                    if (allPlayers == null) return;

                    // CR 109.5 / CR 119.3 — each opponent (every non-caster
                    // player who hasn't left the game, CR 800.4a) loses X life.
                    var lifeLost = 0;
                    foreach (var p in allPlayers)
                    {
                        if (ReferenceEquals(p, caster)) continue;
                        if (p.HasLost) continue;
                        p.LoseLife(x);
                        lifeLost += x;
                    }

                    // CR 119.3 — "you gain life equal to the life lost this
                    // way" is a SEPARATE life-change event from the losses.
                    if (lifeLost > 0) caster.GainLife(lifeLost);
                }),
        };
    }
}
