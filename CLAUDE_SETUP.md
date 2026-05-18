# Claude API Setup for Keyword Analysis

This guide explains how to set up Claude API integration to help analyze unknown keywords.

## Prerequisites

1. **Claude Pro Subscription**: You need an active Anthropic API subscription
2. **API Key**: Get your API key from [Anthropic Console](https://console.anthropic.com/)

## Setup Steps

### 1. Get Your API Key

1. Go to https://console.anthropic.com/
2. Sign in with your account
3. Navigate to API Keys section
4. Create a new API key or copy an existing one

### 2. Set API Key

You can set your API key in one of two ways:

#### Option A: Using a .env file (Recommended for local development)

1. Copy the example file:
   ```bash
   cp .env.example .env
   ```

2. Edit `.env` and add your API key:
   ```bash
   ANTHROPIC_API_KEY=sk-ant-api03-...
   ```

The `.env` file is gitignored and won't be committed to the repository.

#### Option B: Using Environment Variables

**Linux/macOS:**
```bash
export ANTHROPIC_API_KEY='sk-ant-api03-...'
```

**Make it persistent (add to ~/.bashrc or ~/.zshrc):**
```bash
echo 'export ANTHROPIC_API_KEY="sk-ant-api03-..."' >> ~/.bashrc
source ~/.bashrc
```

**Windows (PowerShell):**
```powershell
$env:ANTHROPIC_API_KEY = "sk-ant-api03-..."
```

**Windows (Command Prompt):**
```cmd
set ANTHROPIC_API_KEY=sk-ant-api03-...
```

### 3. Verify Setup

If using environment variables, test that it's set:
```bash
echo $ANTHROPIC_API_KEY
```

If using a `.env` file, just make sure the file exists in the project root directory.

## Usage

### Basic Analysis (without Claude)
```bash
dotnet run --project Majik.Console analyze-keywords keyword_any.csv
```

### Analysis with Claude API
```bash
dotnet run --project Majik.Console analyze-keywords keyword_any.csv --use-claude
```

## How It Works

1. **Standard Analysis First**: The tool first tries to categorize keywords using pattern matching and heuristics
2. **Claude for Unknowns**: Keywords with low confidence (< 0.7) or marked as "Unknown" are sent to Claude API
3. **Structured Response**: Claude analyzes the keyword and returns:
   - Category (Official, Parameterized, Custom, CardName, Unknown)
   - Confidence level
   - Ability type (Static, Triggered, Activated, Replacement)
   - Layer (for static abilities)
   - Description and implementation notes
   - Magic rule reference (if official)

## Model Selection

The tool uses **Claude 3.5 Sonnet** by default (the latest and most capable model). You can override the model:

**Set a different model via environment variable:**
```bash
export CLAUDE_MODEL="claude-3-5-sonnet-20241022"  # Latest 3.5 Sonnet (default)
# or
export CLAUDE_MODEL="claude-3-opus-20240229"      # More powerful but slower/expensive
# or
export CLAUDE_MODEL="claude-3-5-haiku-20241022"  # Faster/cheaper but less capable
```

**Available Models:**
- `claude-sonnet-4-20250514` (default) - Latest and most capable, best for complex analysis
- `claude-3-5-sonnet-20241022` - Previous best, good balance of capability and cost
- `claude-3-opus-20240229` - Very capable, slower and more expensive
- `claude-3-5-haiku-20241022` - Fastest and cheapest, good for simple cases

**Note**: If you get an error about the model not being available, try `claude-3-5-sonnet-20241022` instead.

## Cost Considerations

- **Claude Sonnet 4** (default): Check current pricing at [Anthropic Pricing](https://www.anthropic.com/pricing)
- **Claude 3.5 Sonnet**: ~$3 per million input tokens, ~$15 per million output tokens
- **Claude 3 Opus**: ~$15 per million input tokens, ~$75 per million output tokens
- **Claude 3.5 Haiku**: ~$0.25 per million input tokens, ~$1.25 per million output tokens
- **Typical Request**: ~1000-2000 input tokens, ~500-1200 output tokens per keyword (with enhanced prompt)
- **Estimated Cost (Sonnet 4)**: ~$0.003-0.006 per unknown keyword analyzed
- **For 150 unknown keywords**: ~$0.45-0.90 total

The enhanced prompt uses more tokens but provides significantly better analysis quality.

## Rate Limiting

The tool includes a 1-second delay between Claude API requests to avoid rate limits. For large batches, this means:
- ~60 keywords per minute
- ~150 keywords in ~2.5 minutes

## Example Claude Response

When Claude analyzes a keyword, it returns structured JSON like:

```json
{
  "category": "Official",
  "confidence": 0.95,
  "reason": "This is an official Magic keyword from Rule 702.189",
  "abilityType": "Triggered",
  "layer": null,
  "description": "Firebending N means 'Whenever this creature attacks, add N {R}. Until end of combat, you don't lose this mana as steps and phases end.'",
  "magicRule": "702.189",
  "isOfficial": true,
  "implementationNotes": "Create a triggered ability that triggers on attack declaration, adds red mana to mana pool, and prevents mana burn until end of combat."
}
```

## Troubleshooting

### "ANTHROPIC_API_KEY environment variable is not set"
- Make sure you've exported the environment variable
- Check with `echo $ANTHROPIC_API_KEY`
- Restart your terminal if needed

### "Failed to analyze keyword with Claude API"
- Check your API key is valid
- Verify you have API credits/quota
- Check your internet connection
- Review the error message for specific issues

### Rate Limit Errors
- The tool includes automatic rate limiting (1 second between requests)
- If you still hit limits, you may need to increase the delay in `ClaudeKeywordAnalyzer.cs`

## Advanced Usage

### Analyze Specific Unknown Keywords

You can modify the code to analyze only specific keywords:

```csharp
var claudeAnalyzer = new ClaudeKeywordAnalyzer();
var analysis = await claudeAnalyzer.AnalyzeKeywordAsync("unknown-keyword");
Console.WriteLine($"Category: {analysis.Category}");
Console.WriteLine($"Description: {analysis.Description}");
```

### Batch Analysis

```csharp
var keywords = new List<string> { "keyword1", "keyword2", "keyword3" };
var analyses = await claudeAnalyzer.AnalyzeKeywordsBatchAsync(keywords);
```

## Next Steps

After analyzing keywords with Claude:

1. Review the results in the database (`KeywordMetadata` table)
2. Update implementation status for keywords that are now understood
3. Use Claude's `implementationNotes` to guide keyword implementation
4. Re-run analysis periodically as new keywords are discovered
