#nullable enable

using System;
using System.Collections.Generic;

namespace Chat.Web.Models;

/// <summary>
/// Represents a translation update applied to a message (status, translations, and optional failure metadata).
/// </summary>
public sealed record MessageTranslationUpdate(
    TranslationStatus Status,
    Dictionary<string, string>? Translations = null,
    string? JobId = null,
    DateTime? FailedAt = null,
    TranslationFailureCategory? FailureCategory = null,
    TranslationFailureCode? FailureCode = null,
    string? FailureMessage = null);
