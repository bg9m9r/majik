using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rite of Replication (Zendikar, {2}{U}{U}).
///
/// Sorcery. Scryfall oracle text (verbatim, verified 2026-06-24):
///   "Kicker {5} (You may pay an additional {5} as you cast this spell.)
///    Create a token that's a copy of target creature. If this spell was
///    kicked, create five of those tokens instead."
///
/// ## Implementation (v1)
/// - <b>Sorcery</b> shape, mana cost {2}{U}{U}, mono-blue. Card shape comes
///   from the embedded JSON (<c>rite-of-replication.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same data-driven path as
///   <see cref="CacklingCounterpartFactory"/>).
/// - <b>Copy token (CR 706.2 / CR 707.2).</b> The resolve body spawns a token
///   that's a copy of the chosen creature under the CASTER's control — the
///   same snapshot mechanism as
///   <see cref="CacklingCounterpartFactory.BuildSpellDefinition"/> /
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate"/>
///   (CR 706.2 — the token snapshots the source's printed name, P/T, subtypes,
///   keyword abilities, and colour identity; CR 707.2 — the copy token's
///   controller is the controller of the effect creating it, i.e. the caster,
///   not the source's owner). Unlike Cackling Counterpart, the target is ANY
///   creature ("target creature", not "target creature you control").
/// - <b>Kicker {5} (CR 702.33).</b> Pay-additional rider; the kicked branch
///   mints FIVE token copies "instead" of one. The kicker is a real
///   <see cref="IAdditionalCost"/> primitive — <see cref="KickerAdditionalCost"/>
///   — exposed via <see cref="BuildAdditionalCost"/>. The resolve body reads
///   <see cref="Card.WasKicked"/> at resolution (CR 702.33b — "if this spell
///   was kicked" is checked when the spell resolves; the cast-time payment
///   stamps the sentinel during <see cref="SpellCastFlow"/>'s additional-cost
///   loop, and the cleanup effect the cast flow appends clears it afterward).
///   This mirrors <see cref="RoilEruptionFactory"/>'s kicker-conditional
///   branch, applied to a token-count instead of a damage amount.
///
/// ## Deferred (v1 gaps)
/// - <b>Illegal-on-resolution.</b> If the chosen object is no longer a
///   <see cref="Creature"/> at resolution the token creation is a clean no-op
///   (CR 608.2b); legal-target gating otherwise belongs to the targeting layer.
/// - <b>Token-doubling replacements.</b> The deterministic v1 mints copies via
///   the count-aware <see cref="TokenFactory.CreateOnBattlefield(TokenFactory.TokenSpec, Player, int, Majik.Core.Services.ZoneService?, Majik.Core.Effects.ReplacementBus?)"/>
///   overload with a null bus (no live <c>ReplacementBus</c> in this
///   data-only resolve path), matching the analogue's posture. Doubling Season
///   etc. would multiply the count when wired through a live bus.
/// </summary>
[CardName("Rite of Replication")]
public static class RiteOfReplicationFactory
{
    public const string CardName = "Rite of Replication";
    public const string Slug = "rite-of-replication";
    public const string PrintedManaCost = "{2}{U}{U}";
    public const string KickerCostText = "{5}";

    /// <summary>Number of token copies created when the spell was NOT kicked.</summary>
    public const int UnkickedCopies = 1;

    /// <summary>Number of token copies created when the spell WAS kicked
    /// (CR 702.33b — "create five of those tokens instead").</summary>
    public const int KickedCopies = 5;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Rite of Replication is
    /// cast. Single 1..1 "target creature" request, no X. On resolution spawns
    /// token copies of the chosen creature under <paramref name="caster"/>'s
    /// control — one copy un-kicked, five copies when
    /// <see cref="Card.WasKicked"/> is set on <paramref name="card"/>
    /// (CR 702.33b).
    /// </summary>
    /// <param name="card">The cast card instance — the resolve body reads
    /// <see cref="Card.WasKicked"/> off this same reference so the "create five
    /// instead" branch fires only when the cast actually paid the kicker
    /// (CR 702.33b). The flag is stamped by <see cref="KickerAdditionalCost.Pay"/>
    /// during <see cref="SpellCastFlow"/>'s additional-cost loop and cleared by
    /// the post-resolve cleanup effect the cast flow appends.</param>
    /// <param name="caster">Spell controller — the copy tokens enter under this
    /// player's control (CR 707.2).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        ICard card,
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Token),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                // CR 702.33b — branch on the cast-time kicker stamp. The count
                // (1 vs 5) is locked at the moment the spell resolves.
                bool wasKicked = card is Card concrete && concrete.WasKicked;
                int count = wasKicked ? KickedCopies : UnkickedCopies;

                return new IEffect[]
                {
                    new Effect("Rite of Replication: create token copies of target creature", () =>
                    {
                        // CR 608.2b — illegal-on-resolution check. If the chosen
                        // object is no longer a creature, the token creation is a
                        // clean no-op.
                        if (target is not Creature src) return;

                        // CR 706.2 — copy effects snapshot the source's copiable
                        // values: printed name, P/T, subtypes, keyword abilities,
                        // and colour identity.
                        var keywords = src.Abilities
                            .OfType<KeywordAbility>()
                            .Select(k => k.Keyword)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        var colours = CardColors.GetColors(src).ToList();

                        var spec = new TokenFactory.TokenSpec(
                            Name: src.Name,
                            Power: src.BasePower,
                            Toughness: src.BaseToughness,
                            Subtypes: src.Subtypes.ToArray(),
                            Keywords: keywords,
                            Colors: colours);

                        // CR 707.2 — the copy tokens' controller is the controller
                        // of the effect creating them (the caster), not the
                        // source's owner. Count-aware overload mints `count`
                        // copies (1 un-kicked, 5 kicked; CR 702.33b).
                        TokenFactory.CreateOnBattlefield(
                            spec, caster, count, zones: null, replacements: null);
                    }),
                };
            });
    }

    /// <summary>
    /// Construct Rite of Replication's kicker <see cref="IAdditionalCost"/>
    /// ({5}) for the supplied <paramref name="card"/> instance (CR 702.33).
    /// Convenience builder for callers (tests, bot decision layer) that have
    /// decided to pay the kicker; layer the returned cost onto the cast via
    /// <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c> parameter.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }
}
