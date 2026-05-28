using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData;

/// <summary>
/// Builds typed <see cref="Card"/> instances from Scryfall rows pulled via
/// <see cref="ICardRepository"/>. Permanents get abilities composed by the
/// data-driven binders (<see cref="KeywordBinder"/>,
/// <see cref="OracleManaBinder"/>); instant/sorcery effects are looked up
/// at cast time via <see cref="LookupSpellDefinition"/>. Unknown card
/// names return a vanilla shell so the rest of the engine still functions.
/// </summary>
public sealed class ScryfallCardFactory
{
    private readonly ICardRepository _repo;
    private readonly Majik.Core.Effects.ReplacementBus? _replacements;
    private readonly Majik.Core.Effects.ContinuousEffectsService? _effects;
    private readonly Majik.Core.Abilities.TriggerManager? _triggers;
    private readonly Majik.Core.Events.IEventBus? _eventBus;
    private readonly Majik.Core.Services.ZoneService? _zones;

    public ScryfallCardFactory(ICardRepository repo,
        Majik.Core.Effects.ReplacementBus? replacements = null,
        Majik.Core.Effects.ContinuousEffectsService? effects = null,
        Majik.Core.Abilities.TriggerManager? triggers = null,
        Majik.Core.Events.IEventBus? eventBus = null,
        Majik.Core.Services.ZoneService? zones = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _replacements = replacements;
        _effects = effects;
        _triggers = triggers;
        _eventBus = eventBus;
        _zones = zones;
    }

    public ICard Create(string name, Player owner)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        var entity = _repo.GetByName(name);
        if (entity == null)
        {
            // Unknown name — minimal shell. Definitely a vanilla shell from
            // the bot's perspective: the engine has zero idea what this card
            // does. Tag it so the bot's graceful-degrade path can deprioritise
            // it and emit a one-shot warning.
            var shell = new Card(name, "");
            shell.SetOwner(owner);
            shell.MarkAsVanillaShell();
            return shell;
        }

        var parsed = TypeLineParser.Parse(entity.TypeLine);
        var manaCost = StripBraces(entity.ManaCost ?? "");

        ICard card = PickPrimaryType(parsed.Types) switch
        {
            CardType.Creature => new Creature(
                entity.Name, manaCost,
                ParseStat(entity.Power), ParseStat(entity.Toughness),
                parsed.Supertypes, parsed.Subtypes),
            CardType.Land => new Land(entity.Name, parsed.Supertypes, parsed.Subtypes),
            CardType.Instant => new Instant(entity.Name, manaCost),
            CardType.Sorcery => new Sorcery(entity.Name, manaCost),
            CardType.Enchantment => new Enchantment(entity.Name, manaCost, parsed.Supertypes, parsed.Subtypes),
            CardType.Artifact => new Artifact(entity.Name, manaCost, parsed.Supertypes, parsed.Subtypes),
            CardType.Planeswalker => new Planeswalker(
                entity.Name, manaCost,
                startingLoyalty: entity.Loyalty ?? 0,
                parsed.Supertypes, parsed.Subtypes),
            _ => new Card(entity.Name, manaCost, parsed.Types, parsed.Supertypes, parsed.Subtypes),
        };

        card.SetOwner(owner);

        // CR 202.2c — when the Scryfall `colors` array carries colors that
        // the mana cost cannot account for (no mana cost at all → Dryad
        // Arbor; or a printed color indicator on a card that also has a
        // mana cost), stamp the indicator so CardColors.GetColors picks
        // them up. Without this, color-matters tutors (Green Sun's Zenith,
        // Summoner's Pact, etc.) silently filter out cards like Dryad
        // Arbor whose color comes from the indicator, not from cost pips.
        ApplyColorIndicator(card, entity);

        // Permanent-resident abilities (keyword markers, mana abilities,
        // triggered abilities). Instant/sorcery effects are bound at cast
        // time, not here.
        KeywordBinder.Bind(card, entity, owner);
        OracleManaBinder.Bind(card, entity, owner);
        AffinityBinder.Bind(card, entity);
        SagaBinder.Bind(card, entity, _effects, _zones);
        foreach (var trig in OracleTriggeredAbilityBinder.Bind(card, entity, owner))
        {
            card.AddAbility(trig);
        }
        if (_replacements != null)
        {
            // Chain (most specific → least): Shock → Subtype → Count → Unconditional.
            // Each binder short-circuits when its predecessors claim the card.
            if (!ShockLandBinder.Bind(card, entity, _replacements) &&
                !SubtypeEntersTappedBinder.Bind(card, entity, _replacements) &&
                !ConditionalEntersTappedBinder.Bind(card, entity, _replacements))
            {
                EntersTappedBinder.Bind(card, entity, _replacements);
            }
            // Independent of the tapped chain: ETB +1/+1 counters can
            // co-exist with any tapped-clause (e.g. a hypothetical Stoke the
            // Flames). Register unconditionally.
            EntersWithCountersBinder.Bind(card, entity, _replacements);

            // ETB-as-copy (CR 706.10) — Clone family. Requires the
            // continuous-effects service since the replacement registers a
            // CopyEffect on resolve.
            if (_effects != null)
            {
                EntersAsCopyBinder.Bind(card, entity, _replacements, _effects);
            }
        }

