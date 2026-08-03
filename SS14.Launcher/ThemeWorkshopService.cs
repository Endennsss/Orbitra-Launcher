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
            "/rest/v1/workshop_themes?select=*,theme_likes(count),theme_comments(count)&order=updated_at.desc", ct) ?? [];
        if (userId is not { } id || themes.Count == 0) return themes;
        var likes = await GetAsync<List<WorkshopLikeDto>>(
            $"/rest/v1/theme_likes?select=theme_id&user_id=eq.{id:D}", ct) ?? [];
        var liked = likes.Select(x => x.ThemeId).ToHashSet();
        var favorites = await GetAsync<List<WorkshopLikeDto>>(
            $"/rest/v1/theme_favorites?select=theme_id&user_id=eq.{id:D}", ct) ?? [];
        var favorite = favorites.Select(x => x.ThemeId).ToHashSet();
        return themes.Select(x => x with { IsLiked = liked.Contains(x.Id), IsFavorite = favorite.Contains(x.Id) }).ToList();
    }

    public async Task<IReadOnlyList<WorkshopCommentDto>> GetCommentsAsync(Guid themeId, CancellationToken ct = default) =>
        await GetAsync<List<WorkshopCommentDto>>(
            $"/rest/v1/theme_comments?select=*&theme_id=eq.{themeId:D}&order=created_at.asc", ct) ?? [];

    public async Task PublishAsync(WorkshopPublishRequest request, byte[] archive, byte[] preview, CancellationToken ct = default)
    {
        ThemeArchiveValidator.Validate(archive);
        if (preview.Length > 2 * 1024 * 1024) throw new InvalidDataException("Превью темы превышает лимит 2 МБ.");
        var path = $"themes/{request.Id:D}/theme.zip";
        var previewPath = $"previews/{request.Id:D}/preview.png";
        await UploadAsync("theme-workshop", path, archive, "application/zip", false, ct);
        await UploadAsync("theme-previews", previewPath, preview, "image/png", false, ct);
        var body = new
        {
            id = request.Id, author_user_id = request.AuthorUserId, author_name = request.AuthorName,
            name = request.Name.Trim(), description = request.Description.Trim(), version = request.Version,
            archive_path = path, preview_path = previewPath, background = request.Background, surface = request.Surface,
            accent = request.Accent, text_color = request.TextColor, blur = request.Blur
        };
        using var insert = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/workshop_themes")
        { Content = JsonContent.Create(body, options: Json) };
        await SendAsync(insert, ct);
    }

    public async Task UpdateAsync(WorkshopThemeDto theme, WorkshopPublishRequest request, byte[] archive,
        byte[] preview, CancellationToken ct = default)
    {
        ThemeArchiveValidator.Validate(archive);
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var archivePath = $"themes/{theme.Id:D}/theme-{stamp}.zip";
        var previewPath = $"previews/{theme.Id:D}/preview-{stamp}.png";
        await UploadAsync("theme-workshop", archivePath, archive, "application/zip", false, ct);
        await UploadAsync("theme-previews", previewPath, preview, "image/png", false, ct);
        var body = new
        {
            name = request.Name.Trim(), description = request.Description.Trim(), version = request.Version,
            archive_path = archivePath, preview_path = previewPath, background = request.Background,
            surface = request.Surface, accent = request.Accent, text_color = request.TextColor,
            blur = request.Blur, updated_at = DateTimeOffset.UtcNow
        };
        using var update = new HttpRequestMessage(HttpMethod.Patch,
            $"/rest/v1/workshop_themes?id=eq.{theme.Id:D}&author_user_id=eq.{request.AuthorUserId:D}")
        { Content = JsonContent.Create(body, options: Json) };
        await SendAsync(update, ct);
        await DeleteObjectQuietlyAsync("theme-workshop", theme.ArchivePath);
        if (!string.IsNullOrWhiteSpace(theme.PreviewPath)) await DeleteObjectQuietlyAsync("theme-previews", theme.PreviewPath);
    }

    public async Task DeleteThemeAsync(WorkshopThemeDto theme, Guid userId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/rest/v1/workshop_themes?id=eq.{theme.Id:D}&author_user_id=eq.{userId:D}");
        await SendAsync(request, ct);
        await DeleteObjectQuietlyAsync("theme-workshop", theme.ArchivePath);
        if (!string.IsNullOrWhiteSpace(theme.PreviewPath)) await DeleteObjectQuietlyAsync("theme-previews", theme.PreviewPath);
    }

    public async Task<byte[]> DownloadAsync(WorkshopThemeDto theme, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(
            $"{BaseUrl}/storage/v1/object/public/theme-workshop/{theme.ArchivePath}",
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > ThemeArchiveValidator.MaxArchiveBytes)
            throw new InvalidDataException("Небезопасная тема: загрузка превышает лимит 20 МБ.");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (target.Length + read > ThemeArchiveValidator.MaxArchiveBytes)
                throw new InvalidDataException("Небезопасная тема: загрузка превышает лимит 20 МБ.");
            target.Write(buffer, 0, read);
        }
        var bytes = target.ToArray();
        ThemeArchiveValidator.Validate(bytes);
        _ = IncrementDownloadAsync(theme.Id);
        return bytes;
    }

    public async Task<byte[]?> DownloadPreviewAsync(WorkshopThemeDto theme, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(theme.PreviewPath)) return null;
        return await _http.GetByteArrayAsync(
            $"{BaseUrl}/storage/v1/object/public/theme-previews/{theme.PreviewPath}", ct);
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

    public async Task SetFavoriteAsync(Guid themeId, Guid userId, bool favorite, CancellationToken ct = default)
    {
        HttpRequestMessage request = favorite
            ? new HttpRequestMessage(HttpMethod.Post, "/rest/v1/theme_favorites")
              { Content = JsonContent.Create(new { theme_id = themeId, user_id = userId }, options: Json) }
            : new HttpRequestMessage(HttpMethod.Delete,
                $"/rest/v1/theme_favorites?theme_id=eq.{themeId:D}&user_id=eq.{userId:D}");
        using (request) await SendAsync(request, ct);
    }

    public async Task AddCommentAsync(Guid themeId, Guid userId, string userName, string content, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/v1/theme_comments")
        { Content = JsonContent.Create(new { theme_id = themeId, user_id = userId, user_name = userName, content = content.Trim() }, options: Json) };
        await SendAsync(request, ct);
    }

    public async Task DeleteCommentAsync(long commentId, Guid userId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/rest/v1/theme_comments?id=eq.{commentId}&user_id=eq.{userId:D}");
        await SendAsync(request, ct);
    }

    private async Task UploadAsync(string bucket, string path, byte[] data, string contentType, bool upsert, CancellationToken ct)
    {
        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/storage/v1/object/{bucket}/{path}");
        upload.Headers.Add("x-upsert", upsert ? "true" : "false");
        upload.Content = new ByteArrayContent(data);
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        await SendAsync(upload, ct);
    }

    private async Task DeleteObjectQuietlyAsync(string bucket, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"/storage/v1/object/{bucket}/{path}");
            await SendAsync(request, CancellationToken.None);
        }
        catch { }
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
    [property: JsonPropertyName("preview_path")] string? PreviewPath,
    [property: JsonPropertyName("background")] string Background,
    [property: JsonPropertyName("surface")] string Surface,
    [property: JsonPropertyName("accent")] string Accent,
    [property: JsonPropertyName("text_color")] string TextColor,
    [property: JsonPropertyName("blur")] int Blur,
    [property: JsonPropertyName("downloads")] int Downloads,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("theme_likes")] WorkshopCountDto[]? Likes,
    [property: JsonPropertyName("theme_comments")] WorkshopCountDto[]? Comments,
    bool IsLiked = false,
    bool IsFavorite = false)
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
    string Description, string Version, string Background, string Surface, string Accent, string TextColor, int Blur);
