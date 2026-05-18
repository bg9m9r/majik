using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Majik.Core.CardData.Database;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;

namespace Majik.Core.CardData.Parsing;

/// <summary>
/// Uses Claude API to analyze unknown keywords and provide categorization and implementation guidance.
/// </summary>
public class ClaudeKeywordAnalyzer : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _apiUrl = "https://api.anthropic.com/v1/messages";
    private readonly string _batchApiUrl = "https://api.anthropic.com/v1/messages/batches";
    private readonly string _model;
    private readonly CardDbContext? _dbContext;

    public ClaudeKeywordAnalyzer(string? apiKey = null, string? model = null, CardDbContext? dbContext = null)
    {
        // Load .env file if it exists (silently fails if not found)
        // This allows users to store API keys in a local .env file
        // Search up the directory tree to find .env file
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (currentDir != null)
        {
            var envPath = Path.Combine(currentDir.FullName, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                break;
            }
            currentDir = currentDir.Parent;
        }
        
        _httpClient = new HttpClient();
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") 
            ?? throw new InvalidOperationException(
                "ANTHROPIC_API_KEY is not set. " +
                "Set it via: (1) .env file: ANTHROPIC_API_KEY=your-key, or " +
                "(2) environment variable: export ANTHROPIC_API_KEY='your-key'");
        
        // Use latest Claude model by default, or allow override via environment variable or parameter
        // Available models for batch API (check https://docs.anthropic.com/en/api/models):
        //   - claude-sonnet-4-5-20250929 (Claude Sonnet 4.5 - latest, best for real-world agents)
        //   - claude-sonnet-4-20250514 (Claude Sonnet 4 - high performance)
        //   - claude-3-5-sonnet-20241022 (Claude 3.5 Sonnet - previous best)
        //   - claude-3-opus-20240229 (Claude 3 Opus - most capable but slower)
        //   - claude-3-5-haiku-20241022 (Claude 3.5 Haiku - fastest/cheapest)
        // Using Claude Sonnet 4.5 by default for best analysis quality
        _model = model 
            ?? Environment.GetEnvironmentVariable("CLAUDE_MODEL") 
            ?? "claude-sonnet-4-5-20250929";
        
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        // Note: content-type is set on HttpContent, not DefaultRequestHeaders
        
        _dbContext = dbContext;
    }
    
    /// <summary>
    /// Compute SHA256 hash of a string (for cache key).
    /// </summary>
    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    
    /// <summary>
    /// Get cached response for a request, if available.
    /// </summary>
    private async Task<ClaudeImplementationNotes?> GetCachedResponseAsync(
        string keyword,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext == null)
            return null;
        
        var requestHash = ComputeHash(prompt);
        var cached = await _dbContext.ClaudeRequestCache
            .FirstOrDefaultAsync(c => c.RequestHash == requestHash, cancellationToken);
        
        if (cached != null)
        {
            // Update last accessed time
            cached.LastAccessedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            // Parse and return cached response
            if (!string.IsNullOrEmpty(cached.ParsedNotes))
            {
                try
                {
                    var notes = JsonSerializer.Deserialize<ClaudeImplementationNotes>(cached.ParsedNotes);
                    if (notes != null)
                    {
                        notes.Keyword = keyword;
                        notes.RawResponse = cached.ResponseText;
                        return notes;
                    }
                }
                catch
                {
                    // Fall through to parse from raw response
                }
            }
            
            // Parse from raw response if parsed notes not available
            return ParseImplementationResponse(keyword, cached.ResponseText);
        }
        
        return null;
    }
    
    /// <summary>
    /// Store request/response in cache.
    /// </summary>
    public async Task StoreInCacheAsync(
        string keyword,
        string prompt,
        string responseText,
        ClaudeImplementationNotes? parsedNotes,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext == null)
        {
            // No database context - can't cache (this is expected if dbContext wasn't provided)
            return;
        }
        
        try
        {
            var requestHash = ComputeHash(prompt);
            var now = DateTime.UtcNow;
            
            var cached = await _dbContext.ClaudeRequestCache
                .FirstOrDefaultAsync(c => c.RequestHash == requestHash, cancellationToken);
            
            if (cached == null)
            {
                cached = new ClaudeRequestCacheEntity
                {
                    RequestHash = requestHash,
                    Keyword = keyword,
                    RequestPrompt = prompt,
                    ResponseText = responseText,
                    ParsedNotes = parsedNotes != null ? JsonSerializer.Serialize(parsedNotes) : null,
                    Model = _model,
                    RequestedAt = now,
                    LastAccessedAt = now
                };
                _dbContext.ClaudeRequestCache.Add(cached);
            }
            else
            {
                // Update existing cache entry
                cached.ResponseText = responseText;
                cached.ParsedNotes = parsedNotes != null ? JsonSerializer.Serialize(parsedNotes) : null;
                cached.LastAccessedAt = now;
            }
            
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't throw - caching failure shouldn't break the main flow
            // In production, you might want to log this properly
            throw new InvalidOperationException($"Failed to store cache entry for keyword '{keyword}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Get implementation notes for multiple keywords using Claude's Message Batches API.
    /// This is more efficient and cost-effective (50% discount) than individual requests.
    /// </summary>
    public async Task<Dictionary<string, ClaudeImplementationNotes>> GetImplementationNotesBatchAsync(
        List<(string Keyword, string? MagicRule, string? Description)> keywords,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ClaudeImplementationNotes>();
        var total = keywords.Count;
        var startTime = DateTime.Now;
        string? batchId = null; // Track batch ID for error messages
        
        // Check cache for existing responses
        var keywordsToRequest = new List<(string Keyword, string? MagicRule, string? Description, string Prompt)>();
        var cachedCount = 0;
        
        if (_dbContext != null)
        {
            progressCallback?.Invoke($"Checking cache for {total} keywords...\n");
            foreach (var keywordInfo in keywords)
            {
                var prompt = BuildImplementationPrompt(keywordInfo.Keyword, keywordInfo.MagicRule, keywordInfo.Description);
                var cached = await GetCachedResponseAsync(keywordInfo.Keyword, prompt, cancellationToken);
                
                if (cached != null)
                {
                    results[keywordInfo.Keyword] = cached;
                    cachedCount++;
                }
                else
                {
                    keywordsToRequest.Add((keywordInfo.Keyword, keywordInfo.MagicRule, keywordInfo.Description, prompt));
                }
            }
            
            if (cachedCount > 0)
            {
                progressCallback?.Invoke($"✓ Found {cachedCount} cached responses, {keywordsToRequest.Count} need API calls\n");
            }
        }
        else
        {
            // No cache available, request all
            foreach (var keywordInfo in keywords)
            {
                var prompt = BuildImplementationPrompt(keywordInfo.Keyword, keywordInfo.MagicRule, keywordInfo.Description);
                keywordsToRequest.Add((keywordInfo.Keyword, keywordInfo.MagicRule, keywordInfo.Description, prompt));
            }
        }
        
        // If all keywords are cached, return early
        if (keywordsToRequest.Count == 0)
        {
            progressCallback?.Invoke($"✓ All {total} keywords found in cache!\n");
            return results;
        }
        
        progressCallback?.Invoke($"Creating batch request for {keywordsToRequest.Count} keywords using Claude Message Batches API (50% discount)...\n");
        
        // Build batch request - manually construct to ensure snake_case property names
        // The API expects: custom_id, max_tokens, etc. (snake_case)
        // custom_id must match pattern: ^[a-zA-Z0-9_-]{1,64}$
        var batchRequestsJson = new List<Dictionary<string, object>>();
        foreach (var (keywordInfo, index) in keywordsToRequest.Select((k, i) => (k, i)))
        {
            // Sanitize keyword for custom_id: only alphanumeric, underscore, hyphen; max 64 chars
            var sanitizedKeyword = SanitizeCustomId(keywordInfo.Keyword);
            var customId = $"kw{index}-{sanitizedKeyword}";
            
            // Ensure total length <= 64 (pattern requirement)
            if (customId.Length > 64)
            {
                // Truncate keyword part if needed, keep index prefix
                var maxKeywordLength = 64 - $"kw{index}-".Length;
                if (maxKeywordLength > 0)
                {
                    sanitizedKeyword = sanitizedKeyword.Substring(0, Math.Min(sanitizedKeyword.Length, maxKeywordLength));
                    customId = $"kw{index}-{sanitizedKeyword}";
                }
                else
                {
                    // Fallback: just use index if keyword is too long
                    customId = $"kw{index}";
                }
            }
            
            var request = new Dictionary<string, object>
            {
                ["custom_id"] = customId,
                ["params"] = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["max_tokens"] = 1024,
                    ["messages"] = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["role"] = "user",
                            ["content"] = keywordInfo.Prompt
                        }
                    }
                }
            };
            batchRequestsJson.Add(request);
        }
        
        var batchRequestBodyJson = new Dictionary<string, object>
        {
            ["requests"] = batchRequestsJson
        };
        
        // Submit batch
        progressCallback?.Invoke($"Submitting batch of {total} requests...\n");
        
        var jsonOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        
        var jsonContent = JsonSerializer.Serialize(batchRequestBodyJson, jsonOptions);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        
        var batchResponse = await _httpClient.PostAsync(_batchApiUrl, httpContent, cancellationToken);
        
        if (!batchResponse.IsSuccessStatusCode)
        {
            var errorContent = await batchResponse.Content.ReadAsStringAsync(cancellationToken);
            progressCallback?.Invoke($"✗ Batch creation failed with status {batchResponse.StatusCode}\n");
            progressCallback?.Invoke($"Error response: {errorContent}\n");
            
            // Try to parse error for more details
            try
            {
                var errorJson = JsonDocument.Parse(errorContent);
                if (errorJson.RootElement.TryGetProperty("error", out var errorObj))
                {
                    if (errorObj.TryGetProperty("message", out var message))
                    {
                        progressCallback?.Invoke($"Error message: {message.GetString()}\n");
                    }
                }
            }
            catch
            {
                // Ignore parse errors, just show raw content
            }
            
            throw new InvalidOperationException($"Failed to create batch: {batchResponse.StatusCode} - {errorContent}");
        }
        
        var batchResult = await batchResponse.Content.ReadFromJsonAsync<BatchResponse>(cancellationToken: cancellationToken);
        if (batchResult == null || string.IsNullOrEmpty(batchResult.Id))
        {
            throw new InvalidOperationException("Failed to create batch - no batch ID returned");
        }
        
        batchId = batchResult.Id;
        progressCallback?.Invoke($"✓ Batch created: {batchId}\n");
        progressCallback?.Invoke($"Batch processing started. This may take a few minutes (up to 24 hours, but usually much faster)...\n");
        progressCallback?.Invoke($"Polling batch status every 10 seconds...\n");
        
        // Poll for batch completion
        // Batches can take up to 24 hours, but usually complete in minutes
        var maxWaitTime = TimeSpan.FromHours(2); // Wait up to 2 hours (user can check manually after)
        var pollInterval = TimeSpan.FromSeconds(10); // Poll every 10 seconds (not too aggressive)
        var elapsed = TimeSpan.Zero;
        var lastStatus = "";
        var requestCounts = "";
        
        while (elapsed < maxWaitTime)
        {
            await Task.Delay(pollInterval, cancellationToken);
            elapsed = elapsed.Add(pollInterval);
            
            try
            {
                var statusResponse = await _httpClient.GetAsync($"{_batchApiUrl}/{batchId}", cancellationToken);
                statusResponse.EnsureSuccessStatusCode();
                
                var statusJson = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                var statusDoc = JsonDocument.Parse(statusJson);
                
                // Extract processing_status
                string? processingStatus = null;
                if (statusDoc.RootElement.TryGetProperty("processing_status", out var statusElement))
                {
                    processingStatus = statusElement.GetString();
                }
                
                // Extract request counts for progress info
                if (statusDoc.RootElement.TryGetProperty("request_counts", out var countsElement))
                {
                    var processing = countsElement.TryGetProperty("processing", out var p) ? p.GetInt32() : 0;
                    var succeeded = countsElement.TryGetProperty("succeeded", out var s) ? s.GetInt32() : 0;
                    var errored = countsElement.TryGetProperty("errored", out var e) ? e.GetInt32() : 0;
                    requestCounts = $" (processing: {processing}, succeeded: {succeeded}, errored: {errored})";
                }
                
                // Update status display
                if (processingStatus != lastStatus)
                {
                    progressCallback?.Invoke($"\nStatus: {processingStatus}{requestCounts}\n");
                    lastStatus = processingStatus ?? "";
                }
                else
                {
                    progressCallback?.Invoke($"\rPolling... ({elapsed.TotalMinutes:F1} min elapsed){requestCounts}");
                }
                
                // Check if batch is complete
                if (processingStatus == "ended")
                {
                    progressCallback?.Invoke($"\n✓ Batch completed! Retrieving results...\n");
                    break;
                }
                
                if (processingStatus == "canceling" || processingStatus == "failed")
                {
                    var errorMsg = "Unknown error";
                    if (statusDoc.RootElement.TryGetProperty("error", out var errorElement))
                    {
                        if (errorElement.TryGetProperty("message", out var msg))
                        {
                            errorMsg = msg.GetString() ?? errorMsg;
                        }
                    }
                    throw new InvalidOperationException($"Batch {processingStatus}: {errorMsg}");
                }
                
                // Also check ended_at as fallback
                if (statusDoc.RootElement.TryGetProperty("ended_at", out var endedAt) && 
                    !string.IsNullOrEmpty(endedAt.GetString()))
                {
                    progressCallback?.Invoke($"\n✓ Batch completed (ended_at present)! Retrieving results...\n");
                    break;
                }
            }
            catch (HttpRequestException ex)
            {
                progressCallback?.Invoke($"\n⚠ Error polling batch status: {ex.Message}\n");
                progressCallback?.Invoke($"Will retry in {pollInterval.TotalSeconds} seconds...\n");
                // Continue polling - might be temporary network issue
            }
            catch (JsonException ex)
            {
                progressCallback?.Invoke($"\n⚠ Error parsing batch status: {ex.Message}\n");
                // Continue polling - might be temporary API issue
            }
        }
        
        if (elapsed >= maxWaitTime)
        {
            progressCallback?.Invoke($"\n⚠ Batch is still processing after {maxWaitTime.TotalHours:F1} hours.\n");
            progressCallback?.Invoke($"Batch ID: {batchId}\n");
            progressCallback?.Invoke($"You can check status later with:\n");
            progressCallback?.Invoke($"  curl {_batchApiUrl}/{batchId} -H 'x-api-key: $ANTHROPIC_API_KEY'\n");
            progressCallback?.Invoke($"\nThe batch will continue processing (up to 24 hours total).\n");
            progressCallback?.Invoke($"You can retrieve results later when it completes.\n");
            progressCallback?.Invoke($"For now, returning empty results.\n");
            return results;
        }
        
        // Retrieve batch results
        // First, get the batch status again to get the results_url
        progressCallback?.Invoke($"Fetching batch status to get results URL...\n");
        var statusResponse2 = await _httpClient.GetAsync($"{_batchApiUrl}/{batchId}", cancellationToken);
        statusResponse2.EnsureSuccessStatusCode();
        var statusJson2 = await statusResponse2.Content.ReadAsStringAsync(cancellationToken);
        var statusDoc2 = JsonDocument.Parse(statusJson2);
        
        string? resultsUrl = null;
        if (statusDoc2.RootElement.TryGetProperty("results_url", out var resultsUrlElement))
        {
            resultsUrl = resultsUrlElement.GetString();
        }
        
        if (string.IsNullOrEmpty(resultsUrl))
        {
            // Fallback: try direct endpoint (may not work, but worth trying)
            progressCallback?.Invoke($"No results_url found, trying direct endpoint...\n");
            resultsUrl = $"{_batchApiUrl}/{batchId}/results";
        }
        
        progressCallback?.Invoke($"Retrieving results from: {resultsUrl}\n");
        var resultsResponse = await _httpClient.GetAsync(resultsUrl, cancellationToken);
        
        if (!resultsResponse.IsSuccessStatusCode)
        {
            var errorContent = await resultsResponse.Content.ReadAsStringAsync(cancellationToken);
            progressCallback?.Invoke($"✗ Failed to retrieve results: {resultsResponse.StatusCode}\n");
            progressCallback?.Invoke($"Error: {errorContent}\n");
            throw new InvalidOperationException($"Failed to retrieve batch results: {resultsResponse.StatusCode} - {errorContent}");
        }
        
        // Results are in JSONL format (one JSON object per line)
        var resultsText = await resultsResponse.Content.ReadAsStringAsync(cancellationToken);
        var resultLines = resultsText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        progressCallback?.Invoke($"Processing {resultLines.Length} results from JSONL file...\n");
        progressCallback?.Invoke($"Database context available: {(_dbContext != null ? "Yes" : "No")}\n");
        
        // Parse JSONL results (one JSON object per line)
        int processedResults = 0;
        int errorResults = 0;
        
        foreach (var line in resultLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            try
            {
                var result = JsonSerializer.Deserialize<BatchResult>(line, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (result == null)
                {
                    errorResults++;
                    continue;
                }
                
                // Check for errors in the result
                if (result.Error != null)
                {
                    progressCallback?.Invoke($"  ⚠ Result error for custom_id '{result.CustomId}': {result.Error.Message}\n");
                    errorResults++;
                    continue;
                }
                
                if (result.Output?.Content == null || result.Output.Content.Length == 0)
                {
                    errorResults++;
                    continue;
                }
                
                // Extract keyword from custom_id (format: "kw{index}-{sanitized_keyword}")
                // We need to map back to the original keyword since custom_id is sanitized
                var customId = result.CustomId ?? "";
                var keywordMatch = System.Text.RegularExpressions.Regex.Match(customId, @"^kw(\d+)-(.+)$");
                
                string keyword;
                if (keywordMatch.Success)
                {
                    var indexStr = keywordMatch.Groups[1].Value;
                    if (int.TryParse(indexStr, out var index) && index >= 0 && index < keywordsToRequest.Count)
                    {
                        // Use the original keyword from our list (index matches)
                        keyword = keywordsToRequest[index].Keyword;
                    }
                    else
                    {
                        // Fallback: try to use sanitized version (not ideal but better than nothing)
                        keyword = keywordMatch.Groups[2].Value;
                        progressCallback?.Invoke($"  ⚠ Could not map custom_id '{customId}' to original keyword, using sanitized version\n");
                    }
                }
                else
                {
                    // Try old format as fallback
                    var oldMatch = System.Text.RegularExpressions.Regex.Match(customId, @"keyword-(\d+)-(.+)");
                    if (oldMatch.Success && int.TryParse(oldMatch.Groups[1].Value, out var oldIndex) && oldIndex >= 0 && oldIndex < keywordsToRequest.Count)
                    {
                        keyword = keywordsToRequest[oldIndex].Keyword;
                    }
                    else
                    {
                        progressCallback?.Invoke($"  ⚠ Could not extract keyword from custom_id: {customId}\n");
                        errorResults++;
                        continue;
                    }
                }
                var textContent = result.Output.Content[0].Text;
                
                if (string.IsNullOrEmpty(textContent))
                {
                    errorResults++;
                    continue;
                }
                
                var notes = ParseImplementationResponse(keyword, textContent);
                if (notes != null)
                {
                    results[keyword] = notes;
                    processedResults++;
                    
                    // Store in cache - find the matching keyword info by index or keyword name
                    var keywordInfo = keywordsToRequest.FirstOrDefault(k => k.Keyword == keyword);
                    if (!string.IsNullOrEmpty(keywordInfo.Keyword) && keywordInfo.Keyword == keyword)
                    {
                    try
                    {
                        await StoreInCacheAsync(keyword, keywordInfo.Prompt, textContent, notes, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        progressCallback?.Invoke($"  ⚠ Failed to cache result for '{keyword}': {ex.Message}\n");
                    }
                }
                else
                {
                    progressCallback?.Invoke($"  ⚠ Could not find prompt for keyword '{keyword}' in keywordsToRequest list - skipping cache\n");
                }
                
                if (processedResults % 10 == 0)
                {
                    progressCallback?.Invoke($"  Processed {processedResults}/{resultLines.Length} results...\r");
                }
                }
                else
                {
                    errorResults++;
                    progressCallback?.Invoke($"  ⚠ Failed to parse response for keyword '{keyword}'\n");
                }
            }
            catch (JsonException ex)
            {
                // Skip invalid JSON lines
                errorResults++;
                progressCallback?.Invoke($"  ⚠ JSON parse error: {ex.Message}\n");
                continue;
            }
        }
        
        if (processedResults > 0)
        {
            progressCallback?.Invoke($"  ✓ Processed {processedResults} results successfully");
            if (errorResults > 0)
            {
                progressCallback?.Invoke($", {errorResults} errors");
            }
            progressCallback?.Invoke($"\n");
        }
        
        // Verify cache storage
        if (_dbContext != null && processedResults > 0)
        {
            var cacheCount = await _dbContext.ClaudeRequestCache.CountAsync(cancellationToken);
            progressCallback?.Invoke($"  Cache status: {cacheCount} total entries in database\n");
        }
        
        var totalDuration = DateTime.Now - startTime;
        progressCallback?.Invoke($"\n✓ Retrieved implementation notes for {results.Count}/{total} keywords in {totalDuration.TotalMinutes:F1} minutes\n");
        
        if (results.Count == 0 && keywordsToRequest.Count > 0)
        {
            progressCallback?.Invoke($"\n⚠ WARNING: No results retrieved! This could mean:\n");
            progressCallback?.Invoke($"  - Batch is still processing (check batch ID: {batchId})\n");
            progressCallback?.Invoke($"  - Batch timed out (max wait: {maxWaitTime.TotalHours:F1} hours)\n");
            progressCallback?.Invoke($"  - Results retrieval failed\n");
        }
        
        return results;
    }

    /// <summary>
    /// Get implementation notes for an official keyword.
    /// </summary>
    public async Task<ClaudeImplementationNotes?> GetImplementationNotesAsync(
        string keyword,
        string? magicRule = null,
        string? description = null,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        progressCallback?.Invoke($"Getting implementation notes for '{keyword}'...");
        
        var prompt = BuildImplementationPrompt(keyword, magicRule, description);
        var startTime = DateTime.Now;
        
        // Check cache first
        var cached = await GetCachedResponseAsync(keyword, prompt, cancellationToken);
        if (cached != null)
        {
            progressCallback?.Invoke($"  ✓ Found cached response for '{keyword}'\n");
            return cached;
        }
        
        try
        {
            var requestBody = new
            {
                model = _model,
                max_tokens = 1024, // Reduced from 2048 - sufficient for structured implementation notes
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            progressCallback?.Invoke($"  → Requesting implementation guidance from Claude...");
            var response = await _httpClient.PostAsJsonAsync(_apiUrl, requestBody, cancellationToken);
            response.EnsureSuccessStatusCode();

            progressCallback?.Invoke($"  → Receiving response...");
            var responseContent = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: cancellationToken);
            
            if (responseContent?.Content == null || responseContent.Content.Length == 0)
            {
                progressCallback?.Invoke($"  ✗ No response content received\n");
                return null;
            }

            var textContent = responseContent.Content[0].Text;
            if (string.IsNullOrEmpty(textContent))
            {
                progressCallback?.Invoke($"  ✗ Empty response content\n");
                return null;
            }
            
            progressCallback?.Invoke($"  → Parsing response...");
            var notes = ParseImplementationResponse(keyword, textContent);
            var duration = DateTime.Now - startTime;
            
            if (notes != null)
            {
                // Store in cache
                await StoreInCacheAsync(keyword, prompt, textContent, notes, cancellationToken);
                progressCallback?.Invoke($"  ✓ Got implementation notes for '{keyword}' [{duration.TotalSeconds:F1}s]\n");
            }
            else
            {
                progressCallback?.Invoke($"  ✗ Failed to parse response for '{keyword}'\n");
            }
            
            return notes;
        }
        catch (Exception ex)
        {
            var duration = DateTime.Now - startTime;
            progressCallback?.Invoke($"  ✗ Error getting notes for '{keyword}': {ex.Message} [{duration.TotalSeconds:F1}s]\n");
            throw new InvalidOperationException($"Failed to get implementation notes for keyword '{keyword}' with Claude API: {ex.Message}", ex);
        }
    }

    public string BuildImplementationPrompt(string keyword, string? magicRule, string? description)
    {
        var ruleInfo = magicRule != null ? $"\nMagic Rule: {magicRule}" : "";
        var descInfo = description != null ? $"\nDescription: {description}" : "";
        
        return $@"You are an expert Magic: The Gathering game engine developer. Your task is to provide detailed, technical implementation guidance for Magic keywords.

Keyword: ""{keyword}""{ruleInfo}{descInfo}

Provide a JSON response with detailed implementation notes:
{{
  ""abilityType"": ""Static|Triggered|Activated|Replacement|Action"",
  ""layer"": null or 1-7 (for static abilities - see Rule 613),
  ""sublayer"": null or sublayer letter (e.g., ""a"", ""b"", ""c"", ""d"" for Layer 7),
  ""implementationNotes"": ""Concise but complete technical implementation guide (2-3 paragraphs max):
    - Exact game events/state changes needed
    - Timing and priority considerations
    - Key edge cases
    - Suggested code structure"",
  ""codeExample"": ""Brief C# code example (10-20 lines max) showing core implementation"",
  ""relatedKeywords"": [""2-3 related keywords that share implementation patterns""],
  ""complexity"": ""Simple|Medium|Complex"",
  ""testingNotes"": ""2-3 key test cases to verify correct implementation"",
  ""commonMistakes"": [""1-2 most common implementation mistakes to avoid""]
}}

Focus Areas:
1. **Static Abilities**: Layer (Rule 613), timestamp ordering, state changes.
2. **Triggered Abilities**: Trigger condition, stack timing, intervening-if clauses.
3. **Activated Abilities**: Cost payment, timing restrictions, target selection.
4. **State Management**: What game state to track/modify.
5. **Edge Cases**: Key unusual interactions or special rules.

Be concise but complete. Prioritize actionable guidance over exhaustive detail.";
    }

    public ClaudeImplementationNotes? ParseImplementationResponse(string keyword, string responseText)
    {
        try
        {
            // Try to extract JSON from the response (Claude might wrap it in markdown)
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}') + 1;
            
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                // No JSON found, return basic notes
                return new ClaudeImplementationNotes
                {
                    Keyword = keyword,
                    ImplementationNotes = responseText,
                    RawResponse = responseText
                };
            }
            
            var jsonText = responseText.Substring(jsonStart, jsonEnd - jsonStart);
            var notes = JsonSerializer.Deserialize<ClaudeImplementationNotes>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (notes != null)
            {
                notes.Keyword = keyword;
                notes.RawResponse = responseText;
                
                // Map string ability type to enum
                if (!string.IsNullOrEmpty(notes.AbilityTypeString))
                {
                    notes.AbilityType = MapAbilityType(notes.AbilityTypeString);
                }
            }
            
            return notes;
        }
        catch (Exception ex)
        {
            // Return basic notes on parse error
            return new ClaudeImplementationNotes
            {
                Keyword = keyword,
                ImplementationNotes = $"Failed to parse structured response: {ex.Message}\n\nRaw response:\n{responseText}",
                RawResponse = responseText
            };
        }
    }


    private AbilityType? MapAbilityType(string abilityType)
    {
        return abilityType.ToLowerInvariant() switch
        {
            "static" => AbilityType.Static,
            "triggered" => AbilityType.Triggered,
            "activated" => AbilityType.Activated,
            "replacement" => AbilityType.Replacement,
            _ => null
        };
    }

    /// <summary>
    /// Sanitize a string to be used as custom_id (must match ^[a-zA-Z0-9_-]{1,64}$).
    /// </summary>
    private static string SanitizeCustomId(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "empty";
        
        // Replace invalid characters with underscore, keep only alphanumeric, underscore, hyphen
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            input,
            @"[^a-zA-Z0-9_-]",
            "_");
        
        // Remove consecutive underscores
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"_+", "_");
        
        // Remove leading/trailing underscores
        sanitized = sanitized.Trim('_');
        
        // Ensure it's not empty after sanitization
        if (string.IsNullOrEmpty(sanitized))
            return "keyword";
        
        return sanitized;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Batch API response models.
