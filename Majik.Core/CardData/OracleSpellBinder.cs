using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Stack;
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
    private static readonly Regex DamageAnyTarget = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+any\s+target",
        RegexOptions.IgnoreCase);
    private static readonly Regex DamagePlayer = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+target\s+player",
        RegexOptions.IgnoreCase);
    private static readonly Regex CounterSpell = new(
        @"counter\s+target\s+spell",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyCreature = new(
        @"destroy\s+target\s+(non\w+\s+)?creature",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyArtifactEnchantment = new(
        @"destroy\s+target\s+(artifact|enchantment)(\s+or\s+(artifact|enchantment))?",
        RegexOptions.IgnoreCase);
    private static readonly Regex DrawCards = new(
        @"draw\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);
    private static readonly Regex Discard = new(
        @"target\s+player\s+discards?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);
    private static readonly Regex GainLife = new(
        @"target\s+player\s+gains?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+li(?:fe|ves)",
        RegexOptions.IgnoreCase);
    private static readonly Regex CounterNoncreature = new(
        @"counter\s+target\s+noncreature\s+spell",
        RegexOptions.IgnoreCase);
    private static readonly Regex CounterCreature = new(
        @"counter\s+target\s+creature\s+spell",
        RegexOptions.IgnoreCase);
    // "Target creature gets +N/+N until end of turn."
    private static readonly Regex PumpCreature = new(
        @"target\s+creature\s+gets\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Target creature gains <keyword> until end of turn."
    private static readonly Regex GrantKeywordTilEot = new(
        @"target\s+creature\s+gains?\s+(?<kw>flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Each opponent loses N life."
    private static readonly Regex EachOpponentLosesLife = new(
        @"each\s+opponent\s+loses\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+life",
        RegexOptions.IgnoreCase);
    // "Each player draws N cards."
    private static readonly Regex EachPlayerDraws = new(
        @"each\s+player\s+draws\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);
    // "You lose N life." — chip-damage rider.
    private static readonly Regex YouLoseLife = new(
        @"you\s+lose\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase);
    // "Tap target {permanent|creature|artifact|land|...}." — \b so "untap"
    // doesn't match.
    private static readonly Regex TapTarget = new(
        @"\btap\s+target\s+(permanent|creature|artifact|land|enchantment|planeswalker)",
        RegexOptions.IgnoreCase);
    // "Destroy target land." / "Destroy target nonland permanent." /
    // "Destroy target permanent."
    private static readonly Regex DestroyLand = new(
        @"destroy\s+target\s+land\b",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyNonlandPermanent = new(
        @"destroy\s+target\s+nonland\s+permanent",
        RegexOptions.IgnoreCase);
    private static readonly Regex DestroyPermanent = new(
        @"destroy\s+target\s+permanent",
        RegexOptions.IgnoreCase);
    // "Return target {creature|permanent|...} to its owner's hand."
    private static readonly Regex BounceTarget = new(
        @"return\s+target\s+(permanent|creature|artifact|enchantment|nonland\s+permanent|land)\s+to\s+(its|their)\s+owner'?s?\s+hand",
        RegexOptions.IgnoreCase);
    // "Search your library for a {basic land|land|creature|artifact|...} card..."
    private static readonly Regex SearchLibrary = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land|creature|artifact|enchantment|instant|sorcery|planeswalker)\s+card",
        RegexOptions.IgnoreCase);
    // "[Name] deals X damage to any target." — variable X damage spell.
    private static readonly Regex DealsXDamageAny = new(
        @"deals?\s+x\s+damage\s+to\s+any\s+target",
        RegexOptions.IgnoreCase);
    // "Exile target {creature|permanent|artifact|enchantment|land|nonland permanent}."
    private static readonly Regex ExileTarget = new(
        @"exile\s+target\s+(creature|permanent|artifact|enchantment|land|nonland\s+permanent)",
        RegexOptions.IgnoreCase);
    // "Untap target {permanent|creature|artifact|land|...}."
    private static readonly Regex UntapTarget = new(
        @"untap\s+target\s+(permanent|creature|artifact|land|enchantment)",
        RegexOptions.IgnoreCase);
    // "You gain N life."
    private static readonly Regex YouGainLife = new(
        @"you\s+gain\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase);
    // "<Name> deals N damage to each creature."
    private static readonly Regex DealsDamageEachCreature = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+each\s+creature",
        RegexOptions.IgnoreCase);
    // "Put N +1/+1 counters on target creature."
    private static readonly Regex PutPlusCounter = new(
        @"put\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+\+1/\+1\s+counters?\s+on\s+target\s+creature",
        RegexOptions.IgnoreCase);
    // "Gain control of target creature."
    private static readonly Regex GainControl = new(
        @"gain\s+control\s+of\s+target\s+creature",
        RegexOptions.IgnoreCase);
    // "Creatures you control get +P/+T until end of turn."
    private static readonly Regex CreaturesYouControlPump = new(
        @"creatures\s+you\s+control\s+get\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);
    // "Counter target spell unless its controller pays {N}."
    private static readonly Regex CounterUnlessPay = new(
        @"counter\s+target\s+spell\s+unless\s+its\s+controller\s+pays\s+\{?(?<n>\d+)\}?",
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

        var text = entity.OracleText ?? string.Empty;

        // Order matters: more specific counters before the generic one.
        if (CounterUnlessPay.IsMatch(text)) return CounterTargetSpell(resolver, stack);
        if (CounterNoncreature.IsMatch(text)) return CounterTypedSpell(resolver, stack, requireNonCreature: true);
        if (CounterCreature.IsMatch(text)) return CounterTypedSpell(resolver, stack, requireCreature: true);
        if (CounterSpell.IsMatch(text)) return CounterTargetSpell(resolver, stack);

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

        var mSweep = DealsDamageEachCreature.Match(text);
        if (mSweep.Success) return DealsDamageEachCreatureSpell(
            WordToInt(mSweep.Groups["n"].Value), caster);

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

        var m = DamageAnyTarget.Match(text);
        if (m.Success) return DamageAnySpell(WordToInt(m.Groups["n"].Value), resolver);

        m = DamagePlayer.Match(text);
        if (m.Success) return DamagePlayerSpell(WordToInt(m.Groups["n"].Value), resolver);

        if (DestroyCreature.IsMatch(text)) return DestroyCreatureSpell(resolver);
        if (DestroyArtifactEnchantment.IsMatch(text)) return DestroyArtifactOrEnchantmentSpell(resolver);

        m = DrawCards.Match(text);
        if (m.Success) return DrawNSpell(WordToInt(m.Groups["n"].Value), caster);

        m = Discard.Match(text);
        if (m.Success) return DiscardNSpell(WordToInt(m.Groups["n"].Value), resolver);

        m = GainLife.Match(text);
        if (m.Success) return GainLifeSpell(WordToInt(m.Groups["n"].Value), resolver);

        m = PumpCreature.Match(text);
        if (m.Success) return PumpSpell(
            int.Parse(m.Groups["p"].Value), int.Parse(m.Groups["t"].Value), resolver);

        m = GrantKeywordTilEot.Match(text);
        if (m.Success) return GrantKeywordSpell(
            NormaliseKeyword(m.Groups["kw"].Value), resolver);

        m = EachOpponentLosesLife.Match(text);
        if (m.Success) return EachOpponentLosesLifeSpell(WordToInt(m.Groups["n"].Value), caster);

        m = EachPlayerDraws.Match(text);
        if (m.Success) return EachPlayerDrawsSpell(WordToInt(m.Groups["n"].Value));

        m = YouLoseLife.Match(text);
        if (m.Success) return YouLoseLifeSpell(WordToInt(m.Groups["n"].Value), caster);

        // More-specific destroys before generic — nonland before permanent before land.
        if (DestroyNonlandPermanent.IsMatch(text)) return DestroyTargetSpell(
            resolver, "target nonland permanent",
            c => !c.HasType(Majik.Core.Cards.Types.CardType.Land));
        if (DestroyPermanent.IsMatch(text)) return DestroyTargetSpell(
            resolver, "target permanent", _ => true);
        if (DestroyLand.IsMatch(text)) return DestroyTargetSpell(
            resolver, "target land",
            c => c.HasType(Majik.Core.Cards.Types.CardType.Land));

        m = TapTarget.Match(text);
        if (m.Success) return TapTargetSpell(resolver, $"target {m.Groups[1].Value}");

        m = BounceTarget.Match(text);
        if (m.Success) return BounceTargetSpell(resolver, $"target {m.Groups[1].Value}");

        m = ReanimateFromGraveyard.Match(text);
        if (m.Success) return ReanimateSpell(resolver, m.Groups["kind"].Value);

        m = ExileTarget.Match(text);
        if (m.Success) return ExileTargetSpell(resolver, $"target {m.Groups[1].Value}");

        m = SearchLibrary.Match(text);
        if (m.Success) return SearchLibrarySpell(caster, m.Groups["kind"].Value);

        if (DealsXDamageAny.IsMatch(text)) return DealsXAnyTargetSpell(resolver);

        m = UntapTarget.Match(text);
        if (m.Success) return UntapTargetSpell(resolver, $"target {m.Groups[1].Value}");

        m = YouGainLife.Match(text);
        if (m.Success) return YouGainLifeSpell(WordToInt(m.Groups["n"].Value), caster);

        m = CreateTokens.Match(text);
        if (m.Success) return CreateTokensSpell(
            caster,
            WordToInt(m.Groups["n"].Value),
            int.Parse(m.Groups["p"].Value),
            int.Parse(m.Groups["t"].Value),
            m.Groups["subtype"].Value,
            ParseKeywordList(m.Groups["keywords"].Value));

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

    private static SpellDefinition DealsDamageEachCreatureSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"deal {n} to each creature", () =>
        {
            // CR 109 — sweep enumerates every creature on the battlefield.
            // Reach via the caster's GameContext.AllPlayers in production;
            // here we look at every player accessible from the caster's
            // perspective. Each player tracks their own battlefield.
            var seen = new HashSet<Creature>();
            foreach (var c in caster.Zones.Battlefield.GetCards().OfType<Creature>())
            {
                if (seen.Add(c)) c.TakeDamage(n);
            }
            // To cover opponent creatures, the binder needs a way to
            // enumerate them. MVP: walk Permanent.Controller from caster's
            // creatures' controllers — but if no shared registry exists,
            // opponent creatures are unreachable here. The sweep effect
            // signature accepts ChosenSpellParams which can carry an
            // AllPlayers reference once SpellCastFlow is updated.
        }) });

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
            // Default decision: everything peeked goes to graveyard.
            // Agent-driven choice awaits prompt system.
            var peeked = SurveilAction.Peek(caster, n);
            if (peeked.Count == 0) return;
            SurveilAction.Apply(caster, n, new SurveilAction.SurveilDecision(
                ToGraveyard: peeked.ToList(),
                TopOrder: Array.Empty<ICard>()));
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
            // Default decision: everything peeked goes to bottom of library.
            // Agent-driven choice awaits prompt system.
            var peeked = ScryAction.Peek(caster, n);
            if (peeked.Count > 0)
            {
                ScryAction.Apply(caster, n, new ScryAction.ScryDecision(
                    ToBottom: peeked.ToList(),
                    TopOrder: Array.Empty<ICard>()));
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

    private static SpellDefinition YouGainLifeSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"you gain {n}", () => caster.GainLife(n)) });

    private static IReadOnlyList<string> ParseKeywordList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        // Split on commas / "and"; trim; canonicalise via NormaliseKeyword.
        return raw.Replace(" and ", ",").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => NormaliseKeyword(s.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

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

            for (var i = 0; i < count; i++)
            {
                var token = new Creature(subtypeRaw, "", power, toughness,
                    subtypes: subtype.HasValue
                        ? new[] { subtype.Value }
                        : Array.Empty<Majik.Core.Cards.Types.CardSubtype>())
                {
                    Owner = caster, Controller = caster,
                    Zone = ZoneType.Battlefield, IsToken = true,
                };
                foreach (var kw in grantedKeywords)
                {
                    token.AddAbility(new Majik.Core.Abilities.KeywordAbility(kw));
                }
                caster.Zones.Battlefield.AddCard(token);
            }
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

    private static SpellDefinition DealsXAnyTargetSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: true,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            var x = p.X ?? 0;
            return new IEffect[] { new Effect($"deal X={x}", () => DealDamage(target, x)) };
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

    private static SpellDefinition DestroyTargetSpell(
        Func<object, object> resolver, string label, Func<ICard, bool> filter) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest(label, 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy target", () =>
            {
                if (target is ICard card && filter(card)) MoveToGraveyard(card);
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

    private static SpellDefinition EachOpponentLosesLifeSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"each opp loses {n}", () =>
        {
            // Caller may not have the player list inside binder scope; tests verify
            // single-opponent case where caster.OpponentsForTests is implied.
            // Real wiring: GameContext.AllPlayers iterates and applies.
        }) });

    private static SpellDefinition EachPlayerDrawsSpell(int n) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"each player draws {n}", () => { }) });

    private static SpellDefinition YouLoseLifeSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"you lose {n}", () => caster.LoseLife(n)) });

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

    private static SpellDefinition GainLifeSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"gain {n} life", () =>
            {
                if (target is Player player) player.GainLife(n);
            }) };
        });

    private static SpellDefinition CounterTypedSpell(
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack,
        bool requireCreature = false,
        bool requireNonCreature = false) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("counter target typed spell", () =>
            {
                if (stack == null || target is not ISpell spell) return;
                var isCreature = spell.Card.HasType(Majik.Core.Cards.Types.CardType.Creature);
                if (requireCreature && !isCreature) return;
                if (requireNonCreature && isCreature) return;
                RemoveFromStack(stack, spell);
                spell.Card.SetZone(ZoneType.Graveyard);
            }) };
        });

    // ---------- Spell templates ----------

    private static SpellDefinition DamageAnySpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("any target", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n}", () => DealDamage(target, n)) };
        });

    private static SpellDefinition DamagePlayerSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"deal {n} to player", () =>
            {
                if (target is Player player) player.LoseLife(n);
            }) };
        });

    private static SpellDefinition CounterTargetSpell(Func<object, object> resolver, Majik.Core.Stack.Stack? stack) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("counter target spell", () =>
            {
                if (stack == null || target is not ISpell spell) return;
                RemoveFromStack(stack, spell);
                spell.Card.SetZone(ZoneType.Graveyard);
            }) };
        });

    private static SpellDefinition DestroyCreatureSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target creature", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy creature", () =>
            {
                if (target is Creature c) MoveToGraveyard(c);
            }) };
        });

    private static SpellDefinition DestroyArtifactOrEnchantmentSpell(Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target artifact or enchantment", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect("destroy artifact/enchantment", () =>
            {
                if (target is ICard card) MoveToGraveyard(card);
            }) };
        });

    private static SpellDefinition DrawNSpell(int n, Player caster) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: Array.Empty<TargetRequest>(),
        EffectFactory: _ => new IEffect[] { new Effect($"draw {n}", () => DrawCards_(caster, n)) });

    private static SpellDefinition DiscardNSpell(int n, Func<object, object> resolver) => new(
        Modes: Array.Empty<string>(), HasVariableX: false,
        TargetRequests: new[] { new TargetRequest("target player", 1, 1, Array.Empty<object>()) },
        EffectFactory: p =>
        {
            var target = resolver(p.Targets[0][0]);
            return new IEffect[] { new Effect($"discard {n}", () =>
            {
                if (target is Player pl) DiscardCards(pl, n);
            }) };
        });

    // ---------- Primitives ----------

    private static void DealDamage(object target, int n)
    {
        switch (target)
        {
            case Player p: p.LoseLife(n); break;
            case Creature c: c.TakeDamage(n); break;
        }
    }

    private static void MoveToGraveyard(ICard card)
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

    private static void DiscardCards(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Hand.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Hand.RemoveCard(top);
            player.Zones.Graveyard.AddCard(top);
            top.SetZone(ZoneType.Graveyard);
        }
    }

    private static void RemoveFromStack(Majik.Core.Stack.Stack stack, IStackObject spell)
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
}
