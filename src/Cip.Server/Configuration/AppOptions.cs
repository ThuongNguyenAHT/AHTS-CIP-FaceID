using System.ComponentModel.DataAnnotations;

namespace Cip.Server.Configuration;

public sealed class SourceDataOptions
{
    public const string SectionName = "SourceData";

    [Range(1, 1_000)]
    public int RecentRowCount { get; init; } = 200;

    [Range(1, 120)]
    public int CommandTimeoutSeconds { get; init; } = 10;
}

public sealed class TabletOptions
{
    public const string SectionName = "Tablet";

    [Range(500, 10_000)]
    public int FallbackPollingMilliseconds { get; init; } = 1_000;
}

public sealed class ConsentPolicyOptions
{
    public const string SectionName = "ConsentPolicy";

    [Required]
    public string Version { get; init; } = "1.0";

    [Required]
    public string Title { get; init; } = string.Empty;

    [Required]
    public string Content { get; init; } = string.Empty;

    [Required]
    public string LanguageCode { get; init; } = "vi-VN";
}

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    [Required]
    public string Username { get; init; } = "admin";

    public string Password { get; init; } = string.Empty;
}
