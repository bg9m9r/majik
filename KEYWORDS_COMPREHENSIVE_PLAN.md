# Comprehensive Keyword Support Plan

## Overview

This plan outlines how to support **all 728 keywords** from the database, including:
- Official Magic: The Gathering keywords (Rule 702)
- Parameterized keywords (ward {2}, protection from X)
- Custom/unofficial keywords
- Card names that were incorrectly tagged as keywords
- Keywords that need oracle text parsing

**Goal**: Automatically handle all keywords from the database, with graceful fallbacks for unknown keywords.

**Current Status**: 50 keywords implemented, 678 remaining to categorize and implement.

**Official Magic Keywords (Rule 702)**: 189 keywords defined in the rules

## Current Status

- ✅ 50 keywords implemented
- ✅ Parameterized keyword parsing (ward, protection, hexproof from)
- ✅ Card name filtering heuristics
- ⏳ 678 keywords remaining to categorize and implement

## Keyword Categories

### Category 1: Official Magic Keywords (Rule 702)

These are defined in the Magic Comprehensive Rules section 702. These should be fully implemented.

**Static Abilities (Layer 6):**
- Deathtouch, Defender, Double Strike, First Strike, Flash, Flying, Haste, Hexproof, Indestructible, Intimidate, Landwalk variants, Lifelink, Protection, Reach, Shroud, Trample, Vigilance, Shadow, Horsemanship, Fear, Skulk, Menace, Infect, Wither, Toxic, Decayed

**Triggered Abilities:**
- Prowess, Landfall, Exalted, Enrage, Ward, Constellation, Delirium, Morbid, Raid, Revolt, Threshold, Hellbent, Metalcraft, Spell mastery, etc.

**Activated Abilities:**
- Cycling (and variants), Equip, Channel, Flashback, Kicker, Multikicker, Buyback, Retrace, Rebound, Replicate, Overload, etc.

**Other:**
- Bestow, Bloodrush, Dash, Devotion, Dredge, Embalm, Eternalize, Evoke, Exploit, Fading, Flashback, Forecast, Fortify, Graft, Haunt, Hideaway, Imprint, Kicker, Level Up, Madness, Manifest, Megamorph, Meld, Miracle, Morph, Ninjutsu, Outlast, Overload, Persist, Phasing, Provoke, Prowl, Recover, Reinforce, Replicate, Retrace, Ripple, Scavenge, Splice, Storm, Sunburst, Suspend, Transfigure, Transmute, Unearth, Vanishing, etc.

### Category 2: Parameterized Keywords

Keywords that have variable parameters. These need special parsing:

- **Ward {X}**: "ward {2}", "ward {1}"
- **Protection from X**: "protection from red", "protection from artifacts"
- **Hexproof from X**: "hexproof from blue"
- **Cycling variants**: "basic landcycling", "forestcycling", etc.
- **Kicker variants**: "kicker {R}", "multikicker {2}"
- **Landwalk variants**: "islandwalk", "nonbasic landwalk", "legendary landwalk"
- **Typecycling**: "wizardcycling", "slivercycling", etc.

### Category 3: Card Names (False Positives)

Many entries in the CSV are actually card names, not keywords:
- "A Test of Your Reflexes!"
- "Fire of Tzeentch"
- "10,000 Needles"
- "Bad Wolf"
- "I. AM. TALKING!"
- etc.

These should be filtered out and not treated as keywords.

### Category 4: Custom/Unofficial Keywords

Keywords from custom sets, fan-made cards, or special formats:
- "Airbend", "Earthbend", "Firebending", "Waterbend" (custom)
- "Doctor's companion" (special set)
- "Friends forever" (special set)
- Various custom abilities

These may need special handling or can be ignored if not used.

### Category 5: Ability Names (Not Keywords)

Some entries are ability names or card-specific abilities, not reusable keywords:
- "Ability" (too generic)
- "Attack" (not a keyword)
- "Magic" (not a keyword)
- Various card-specific ability names

## Implementation Strategy

### Phase 1: Keyword Categorization and Analysis

**Objective**: Categorize all 728 keywords from the CSV.

**Tasks**:

1. **Create Keyword Analyzer Tool**
   - Parse CSV file
   - Categorize each keyword:
     - Official Magic keyword (Rule 702)
     - Parameterized variant
     - Card name (filter out)
     - Custom/unofficial keyword
     - Ability name (not a keyword)
   - Generate categorization report

2. **Build Official Keyword List**
   - Extract all keywords from Magic Rules (Rule 702)
   - Create comprehensive list of official keywords
   - Map to ability types and layers

3. **Create Keyword Database**
   - Store keyword metadata in database
   - Track implementation status
   - Store categorization results

