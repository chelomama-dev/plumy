using Microsoft.Net.Http.Headers;

namespace Plumy.Api.Infrastructure;

/// <summary>
/// HTTP's own optimistic concurrency (RFC 9110): the response carries the resource's version in ETag, and a change
/// sends it back in If-Match. A stale If-Match gets 412 Precondition Failed.
/// </summary>
internal static class EntityTags
{
    /// <summary>
    /// The version the client expects, or null when it didn't say (no header, or "*": any version).
    /// </summary>
    public static string? ExpectedVersion(HttpRequest request)
    {
        var header = request.Headers.IfMatch.ToString().Trim();
        if (header.Length == 0)
            return null;

        // Not a list of tags: compared as it came, so a bad header is refused (412) instead of skipping the check
        if (!EntityTagHeaderValue.TryParseList(request.Headers.IfMatch, out var tags) || tags.Count == 0)
            return header;

        // Several tags mean "any of these": a client of ours sends just one, so the first is the one to check
        var tag = tags[0];
        if (tag.Equals(EntityTagHeaderValue.Any))
            return null;

        // If-Match compares strongly (RFC 9110, 13.1.1): a weak tag never matches. Returned whole (W/"..."), it
        // can't equal a version
        return tag.IsWeak ? tag.ToString() : tag.Tag.Value?.Trim('"');
    }

    public static void Set(HttpResponse response, string version) =>
        response.Headers.ETag = new EntityTagHeaderValue($"\"{version}\"").ToString();
}
