using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Doppelgang (Streets of New Capenna, {X}{X}{X}{G}{U}).
///
/// Sorcery. Scryfall oracle text (verbatim, verified 2026-06-24 against the
/// embedded seed):
///   "For each of X target permanents, create X tokens that are copies of
///    that permanent."
///
/// ## Why it gets its own factory
/// Doppelgang is the X-scaled, multi-target generalisation of the
/// copy-a-permanent token mechanic shared by
/// <see cref="CacklingCounterpartFactory"/> /
/// <see cref="EsikasChariotFactory"/> /
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate"/>.
/// Two primitives combine:
/// <list type="bullet">
///   <item><description><b>X target permanents</b> — the same open-cardinality
///   <see cref="TargetRequest"/> + <see cref="SpellDefinition.HasVariableX"/>
///   pattern as <see cref="ByForceFactory"/> ("X target artifacts"). The chosen
///   X arrives at resolution as the count of supplied targets; here the
///   candidate pool is EVERY permanent on EVERY battlefield (CR 109 — Doppelgang
///   has no controller/type restriction).</description></item>
///   <item><description><b>X copies per target</b> — the X that fixed the target
///   count ALSO fixes how many copies each target spawns (CR 107.3 — a single X
///   is chosen once as the spell is cast and both occurrences use that value).
///   So 3 chosen targets ⇒ each makes 3 copies (9 tokens total).</description></item>
/// </list>
/// Every primitive already ships, so no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Sorcery shape, printed cost <c>{X}{X}{X}{G}{U}</c>, Simic (G/U). Card shape
///   comes from the embedded JSON (<c>doppelgang.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="SpellDefinition.HasVariableX"/> = true so the cast flow prompts
///   for X at cast time (CR 601.2f). One open-cardinality
///   <see cref="TargetRequest"/> (<c>MinTargets = 0, MaxTargets = int.MaxValue</c>)
///   gathering every permanent. As with By Force / Indomitable Creativity the
///   engine cannot yet bind <c>MinTargets = X</c> dynamically, so callers supply
///   exactly X chosen targets and the resolve closure derives X from the chosen
///   list's cardinality.
/// - Resolve: <c>X = chosenTargets.Count</c>. For each chosen target still on
///   the battlefield (CR 608.2b), create X token copies under the caster's
///   control (CR 707.2 — a copy token's controller is the controller of the
///   effect creating it).
///
/// ## Rules citations
/// - CR 107.3 — a single value of X is chosen for the whole spell; both
///   occurrences ("X target permanents" and "X tokens") share it.
/// - CR 601.2f — X is chosen as the spell is cast (variable cost).
/// - CR 608.2b — resolution-time legality re-check (target still a permanent on
///   the battlefield) before copying it.
/// - CR 706.2 — copy effects snapshot the source's copiable values (printed
///   name, P/T, subtypes, keyword abilities, colour identity).
/// - CR 707.2 — the copy token's controller is the controller of the effect
///   creating it (the caster), not the source's owner.
///
/// ## v1 gaps (shared with the existing copy-token implementations)
/// - <b>Non-creature copies are lossy</b>: <see cref="TokenFactory"/> mints
///   creature tokens, so token copies of non-creature permanents (artifacts,
///   enchantments, lands, planeswalkers) are not faithfully represented. The
///   resolve body copies <see cref="Creature"/> targets only and no-ops on a
///   non-creature target — identical posture to
///   <see cref="CacklingCounterpartFactory"/>,
///   <see cref="EsikasChariotFactory"/>, and
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.CreateCopyTokenTemplate"/>.
///   When the engine grows a general permanent-token mechanism this widens
///   without changing the X-targets / X-copies skeleton here.
/// - <b>X-keyed target count</b>: there is no <c>MinTargets = X</c> binding on
///   <see cref="TargetRequest"/>; callers pre-supply exactly X targets and the
///   resolve closure trusts the chosen-target list cardinality (same as By
///   Force / Indomitable Creativity).
/// </summary>
[CardName("Doppelgang")]
public static class DoppelgangFactory
{
    public const string CardName = "Doppelgang";
    public const string Slug = "doppelgang";
    public const string PrintedManaCost = "{X}{X}{X}{G}{U}";