**Deliverables**:
- `KeywordAnalyzer.cs`: Tool to analyze and categorize keywords
- `OfficialKeywordList.cs`: Complete list of official Magic keywords
- Database table: `KeywordMetadata` with categorization

### Phase 2: Systematic Keyword Implementation

**Objective**: Implement all official Magic keywords systematically.

**Approach**: Implement by category and complexity:

#### 2.1 Static Abilities (Priority: High)

**Simple Static Abilities** (Layer 6, no parameters):
- Implement all evasion abilities
- Implement all combat abilities
- Implement all protection abilities
- Use template-based approach for similar keywords

**Template System**:
```csharp
// Template for simple static abilities
RegisterStaticKeyword("keyword_name", 
    layer: 6,
    description: "...",
    effectHandler: (permanent) => { /* effect logic */ });
```

#### 2.2 Triggered Abilities (Priority: Medium)

**Common Triggered Abilities**:
- Prowess, Landfall, Exalted, Enrage, Constellation, etc.
- Create triggered ability implementations
- Register with TriggerManager

**Implementation Pattern**:
```csharp
RegisterTriggeredKeyword("prowess",
    triggerCondition: (event) => event is SpellCastEvent,
    effect: (source, controller) => { /* +1/+1 until end of turn */ });
```

#### 2.3 Activated Abilities (Priority: Medium)

**Common Activated Abilities**:
- Cycling variants, Equip, Channel, Flashback, etc.
- Handle variable costs
- Create activated ability implementations

#### 2.4 Complex Keywords (Priority: Low)

**Keywords requiring special handling**:
- Bestow, Bloodrush, Dash, Devotion, Dredge, etc.
- May need custom implementations
- May need oracle text parsing

### Phase 3: Parameterized Keyword System

**Objective**: Handle all parameterized keyword variations.

**Components**:

1. **Parameterized Keyword Parser**
   - Extend `KeywordParser` to handle all parameterized forms
   - Support patterns:
     - `{keyword} {cost}` (ward {2})
     - `{keyword} from {quality}` (protection from red)
     - `{keyword} {type}` (forestcycling)
     - `{keyword} {number}` (kicker {R})

2. **Parameterized Keyword Registry**
   - Store base keyword + parameter patterns
   - Map to ability creation functions
   - Handle parameter validation

3. **Dynamic Ability Creation**
   - Create abilities with parameters at runtime
   - Store parameters in database
   - Apply parameters when creating abilities

**Example**:
```csharp
RegisterParameterizedKeyword("ward",
    pattern: @"ward\s+\{([^}]+)\}",
    createAbility: (source, controller, parameters) => 
        CreateWardAbility(source, controller, parameters["cost"]));
```

### Phase 4: Fallback System for Unknown Keywords

**Objective**: Handle keywords that aren't registered yet.

**Strategy**:

1. **Oracle Text Fallback**
   - If keyword not found, check oracle text
   - Parse oracle text for ability description
   - Create ability from parsed oracle text
   - Cache result for future use

2. **Generic Keyword Handler**
   - Create generic ability placeholder
   - Store keyword name and parameters
   - Allow manual override later
   - Log unknown keywords for review

3. **Keyword Learning System**
   - Track unknown keywords
   - Analyze patterns
   - Suggest implementations
   - Auto-generate implementations where possible

**Implementation**:
```csharp
public class UnknownKeywordHandler
{
    public CardAbilityEntity? HandleUnknownKeyword(
        string keyword, 
        CardEntity card,
        int cardId)
    {
        // Try oracle text parsing
        if (!string.IsNullOrWhiteSpace(card.OracleText))
        {
            var parser = new OracleTextParser();
            var parsed = parser.Parse(card.OracleText);
            // Try to match keyword to parsed ability
        }
        
        // Create generic placeholder
        return CreateGenericKeywordAbility(keyword, cardId);
    }
}
```

### Phase 5: Card Name Filtering Enhancement

**Objective**: Improve filtering to accurately identify card names vs keywords.

**Enhancements**:

1. **Database Cross-Reference**
   - Check if keyword matches a card name in database
   - If it's a card name, filter it out
   - Use fuzzy matching for variations

2. **Pattern Recognition**
   - Improve heuristics:
     - Length thresholds
     - Capitalization patterns
     - Punctuation patterns
     - Word count
     - Common Magic terms

3. **Machine Learning Approach** (Future)
   - Train model to classify keywords vs card names
   - Use card name database as training data
   - Continuously improve accuracy