        // Vanilla-shell detection. After running the full binder chain, if a
        // card has printed oracle text whose meaning was NOT captured by any
        // of:
        //   - the keyword pipeline (KeywordBinder, OracleManaBinder,
        //     ETB replacement chain),
        //   - the triggered-ability binder (OracleTriggeredAbilityBinder),
        //   - the saga / affinity binders,
        // then the engine produced a card that LOOKS like the printed card
        // (right name, cost, P/T, types) but does NOT enforce its rules. Tag
        // it so the bot's graceful-degrade path treats it as unimplemented.
        //
        // Instant/sorcery special case: abilities live in the binder-built
        // SpellDefinition (resolved at cast time), not on the Card. We still
        // flag them when the oracle text is non-trivial AND no compiled
        // template is registered for this name — when a template IS present,
        // we leave the flag false because the cast path will actually do
        // something on resolve.
        if (IsLikelyVanillaShell(card, entity))
        {
            (card as Card)?.MarkAsVanillaShell();
        }

        return card;
    }

    /// <summary>
    /// Inspect the built card + its source row and decide whether it's a
    /// "vanilla shell" — see <see cref="ICard.IsVanillaShell"/>. The check
    /// is split: permanents need an attached ability OR keyword-only text;
    /// instants/sorceries need a compiled template (the runtime binder is
    /// not consulted here — too expensive on every Create — but coverage
    /// in practice is &gt;99% gated through the compiled table for the
    /// SpellBound tier).
    /// </summary>
    private bool IsLikelyVanillaShell(ICard card, CardEntity entity)
    {
        var oracle = entity.OracleText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oracle))
        {
            // True vanilla creature / basic land — no printed rules text to
            // enforce. The engine plays these correctly as plain bodies, so
            // they are NOT vanilla shells from the bot's perspective.
            return false;
        }

        var isInstantOrSorcery =
            card.HasType(Majik.Core.Cards.Types.CardType.Instant)
            || card.HasType(Majik.Core.Cards.Types.CardType.Sorcery);

        if (isInstantOrSorcery)
        {
            // Compiled spell-template cache was removed when the SQLite
            // backing store was deleted. The bot now relies on the
            // resolver to clear the vanilla-shell flag when a live
            // template walk binds (see ClearVanillaShellOnSpellBind on
            // the production TurnDriver path). Default to NOT tagging
            // instants/sorceries as vanilla shells — the live walk
            // covers them at cast time.
            return false;
        }

        // Permanent path: has at least one ability → engine covers it.
        // The previous "keyword-only oracle text" fast path lived in
        // CoverageClassifier and consumed the Scryfall `keywords` JSON
        // array — that array is not carried by the embedded seed, so
        // tagging on oracle-text emptiness alone would over-flag cards
        // whose abilities are bound entirely from keywords. Be
        // conservative and only flag when there are no abilities AND no
        // oracle text at all (vanilla creatures / lands).
        var hasAnyAbility = card.Abilities.Count > 0;
        if (hasAnyAbility) return false;
        return !string.IsNullOrWhiteSpace(entity.OracleText);
    }

    /// <summary>
    /// Look up a runnable <see cref="SpellDefinition"/> for a card at cast
    /// time. Returns null when no template matches — caller decides whether
    /// to fall back to a do-nothing vanilla spell.
    /// </summary>
    public SpellDefinition? LookupSpellDefinition(
        string name,
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack = null)
    {
        var entity = _repo.GetByName(name);
        if (entity == null) return null;

        // The offline compiled-template cache (DbCompiledSpellTemplateRepository)
        // was deleted along with the SQLite backing store. Every cast now
        // walks the live SpellTemplateRegistry — measured cost is sub-ms
        // per cast on the bounded template set, and the cache hit-rate was
        // already low after the registry grew beyond the cached snapshot.
        return OracleSpellBinder.Bind(
            entity, caster, targetResolver,
            effects: _effects, stack, replacements: _replacements,
            triggers: _triggers, eventBus: _eventBus, zones: _zones);
    }

    private static CardType PickPrimaryType(IReadOnlyList<CardType> types)
    {
        foreach (var preferred in new[]
        {
            CardType.Land, CardType.Creature, CardType.Planeswalker,
            CardType.Instant, CardType.Sorcery,
            CardType.Enchantment, CardType.Artifact,
        })
        {
            if (types.Contains(preferred)) return preferred;
        }
        return types.Count > 0 ? types[0] : CardType.Artifact;
    }

    private static int ParseStat(string? raw) =>
        int.TryParse(raw, out var n) ? n : 0;

    private static string StripBraces(string s) =>
        s.Replace("{", "").Replace("}", "");

    /// <summary>
    /// Parse the Scryfall <c>colors</c> JSON array on the seed row via the
    /// shared <see cref="CardColors.ParseScryfallColors"/> helper and stamp
    /// any colors as the runtime color indicator on the built
    /// <see cref="Card"/>. We always stamp when the array is non-empty
    /// (rather than only when the mana cost can't explain the colors) so
    /// the runtime mirror is uniform — <see cref="CardColors.GetColors"/>
    /// unions indicator + cost pips and the duplicate is a no-op. The
    /// helper degrades silently to "no indicator" for malformed seed rows
    /// so the binder pipeline can't crash on a bad row.
    /// </summary>
    private static void ApplyColorIndicator(ICard card, CardEntity entity)
    {
        if (card is not Card concrete) return;
        var colors = CardColors.ParseScryfallColors(entity.Colors);
        if (colors.Count == 0) return;
        concrete.SetColorIndicator(colors);
    }
}
