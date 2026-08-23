using System.Text;
using System.Text.Json;

namespace MB.ComTools.Apps.Content.Services;

public class GenAiService
{
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

    public async Task<string?> GetChatCompletionAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
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

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointPath);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            
            var payload = new
            {
                model = modelName,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                },
                stream = false
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

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
                return null;
            }

            var body = responseBody;
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var json = JsonDocument.Parse(body);

            if (TryGetString(json.RootElement, out var directAnswer, "answer"))
            {
                return directAnswer;
            }

            if (TryGetChoiceContent(json.RootElement, out var choiceAnswer))
            {
                return choiceAnswer;
            }

            if (TryGetString(json.RootElement, out var outputText, "output_text"))
            {
                return outputText;
            }

            return null;
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

    private static bool TryGetChoiceContent(JsonElement root, out string? value)
    {
        value = null;

        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return false;
        }

        var first = choices[0];
        if (!first.TryGetProperty("message", out var message))
        {
            return false;
        }

        return TryGetString(message, out value, "content");
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
