using Majik.Core.CardData.Database;
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
    private readonly ICompiledSpellTemplateRepository? _compiledRepo;

    public ScryfallCardFactory(ICardRepository repo,
        Majik.Core.Effects.ReplacementBus? replacements = null,
        ICompiledSpellTemplateRepository? compiledSpellRepo = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _replacements = replacements;
        _compiledRepo = compiledSpellRepo;
    }

    public ICard Create(string name, Player owner)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required", nameof(name));
        if (owner == null) throw new ArgumentNullException(nameof(owner));

        var entity = _repo.GetByName(name);
        if (entity == null)
        {
            var shell = new Card(name, "");
            shell.SetOwner(owner);
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

        // Permanent-resident abilities (keyword markers, mana abilities,
        // triggered abilities). Instant/sorcery effects are bound at cast
        // time, not here.
        KeywordBinder.Bind(card, entity, owner);
        OracleManaBinder.Bind(card, entity, owner);
        AffinityBinder.Bind(card, entity);
        SagaBinder.Bind(card, entity);
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
        }

        return card;
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

        // Phase 2 fast path: if the offline compile pipeline has a row for
        // this card, route through Rehydrate instead of walking the regex
        // registry. Falls back to the live walk when the compiled table is
        // unwired (no repo configured), the card has no compiled row, or
        // the stored template doesn't resolve in this build's Registry.
        var compiled = _compiledRepo?.Lookup(entity.Name);
        if (compiled is not null)
        {
            var fast = OracleSpellBinder.BindCompiled(
                compiled.TemplateName,
                compiled.ParamsJson,
                entity, caster, targetResolver, effects: null, stack);
            if (fast is not null) return fast;
        }

        return OracleSpellBinder.Bind(entity, caster, targetResolver, stack);
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
}