    /// <summary>Build the card shape (name / Sorcery / {X}{X}{X}{G}{U}) from the
    /// embedded JSON definition.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Doppelgang uses on resolution.
    /// <see cref="SpellDefinition.HasVariableX"/> is true so the engine prompts
    /// for X at cast time; the single <see cref="TargetRequest"/> is
    /// open-cardinality and callers pre-supply exactly X targets (see class
    /// xmldoc gap note). On resolution X = the chosen-target count and each
    /// chosen permanent spawns X token copies under <paramref name="caster"/>'s
    /// control.
    /// </summary>
    /// <param name="caster">Spell controller — every copy token enters under
    /// this player's control (CR 707.2).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "X target permanents",
                    MinTargets: 0,
                    MaxTargets: int.MaxValue,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Token,
                    // CR 109 — any permanent on any battlefield is a legal
                    // candidate. Doppelgang has no controller/type restriction.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p => new IEffect[]
            {
                new Effect(
                    $"{CardName}: for each of X target permanents, create X token copies.",
                    () => Resolve(
                        caster,
                        p.Targets.Count == 0 ? Array.Empty<object>() : p.Targets[0],
                        zones: null)),
            });
    }

    /// <summary>
    /// Resolve Doppelgang against the supplied chosen targets. CR 107.3 — X is
    /// the number of chosen targets, and that same X is how many copies EACH
    /// target spawns. For every chosen target still a permanent on the
    /// battlefield (CR 608.2b) create X token copies under
    /// <paramref name="caster"/>'s control (CR 707.2). Returns the tokens
    /// actually created. Exposed for direct invocation by tests / bots without
    /// driving the full cast flow.
    /// </summary>
    /// <param name="caster">Controller of every created copy token (CR 707.2).</param>
    /// <param name="chosenTargets">The X targets chosen as the spell was cast;
    /// X is this list's cardinality.</param>
    /// <param name="zones">Optional <see cref="ZoneService"/> so each minted
    /// token publishes <c>CardMovedEvent</c> on battlefield entry.</param>
    public static IReadOnlyList<Creature> Resolve(
        Player caster,
        IReadOnlyList<object> chosenTargets,
        ZoneService? zones)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(chosenTargets);

        // CR 107.3 — the single chosen X both fixed the target count and is the
        // number of copies each target makes.
        var x = chosenTargets.Count;

        var created = new List<Creature>();
        foreach (var raw in chosenTargets)
        {
            // CR 608.2b — resolution-time legality re-check: the target must
            // still be a permanent on the battlefield.
            if (raw is not Permanent perm) continue;
            if (perm.Zone != ZoneType.Battlefield) continue;

            // v1 copy-token lossiness (see class xmldoc): TokenFactory mints
            // creature tokens, so only Creature targets are copied faithfully.
            // A non-creature permanent target is a clean no-op rather than a
            // mis-typed token. Same posture as Cackling Counterpart / Esika's
            // Chariot / CreateCopyTokenTemplate.
            if (perm is not Creature src) continue;

            var spec = BuildCopySpec(src);

            // CR 107.3 — create X copies of this target.
            for (var i = 0; i < x; i++)
            {
                // CR 707.2 — the copy token's controller is the controller of
                // the effect (the caster), not the source's owner.
                created.Add(TokenFactory.CreateOnBattlefield(spec, caster, zones));
            }
        }

        return created;
    }

    /// <summary>
    /// CR 706.2 — snapshot the source creature's copiable values (printed name,
    /// base P/T, subtypes, keyword abilities, colour identity) into a
    /// <see cref="TokenFactory.TokenSpec"/>. Lossy w.r.t. later characteristic
    /// changes, matching the existing v1 copy-token implementations.
    /// </summary>
    private static TokenFactory.TokenSpec BuildCopySpec(Creature src)
    {
        var keywords = src.Abilities
            .OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var colours = CardColors.GetColors(src).ToList();

        return new TokenFactory.TokenSpec(
            Name: src.Name,
            Power: src.BasePower,
            Toughness: src.BaseToughness,
            Subtypes: src.Subtypes.ToArray(),
            Keywords: keywords,
            Colors: colours);
    }
}
