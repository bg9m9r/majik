using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Pattern-matches an instant/sorcery's oracle text to one of a handful of
/// canonical templates, returning a runnable <see cref="SpellDefinition"/>.
/// Returns null when no template matches — caller can fall back to a
/// vanilla shell so the card still loads.
///
/// Templates handled (first match wins):
///   - "Counter target spell."                       → counter
///   - "Deals N damage to any target."               → damage (creature or player)
///   - "Deals N damage to target player [or planeswalker]." → player damage
///   - "Destroy target creature."                    → destroy creature
///   - "Destroy target artifact or enchantment."     → destroy permanent
///   - "Draw N cards."                               → draw (caster)
///   - "Target player discards N cards."             → discard
///
/// Word numerals ("one", "two", "three", …) are translated to digits.
/// </summary>
public static class OracleSpellBinder
{
    internal static SpellTemplateRegistry Registry { get; } =
        new SpellTemplateRegistry(new ISpellTemplate[]
        {
            new SpellTemplates.Templates.Counter.CounterUnlessPayTemplate(),
            new SpellTemplates.Templates.Counter.CounterNoncreatureTemplate(),
            new SpellTemplates.Templates.Counter.CounterCreatureTemplate(),
            new SpellTemplates.Templates.Counter.CounterTargetSpellTemplate(),
            new SpellTemplates.Templates.Damage.DealsXDamageAnyTemplate(),
            new SpellTemplates.Templates.Damage.DamageAnyTargetTemplate(),
            new SpellTemplates.Templates.Damage.DamagePlayerTemplate(),
            new SpellTemplates.Templates.Damage.DealsDamageEachCreatureTemplate(),
            new SpellTemplates.Templates.Damage.EachOpponentLosesLifeTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyCreatureCmcLimitTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyUpToArtifactEnchantmentTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyNonlandPermanentTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyArtifactEnchantmentTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyCreatureTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyLandTemplate(),
            new SpellTemplates.Templates.Destroy.DestroyPermanentTemplate(),
            new SpellTemplates.Templates.Resource.DrawCardsTemplate(),
            new SpellTemplates.Templates.Resource.DiscardTemplate(),
            new SpellTemplates.Templates.Resource.GainLifeTemplate(),
            new SpellTemplates.Templates.Resource.YouGainLifeTemplate(),
            new SpellTemplates.Templates.Resource.YouLoseLifeTemplate(),
            new SpellTemplates.Templates.Resource.EachPlayerDrawsTemplate(),
            new SpellTemplates.Templates.Resource.TargetPlayerLosesLifeTemplate(),
        });

