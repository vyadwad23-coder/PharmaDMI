using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace InsightService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InsightsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InsightsController> _logger;

    // Zero-install, zero-key public gateways that serve open-source LLMs.
    // Pollinations.ai is the primary - it works anonymously and exposes both
    // a chat-style POST endpoint and a simple GET endpoint as a backup path.
    private const string PollinationsChatUrl = "https://text.pollinations.ai/";
    private const string PollinationsGetBase = "https://text.pollinations.ai/";

    // Default open-source models served by the public gateway (Llama / Qwen / Mistral family).
    // Order matters: we try them in sequence so a single saturated model never blocks the user.
    private static readonly string[] PublicOssModels = new[] { "openai-fast", "openai", "llama", "mistral", "qwen-coder" };

    public InsightsController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<InsightsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------------
    // Endpoints
    // ---------------------------------------------------------------------

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] QueryRequest request)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var telemetrySummary = await FetchJson(client, "http://localhost:5001/api/telemetry/summary");
            var activeAlerts    = await FetchJson(client, "http://localhost:5002/api/alerts/active");
            var alertSummary    = await FetchJson(client, "http://localhost:5002/api/alerts/summary");

            var systemContext = BuildSystemContext(telemetrySummary, activeAlerts, alertSummary);
            var (answer, source, diagnostics) = await GenerateAiAnswerAsync(systemContext, request.Question);

            if (string.IsNullOrWhiteSpace(answer))
            {
                answer = GenerateRuleBasedInsight(request.Question, telemetrySummary, activeAlerts, alertSummary);
                source = "Local Rule Engine (all AI backends unreachable)";
            }

            return Ok(new { Answer = answer, Source = source, Diagnostics = diagnostics, Timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating insight");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var client = _httpClientFactory.CreateClient();
        var telemetrySummary = await FetchJson(client, "http://localhost:5001/api/telemetry/summary");
        var alertSummary     = await FetchJson(client, "http://localhost:5002/api/alerts/summary");
        var activeAlerts     = await FetchJson(client, "http://localhost:5002/api/alerts/active");

        var systemContext = BuildSystemContext(telemetrySummary, activeAlerts, alertSummary);
        var (answer, source, _) = await GenerateAiAnswerAsync(
            systemContext,
            "Give me a concise plant-wide health summary (3-5 sentences). Highlight critical alerts, machines that need attention, and overall stability.");

        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = GenerateRuleBasedInsight("overall plant health summary", telemetrySummary, activeAlerts, alertSummary);
            source = "Local Rule Engine (all AI backends unreachable)";
        }

        return Ok(new { Summary = answer, Source = source, Timestamp = DateTime.UtcNow });
    }

    [HttpGet("machine/{machineId}")]
    public async Task<IActionResult> GetMachineInsight(string machineId)
    {
        var client = _httpClientFactory.CreateClient();
        var latest = await FetchJson(client, $"http://localhost:5001/api/machines/{machineId}/latest");
        var alerts = await FetchJson(client, $"http://localhost:5002/api/alerts/{machineId}");

        var systemContext = $$"""
            You are an AI assistant for a pharmaceutical manufacturing plant.
            Focus only on machine {{machineId}}.

            LATEST TELEMETRY:
            {{latest}}

            RECENT ALERTS FOR THIS MACHINE:
            {{alerts}}

            Provide a short health narrative for this machine: state, deviations from
            normal, suspected root cause, and recommended next action. Be specific
            with the numeric values from telemetry. 2-3 short paragraphs max.
            """;

        var (answer, source, _) = await GenerateAiAnswerAsync(
            systemContext,
            $"Give me a current health narrative for machine {machineId}.");

        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = GenerateMachineInsight(machineId, latest, alerts);
            source = "Local Rule Engine (all AI backends unreachable)";
        }

        return Ok(new { MachineId = machineId, Insight = answer, Source = source, Timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Probes every AI backend so you can see which ones your network can actually reach.
    /// Hit <c>GET /api/insights/diagnostics</c> to debug "all AI backends unreachable" errors.
    /// </summary>
    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics()
    {
        var results = new List<object>();

        async Task Probe(string name, Func<Task<(bool ok, string detail)>> attempt)
        {
            var start = DateTime.UtcNow;
            try
            {
                var (ok, detail) = await attempt();
                results.Add(new
                {
                    Backend = name,
                    Reachable = ok,
                    Detail = detail,
                    ElapsedMs = (int)(DateTime.UtcNow - start).TotalMilliseconds
                });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    Backend = name,
                    Reachable = false,
                    Detail = ex.GetType().Name + ": " + ex.Message,
                    ElapsedMs = (int)(DateTime.UtcNow - start).TotalMilliseconds
                });
            }
        }

        await Probe("Pollinations (public OSS, no key)", async () =>
        {
            var r = await CallPollinationsPost("You are a tester.", "Reply with the single word OK.");
            return (!string.IsNullOrWhiteSpace(r), Truncate(r, 200));
        });

        var ollamaUrl = GetSetting("OLLAMA_URL");
        if (!string.IsNullOrEmpty(ollamaUrl))
        {
            await Probe($"Ollama ({ollamaUrl})", async () =>
            {
                var r = await CallOllama(ollamaUrl, GetSetting("OLLAMA_MODEL") ?? "llama3.2",
                    "You are a tester.", "Reply OK.");
                return (!string.IsNullOrWhiteSpace(r), Truncate(r, 200));
            });
        }

        var hfToken = GetSetting("HF_TOKEN") ?? GetSetting("HUGGINGFACE_API_KEY");
        if (!string.IsNullOrEmpty(hfToken))
        {
            await Probe("Hugging Face Inference API", async () =>
            {
                var r = await CallHuggingFace(hfToken,
                    GetSetting("HF_MODEL") ?? "meta-llama/Meta-Llama-3-8B-Instruct",
                    "You are a tester.", "Reply OK.");
                return (!string.IsNullOrWhiteSpace(r), Truncate(r, 200));
            });
        }

        var anthropicKey = GetSetting("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(anthropicKey))
        {
            await Probe("Anthropic Claude", async () =>
            {
                var r = await CallClaude(anthropicKey, "You are a tester.", "Reply OK.");
                return (!string.IsNullOrWhiteSpace(r), Truncate(r, 200));
            });
        }

        return Ok(new { Timestamp = DateTime.UtcNow, Backends = results });
    }

    // ---------------------------------------------------------------------
    // AI backend selection
    // ---------------------------------------------------------------------

    /// <summary>
    /// Tries multiple AI backends in priority order. The default flow uses
    /// only open-source models and requires zero installation and zero keys.
    /// Order:
    ///   1. Anthropic Claude        (if ANTHROPIC_API_KEY is set - opt-in)
    ///   2. Ollama (local OSS LLM)  (if OLLAMA_URL is set - opt-in)
    ///   3. Hugging Face Inference  (if HF_TOKEN is set - opt-in)
    ///   4. Public OSS gateway      (zero-config default, Pollinations -> Llama/Qwen/Mistral)
    /// </summary>
    private async Task<(string answer, string source, List<string> diagnostics)> GenerateAiAnswerAsync(string systemContext, string userQuestion)
    {
        var diagnostics = new List<string>();

        var anthropicKey = GetSetting("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(anthropicKey))
        {
            try
            {
                var r = await CallClaude(anthropicKey, systemContext, userQuestion);
                if (!string.IsNullOrWhiteSpace(r)) return (r, "Claude AI (Anthropic)", diagnostics);
                diagnostics.Add("Claude: empty response");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Claude: {ex.Message}");
                _logger.LogWarning(ex, "Claude backend failed");
            }
        }

        var ollamaUrl = GetSetting("OLLAMA_URL");
        if (!string.IsNullOrEmpty(ollamaUrl))
        {
            var ollamaModel = GetSetting("OLLAMA_MODEL") ?? "llama3.2";
            try
            {
                var r = await CallOllama(ollamaUrl, ollamaModel, systemContext, userQuestion);
                if (!string.IsNullOrWhiteSpace(r)) return (r, $"Open-Source AI · Ollama ({ollamaModel})", diagnostics);
                diagnostics.Add("Ollama: empty response");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Ollama: {ex.Message}");
                _logger.LogWarning(ex, "Ollama backend failed");
            }
        }

        var hfToken = GetSetting("HF_TOKEN") ?? GetSetting("HUGGINGFACE_API_KEY");
        if (!string.IsNullOrEmpty(hfToken))
        {
            var hfModel = GetSetting("HF_MODEL") ?? "meta-llama/Meta-Llama-3-8B-Instruct";
            try
            {
                var r = await CallHuggingFace(hfToken, hfModel, systemContext, userQuestion);
                if (!string.IsNullOrWhiteSpace(r)) return (r, $"Open-Source AI · Hugging Face ({hfModel})", diagnostics);
                diagnostics.Add("HuggingFace: empty response");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"HuggingFace: {ex.Message}");
                _logger.LogWarning(ex, "Hugging Face backend failed");
            }
        }

        // ---- Public open-source AI gateway: try POST, then GET, across multiple OSS models ----
        foreach (var model in PublicOssModels)
        {
            try
            {
                var r = await CallPollinationsPost(systemContext, userQuestion, model);
                if (!string.IsNullOrWhiteSpace(r) && !LooksLikeError(r))
                    return (r, $"Open-Source AI · Pollinations ({model}, no key)", diagnostics);
                if (!string.IsNullOrWhiteSpace(r)) diagnostics.Add($"Pollinations POST/{model}: {Truncate(r, 120)}");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Pollinations POST/{model}: {ex.Message}");
                _logger.LogInformation("Pollinations POST {Model} failed: {Message}", model, ex.Message);
            }
        }

        // GET fallback: combine system+user into a single prompt
        try
        {
            var combined = systemContext + "\n\nQUESTION: " + userQuestion + "\n\nANSWER:";
            var r = await CallPollinationsGet(combined, "openai-fast");
            if (!string.IsNullOrWhiteSpace(r) && !LooksLikeError(r))
                return (r, "Open-Source AI · Pollinations GET (no key)", diagnostics);
            if (!string.IsNullOrWhiteSpace(r)) diagnostics.Add($"Pollinations GET: {Truncate(r, 120)}");
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Pollinations GET: {ex.Message}");
            _logger.LogInformation("Pollinations GET fallback failed: {Message}", ex.Message);
        }

        return (string.Empty, string.Empty, diagnostics);
    }

    // ---------------------------------------------------------------------
    // Backend implementations
    // ---------------------------------------------------------------------

    private async Task<string> CallClaude(string apiKey, string systemPrompt, string userQuestion)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(45);
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = "claude-sonnet-4-20250514",
            max_tokens = 800,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userQuestion } }
        };

        var json = JsonSerializer.Serialize(payload);
        var response = await client.PostAsync(
            "https://api.anthropic.com/v1/messages",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var responseJson = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<JsonElement>(responseJson);
        return parsed.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private async Task<string> CallOllama(string baseUrl, string model, string systemPrompt, string userQuestion)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        var payload = new
        {
            model,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userQuestion }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var url = baseUrl.TrimEnd('/') + "/api/chat";
        var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<JsonElement>(responseJson);
        if (parsed.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content))
            return content.GetString() ?? string.Empty;
        return string.Empty;
    }

    private async Task<string> CallHuggingFace(string token, string model, string systemPrompt, string userQuestion)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var payload = new
        {
            model,
            max_tokens = 800,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userQuestion }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        var response = await client.PostAsync(
            "https://router.huggingface.co/v1/chat/completions",
            new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return ExtractOpenAiText(responseJson);
    }

    private async Task<string> CallPollinationsPost(string systemPrompt, string userQuestion, string model = "openai-fast")
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(45);

        var payload = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userQuestion }
            },
            model,
            @private = true,
            seed = Random.Shared.Next()
        };

        var json = JsonSerializer.Serialize(payload);
        var response = await client.PostAsync(
            PollinationsChatUrl,
            new StringContent(json, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();

        // Pollinations may return either plain text or an OpenAI-shaped JSON object.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");

        var openAiText = TryExtractOpenAiText(body);
        return string.IsNullOrWhiteSpace(openAiText) ? body.Trim() : openAiText;
    }

    private async Task<string> CallPollinationsGet(string prompt, string model)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(45);

        // Pollinations imposes practical URL length limits; trim the prompt if huge.
        var trimmed = prompt.Length > 6000 ? prompt.Substring(prompt.Length - 6000) : prompt;
        var url = PollinationsGetBase + Uri.EscapeDataString(trimmed) + "?model=" + Uri.EscapeDataString(model);

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        return body.Trim();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string ExtractOpenAiText(string responseJson)
    {
        var text = TryExtractOpenAiText(responseJson);
        return text ?? string.Empty;
    }

    private static string? TryExtractOpenAiText(string responseJson)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(responseJson);
            if (parsed.ValueKind != JsonValueKind.Object) return null;

            if (parsed.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content))
                    return content.GetString();

                if (first.TryGetProperty("text", out var text))
                    return text.GetString();
            }

            // Some gateways nest the response under "response" or "output".
            if (parsed.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.String)
                return resp.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return true;
        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith("{")) return false;
        try
        {
            var p = JsonSerializer.Deserialize<JsonElement>(body);
            return p.ValueKind == JsonValueKind.Object && p.TryGetProperty("error", out _);
        }
        catch { return false; }
    }

    private string? GetSetting(string key)
    {
        var v = _configuration[key];
        if (string.IsNullOrWhiteSpace(v))
            v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");

    private static string BuildSystemContext(string telemetrySummary, string activeAlerts, string alertSummary) => $"""
        You are an AI assistant for a pharmaceutical manufacturing plant Digital Intelligence Platform.

        CURRENT PLANT STATUS:
        {telemetrySummary}

        ACTIVE ALERTS:
        {activeAlerts}

        ALERT SUMMARY:
        {alertSummary}

        MACHINES IN PLANT:
        - M001: Reactor Vessel A (Bioreactor) - Block A
        - M002: Mixing Unit B (Mixer) - Block B
        - M003: Filtration Unit C (Filter) - Block C
        - M004: Dryer Unit D (Dryer) - Block D
        - M005: Granulator E (Granulator) - Block A

        THRESHOLD LIMITS:
        - Bioreactor Temperature: Warning >40°C, Critical >45°C
        - Bioreactor Pressure: Warning >3.0 bar, Critical >4.0 bar
        - Mixer Vibration: Warning >1.0 mm/s, Critical >1.5 mm/s
        - Dryer Temperature: Warning >95°C, Critical >105°C
        - Filter Pressure: Warning >4.0 bar, Critical >5.0 bar

        Answer as a pharma plant expert. Be specific with numbers from the data above,
        suggest actionable steps, and flag safety concerns clearly. Keep responses
        concise but informative (2-4 paragraphs max).
        """;

    // ---------------------------------------------------------------------
    // Local rule engine (offline fallback). Now actually parses the JSON
    // so the user gets real numbers even when no AI backend is reachable.
    // ---------------------------------------------------------------------

    private static string GenerateRuleBasedInsight(string question, string telemetryJson, string alertsJson, string? alertSummaryJson = null)
    {
        var q = question.ToLower();
        var telemetry = ParseTelemetrySummary(telemetryJson);
        var alerts = ParseAlerts(alertsJson);

        var sb = new StringBuilder();

        if (q.Contains("status") || q.Contains("overall") || q.Contains("health") || q.Contains("summary"))
        {
            var critical = alerts.Count(a => a.Severity == "Critical");
            var warnings = alerts.Count(a => a.Severity == "Warning");
            sb.Append($"Plant status snapshot: {telemetry.Count} machines reporting, ");
            sb.Append(critical > 0 ? $"{critical} CRITICAL alert(s)" : "no critical alerts");
            sb.Append(warnings > 0 ? $", {warnings} warning(s). " : ". ");

            if (alerts.Any())
            {
                sb.Append("Top issues: ");
                sb.AppendJoin("; ", alerts.Take(3).Select(a => $"{a.MachineId} {a.Severity} — {a.Message}"));
                sb.Append(". ");
            }
            else
            {
                sb.Append("All monitored parameters are within thresholds. ");
            }

            if (telemetry.Any())
            {
                sb.Append("Latest readings: ");
                sb.AppendJoin("; ", telemetry.Select(t =>
                    $"{t.MachineId} T={t.Temperature:F1}°C P={t.Pressure:F2}bar V={t.Vibration:F2}mm/s"));
                sb.Append('.');
            }
            return sb.ToString();
        }

        if (q.Contains("temperature"))
        {
            var hottest = telemetry.OrderByDescending(t => t.Temperature).FirstOrDefault();
            sb.Append("Temperature readings across the plant: ");
            sb.AppendJoin("; ", telemetry.Select(t => $"{t.MachineId}={t.Temperature:F1}°C"));
            sb.Append('.');
            if (hottest != null)
                sb.Append($" Highest is {hottest.MachineId} at {hottest.Temperature:F1}°C. Bioreactor critical threshold is 45°C; Dryer critical is 105°C.");
            return sb.ToString();
        }

        if (q.Contains("pressure"))
        {
            sb.Append("Pressure readings: ");
            sb.AppendJoin("; ", telemetry.Select(t => $"{t.MachineId}={t.Pressure:F2}bar"));
            sb.Append(". Bioreactor critical >4.0 bar; Filter critical >5.0 bar.");
            return sb.ToString();
        }

        if (q.Contains("vibration"))
        {
            sb.Append("Vibration readings: ");
            sb.AppendJoin("; ", telemetry.Select(t => $"{t.MachineId}={t.Vibration:F2}mm/s"));
            sb.Append(". Mixer critical >1.5 mm/s. Granulator E and Mixing Unit B typically run highest.");
            return sb.ToString();
        }

        if (q.Contains("alert") || q.Contains("critical") || q.Contains("warning"))
        {
            if (!alerts.Any()) return "No active alerts. All machines are within configured thresholds.";
            sb.Append($"There are {alerts.Count} active alert(s): ");
            sb.AppendJoin("; ", alerts.Select(a => $"[{a.Severity}] {a.MachineId} — {a.Message}"));
            sb.Append('.');
            return sb.ToString();
        }

        if (q.Contains("production") || q.Contains("rate") || q.Contains("output"))
        {
            sb.Append("Production rates by machine: ");
            sb.AppendJoin("; ", telemetry.Select(t => $"{t.MachineId}={t.ProductionRate:F1}"));
            sb.Append('.');
            return sb.ToString();
        }

        // Generic fall-through: give them a one-line situation report regardless of question.
        if (telemetry.Any() || alerts.Any())
        {
            sb.Append("Quick situation report (AI backends are unreachable from this host, answering from live telemetry): ");
            sb.Append($"{alerts.Count(a => a.Severity == "Critical")} critical, {alerts.Count(a => a.Severity == "Warning")} warnings active. ");
            if (telemetry.Any())
            {
                var hottest = telemetry.OrderByDescending(t => t.Temperature).First();
                var shakiest = telemetry.OrderByDescending(t => t.Vibration).First();
                sb.Append($"Hottest: {hottest.MachineId} {hottest.Temperature:F1}°C. ");
                sb.Append($"Highest vibration: {shakiest.MachineId} {shakiest.Vibration:F2}mm/s. ");
            }
            sb.Append("Ask about temperature, pressure, vibration, alerts, or production for more detail.");
            return sb.ToString();
        }

        return "I can answer questions about machine health, alerts, temperature, pressure, vibration, and production rates. AI backends are currently unreachable from this host — check /api/insights/diagnostics for details.";
    }

    private static string GenerateMachineInsight(string machineId, string latestJson, string alertsJson)
    {
        var machineNames = new Dictionary<string, string>
        {
            { "M001", "Reactor Vessel A" }, { "M002", "Mixing Unit B" },
            { "M003", "Filtration Unit C" }, { "M004", "Dryer Unit D" }, { "M005", "Granulator E" }
        };
        var name = machineNames.GetValueOrDefault(machineId, machineId);

        var reading = ParseSingleReading(latestJson);
        var alerts = ParseAlerts(alertsJson);

        var sb = new StringBuilder();
        sb.Append($"{name} ({machineId}) status: ");
        if (reading != null)
        {
            sb.Append($"T={reading.Temperature:F1}°C, P={reading.Pressure:F2}bar, V={reading.Vibration:F2}mm/s, ");
            sb.Append($"Production={reading.ProductionRate:F1}. ");
        }
        else
        {
            sb.Append("no recent telemetry available. ");
        }

        if (alerts.Any())
        {
            sb.Append($"Active alerts ({alerts.Count}): ");
            sb.AppendJoin("; ", alerts.Take(3).Select(a => $"[{a.Severity}] {a.Message}"));
            sb.Append('.');
        }
        else
        {
            sb.Append("No active alerts on this machine.");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------------
    // Lightweight JSON parsing for offline mode
    // ---------------------------------------------------------------------

    private record TelemetryRow(string MachineId, double Temperature, double Pressure, double Vibration, double ProductionRate);
    private record AlertRow(string MachineId, string Severity, string Message);

    private static List<TelemetryRow> ParseTelemetrySummary(string json)
    {
        var rows = new List<TelemetryRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray()
                    : Array.Empty<JsonElement>());

            foreach (var item in items)
            {
                rows.Add(new TelemetryRow(
                    GetString(item, "machineId", "MachineId", "id", "Id") ?? "?",
                    GetDouble(item, "temperature", "Temperature"),
                    GetDouble(item, "pressure", "Pressure"),
                    GetDouble(item, "vibration", "Vibration"),
                    GetDouble(item, "productionRate", "ProductionRate", "production")));
            }
        }
        catch { }
        return rows;
    }

    private static TelemetryRow? ParseSingleReading(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var doc = JsonDocument.Parse(json);
            var item = doc.RootElement;
            if (item.ValueKind != JsonValueKind.Object) return null;
            return new TelemetryRow(
                GetString(item, "machineId", "MachineId", "id", "Id") ?? "?",
                GetDouble(item, "temperature", "Temperature"),
                GetDouble(item, "pressure", "Pressure"),
                GetDouble(item, "vibration", "Vibration"),
                GetDouble(item, "productionRate", "ProductionRate", "production"));
        }
        catch { return null; }
    }

    private static List<AlertRow> ParseAlerts(string json)
    {
        var rows = new List<AlertRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray()
                    : Array.Empty<JsonElement>());

            foreach (var item in items)
            {
                rows.Add(new AlertRow(
                    GetString(item, "machineId", "MachineId") ?? "?",
                    GetString(item, "severity", "Severity") ?? "Info",
                    GetString(item, "message", "Message", "description", "Description") ?? ""));
            }
        }
        catch { }
        return rows;
    }

    private static string? GetString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        return null;
    }

    private static double GetDouble(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(n, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
                if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var d2)) return d2;
            }
        return 0d;
    }

    private static async Task<string> FetchJson(HttpClient client, string url)
    {
        try
        {
            var response = await client.GetAsync(url);
            return await response.Content.ReadAsStringAsync();
        }
        catch { return "{}"; }
    }
}

public class QueryRequest
{
    public string Question { get; set; } = string.Empty;
}