**Implementation**:
```csharp
public class KeywordFilter
{
    private readonly HashSet<string> _cardNames; // Loaded from database
    
    public bool IsCardName(string keyword)
    {
        // Check exact match
        if (_cardNames.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            return true;
        
        // Check fuzzy match
        // Check patterns
        // Return result
    }
}
```

### Phase 6: Custom Keyword Support

**Objective**: Support custom/unofficial keywords when needed.

**Strategy**:

1. **Custom Keyword Registry**
   - Allow registration of custom keywords
   - Store in database
   - Support custom ability implementations

2. **Keyword Plugin System**
   - Load custom keyword definitions from files
   - Support JSON/YAML keyword definitions
   - Allow runtime registration

3. **Fallback to Oracle Text**
   - For custom keywords, parse oracle text
   - Create abilities from parsed text
   - Cache implementations

## Database Schema Enhancements

### New Table: KeywordMetadata

```sql
CREATE TABLE KeywordMetadata (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Keyword TEXT NOT NULL UNIQUE,
    Category TEXT NOT NULL, -- 'Official', 'Parameterized', 'Custom', 'CardName', 'Unknown'
    AbilityType TEXT, -- 'Static', 'Triggered', 'Activated', 'Replacement'
    Layer INTEGER, -- For static abilities
    IsParameterized BOOLEAN DEFAULT 0,
    ParameterPattern TEXT, -- Regex pattern for parameters
    Description TEXT,
    ImplementationStatus TEXT, -- 'Implemented', 'Placeholder', 'NotImplemented'
    Notes TEXT,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME
);
```

### New Table: KeywordParameters

```sql
CREATE TABLE KeywordParameters (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    KeywordMetadataId INTEGER NOT NULL,
    ParameterName TEXT NOT NULL,
    ParameterType TEXT NOT NULL, -- 'Cost', 'Quality', 'Type', 'Number', 'String'
    IsRequired BOOLEAN DEFAULT 1,
    DefaultValue TEXT,
    FOREIGN KEY (KeywordMetadataId) REFERENCES KeywordMetadata(Id)
);
```

## Implementation Phases

### Phase 1: Analysis and Categorization (Week 1)
- Create keyword analyzer tool
- Categorize all 728 keywords
- Build official keyword list from rules
- Create keyword metadata database

### Phase 2: Core Keywords (Week 2)
- Implement all simple static abilities (~30 keywords)
- Implement common triggered abilities (~20 keywords)
- Implement common activated abilities (~15 keywords)

### Phase 3: Parameterized Keywords (Week 2-3)
- Enhance parameterized keyword parser
- Implement parameterized keyword handlers
- Support all cycling variants
- Support all landwalk variants
- Support ward, protection, hexproof from

### Phase 4: Complex Keywords (Week 3-4)
- Implement complex keywords (bestow, bloodrush, etc.)
- Handle keywords requiring special logic
- Integrate with existing systems

### Phase 5: Fallback System (Week 4)
- Implement oracle text fallback
- Create generic keyword handler
- Implement keyword learning system
- Enhanced card name filtering

### Phase 6: Custom Keywords (Week 4-5)
- Custom keyword registry
- Plugin system
- Runtime registration

### Phase 7: Testing and Refinement (Week 5)
- Test all keywords from database
- Validate filtering accuracy
- Performance optimization
- Documentation

## Tools and Utilities

### 1. Keyword Analyzer Tool

**File**: `Majik.Tools/KeywordAnalyzer.cs`

**Purpose**: Analyze and categorize keywords from CSV

**Features**:
- Parse CSV file
- Cross-reference with card names database
- Check against official keyword list
- Identify parameterized patterns
- Generate categorization report
- Export to database

### 2. Keyword Implementation Generator

**File**: `Majik.Tools/KeywordGenerator.cs`

**Purpose**: Auto-generate keyword implementations from templates

**Features**:
- Template-based code generation
- Generate static ability implementations
- Generate triggered ability implementations
- Generate activated ability implementations
- Output to KeywordRegistry.cs

### 3. Keyword Test Suite

**File**: `Majik.Core.Tests/Keywords/KeywordTests.cs`

**Purpose**: Comprehensive tests for all keywords

**Features**:
- Test each keyword implementation
- Test parameterized keywords
- Test keyword filtering
- Test fallback system

## Keyword Implementation Templates

### Template 1: Simple Static Ability

```csharp
Register("{keyword}", new KeywordInfo(
    KeywordType.Static,
    layer: 6,
    description: "{description}",
    createAbility: (source, controller) => new StaticAbility(
        source,
        controller,
        "{Keyword}",
        isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
        applyEffect: () => { /* Effect handled by {system} */ }) as object));
```

### Template 2: Triggered Ability