    // "Target creature gets +N/+N until end of turn."
    private static readonly Regex PumpCreature = new(
        @"target\s+creature\s+gets\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Target creature gains <keyword> until end of turn."
    private static readonly Regex GrantKeywordTilEot = new(
        @"target\s+creature\s+gains?\s+(?<kw>flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Tap target {permanent|creature|artifact|land|...}." — \b so "untap"
    // doesn't match.
    private static readonly Regex TapTarget = new(
        @"\btap\s+target\s+(permanent|creature|artifact|land|enchantment|planeswalker)",
        RegexOptions.IgnoreCase);
    // "Return target {creature|permanent|...} to its owner's hand."
    private static readonly Regex BounceTarget = new(
        @"return\s+target\s+(permanent|creature|artifact|enchantment|nonland\s+permanent|land)\s+to\s+(its|their)\s+owner'?s?\s+hand",
        RegexOptions.IgnoreCase);
    // "Search your library for a {basic land|land|creature|artifact|...} card..."
    private static readonly Regex SearchLibrary = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land|creature|artifact|enchantment|instant|sorcery|planeswalker)\s+card",
        RegexOptions.IgnoreCase);
    // "Search your library for a basic land card, put it onto the battlefield tapped, then shuffle."
    // (Cultivate / Rampant Growth style — dispatched BEFORE SearchLibrary so the
    //  more-specific battlefield destination wins.)
    private static readonly Regex SearchLandToBattlefieldTapped = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land)\s+card[^.]*put\s+(?:it|that\s+card)\s+onto\s+the\s+battlefield\s+tapped",
        RegexOptions.IgnoreCase);
    // "Search your library for a basic land card and put it onto the battlefield."
    // (untapped variant — matches only when 'tapped' is NOT present, because the
    //  tapped regex above is dispatched first.)
    private static readonly Regex SearchLandToBattlefield = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land)\s+card[^.]*put\s+(?:it|that\s+card)\s+onto\s+the\s+battlefield",
        RegexOptions.IgnoreCase);
    // "Exile target {creature|permanent|artifact|enchantment|land|nonland permanent}."
    private static readonly Regex ExileTarget = new(
        @"exile\s+target\s+(creature|permanent|artifact|enchantment|land|nonland\s+permanent)",
        RegexOptions.IgnoreCase);
    // "Untap target {permanent|creature|artifact|land|...}."
    private static readonly Regex UntapTarget = new(
        @"untap\s+target\s+(permanent|creature|artifact|land|enchantment)",
        RegexOptions.IgnoreCase);
    // "Put N +1/+1 counters on target creature."
    private static readonly Regex PutPlusCounter = new(
        @"put\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+\+1/\+1\s+counters?\s+on\s+target\s+creature",
        RegexOptions.IgnoreCase);
    // "Each creature you control gets a +1/+1 counter on it."
    private static readonly Regex CreaturesGetPlusCounter = new(
        @"each\s+creature\s+you\s+control\s+gets\s+a\s+\+1/\+1\s+counter\s+on\s+it",
        RegexOptions.IgnoreCase);
    // "Put N -1/-1 counters on target creature."
    private static readonly Regex PutMinusCounter = new(
        @"put\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+-1/-1\s+counters?\s+on\s+target\s+creature",
        RegexOptions.IgnoreCase);
    // "Gain control of target creature."
    private static readonly Regex GainControl = new(
        @"gain\s+control\s+of\s+target\s+creature",
        RegexOptions.IgnoreCase);
    // "Creatures you control get +P/+T until end of turn."
    private static readonly Regex CreaturesYouControlPump = new(
        @"creatures\s+you\s+control\s+get\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Target player mills N cards."
    private static readonly Regex MillTarget = new(
        @"target\s+player\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);
    // "Mill N cards." — self-mill (caster only; must not match "target player mills").
    private static readonly Regex MillSelf = new(
        @"^\s*mill\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    // "Surveil N." — look at top N, default decision sends all to graveyard.
    private static readonly Regex SurveilSelf = new(
        @"^\s*surveil\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    // "Return target [TYPE] card from your graveyard to your hand."
    private static readonly Regex ReanimateFromGraveyard = new(
        @"return\s+target\s+(?<kind>card|creature|instant|sorcery|artifact|enchantment|planeswalker|land)?\s*card\s+from\s+your\s+graveyard\s+to\s+your\s+hand",
        RegexOptions.IgnoreCase);
    // "Scry N." — standalone (no draw tail; must not match "scry N, then draw").
    private static readonly Regex ScrySelf = new(
        @"^\s*scry\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    // "Scry N" — generic (catches "Scry N, then draw …" and any other scry variant).
    private static readonly Regex ScryN = new(
        @"\bscry\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase);
    // "Each opponent mills N cards."
    private static readonly Regex EachOpponentMills = new(
        @"each\s+opponent\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);
    // "Each player mills N cards."
    private static readonly Regex EachPlayerMills = new(
        @"each\s+player\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);
    // "Create [a|N] Treasure token(s)." — predefined artifact, no P/T text.
    private static readonly Regex CreateTreasureTokens = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+treasure\s+tokens?\b",
        RegexOptions.IgnoreCase);
    // "Create [a|N] Food token(s)."
    private static readonly Regex CreateFoodTokens = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+food\s+tokens?\b",
        RegexOptions.IgnoreCase);
    // "Create [a|N] Clue token(s)."
    private static readonly Regex CreateClueTokens = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+clue\s+tokens?\b",
        RegexOptions.IgnoreCase);
    // "Investigate." / "Investigate N times." — CR 701.30 keyword action:
    // create a Clue token.
    private static readonly Regex InvestigateSingle = new(
        @"^\s*investigate\s*\.",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex InvestigateNTimes = new(
        @"investigate\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+times",
        RegexOptions.IgnoreCase);
    // "Create [a|N] [P]/[T] [colour] [subtype] creature token[s] [with KW (and KW)]."
    // Captures: count, P, T, optional colour, subtype, optional keyword list.
    private static readonly Regex CreateTokens = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?<p>\d+)/(?<t>\d+)\s+(?<colour>white|blue|black|red|green|colorless)?\s*(?<subtype>[A-Za-z]+)\s+creature\s+tokens?(?:\s+with\s+(?<keywords>[A-Za-z, ]+))?",
        RegexOptions.IgnoreCase);
    // "Creatures you control gain KEYWORD until end of turn." — global
    // keyword grant; companion to CreaturesYouControlPump's +P/+T grant.
    private static readonly Regex CreaturesYouControlGain = new(
        @"creatures\s+you\s+control\s+gain\s+(?<kw>flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Exile target card from a graveyard." / "Exile target card from your graveyard."
    // / "Exile target creature card from a graveyard." (with optional card-type filter)
    private static readonly Regex ExileFromGraveyard = new(
        @"exile\s+target\s+(?<kind>creature|instant|sorcery|artifact|enchantment|planeswalker|land)?\s*card\s+from\s+(?:a|your)\s+graveyard",
        RegexOptions.IgnoreCase);
    // "Target player reveals their hand. You choose a nonland card from it.
    //  That player discards that card. You lose N life." (Thoughtseize template)
    private static readonly Regex ThoughtseizePattern = new(
        @"target\s+player\s+reveals\s+their\s+hand\.\s*you\s+choose\s+a\s+nonland\s+card\s+from\s+it\.\s*that\s+player\s+discards\s+that\s+card\.\s*you\s+lose\s+(?<life>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase);
    // "Search your library for a <color> creature card with mana value X or less,
    //  put it onto the battlefield, then shuffle. Shuffle <name> into its owner's
    //  library." (Green Sun's Zenith pattern — X-cost green creature tutor.)
    // The "shuffle into library" suffix distinguishes GSZ from a generic tutor.
    private static readonly Regex GreenSunsZenithPattern = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<color>green|white|blue|black|red)\s+creature\s+card\s+with\s+mana\s+value\s+x\s+or\s+less[^.]*put\s+it\s+onto\s+the\s+battlefield[^.]*shuffle\.\s*shuffle[^.]+into\s+its\s+owner'?s?\s+library",
        RegexOptions.IgnoreCase);
    // Malevolent Rumble: "Reveal the top four cards of your library. You may put
    // a permanent card from among them into your hand. Put the rest into your
    // graveyard. Create a 0/1 colorless Eldrazi Spawn creature token…"
    private static readonly Regex MalevolentRumblePattern = new(
        @"reveal\s+the\s+top\s+four\s+cards.*permanent\s+card.*into\s+your\s+hand.*create\s+a\s+0/1\s+colorless\s+eldrazi\s+spawn",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        Bind(entity, caster, resolver, null, stack);

    public static SpellDefinition? Bind(
        CardEntity entity,
        Player caster,
        Func<object, object> resolver,
        Majik.Core.Effects.ContinuousEffectsService? effects,
        Majik.Core.Stack.Stack? stack)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));

        // New path: try the template registry first. Empty today, populated
        // task-by-task. Null result falls through to the legacy chain below.
        var ctx = new SpellBindContext(entity, caster, resolver, effects, stack);
        if (Registry.TryBind(ctx) is { } fromRegistry) return fromRegistry;

        var text = entity.OracleText ?? string.Empty;

        var mMill = MillTarget.Match(text);
        if (mMill.Success) return MillTargetSpell(WordToInt(mMill.Groups["n"].Value), resolver);

        var mMillSelf = MillSelf.Match(text);
        if (mMillSelf.Success) return MillSelfSpell(caster, WordToInt(mMillSelf.Groups["n"].Value));

        var mSurveil = SurveilSelf.Match(text);
        if (mSurveil.Success) return SurveilSelfSpell(caster, WordToInt(mSurveil.Groups["n"].Value));

        var mScrySelf = ScrySelf.Match(text);
        if (mScrySelf.Success) return ScryNSpell(caster, text, WordToInt(mScrySelf.Groups["n"].Value));

        var mScryN = ScryN.Match(text);
        if (mScryN.Success) return ScryNSpell(caster, text, WordToInt(mScryN.Groups["n"].Value));

        var mEachOpp = EachOpponentMills.Match(text);
        if (mEachOpp.Success) return EachOpponentMillsSpell(caster, WordToInt(mEachOpp.Groups["n"].Value));

        var mEachPl = EachPlayerMills.Match(text);
        if (mEachPl.Success) return EachPlayerMillsSpell(caster, WordToInt(mEachPl.Groups["n"].Value));

        if (CreaturesGetPlusCounter.IsMatch(text))
            return CreaturesGetPlusCounterSpell(caster);

        var mMinus = PutMinusCounter.Match(text);
        if (mMinus.Success) return PutMinusOneMinusOneSpell(
            WordToInt(mMinus.Groups["n"].Value), resolver);

        var mPlus = PutPlusCounter.Match(text);
        if (mPlus.Success) return PutPlusOnePlusOneSpell(
            WordToInt(mPlus.Groups["n"].Value), resolver);

        if (GainControl.IsMatch(text) && effects != null)
            return GainControlSpell(resolver, caster, effects);

        var mGlobal = CreaturesYouControlPump.Match(text);
        if (mGlobal.Success && effects != null) return CreaturesYouControlPumpSpell(
            int.Parse(mGlobal.Groups["p"].Value),
            int.Parse(mGlobal.Groups["t"].Value),
            caster, effects);

        var mGrantAll = CreaturesYouControlGain.Match(text);
        if (mGrantAll.Success && effects != null) return CreaturesYouControlGainKeywordSpell(
            NormaliseKeyword(mGrantAll.Groups["kw"].Value), caster, effects);

        // Thoughtseize (reveal-choose-discard + caster loses life) — before generic Discard.
        var mTs = ThoughtseizePattern.Match(text);
        if (mTs.Success) return ThoughtseizeSpell(caster, resolver, WordToInt(mTs.Groups["life"].Value));

        var m = PumpCreature.Match(text);
        if (m.Success) return PumpSpell(
            int.Parse(m.Groups["p"].Value), int.Parse(m.Groups["t"].Value), resolver);

        m = GrantKeywordTilEot.Match(text);
        if (m.Success) return GrantKeywordSpell(
            NormaliseKeyword(m.Groups["kw"].Value), resolver);

        m = TapTarget.Match(text);
        if (m.Success) return TapTargetSpell(resolver, $"target {m.Groups[1].Value}");

        m = BounceTarget.Match(text);
        if (m.Success) return BounceTargetSpell(resolver, $"target {m.Groups[1].Value}");

        m = ReanimateFromGraveyard.Match(text);
        if (m.Success) return ReanimateSpell(resolver, m.Groups["kind"].Value);

        // ExileFromGraveyard before ExileTarget — "exile target creature card from a graveyard"
        // would otherwise match ExileTarget's creature alternative first.
        var mEgy = ExileFromGraveyard.Match(text);
        if (mEgy.Success) return ExileFromGraveyardSpell(resolver, mEgy.Groups["kind"].Value.Trim());

        m = ExileTarget.Match(text);
        if (m.Success) return ExileTargetSpell(resolver, $"target {m.Groups[1].Value}");

        // GreenSunsZenithPattern must come before SearchLibrary / SearchLandToBattlefield
        // because its oracle text also contains "search your library for a … creature card"
        // and would otherwise be caught by those generic regexes.
        var mGsz = GreenSunsZenithPattern.Match(text);
        if (mGsz.Success) return GreenSunsZenithSpell(caster, mGsz.Groups["color"].Value);

        // SearchLandToBattlefieldTapped / SearchLandToBattlefield must come BEFORE
        // SearchLibrary — the generic regex also matches "search your library for a basic
        // land card" (it stops at the word "card"), so it would hijack these if it ran first.
        m = SearchLandToBattlefieldTapped.Match(text);
        if (m.Success) return SearchLandToBattlefieldSpell(caster, m.Groups["kind"].Value, tapped: true);

        m = SearchLandToBattlefield.Match(text);
        if (m.Success) return SearchLandToBattlefieldSpell(caster, m.Groups["kind"].Value, tapped: false);

        m = SearchLibrary.Match(text);
        if (m.Success) return SearchLibrarySpell(caster, m.Groups["kind"].Value);

        m = UntapTarget.Match(text);
        if (m.Success) return UntapTargetSpell(resolver, $"target {m.Groups[1].Value}");

        // Investigate keyword action (CR 701.30) — checked before CreateClueTokens "create" pattern.
        var mInvN = InvestigateNTimes.Match(text);
        if (mInvN.Success) return InvestigateNTimesSpell(caster, WordToInt(mInvN.Groups["n"].Value));

        if (InvestigateSingle.IsMatch(text))
            return InvestigateNTimesSpell(caster, 1);

        // Predefined artifact tokens — checked before creature-token regex (more specific).
        m = CreateTreasureTokens.Match(text);
        if (m.Success) return CreateTreasureTokensSpell(caster, WordToInt(m.Groups["n"].Value));

        m = CreateFoodTokens.Match(text);
        if (m.Success) return CreateFoodTokensSpell(caster, WordToInt(m.Groups["n"].Value));

        m = CreateClueTokens.Match(text);
        if (m.Success) return CreateClueTokensSpell(caster, WordToInt(m.Groups["n"].Value));

        m = CreateTokens.Match(text);
        if (m.Success) return CreateTokensSpell(
            caster,
            WordToInt(m.Groups["n"].Value),
            int.Parse(m.Groups["p"].Value),
            int.Parse(m.Groups["t"].Value),
            m.Groups["subtype"].Value,
            ParseKeywordList(m.Groups["keywords"].Value));

        // Malevolent Rumble: reveal top 4, may put first permanent to hand,
        // rest to graveyard, create an Eldrazi Spawn token.
        if (MalevolentRumblePattern.IsMatch(text)) return MalevolentRumbleSpell(caster);

        return null;
    }

    private static SpellDefinition GainControlSpell(
        Func<object, object> resolver, Player caster,
        Majik.Core.Effects.ContinuousEffectsService effects) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("gain control", () =>
            {
                if (target is Permanent perm)
                    effects.Register(new Majik.Core.Effects.ControlChangeEffect(perm, caster));
            }) };
        });

    private static SpellDefinition CreaturesYouControlPumpSpell(
        int p, int t, Player caster,
        Majik.Core.Effects.ContinuousEffectsService effects) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"creatures +{p}/+{t} EOT", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                effects.Register(new GlobalPumpEffect(c, p, t));
            }
        }) });

    private static SpellDefinition CreaturesYouControlGainKeywordSpell(
        string keyword, Player caster,
        Majik.Core.Effects.ContinuousEffectsService effects) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"creatures gain {keyword} EOT", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                effects.Register(new GrantKeywordUntilEndOfTurnEffect(c, keyword));
            }
        }) });

    /// <summary>Layer 7c pump-this-creature effect, EOT.</summary>
    private sealed class GlobalPumpEffect : Majik.Core.Effects.ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        public GlobalPumpEffect(Creature target, int p, int t)
        { _target = target; _p = p; _t = t; }
        public override Majik.Core.Effects.Layer Layer => Majik.Core.Effects.Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(Majik.Core.Effects.CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _t; }
    }

    private static SpellDefinition PutPlusOnePlusOneSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"+{n} counters", () =>
            {
                if (target is Permanent perm)
                    perm.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, n);
            }) };
        });

    private static SpellDefinition PutMinusOneMinusOneSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"-{n} counters", () =>
            {
                if (target is Permanent perm)
                    perm.Counters.Add(Majik.Core.Counters.CounterType.MinusOneMinusOne, n);
            }) };
        });

    private static SpellDefinition CreaturesGetPlusCounterSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("+1/+1 counter to each", () =>
        {
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                c.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);
            }
        }) });

    private static SpellDefinition ReanimateSpell(Func<object, object> resolver, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(
            string.IsNullOrEmpty(kindRaw) ? "target card in graveyard" : $"target {kindRaw} card in graveyard",
            1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("return from gy", () =>
            {
                if (target is not ICard card) return;
                var owner = card.Owner;
                if (owner == null) return;
                if (card.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
                owner.Zones.Hand.AddCard(card);
                card.SetZone(ZoneType.Hand);
            }) };
        });

    private static SpellDefinition MillTargetSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"mill {n}", () =>
            {
                if (target is not Player pl) return;
                MillAction.Apply(pl, n);
            }) };
        });

    private static SpellDefinition MillSelfSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"mill self {n}", () =>
        {
            MillAction.Apply(caster, n);
        }) });

    private static SpellDefinition SurveilSelfSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"surveil {n}", () =>
        {
            var peeked = SurveilAction.Peek(caster, n);
            if (peeked.Count == 0) return;

            // Consult the registered agent when available; fall back to the
            // pre-agent default (all-to-graveyard) when none is registered.
            // TODO: remove sync-over-async once IEffect.Execute becomes async.
            var agent = AgentRegistry.Get(caster);
            SurveilAction.SurveilDecision decision;
            if (agent != null)
            {
                decision = agent.ChooseSurveilDecisionAsync(null, peeked)
                    .GetAwaiter().GetResult();
            }
            else
            {
                decision = new SurveilAction.SurveilDecision(
                    ToGraveyard: peeked.ToList(),
                    TopOrder: Array.Empty<ICard>());
            }
            SurveilAction.Apply(caster, n, decision);
        }) });

    // "Preordain"-style: scry happens (default-all-bottom decision), then "draw a card"
    // tail clause fires. Cantrip portion is the substantive effect.
    private static readonly Regex ScryThenDrawTail = new(
        @"scry\s+\d+[^.]*[,.]?\s*then\s+draw\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    private static SpellDefinition ScryNSpell(Player caster, string oracleText, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("scry+draw", () =>
        {
            var peeked = ScryAction.Peek(caster, n);
            if (peeked.Count > 0)
            {
                // Consult the registered agent when available; fall back to the
                // pre-agent default (all-to-bottom) when none is registered.
                // TODO: remove sync-over-async once IEffect.Execute becomes async.
                var agent = AgentRegistry.Get(caster);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    decision = agent.ChooseScryDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    decision = new ScryAction.ScryDecision(
                        ToBottom: peeked.ToList(),
                        TopOrder: Array.Empty<ICard>());
                }
                ScryAction.Apply(caster, n, decision);
            }

            var tail = ScryThenDrawTail.Match(oracleText);
            if (tail.Success)
            {
                DrawCards_(caster, WordToInt(tail.Groups["n"].Value));
            }
        }) });

    private static SpellDefinition EachOpponentMillsSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"each opponent mills {n}", () =>
        {
            // Opponents are resolved via ChosenSpellParams.AllPlayers when
            // SpellCastFlow is updated to pass the full player list.
            // Until then, tests can supply players via the params.
            if (p.AllPlayers != null)
            {
                foreach (var pl in p.AllPlayers.Where(pl => !ReferenceEquals(pl, caster)))
                    MillAction.Apply(pl, n);
            }
        }) });

    private static SpellDefinition EachPlayerMillsSpell(Player caster, int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p => new IEffect[] { new Effect($"each player mills {n}", () =>
        {
            // All players are resolved via ChosenSpellParams.AllPlayers when
            // SpellCastFlow is updated to pass the full player list.
            if (p.AllPlayers != null)
            {
                foreach (var pl in p.AllPlayers)
                    MillAction.Apply(pl, n);
            }
        }) });

    private static SpellDefinition UntapTargetSpell(Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("untap target", () =>
            {
                if (target is Permanent perm && perm.IsTapped) perm.Untap();
            }) };
        });

    private static IReadOnlyList<string> ParseKeywordList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        // Split on commas / "and"; trim; canonicalise via NormaliseKeyword.
        return raw.Replace(" and ", ",").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => NormaliseKeyword(s.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static SpellDefinition CreateTreasureTokensSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} Treasure", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateTreasure(caster);
        }) });

    private static SpellDefinition CreateFoodTokensSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} Food", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateFood(caster);
        }) });

    private static SpellDefinition CreateClueTokensSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} Clue", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateClue(caster);
        }) });

    // CR 701.30 — "To investigate" means to create a Clue token.
    private static SpellDefinition InvestigateNTimesSpell(Player caster, int count) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"investigate {count}", () =>
        {
            for (var i = 0; i < count; i++)
                TokenFactory.CreateClue(caster);
        }) });

    private static SpellDefinition CreateTokensSpell(
        Player caster, int count, int power, int toughness, string subtypeRaw,
        IReadOnlyList<string> grantedKeywords) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"create {count} {power}/{toughness}", () =>
        {
            // Subtype enum lookup is best-effort; tokens with unrecognised
            // subtypes still spawn with no subtype attached.
            Majik.Core.Cards.Types.CardSubtype? subtype = null;
            if (Enum.TryParse<Majik.Core.Cards.Types.CardSubtype>(
                char.ToUpperInvariant(subtypeRaw[0]) + subtypeRaw[1..].ToLowerInvariant(),
                out var st))
            {
                subtype = st;
            }

            var subtypes = subtype.HasValue
                ? new[] { subtype.Value }
                : Array.Empty<Majik.Core.Cards.Types.CardSubtype>();

            var spec = new TokenFactory.TokenSpec(
                Name: subtypeRaw,
                Power: power,
                Toughness: toughness,
                Subtypes: subtypes,
                Keywords: grantedKeywords);

            for (var i = 0; i < count; i++)
                TokenFactory.CreateOnBattlefield(spec, caster);
        }) });

    private static SpellDefinition ExileTargetSpell(Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("exile target", () =>
            {
                if (target is ICard card) MoveToExile(card);
            }) };
        });

    private static SpellDefinition SearchLibrarySpell(Player caster, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"tutor {kindRaw}", () =>
        {
            // MVP tutor: deterministic — first library card matching predicate.
            // Real implementation: prompt agent for choice; this default keeps
            // tests deterministic until SpellCastFlow learns library-target prompts.
            bool Pred(ICard c) => kindRaw.ToLowerInvariant() switch
            {
                "basic land" => c.HasType(Majik.Core.Cards.Types.CardType.Land),
                "land" => c.HasType(Majik.Core.Cards.Types.CardType.Land),
                "creature" => c.HasType(Majik.Core.Cards.Types.CardType.Creature),
                "artifact" => c.HasType(Majik.Core.Cards.Types.CardType.Artifact),
                "enchantment" => c.HasType(Majik.Core.Cards.Types.CardType.Enchantment),
                "instant" => c.HasType(Majik.Core.Cards.Types.CardType.Instant),
                "sorcery" => c.HasType(Majik.Core.Cards.Types.CardType.Sorcery),
                "planeswalker" => c.HasType(Majik.Core.Cards.Types.CardType.Planeswalker),
                _ => false,
            };
            var pick = caster.Zones.Library.GetCards().FirstOrDefault(Pred);
            if (pick == null) return;
            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
            // CR 701.19c — shuffle after a search effect.
            // (No IZone.Shuffle yet; GameDriver owns shuffle. Skip for MVP —
            // search ordering not exposed via library iteration today.)
        }) });

    // Basic land names per CR 305.6.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase) { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    private static SpellDefinition SearchLandToBattlefieldSpell(
        Player caster, string kindRaw, bool tapped) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"tutor land -> battlefield{(tapped ? " tapped" : "")}", () =>
        {
            bool Pred(ICard c)
            {
                if (!c.HasType(Majik.Core.Cards.Types.CardType.Land)) return false;
                if (kindRaw.Contains("basic", StringComparison.OrdinalIgnoreCase))
                    return BasicLandNames.Contains(c.Name);
                return true;
            }

            var pick = caster.Zones.Library.GetCards().FirstOrDefault(Pred);
            if (pick == null) return;
            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            if (tapped && pick is Permanent perm)
                perm.Tap();
            // CR 701.19c — shuffle after a search effect (skipped for MVP;
            // same rationale as SearchLibrarySpell above).
        }) });

    /// <summary>
    /// Green Sun's Zenith template — {X}{G} sorcery (Rule 107.4b X cost).
    /// Tutors the first library card whose color matches <paramref name="colorRaw"/> and
    /// whose mana value ≤ X, placing it directly onto the battlefield (CR 701.19a).
    ///
    /// Color is determined by <see cref="CardColors.GetColors"/>, which derives color
    /// from the card's mana cost pips (CR 105.2a).
    ///
    /// Post-resolution self-return-to-library (the "Shuffle Green Sun's Zenith into
    /// its owner's library" clause, CR 608.2c override) is DEFERRED — v1 lets the
    /// spell go to the graveyard like any other sorcery. Engine infrastructure for
    /// a generic "ShuffleSourceToLibraryOnResolve" hook in SpellCastFlow is needed
    /// to implement it correctly.
    /// </summary>
    private static SpellDefinition GreenSunsZenithSpell(Player caster, string colorRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: p =>
        {
            var x = p.X ?? 0;
            // Map the oracle-text color word to the ManaColor enum value.
            var targetColor = colorRaw.ToLowerInvariant() switch
            {
                "white"  => ManaColor.White,
                "blue"   => ManaColor.Blue,
                "black"  => ManaColor.Black,
                "red"    => ManaColor.Red,
                "green"  => ManaColor.Green,
                _        => ManaColor.Green,
            };
            return new IEffect[] { new Effect($"GSZ x={x}", () =>
            {
                // CR 701.19a — search is a library action; pick the first qualifying card.
                // Real implementation would prompt the agent; v1 is deterministic (first match).
                var pick = caster.Zones.Library.GetCards()
                    .FirstOrDefault(c =>
                        c.HasType(Majik.Core.Cards.Types.CardType.Creature) &&
                        CardColors.GetColors(c).Contains(targetColor) &&
                        ManaCost.Parse(c.ManaCost).TotalValue <= x);
                if (pick != null)
                {
                    caster.Zones.Library.RemoveCard(pick);
                    caster.Zones.Battlefield.AddCard(pick);
                    pick.SetZone(ZoneType.Battlefield);
                }
                // CR 701.19c — shuffle after a search effect (deferred, same rationale
                // as other search spells in this binder).
            }) };
        });

    private static void MoveToExile(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            if (card.Zone == ZoneType.Battlefield) owner.Zones.Battlefield.RemoveCard(card);
            else if (card.Zone == ZoneType.Graveyard) owner.Zones.Graveyard.RemoveCard(card);
            else if (card.Zone == ZoneType.Hand) owner.Zones.Hand.RemoveCard(card);
            else if (card.Zone == ZoneType.Library) owner.Zones.Library.RemoveCard(card);
            owner.Zones.Exile.AddCard(card);
        }
        card.SetZone(ZoneType.Exile);
    }

    private static SpellDefinition TapTargetSpell(Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("tap target", () =>
            {
                if (target is Permanent perm && !perm.IsTapped) perm.Tap();
            }) };
        });

    private static SpellDefinition BounceTargetSpell(
        Func<object, object> resolver, string label) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("bounce", () =>
            {
                if (target is ICard card) ReturnToOwnersHand(card);
            }) };
        });

    private static void ReturnToOwnersHand(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            if (card.Zone == ZoneType.Battlefield)
                owner.Zones.Battlefield.RemoveCard(card);
            else if (card.Zone == ZoneType.Graveyard)
                owner.Zones.Graveyard.RemoveCard(card);
            else if (card.Zone == ZoneType.Exile)
                owner.Zones.Exile.RemoveCard(card);
            owner.Zones.Hand.AddCard(card);
        }
        card.SetZone(ZoneType.Hand);
    }

    private static string NormaliseKeyword(string raw) =>
        // Collapse multi-word "first strike" / "double strike"; preserve casing
        // canonical to engine ("First strike" matches CombatAbilities check).
        raw.ToLowerInvariant() switch
        {
            "first strike" => "First strike",
            "double strike" => "Double strike",
            _ => char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant(),
        };

    private static SpellDefinition PumpSpell(int p, int t, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: param =>
        {
            var target = resolver(param.Targets[0][0]);
            return new IEffect[] { new Effect($"+{p}/+{t} EOT", () =>
            {
                if (target is Creature c && c.ActiveEffects != null)
                {
                    c.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(c, p, t));
                }
            }) };
        });

    private static SpellDefinition GrantKeywordSpell(string keyword, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: param =>
        {
            var target = resolver(param.Targets[0][0]);
            return new IEffect[] { new Effect($"grants {keyword} EOT", () =>
            {
                if (target is Creature c && c.ActiveEffects != null)
                {
                    c.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(c, keyword));
                }
            }) };
        });

    private static SpellDefinition ExileFromGraveyardSpell(Func<object, object> resolver, string kindRaw) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(
            string.IsNullOrEmpty(kindRaw) ? "target card in graveyard" : $"target {kindRaw} card in graveyard",
            1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("exile from gy", () =>
            {
                if (target is ICard card && card.Zone == ZoneType.Graveyard)
                    MoveToExile(card);
            }) };
        });

    /// <summary>Layer 7c +P/+T effect with end-of-turn expiry.</summary>
    private sealed class PumpUntilEndOfTurnEffect : Majik.Core.Effects.ContinuousEffect
    {
        private readonly Creature _target;
        private readonly int _p, _t;
        public PumpUntilEndOfTurnEffect(Creature target, int p, int t)
        { _target = target; _p = p; _t = t; }
        public override Majik.Core.Effects.Layer Layer => Majik.Core.Effects.Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(Majik.Core.Effects.CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _t; }
    }

    /// <summary>Layer 6 keyword grant with end-of-turn expiry.</summary>
    private sealed class GrantKeywordUntilEndOfTurnEffect : Majik.Core.Effects.ContinuousEffect
    {
        private readonly Creature _target;
        private readonly string _kw;
        public GrantKeywordUntilEndOfTurnEffect(Creature target, string kw)
        { _target = target; _kw = kw; }
        public override Majik.Core.Effects.Layer Layer => Majik.Core.Effects.Layer.Abilities;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(Majik.Core.Effects.CreatureCharacteristics chars)
        { chars.Keywords.Add(_kw); }
    }

    /// <summary>
    /// Thoughtseize template (v1 — deterministic pick: first non-land card in target's hand).
    /// Real Thoughtseize lets the caster choose; v1 simplification picks deterministically.
    /// Caster loses <paramref name="lifeLoss"/> life after the discard.
    /// </summary>
    private static SpellDefinition ThoughtseizeSpell(Player caster, Func<object, object> resolver, int lifeLoss) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("thoughtseize", () =>
            {
                if (target is not Player tp) return;
                // v1: deterministic pick — first non-land card in target's hand.
                var pick = tp.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
                if (pick != null)
                {
                    tp.Zones.Hand.RemoveCard(pick);
                    tp.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
                caster.LoseLife(lifeLoss);
            }) };
        });

    // ---------- Primitives ----------

    internal static void DealDamage(object target, int n)
    {
        switch (target)
        {
            case Player p: p.LoseLife(n); break;
            case Creature c: c.TakeDamage(n); break;
        }
    }

    internal static void MoveToGraveyard(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            owner.Zones.Battlefield.RemoveCard(card);
            owner.Zones.Graveyard.AddCard(card);
        }
        card.SetZone(ZoneType.Graveyard);
    }

    private static void DrawCards_(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    internal static void RemoveFromStack(Majik.Core.Stack.Stack stack, IStackObject spell)
    {
        var keep = new List<IStackObject>();
        while (!stack.IsEmpty)
        {
            var top = stack.Pop()!;
            if (!ReferenceEquals(top, spell)) keep.Add(top);
        }
        for (var i = keep.Count - 1; i >= 0; i--)
        {
            stack.Push(keep[i]);
        }
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };

    /// <summary>
    /// Malevolent Rumble (Duskmourn).
    /// Reveal top 4 — auto-pick first permanent card to caster's hand, rest to
    /// graveyard, create one Eldrazi Spawn token.
    ///
    /// v1 gaps (deferred):
    /// - Real player choice among the revealed permanents (no prompt yet).
    /// - "You may put … into your hand" is optional — v1 always picks if a
    ///   permanent is present (opt-out awaits agent prompt system).
    /// </summary>
    private static SpellDefinition MalevolentRumbleSpell(Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect("Malevolent Rumble", () =>
        {
            // Reveal top 4 (may be fewer if library is smaller).
            var top4 = caster.Zones.Library.GetCards().Take(4).ToList();

            if (top4.Count > 0)
            {
                // CR 603 / 700.3a: permanent cards — creature, artifact, enchantment,
                // land, planeswalker, battle.
                var permanentCard = top4.FirstOrDefault(c =>
                    c.HasType(Majik.Core.Cards.Types.CardType.Creature) ||
                    c.HasType(Majik.Core.Cards.Types.CardType.Artifact) ||
                    c.HasType(Majik.Core.Cards.Types.CardType.Enchantment) ||
                    c.HasType(Majik.Core.Cards.Types.CardType.Land) ||
                    c.HasType(Majik.Core.Cards.Types.CardType.Planeswalker));

                foreach (var c in top4)
                {
                    caster.Zones.Library.RemoveCard(c);
                    if (ReferenceEquals(c, permanentCard))
                    {
                        caster.Zones.Hand.AddCard(c);
                        c.SetZone(ZoneType.Hand);
                    }
                    else
                    {
                        caster.Zones.Graveyard.AddCard(c);
                        c.SetZone(ZoneType.Graveyard);
                    }
                }
            }

            // Token creation is unconditional — not gated on library size.
            Majik.Core.Tokens.TokenFactory.CreateEldraziSpawn(caster);
        }) });
}