/// </summary>
internal class BatchResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal class BatchStatus
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("processing_status")]
    public string? ProcessingStatus { get; set; }
    
    [JsonPropertyName("processed_at")]
    public DateTime? ProcessedAt { get; set; }
    
    [JsonPropertyName("failed_at")]
    public DateTime? FailedAt { get; set; }
    
    [JsonPropertyName("error")]
    public BatchError? Error { get; set; }
    
    [JsonPropertyName("results_url")]
    public string? ResultsUrl { get; set; }
}

internal class BatchError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal class BatchResults
{
    [JsonPropertyName("results")]
    public List<BatchResult>? Results { get; set; }
}

internal class BatchResult
{
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
    
    [JsonPropertyName("output")]
    public BatchOutput? Output { get; set; }
    
    [JsonPropertyName("error")]
    public BatchError? Error { get; set; }
}

internal class BatchOutput
{
    [JsonPropertyName("content")]
    public BatchContent[]? Content { get; set; }
}

internal class BatchContent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Implementation notes from Claude API for official keywords.
/// </summary>
public class ClaudeImplementationNotes
{
    [JsonIgnore]
    public string Keyword { get; set; } = "";
    
    [JsonPropertyName("abilityType")]
    public string? AbilityTypeString { get; set; }
    
    [JsonIgnore]
    public AbilityType? AbilityType { get; set; }
    