```csharp
Register("{keyword}", new KeywordInfo(
    KeywordType.Triggered,
    layer: null,
    description: "{description}",
    createAbility: (source, controller) => 
    {
        // Create triggered ability
        var trigger = new TriggerCondition(...);
        var effect = new Effect(...);
        return new TriggeredAbility(source, controller, null, new[] { effect });
    }));
```

### Template 3: Activated Ability

```csharp
Register("{keyword}", new KeywordInfo(
    KeywordType.Activated,
    layer: null,
    description: "{description}",
    createAbility: (source, controller) => 
    {
        // Create activated ability with variable cost
        var cost = ParseCostFromOracleText(...);
        var effect = new Effect(...);
        return new ActivatedAbility(source, controller, null, new[] { cost }, new[] { effect });
    }));
```

## Handling Unknown Keywords

### Strategy 1: Oracle Text Parsing

When an unknown keyword is encountered:

1. Check if card has oracle text
2. Parse oracle text for ability description
3. Match keyword name to parsed ability
4. Create ability from parsed text
5. Cache result

### Strategy 2: Generic Placeholder

If oracle text parsing fails:

1. Create generic `CardAbilityEntity` with:
   - Type: Unknown
   - Original keyword stored
   - Flagged for manual review
2. Log for analysis
3. Allow manual override later

### Strategy 3: Learning System

Track unknown keywords:

1. Collect unknown keywords
2. Analyze patterns
3. Suggest implementations
4. Auto-generate where possible
5. Manual review queue

## Metrics and Success Criteria

### Success Metrics

1. **Coverage**: % of real keywords implemented
   - Target: 95%+ of official Magic keywords
   - Target: 80%+ of all keywords in database (excluding card names)

2. **Accuracy**: % of keywords correctly categorized
   - Target: 99%+ card name filtering accuracy
   - Target: 95%+ keyword vs ability name accuracy

3. **Performance**: Keyword processing time
   - Target: < 10ms per card keyword processing
   - Target: < 1s for batch processing 1000 cards

4. **Fallback Success**: % of unknown keywords handled
   - Target: 70%+ handled via oracle text parsing
   - Target: 100% have placeholder created

### Success Criteria

1. ✅ All official Magic keywords (Rule 702) implemented
2. ✅ All parameterized keyword variants supported
3. ✅ Card names filtered with 99%+ accuracy
4. ✅ Unknown keywords handled gracefully
5. ✅ Fallback system works for unregistered keywords
6. ✅ Performance meets targets
7. ✅ Comprehensive test coverage (> 90%)

## Risk Mitigation

### Risk 1: Too Many Keywords to Implement
**Mitigation**: 
- Prioritize by frequency of use
- Use templates for similar keywords
- Implement in phases
- Accept placeholders for rare keywords

### Risk 2: Parameterized Keywords Complexity
**Mitigation**:
- Start with common patterns
- Extend parser incrementally
- Use regex patterns
- Test thoroughly

### Risk 3: Card Name Filtering Accuracy
**Mitigation**:
- Use database cross-reference
- Multiple heuristics
- Manual review queue
- Continuous improvement

### Risk 4: Performance with 728 Keywords
**Mitigation**:
- Efficient data structures (dictionary lookup)
- Lazy initialization
- Caching
- Batch processing optimization

## Future Enhancements

1. **Machine Learning**: Train model to classify keywords
2. **Auto-Generation**: Generate implementations from templates
3. **Community Contributions**: Allow community to add keyword implementations
4. **Keyword Versioning**: Handle keyword rule changes over time
5. **Dynamic Loading**: Load keyword definitions from external files
6. **Analytics**: Track which keywords are most common
7. **Optimization**: Profile and optimize hot paths

## Quick Start: Using the Keyword Analyzer

A `KeywordAnalyzer` tool has been created to help categorize all keywords:

```csharp
var analyzer = new KeywordAnalyzer();
analyzer.LoadCardNames(dbContext); // Load card names to filter them out
var analyses = analyzer.AnalyzeKeywordsFromCsv("keyword_any.csv");
var report = analyzer.GenerateReport(analyses);

// Report shows:
// - How many are official keywords
// - How many are card names
// - How many are parameterized
// - How many are custom/unknown
```

This tool can be integrated into the Console app as a command to analyze keywords.

## References

- **Magic Rules**: Rule 702 (Keyword Abilities) - 189 official keywords defined
- **Database**: `keyword_any.csv` (728 entries)
- **Current Implementation**: `KeywordRegistry.cs` (50 keywords)
- **Parser**: `KeywordParser.cs`, `KeywordAbilityParser.cs`
- **Analyzer Tool**: `Majik.Tools/KeywordAnalyzer.cs`
