using System.Text.Json.Serialization;

namespace Nyautomator.Discord.Events;

/// <summary>
/// Represents a Discord user object (partial).
/// </summary>
public class DiscordUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("discriminator")]
    public string? Discriminator { get; set; }

    [JsonPropertyName("global_name")]
    public string? GlobalName { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("bot")]
    public bool? Bot { get; set; }

    /// <summary>
    /// Returns the display name, falling back to username.
    /// </summary>
    public string DisplayName => GlobalName ?? Username ?? "Unknown";
}

/// <summary>
/// Represents a Discord guild (server) object (partial).
/// </summary>
public class DiscordGuild
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("owner_id")]
    public string? OwnerId { get; set; }
}

/// <summary>
/// Represents a Discord channel object (partial).
/// </summary>
public class DiscordChannel
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("guild_id")]
    public string? GuildId { get; set; }
}

/// <summary>
/// Represents a Discord guild member object (partial).
/// </summary>
public class DiscordGuildMember
{
    [JsonPropertyName("user")]
    public DiscordUser? User { get; set; }

    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    [JsonPropertyName("roles")]
    public string[]? Roles { get; set; }

    [JsonPropertyName("joined_at")]
    public string? JoinedAt { get; set; }

    [JsonPropertyName("premium_since")]
    public string? PremiumSince { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    /// <summary>
    /// Returns the display name: nick > global_name > username.
    /// </summary>
    public string DisplayName => Nick ?? User?.GlobalName ?? User?.Username ?? "Unknown";
}

/// <summary>
/// Represents a Discord emoji object (partial).
/// </summary>
public class DiscordEmoji
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("animated")]
    public bool? Animated { get; set; }
}

/// <summary>
/// Represents an embedded attachment in a Discord message.
/// </summary>
public class DiscordAttachment
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}

/// <summary>
/// Represents a Discord embed object (partial).
/// </summary>
public class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("color")]
    public int? Color { get; set; }
}

/// <summary>
/// Represents interaction data for slash commands and components.
/// </summary>
public class DiscordInteractionData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }
}
