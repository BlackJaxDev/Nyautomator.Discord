using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nyautomator.Discord;

/// <summary>
/// Lightweight HTTP client for the Discord REST API (v10) using a bot token.
/// </summary>
public sealed class DiscordRestClient
{
    private const string ApiBase = "https://discord.com/api/v10";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly string _botToken;

    public DiscordRestClient(string botToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException("Discord bot token is required.", nameof(botToken));

        _botToken = botToken.Trim();
        _http = CreateHttpClient(_botToken);
    }

    private static HttpClient CreateHttpClient(string botToken)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        try
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "DiscordBot (https://github.com/BlackJaxDev/Nyautomator, 1.0)");
        }
        catch
        {
            // Ignore user-agent parse errors.
        }

        return client;
    }

    // ─── Channel Messages ────────────────────────────────────────────

    /// <summary>
    /// Sends a message to a Discord channel.
    /// Returns the created message object on success, or null on failure.
    /// </summary>
    public async Task<DiscordMessageResponse?> SendMessageAsync(
        string channelId,
        string content,
        DiscordEmbedPayload[]? embeds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID is required.", nameof(channelId));

        var payload = new SendMessagePayload
        {
            Content = string.IsNullOrWhiteSpace(content) ? null : content,
            Embeds = embeds
        };

        var url = $"{ApiBase}/channels/{Uri.EscapeDataString(channelId)}/messages";
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[Discord REST] Send message failed ({(int)response.StatusCode}): {errorBody}");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<DiscordMessageResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    // ─── Reactions ───────────────────────────────────────────────────

    /// <summary>
    /// Adds a reaction emoji to a message.
    /// For Unicode emojis, pass the emoji directly (e.g. "❤️").
    /// For custom emojis, use "name:id" format.
    /// </summary>
    public async Task<bool> AddReactionAsync(
        string channelId,
        string messageId,
        string emoji,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID is required.", nameof(channelId));
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID is required.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(emoji))
            throw new ArgumentException("Emoji is required.", nameof(emoji));

        var encodedEmoji = Uri.EscapeDataString(emoji.Trim());
        var url = $"{ApiBase}/channels/{Uri.EscapeDataString(channelId)}/messages/{Uri.EscapeDataString(messageId)}/reactions/{encodedEmoji}/@me";

        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            // Discord expects an empty body for reaction PUT
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
            return true;

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[Discord REST] Add reaction failed ({(int)response.StatusCode}): {errorBody}");
        return false;
    }

    // ─── Edit Message ────────────────────────────────────────────────

    /// <summary>
    /// Edits an existing Discord message.
    /// </summary>
    public async Task<DiscordMessageResponse?> EditMessageAsync(
        string channelId,
        string messageId,
        string? content,
        DiscordEmbedPayload[]? embeds = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID is required.", nameof(channelId));
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID is required.", nameof(messageId));

        var payload = new SendMessagePayload
        {
            Content = content,
            Embeds = embeds
        };

        var url = $"{ApiBase}/channels/{Uri.EscapeDataString(channelId)}/messages/{Uri.EscapeDataString(messageId)}";
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[Discord REST] Edit message failed ({(int)response.StatusCode}): {errorBody}");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<DiscordMessageResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    // ─── Delete Message ──────────────────────────────────────────────

    /// <summary>
    /// Deletes a message from a channel.
    /// </summary>
    public async Task<bool> DeleteMessageAsync(
        string channelId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID is required.", nameof(channelId));
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID is required.", nameof(messageId));

        var url = $"{ApiBase}/channels/{Uri.EscapeDataString(channelId)}/messages/{Uri.EscapeDataString(messageId)}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
            return true;

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"[Discord REST] Delete message failed ({(int)response.StatusCode}): {errorBody}");
        return false;
    }

    // ─── Get Channel ─────────────────────────────────────────────────

    /// <summary>
    /// Retrieves basic channel information. Useful for validating channel IDs.
    /// </summary>
    public async Task<DiscordChannelResponse?> GetChannelAsync(
        string channelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        var url = $"{ApiBase}/channels/{Uri.EscapeDataString(channelId)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<DiscordChannelResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    // ─── Get Guild Channels ──────────────────────────────────────────

    /// <summary>
    /// Retrieves all channels in a guild. Useful for populating channel dropdowns.
    /// </summary>
    public async Task<List<DiscordChannelResponse>?> GetGuildChannelsAsync(
        string guildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guildId))
            return null;

        var url = $"{ApiBase}/guilds/{Uri.EscapeDataString(guildId)}/channels";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<DiscordChannelResponse>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves guilds visible to the authenticated token owner.
    /// With bot authorization, this returns guilds where the bot is present.
    /// </summary>
    public async Task<List<DiscordGuildResponse>?> GetCurrentUserGuildsAsync(
        CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}/users/@me/guilds";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<DiscordGuildResponse>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves one member in a guild.
    /// </summary>
    public async Task<DiscordGuildMemberResponse?> GetGuildMemberAsync(
        string guildId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guildId))
            throw new ArgumentException("Guild ID is required.", nameof(guildId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        var url = $"{ApiBase}/guilds/{Uri.EscapeDataString(guildId)}/members/{Uri.EscapeDataString(userId)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[Discord REST] Get guild member failed ({(int)response.StatusCode}): {errorBody}");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<DiscordGuildMemberResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves guild members using Discord's paged list endpoint.
    /// </summary>
    public async Task<List<DiscordGuildMemberResponse>?> ListGuildMembersAsync(
        string guildId,
        int limit = 1000,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guildId))
            throw new ArgumentException("Guild ID is required.", nameof(guildId));

        limit = Math.Clamp(limit, 1, 1000);
        var query = $"limit={limit}";
        if (!string.IsNullOrWhiteSpace(after))
            query += $"&after={Uri.EscapeDataString(after.Trim())}";

        var url = $"{ApiBase}/guilds/{Uri.EscapeDataString(guildId)}/members?{query}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[Discord REST] List guild members failed ({(int)response.StatusCode}): {errorBody}");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<DiscordGuildMemberResponse>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches guild members by username or nickname prefix.
    /// </summary>
    public async Task<List<DiscordGuildMemberResponse>?> SearchGuildMembersAsync(
        string guildId,
        string query,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guildId))
            throw new ArgumentException("Guild ID is required.", nameof(guildId));
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query is required.", nameof(query));

        limit = Math.Clamp(limit, 1, 1000);
        var url = $"{ApiBase}/guilds/{Uri.EscapeDataString(guildId)}/members/search?query={Uri.EscapeDataString(query.Trim())}&limit={limit}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[Discord REST] Search guild members failed ({(int)response.StatusCode}): {errorBody}");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<DiscordGuildMemberResponse>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves guild roles.
    /// </summary>
    public async Task<List<DiscordRoleResponse>?> GetGuildRolesAsync(
        string guildId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guildId))
            throw new ArgumentException("Guild ID is required.", nameof(guildId));

        var url = $"{ApiBase}/guilds/{Uri.EscapeDataString(guildId)}/roles";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[Discord REST] Get guild roles failed ({(int)response.StatusCode}): {errorBody}");
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<DiscordRoleResponse>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds one role to one guild member.
    /// </summary>
    public Task<DiscordApiOperationResult> AddGuildMemberRoleAsync(
        string guildId,
        string userId,
        string roleId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => ModifyGuildMemberRoleAsync(HttpMethod.Put, guildId, userId, roleId, reason, cancellationToken);

    /// <summary>
    /// Removes one role from one guild member.
    /// </summary>
    public Task<DiscordApiOperationResult> RemoveGuildMemberRoleAsync(
        string guildId,
        string userId,
        string roleId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => ModifyGuildMemberRoleAsync(HttpMethod.Delete, guildId, userId, roleId, reason, cancellationToken);

    private async Task<DiscordApiOperationResult> ModifyGuildMemberRoleAsync(
        HttpMethod method,
        string guildId,
        string userId,
        string roleId,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(guildId))
            throw new ArgumentException("Guild ID is required.", nameof(guildId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(roleId))
            throw new ArgumentException("Role ID is required.", nameof(roleId));

        var url = $"{ApiBase}/guilds/{Uri.EscapeDataString(guildId)}/members/{Uri.EscapeDataString(userId)}/roles/{Uri.EscapeDataString(roleId)}";

        using var request = new HttpRequestMessage(method, url)
        {
            Content = method == HttpMethod.Put
                ? new StringContent(string.Empty, Encoding.UTF8, "application/json")
                : null
        };

        if (!string.IsNullOrWhiteSpace(reason))
            request.Headers.TryAddWithoutValidation("X-Audit-Log-Reason", Uri.EscapeDataString(reason.Trim()));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            Console.WriteLine($"[Discord REST] Modify guild member role failed ({(int)response.StatusCode}): {body}");

        return new DiscordApiOperationResult(response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }

    // ─── Payload / Response Types ────────────────────────────────────

    public sealed class SendMessagePayload
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("embeds")]
        public DiscordEmbedPayload[]? Embeds { get; set; }

        [JsonPropertyName("tts")]
        public bool? Tts { get; set; }
    }

    public sealed class DiscordMessageResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("channel_id")]
        public string? ChannelId { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; }

        [JsonPropertyName("author")]
        public DiscordAuthorResponse? Author { get; set; }
    }

    public sealed class DiscordAuthorResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("bot")]
        public bool? Bot { get; set; }
    }

    public sealed class DiscordChannelResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("guild_id")]
        public string? GuildId { get; set; }

        [JsonPropertyName("position")]
        public int? Position { get; set; }

        [JsonPropertyName("parent_id")]
        public string? ParentId { get; set; }
    }

    public sealed class DiscordGuildResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("owner")]
        public bool? Owner { get; set; }

        [JsonPropertyName("permissions")]
        public string? Permissions { get; set; }
    }

    public sealed class DiscordGuildMemberResponse
    {
        [JsonPropertyName("user")]
        public DiscordUserResponse? User { get; set; }

        [JsonPropertyName("nick")]
        public string? Nick { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("banner")]
        public string? Banner { get; set; }

        [JsonPropertyName("roles")]
        public List<string>? Roles { get; set; }

        [JsonPropertyName("joined_at")]
        public string? JoinedAt { get; set; }

        [JsonPropertyName("premium_since")]
        public string? PremiumSince { get; set; }

        [JsonPropertyName("deaf")]
        public bool? Deaf { get; set; }

        [JsonPropertyName("mute")]
        public bool? Mute { get; set; }

        [JsonPropertyName("flags")]
        public int? Flags { get; set; }

        [JsonPropertyName("pending")]
        public bool? Pending { get; set; }

        [JsonPropertyName("permissions")]
        public string? Permissions { get; set; }

        [JsonPropertyName("communication_disabled_until")]
        public string? CommunicationDisabledUntil { get; set; }

        [JsonPropertyName("avatar_decoration_data")]
        public JsonElement? AvatarDecorationData { get; set; }

        [JsonPropertyName("collectibles")]
        public JsonElement? Collectibles { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public sealed class DiscordUserResponse
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

        [JsonPropertyName("system")]
        public bool? System { get; set; }

        [JsonPropertyName("mfa_enabled")]
        public bool? MfaEnabled { get; set; }

        [JsonPropertyName("banner")]
        public string? Banner { get; set; }

        [JsonPropertyName("accent_color")]
        public int? AccentColor { get; set; }

        [JsonPropertyName("locale")]
        public string? Locale { get; set; }

        [JsonPropertyName("verified")]
        public bool? Verified { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("public_flags")]
        public int? PublicFlags { get; set; }

        [JsonPropertyName("flags")]
        public int? Flags { get; set; }

        [JsonPropertyName("premium_type")]
        public int? PremiumType { get; set; }

        [JsonPropertyName("avatar_decoration_data")]
        public JsonElement? AvatarDecorationData { get; set; }

        [JsonPropertyName("collectibles")]
        public JsonElement? Collectibles { get; set; }

        [JsonPropertyName("primary_guild")]
        public JsonElement? PrimaryGuild { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    public sealed class DiscordRoleResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("color")]
        public int? Color { get; set; }

        [JsonPropertyName("hoist")]
        public bool? Hoist { get; set; }

        [JsonPropertyName("position")]
        public int? Position { get; set; }

        [JsonPropertyName("permissions")]
        public string? Permissions { get; set; }

        [JsonPropertyName("managed")]
        public bool? Managed { get; set; }

        [JsonPropertyName("mentionable")]
        public bool? Mentionable { get; set; }
    }

    public sealed record DiscordApiOperationResult(bool Success, int StatusCode, string? Body);
}

/// <summary>
/// Represents a Discord embed object for message payloads.
/// </summary>
public sealed class DiscordEmbedPayload
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("color")]
    public int? Color { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("footer")]
    public DiscordEmbedFooter? Footer { get; set; }

    [JsonPropertyName("thumbnail")]
    public DiscordEmbedMedia? Thumbnail { get; set; }

    [JsonPropertyName("image")]
    public DiscordEmbedMedia? Image { get; set; }

    [JsonPropertyName("author")]
    public DiscordEmbedAuthor? Author { get; set; }

    [JsonPropertyName("fields")]
    public DiscordEmbedField[]? Fields { get; set; }
}

public sealed class DiscordEmbedFooter
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }
}

public sealed class DiscordEmbedMedia
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class DiscordEmbedAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }
}

public sealed class DiscordEmbedField
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("inline")]
    public bool? Inline { get; set; }
}
