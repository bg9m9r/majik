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
/// Named-card factory for Cackling Counterpart (Innistrad, {1}{U}{U}).
///
/// Instant. Scryfall oracle text (verbatim, verified 2026-06-14):
///   "Create a token that's a copy of target creature you control.
///    Flashback {5}{U}{U} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// ## Implementation (v1)
/// - <b>Instant</b> shape, mana cost {1}{U}{U}, mono-blue. Card shape comes
///   from the embedded JSON (<c>cackling-counterpart.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same data-driven shape path as
///   <see cref="PlayWithFireFactory"/>).
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target creature
///   you control" request. On resolution it spawns a single token that's a
///   copy of the chosen creature under the caster's control — the same
///   copy-token mechanism as the shared
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate"/>
///   (CR 706.2 — the token snapshots the source's printed name, P/T,
///   subtypes, keyword abilities, and colour identity; CR 707.2 — the copy
///   token's controller is the controller of the effect creating it, i.e.
///   the caster, not the source's owner). The resolver is supplied by the
///   caller's <see cref="GameContext"/> because a <see cref="SpellDefinition"/>
///   needs the live target resolver (not expressible in the data-only JSON).
/// - <b>Flashback {5}{U}{U}</b> (CR 702.34). The printed flashback cost is an
///   all-mana cost, so — mirroring <see cref="FireboltFactory"/> /
///   <see cref="FaithlessLootingFactory"/> — it is parsed out of
///   <see cref="OracleText"/> via <see cref="FlashbackOracleParser"/> and
///   surfaced as a <see cref="FlashbackAlternativeCost"/> through
///   <see cref="BuildFlashbackCost"/>. Callers thread the returned alt-cost
///   into <see cref="Majik.Core.Game.SpellCastFlow"/> when casting from the
///   graveyard; the post-resolution exile (CR 702.34b) is performed by the
///   cost's <c>OnResolved</c> hook (no extra wiring here).
///
/// ## Deferred (v1 gaps)
/// - <b>"you control" target filter</b>: the cast-flow targeting subsystem
///   gates legal candidates; the resolve body additionally checks the chosen
///   object is a <see cref="Creature"/> and no-ops if not (CR 608.2b). A
///   strict caster-control re-check at resolution is left to the targeting
///   layer (same posture as every "target creature you control" factory) —
///   the deterministic test path passes a caster-controlled creature.
/// - <b>"You may have it enter as …" / except-clause riders</b>: Cackling
///   Counterpart has no rider; the plain copy is faithful. (The shared
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate"/>
///   documents the rider gap for cards that DO have one, e.g. Heat Shimmer.)
/// </summary>
[CardName("Cackling Counterpart")]
public static class CacklingCounterpartFactory
{
    public const string CardName = "Cackling Counterpart";
    public const string Slug = "cackling-counterpart";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>
    /// Oracle text reference. Drives <see cref="BuildFlashbackCost"/> via
    /// <see cref="FlashbackOracleParser"/> so the named-factory path and the
    /// data-driven oracle binder path agree on the {5}{U}{U} flashback shape.
    /// </summary>
    public const string OracleText =
        "Create a token that's a copy of target creature you control.\n" +
        "Flashback {5}{U}{U} (You may cast this card from your graveyard for its " +
        "flashback cost. Then exile it.)";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Cackling Counterpart
    /// is cast (printed cost or flashback). Single 1..1 "target creature you
    /// control" request, no X. On resolution spawns a token copy of the
    /// chosen creature under <paramref name="caster"/>'s control.
    /// </summary>
    /// <param name="caster">Spell controller — the copy token enters under
    /// this player's control (CR 707.2).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature you control", 1, 1, Array.Empty<object>(),
                    Intent: BotIntent.Token),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Cackling Counterpart: create a token copy of target creature you control", () =>
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

                        // CR 707.2 — the copy token's controller is the controller
                        // of the effect creating it (the caster), not the source's
                        // owner.
                        TokenFactory.CreateOnBattlefield(spec, caster, zones: null);
                    }),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost ({5}{U}{U}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost) keeps the
    /// named-factory path and the data-driven oracle binder path agreeing on
    /// shape (CR 702.34).
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Cackling Counterpart's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