    [JsonPropertyName("layer")]
    public int? Layer { get; set; }
    
    [JsonPropertyName("sublayer")]
    public string? Sublayer { get; set; }
    
    [JsonPropertyName("implementationNotes")]
    public string? ImplementationNotes { get; set; }
    
    [JsonPropertyName("codeExample")]
    public string? CodeExample { get; set; }
    
    [JsonPropertyName("relatedKeywords")]
    public List<string>? RelatedKeywords { get; set; }
    
    [JsonPropertyName("complexity")]
    public string? Complexity { get; set; }
    
    [JsonPropertyName("testingNotes")]
    public string? TestingNotes { get; set; }
    
    [JsonPropertyName("commonMistakes")]
    public List<string>? CommonMistakes { get; set; }
    
    [JsonIgnore]
    public string? RawResponse { get; set; }
}

/// <summary>
/// Analysis result from Claude API.
/// </summary>
public class ClaudeKeywordAnalysis
{
    [JsonIgnore]
    public string Keyword { get; set; } = "";
    
    [JsonPropertyName("category")]
    public string? CategoryString { get; set; }
    
    [JsonIgnore]
    public Database.KeywordCategory Category { get; set; }
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    
    [JsonPropertyName("baseKeyword")]
    public string? BaseKeyword { get; set; }
    
