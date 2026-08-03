using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SS14.Launcher;

public sealed class OrbitraSocialService : IDisposable
{
    private const string BaseUrl = "https://lvhysaqgxynjcfavrvui.supabase.co";
    private const string Key = "sb_publishable_-MjoEbdhEVaP1QsIrPcbIA_BxqxLw5j";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
    public OrbitraSocialService()
    {
        _http.DefaultRequestHeaders.Add("apikey", Key);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
    }

    public async Task<OrbitraProfileDto?> GetProfileAsync(Guid id, CancellationToken ct = default) =>
        (await GetAsync<List<OrbitraProfileDto>>($"/rest/v1/orbitra_profiles?select=*&user_id=eq.{id:D}&limit=1", ct)).FirstOrDefault();

    public async Task<OrbitraProfileDto?> FindProfileAsync(string username, CancellationToken ct = default) =>
        (await GetAsync<List<OrbitraProfileDto>>($"/rest/v1/orbitra_profiles?select=*&username=ilike.{Uri.EscapeDataString(username.Trim())}&limit=1", ct)).FirstOrDefault();

    public async Task UpsertProfileAsync(Guid id, string username, bool shareServer, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/orbitra_profiles?on_conflict=user_id")
        { Content = JsonContent.Create(new { user_id=id, username, share_current_server=shareServer, updated_at=DateTimeOffset.UtcNow }, options: Json) };
        req.Headers.Add("Prefer", "resolution=merge-duplicates"); await SendAsync(req, ct);
    }

    public async Task<string> UploadAvatarAsync(Guid id, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (bytes.Length > 2 * 1024 * 1024) throw new InvalidOperationException("Аватар превышает 2 МБ.");
        var ext = contentType == "image/png" ? "png" : "jpg";
        // Versioned object names avoid Storage upsert requiring UPDATE/SELECT permissions and
        // also prevent stale avatars from the public CDN cache.
        var path = $"avatars/{id:D}/{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.{ext}";
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/storage/v1/object/orbitra-avatars/{path}");
        upload.Headers.Add("x-upsert", "false"); upload.Content = new ByteArrayContent(bytes);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType); await SendAsync(upload, ct);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/rest/v1/orbitra_profiles?user_id=eq.{id:D}")
        { Content = JsonContent.Create(new { avatar_path=path, updated_at=DateTimeOffset.UtcNow }, options: Json) };
        await SendAsync(patch, ct); return $"{BaseUrl}/storage/v1/object/public/orbitra-avatars/{path}?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
    }

    public async Task<IReadOnlyList<OrbitraFriendDto>> GetFriendsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await GetAsync<List<OrbitraFriendshipDto>>($"/rest/v1/orbitra_friendships?select=*&or=(requester_id.eq.{userId:D},addressee_id.eq.{userId:D})", ct);
        var result = new List<OrbitraFriendDto>();
        foreach (var row in rows)
        {
            var otherId = row.RequesterId == userId ? row.AddresseeId : row.RequesterId;
            var profile = await GetProfileAsync(otherId, ct);
            if (profile != null) result.Add(new(profile, row.Status, row.AddresseeId == userId && row.Status == "pending"));
        }
        return result;
    }

    public async Task SendFriendRequestAsync(Guid from, Guid to, CancellationToken ct = default)
    {
        if (from == to) throw new InvalidOperationException("Нельзя добавить самого себя.");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/orbitra_friendships")
        { Content = JsonContent.Create(new { requester_id=from, addressee_id=to, status="pending" }, options: Json) };
        await SendAsync(req, ct);
    }
    public async Task AcceptFriendAsync(Guid me, Guid requester, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"/rest/v1/orbitra_friendships?requester_id=eq.{requester:D}&addressee_id=eq.{me:D}")
        { Content = JsonContent.Create(new { status="accepted" }, options: Json) }; await SendAsync(req, ct);
    }
    public async Task RemoveFriendAsync(Guid me, Guid other, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/rest/v1/orbitra_friendships?or=(and(requester_id.eq.{me:D},addressee_id.eq.{other:D}),and(requester_id.eq.{other:D},addressee_id.eq.{me:D}))");
        await SendAsync(req, ct);
    }
    public async Task ReportAsync(Guid reporter, Guid target, string reason, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/orbitra_reports")
        { Content = JsonContent.Create(new { reporter_id=reporter, target_id=target, reason=reason.Trim() }, options: Json) }; await SendAsync(req, ct);
    }
    public async Task UpdatePresenceAsync(Guid id, bool share, string? address, string? name, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"/rest/v1/orbitra_profiles?user_id=eq.{id:D}")
        { Content = JsonContent.Create(new { share_current_server=share, current_server=share?address:null, current_server_name=share?name:null, presence_updated_at=DateTimeOffset.UtcNow, updated_at=DateTimeOffset.UtcNow }, options: Json) };
        await SendAsync(req, ct);
    }
    public string? AvatarUrl(string? path) => string.IsNullOrWhiteSpace(path) ? null : $"{BaseUrl}/storage/v1/object/public/orbitra-avatars/{path}";

    private async Task<T> GetAsync<T>(string path, CancellationToken ct) =>
        await _http.GetFromJsonAsync<T>(path, Json, ct) ?? throw new InvalidOperationException("Пустой ответ Supabase.");
    private async Task SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await _http.SendAsync(req, ct); if (res.IsSuccessStatusCode) return;
        var text = await res.Content.ReadAsStringAsync(ct);
        if (text.Contains("PGRST205", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Социальный модуль ещё не настроен в Supabase.");
        throw new HttpRequestException($"Supabase: {(int)res.StatusCode} {text}");
    }
    public void Dispose() => _http.Dispose();
}

public sealed record OrbitraProfileDto(
    [property:JsonPropertyName("user_id")] Guid UserId,
    [property:JsonPropertyName("username")] string Username,
    [property:JsonPropertyName("avatar_path")] string? AvatarPath,
    [property:JsonPropertyName("share_current_server")] bool ShareCurrentServer,
    [property:JsonPropertyName("current_server")] string? CurrentServer,
    [property:JsonPropertyName("current_server_name")] string? CurrentServerName,
    [property:JsonPropertyName("presence_updated_at")] DateTimeOffset? PresenceUpdatedAt,
    [property:JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);
public sealed record OrbitraFriendshipDto(
    [property:JsonPropertyName("requester_id")] Guid RequesterId,
    [property:JsonPropertyName("addressee_id")] Guid AddresseeId,
    [property:JsonPropertyName("status")] string Status);
public sealed record OrbitraFriendDto(OrbitraProfileDto Profile, string Status, bool IsIncoming);
