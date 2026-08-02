using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SS14.Launcher;

public sealed class ThemeWorkshopService : IDisposable
{
    private const string BaseUrl = "https://lvhysaqgxynjcfavrvui.supabase.co";
    private const string PublicKey = "sb_publishable_-MjoEbdhEVaP1QsIrPcbIA_BxqxLw5j";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ThemeWorkshopService()
    {
        _http.DefaultRequestHeaders.Add("apikey", PublicKey);
    }

    public async Task<IReadOnlyList<WorkshopThemeDto>> GetThemesAsync(Guid? userId, CancellationToken ct = default)
    {
        var themes = await GetAsync<List<WorkshopThemeDto>>(
            "/rest/v1/workshop_themes?select=*,theme_likes(count),theme_comments(count)&order=created_at.desc", ct) ?? [];
        if (userId is not { } id || themes.Count == 0) return themes;
        var likes = await GetAsync<List<WorkshopLikeDto>>(
            $"/rest/v1/theme_likes?select=theme_id&user_id=eq.{id:D}", ct) ?? [];
        var liked = likes.Select(x => x.ThemeId).ToHashSet();
        return themes.Select(x => x with { IsLiked = liked.Contains(x.Id) }).ToList();
    }

    public async Task<IReadOnlyList<WorkshopCommentDto>> GetCommentsAsync(Guid themeId, CancellationToken ct = default) =>
        await GetAsync<List<WorkshopCommentDto>>(
            $"/rest/v1/theme_comments?select=*&theme_id=eq.{themeId:D}&order=created_at.asc", ct) ?? [];

    public async Task PublishAsync(WorkshopPublishRequest request, byte[] archive, CancellationToken ct = default)
    {
        if (archive.Length > 20 * 1024 * 1024) throw new InvalidDataException("Архив темы превышает лимит 20 МБ.");
        var path = $"themes/{request.Id:D}/theme.zip";
        using (var upload = new HttpRequestMessage(HttpMethod.Post, $"/storage/v1/object/theme-workshop/{path}"))
        {
            upload.Headers.Add("x-upsert", "false");
            upload.Content = new ByteArrayContent(archive);
            upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            await SendAsync(upload, ct);
        }
        var body = new
        {
            id = request.Id, author_user_id = request.AuthorUserId, author_name = request.AuthorName,
            name = request.Name.Trim(), description = request.Description.Trim(), version = "1.0",
            archive_path = path, background = request.Background, surface = request.Surface,
            accent = request.Accent, text_color = request.TextColor, blur = request.Blur
        };
        using var insert = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/workshop_themes")
        { Content = JsonContent.Create(body, options: Json) };
        await SendAsync(insert, ct);
    }

    public async Task<byte[]> DownloadAsync(WorkshopThemeDto theme, CancellationToken ct = default)
    {
        var bytes = await _http.GetByteArrayAsync(
            $"{BaseUrl}/storage/v1/object/public/theme-workshop/{theme.ArchivePath}", ct);
        _ = IncrementDownloadAsync(theme.Id);
        return bytes;
    }

    public async Task SetLikeAsync(Guid themeId, Guid userId, bool liked, CancellationToken ct = default)
    {
        HttpRequestMessage request;
        if (liked)
            request = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/theme_likes")
            { Content = JsonContent.Create(new { theme_id = themeId, user_id = userId }, options: Json) };
        else
            request = new HttpRequestMessage(HttpMethod.Delete,
                $"/rest/v1/theme_likes?theme_id=eq.{themeId:D}&user_id=eq.{userId:D}");
        using (request) await SendAsync(request, ct);
    }

    public async Task AddCommentAsync(Guid themeId, Guid userId, string userName, string content, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/theme_comments")
        { Content = JsonContent.Create(new { theme_id = themeId, user_id = userId, user_name = userName, content = content.Trim() }, options: Json) };
        await SendAsync(request, ct);
    }

    private async Task IncrementDownloadAsync(Guid themeId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/rpc/increment_theme_download")
            { Content = JsonContent.Create(new { theme = themeId }, options: Json) };
            await SendAsync(request, CancellationToken.None);
        }
        catch { }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.IsAbsoluteUri != true)
            request.RequestUri = new Uri(BaseUrl + request.RequestUri);
        var response = await _http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var detail = await response.Content.ReadAsStringAsync(ct);
        response.Dispose();
        if (detail.Contains("PGRST205", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Мастерская ещё не настроена в Supabase. Выполните файл supabase/theme-workshop.sql.");
        throw new HttpRequestException($"Supabase: {(int)response.StatusCode} {detail}");
    }

    public void Dispose() => _http.Dispose();
}

public sealed record WorkshopThemeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("author_user_id")] Guid AuthorUserId,
    [property: JsonPropertyName("author_name")] string AuthorName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("archive_path")] string ArchivePath,
    [property: JsonPropertyName("background")] string Background,
    [property: JsonPropertyName("surface")] string Surface,
    [property: JsonPropertyName("accent")] string Accent,
    [property: JsonPropertyName("text_color")] string TextColor,
    [property: JsonPropertyName("blur")] int Blur,
    [property: JsonPropertyName("downloads")] int Downloads,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("theme_likes")] WorkshopCountDto[]? Likes,
    [property: JsonPropertyName("theme_comments")] WorkshopCountDto[]? Comments,
    bool IsLiked = false)
{
    public int LikeCount => Likes?.FirstOrDefault()?.Count ?? 0;
    public int CommentCount => Comments?.FirstOrDefault()?.Count ?? 0;
}

public sealed record WorkshopCountDto([property: JsonPropertyName("count")] int Count);
public sealed record WorkshopLikeDto([property: JsonPropertyName("theme_id")] Guid ThemeId);
public sealed record WorkshopCommentDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("theme_id")] Guid ThemeId,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
public sealed record WorkshopPublishRequest(Guid Id, Guid AuthorUserId, string AuthorName, string Name,
    string Description, string Background, string Surface, string Accent, string TextColor, int Blur);
