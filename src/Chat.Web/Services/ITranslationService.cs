#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Chat.Web.Services;

/// <summary>
/// Request to translate text to one or more target languages.
/// Always translates to EN + another language, with EN translation always stored.
/// </summary>
public class TranslateRequest
{
    /// <summary>
    /// Text to translate (required).
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Source language code (e.g., "pl"). If not specified, auto-detects.
    /// </summary>
    public string? SourceLanguage { get; init; }

    /// <summary>
    /// Target languages with deployment configurations (required, at least one).
    /// Always includes EN + other language(s).
    /// </summary>
    public required IReadOnlyList<TranslationTarget> Targets { get; init; }

    /// <summary>
    /// If true, bypass cache and force fresh translation. Default: false.
    /// Use when message content must be newly translated (e.g., user explicitly requested).
    /// </summary>
    public bool ForceRefresh { get; init; }

    /// <summary>
    /// Desired tone: "formal", "informal", or "neutral". Only supported with LLM models.
    /// </summary>
    public string? Tone { get; init; }
}

/// <summary>
/// Target language configuration for translation.
/// </summary>
public class TranslationTarget
{
    /// <summary>
    /// Target language code (e.g., "en", "cs").
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// Deployment name for specific model (e.g., "gpt-4o-mini").
    /// If not specified, uses configured default or NMT.
    /// </summary>
    public string? DeploymentName { get; init; }
}

/// <summary>
/// Result of a translation request.
/// </summary>
public class TranslateResponse
{
    /// <summary>
    /// Detected source language code (if auto-detection was used).
    /// </summary>
    public string? DetectedLanguage { get; init; }

    /// <summary>
    /// Confidence score for detected language (0.0 to 1.0).
    /// </summary>
    public double DetectedLanguageScore { get; init; }

    /// <summary>
    /// Translations for each requested target language.
    /// Always includes EN translation as first or prominent entry.
    /// </summary>
    public required IReadOnlyList<Translation> Translations { get; init; }

    /// <summary>
    /// True if result was retrieved from cache.
    /// </summary>
    public bool FromCache { get; init; }
}

/// <summary>
/// A single translation result.
/// </summary>
public class Translation
{
    /// <summary>
    /// Translated text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Target language code.
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// Number of source characters (NMT models).
    /// </summary>
    public int? SourceCharacters { get; init; }

    /// <summary>
    /// Number of instruction tokens (LLM models).
    /// </summary>
    public int? InstructionTokens { get; init; }

    /// <summary>
    /// Number of source tokens (LLM models).
    /// </summary>
    public int? SourceTokens { get; init; }

    /// <summary>
    /// Number of target tokens (LLM models).
    /// </summary>
    public int? TargetTokens { get; init; }
}

/// <summary>
/// Service for translating text using Azure AI Translator.
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// Translates text to one or more target languages.
    /// </summary>
    /// <param name="request">Translation request with source text and target languages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translation response with results for each target language.</returns>
    Task<TranslateResponse> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken = default);
}
