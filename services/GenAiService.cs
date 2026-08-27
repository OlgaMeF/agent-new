using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MB.ComTools.Apps.Content.Services;

/// <summary>
/// OpenAI-compatible chat client. Supports plain completions and native tool calling
/// (<c>tools</c> / <c>tool_call</c> or <c>tool_calls</c>) used by the agent router.
/// </summary>
public class GenAiService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GenAiService> _logger;
    private string? _cachedModel;

    public GenAiService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GenAiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Plain text completion. Used for answer prose when no tools are needed.
    /// </summary>
    public async Task<string?> GetChatCompletionAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        return await GetChatCompletionAsync(systemMessage: null, userMessage, cancellationToken);
    }

    /// <summary>
    /// Plain text completion with an optional system message.
    /// </summary>
    public async Task<string?> GetChatCompletionAsync(
        string? systemMessage,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<GenAiChatMessage>();

        if (!string.IsNullOrWhiteSpace(systemMessage))
        {
            messages.Add(new GenAiChatMessage("system", systemMessage));
        }

        messages.Add(new GenAiChatMessage("user", userMessage));

        var result = await CompleteAsync(messages, tools: null, toolChoice: null, cancellationToken);
        return result?.Content;
    }

    /// <summary>
    /// Chat completion with optional native tool definitions.
    /// When the model selects tools, <see cref="GenAiCompletionResult.ToolCalls"/> is populated.
    /// </summary>
    public Task<GenAiCompletionResult?> CompleteAsync(
        IReadOnlyList<GenAiChatMessage> messages,
        IReadOnlyList<GenAiToolDefinition>? tools,
        string? toolChoice,
        CancellationToken cancellationToken = default)
    {
        return CompleteInternalAsync(messages, tools, toolChoice, cancellationToken);
    }

    private async Task<GenAiCompletionResult?> CompleteInternalAsync(
        IReadOnlyList<GenAiChatMessage> messages,
        IReadOnlyList<GenAiToolDefinition>? tools,
        string? toolChoice,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return null;
        }

        try
        {
            var apiKey = _configuration["GenAi:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("GenAI ApiKey is not configured.");
                return null;
            }

            var configuredPath = _configuration["GenAi:ChatCompletionsPath"];
            var endpointPath = string.IsNullOrWhiteSpace(configuredPath)
                ? "openai/chat/completions"
                : configuredPath.TrimStart('/');

            var modelName = await ResolveModelNameAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(modelName))
            {
                _logger.LogWarning("GenAI model could not be resolved from configuration or config endpoint.");
                return null;
            }

            var payload = BuildPayload(modelName, messages, tools, toolChoice);
            var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointPath);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.Headers.WwwAuthenticate.Count > 0)
            {
                _logger.LogWarning(
                    "GenAI WWW-Authenticate: {AuthHeader}",
                    string.Join(", ", response.Headers.WwwAuthenticate.Select(h => h.ToString())));
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GenAI completion failed status={StatusCode} bodyLength={Length}",
                    (int)response.StatusCode,
                    responseBody?.Length ?? 0);
                _logger.LogWarning(
                    "GenAI completion failed status={Status} body={Body}",
                    response.StatusCode,
                    responseBody);
                return null;
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            return ParseCompletion(responseBody);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("GenAI request was cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenAI request failed.");
            return null;
        }
    }

    private static object BuildPayload(
        string modelName,
        IReadOnlyList<GenAiChatMessage> messages,
        IReadOnlyList<GenAiToolDefinition>? tools,
        string? toolChoice)
    {
        var messagePayload = messages.Select(message =>
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                return (object)new
                {
                    role = message.Role,
                    content = message.Content,
                    tool_calls = message.ToolCalls.Select(call => new
                    {
                        id = call.Id,
                        type = "function",
                        function = new
                        {
                            name = call.Name,
                            arguments = call.ArgumentsJson
                        }
                    }).ToArray()
                };
            }

            if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                return new
                {
                    role = "tool",
                    tool_call_id = message.ToolCallId,
                    content = message.Content ?? string.Empty
                };
            }

            return new
            {
                role = message.Role,
                content = message.Content
            };
        }).ToArray();

        if (tools is null || tools.Count == 0)
        {
            return new
            {
                model = modelName,
                messages = messagePayload,
                stream = false
            };
        }

        var toolPayload = tools.Select(tool => new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters
            }
        }).ToArray();

        // tool_choice: "auto" | "none" | { type, function: { name } }
        object resolvedChoice = string.IsNullOrWhiteSpace(toolChoice)
            ? "auto"
            : toolChoice.Trim().ToLowerInvariant() switch
            {
                "auto" or "none" or "required" => toolChoice.Trim().ToLowerInvariant(),
                _ => new
                {
                    type = "function",
                    function = new { name = toolChoice.Trim() }
                }
            };

        return new
        {
            model = modelName,
            messages = messagePayload,
            tools = toolPayload,
            //tool_choice = resolvedChoice,
            stream = false
        };
    }

    private static GenAiCompletionResult? ParseCompletion(string responseBody)
    {
        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;

        // Non-standard wrappers some gateways use.
        if (TryGetString(root, out var directAnswer, "answer") && !string.IsNullOrWhiteSpace(directAnswer))
        {
            return new GenAiCompletionResult(directAnswer, Array.Empty<GenAiToolCall>(), "stop");
        }

        if (TryGetString(root, out var outputText, "output_text") && !string.IsNullOrWhiteSpace(outputText))
        {
            return new GenAiCompletionResult(outputText, Array.Empty<GenAiToolCall>(), "stop");
        }

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var first = choices[0];
        var finishReason = first.TryGetProperty("finish_reason", out var finishElement)
            && finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString()
                : null;

        if (!first.TryGetProperty("message", out var message))
        {
            return null;
        }

        TryGetString(message, out var content, "content");

        var toolCalls = new List<GenAiToolCall>();

        if (message.TryGetProperty("tool_calls", out var toolCallsElement)
            && toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCallsElement.EnumerateArray())
            {
                if (!TryParseToolCall(call, out var parsed))
                {
                    continue;
                }

                toolCalls.Add(parsed);
            }
        }

        // Some gateways emit a singular tool_call object or one-element array.
        if (toolCalls.Count == 0
            && message.TryGetProperty("tool_call", out var toolCallElement))
        {
            if (toolCallElement.ValueKind == JsonValueKind.Object
                && TryParseToolCall(toolCallElement, out var singular))
            {
                toolCalls.Add(singular);
            }
            else if (toolCallElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in toolCallElement.EnumerateArray())
                {
                    if (TryParseToolCall(call, out var parsed))
                    {
                        toolCalls.Add(parsed);
                    }
                }
            }
        }

        // Older function_call shape (single call).
        if (toolCalls.Count == 0
            && message.TryGetProperty("function_call", out var functionCall)
            && functionCall.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(functionCall, out var name, "name")
                && !string.IsNullOrWhiteSpace(name))
            {
                var args = functionCall.TryGetProperty("arguments", out var argsElement)
                    && argsElement.ValueKind == JsonValueKind.String
                        ? argsElement.GetString() ?? "{}"
                        : "{}";

                toolCalls.Add(new GenAiToolCall("function_call", name!, args));
            }
        }

        return new GenAiCompletionResult(
            string.IsNullOrWhiteSpace(content) ? null : content!.Trim(),
            toolCalls,
            finishReason);
    }

    private static bool TryParseToolCall(JsonElement call, out GenAiToolCall toolCall)
    {
        toolCall = default!;

        var id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetString(function, out var name, "name") || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var arguments = "{}";
        if (function.TryGetProperty("arguments", out var argsElement))
        {
            arguments = argsElement.ValueKind switch
            {
                JsonValueKind.String => argsElement.GetString() ?? "{}",
                JsonValueKind.Object => argsElement.GetRawText(),
                _ => "{}"
            };
        }

        toolCall = new GenAiToolCall(id, name!, arguments);
        return true;
    }

    private async Task<string?> ResolveModelNameAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_cachedModel))
        {
            return _cachedModel;
        }

        var configuredModel = _configuration["GenAi:Model"];
        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            _cachedModel = configuredModel;
            return _cachedModel;
        }

        var configUrl = _configuration["GenAi:ConfigUrl"];
        var apiKey = _configuration["GenAi:ApiKey"];

        if (string.IsNullOrWhiteSpace(configUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var configRequest = new HttpRequestMessage(HttpMethod.Get, configUrl);
            configRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

            using var configResponse = await _httpClient.SendAsync(configRequest, cancellationToken);
            var configBody = await configResponse.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("GenAI config status={StatusCode}", (int)configResponse.StatusCode);
            _logger.LogInformation("GenAI config received={Received}", !string.IsNullOrWhiteSpace(configBody));

            if (!configResponse.IsSuccessStatusCode || string.IsNullOrWhiteSpace(configBody))
            {
                return null;
            }

            using var configJson = JsonDocument.Parse(configBody);
            if (!configJson.RootElement.TryGetProperty("advertisedModels", out var models)
                || models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var model in models.EnumerateArray())
            {
                if (!model.TryGetProperty("name", out var nameElement)
                    || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var modelName = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(modelName))
                {
                    _cachedModel = modelName.Trim();
                    return _cachedModel;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve GenAI model from config endpoint.");
            return null;
        }
    }

    private static bool TryGetString(JsonElement element, out string? value, string propertyName)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }
}

public sealed record GenAiChatMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    IReadOnlyList<GenAiToolCall>? ToolCalls = null);

public sealed record GenAiToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

public sealed record GenAiToolDefinition(
    string Name,
    string Description,
    object Parameters);

public sealed record GenAiCompletionResult(
    string? Content,
    IReadOnlyList<GenAiToolCall> ToolCalls,
    string? FinishReason)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
}
