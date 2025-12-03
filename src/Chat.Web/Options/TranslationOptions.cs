#nullable enable
namespace Chat.Web.Options;

/// <summary>
/// Configuration options for Azure AI Translator service.
/// </summary>
public class TranslationOptions
{
    /// <summary>
    /// Enable translation service. If false, translation requests are skipped.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Azure AI Services resource ID (for Managed Identity authentication in Azure).
    /// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{name}
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Azure AI Translator endpoint URL (e.g., https://your-resource.cognitiveservices.azure.com).
    /// For global endpoint, use: https://api.cognitive.microsofttranslator.com
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure AI Translator subscription key (Ocp-Apim-Subscription-Key).
    /// Required for local development. Not needed in Azure (uses Managed Identity).
    /// </summary>
    public string? SubscriptionKey { get; set; }

    /// <summary>
    /// Azure region for the Translator resource (Ocp-Apim-Subscription-Region).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// API version to use. Default: 2025-10-01-preview (latest preview version).
    /// </summary>
    public string ApiVersion { get; set; } = "2025-10-01-preview";

    /// <summary>
    /// Translation provider type: NMT (Neural Machine Translation), LLM-GPT4oMini, or LLM-GPT4o.
    /// This indicates which translation method is configured in the Azure resource.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Model deployment name for LLM-based translation (e.g., gpt-4o-mini, gpt-4o).
    /// Required when Provider is LLM-GPT4oMini or LLM-GPT4o.
    /// Not used for NMT (Neural Machine Translation).
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// Redis cache TTL for translations in seconds (default: 3600 = 1 hour).
    /// Set to 0 to disable caching.
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 3600;
}