    [JsonPropertyName("parameters")]
    public Dictionary<string, string>? Parameters { get; set; }
    
    [JsonPropertyName("abilityType")]
    public string? AbilityTypeString { get; set; }
    
    [JsonIgnore]
    public AbilityType? AbilityType { get; set; }
    
    [JsonPropertyName("layer")]
    public int? Layer { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("magicRule")]
    public string? MagicRule { get; set; }
    
    [JsonPropertyName("isOfficial")]
    public bool? IsOfficial { get; set; }
    
    [JsonPropertyName("implementationNotes")]
    public string? ImplementationNotes { get; set; }
    
    [JsonPropertyName("examples")]
    public List<string>? Examples { get; set; }
    
    [JsonPropertyName("relatedKeywords")]
    public List<string>? RelatedKeywords { get; set; }
    
    [JsonPropertyName("complexity")]
    public string? Complexity { get; set; }
    
    [JsonPropertyName("requiresOracleText")]
    public bool? RequiresOracleText { get; set; }
    
    [JsonPropertyName("sublayer")]
    public string? Sublayer { get; set; }
    
    [JsonIgnore]
    public string? RawResponse { get; set; }
}

/// <summary>
/// Claude API response structure.
/// </summary>
internal class ClaudeResponse
{
    [JsonPropertyName("content")]
    public ClaudeContent[]? Content { get; set; }
}

internal class ClaudeContent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
