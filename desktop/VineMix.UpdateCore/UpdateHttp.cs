using System.Net;
using System.Net.Http.Headers;

namespace VineMix.UpdateCore;

internal sealed class UpdateHttp : IDisposable
{
    private readonly HttpClient _client;
    internal UpdateHttp(HttpMessageHandler? handler)
    {
        handler ??= new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false };
        if (handler is HttpClientHandler clientHandler) clientHandler.AllowAutoRedirect = false;
        if (handler is SocketsHttpHandler socketsHandler) socketsHandler.AllowAutoRedirect = false;
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal async Task<HttpResponseMessage> GetAsync(Uri uri, bool asset, CancellationToken token)
    {
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            if (!IsHttps(uri) || (asset ? !IsAssetHost(uri) : uri.Host != "api.github.com"))
                throw new UpdateValidationException("The update download redirected outside its trusted HTTPS origins.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("VineMix-Updater/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json"));
            if (!asset) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (!asset || redirect == 3 || location is null) throw new UpdateValidationException("The update service returned an unexpected redirect.");
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                // Once on the CDN, never redirect back through a repository URL
                // or to an unrelated subdomain. No credentials/cookies are sent.
                if (!IsHttps(next) || !IsCdn(next) || (IsCdn(uri) && next.Host != uri.Host))
                    throw new UpdateValidationException("The update download redirected outside GitHub's release asset CDN.");
                uri = next; continue;
            }
            return response;
        }
        throw new UpdateValidationException("The update service returned too many redirects.");
    }
    internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken token)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximum)
            throw new UpdateValidationException("The update response exceeded its size limit.");
        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var result = new MemoryStream();
        byte[] buffer = new byte[16384];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > maximum) throw new UpdateValidationException("The update response exceeded its size limit.");
            result.Write(buffer, 0, count);
        }
        if (content.Headers.ContentLength is long length && result.Length != length)
            throw new UpdateValidationException("The update response was interrupted or has the wrong length.");
        return result.ToArray();
    }
    private static bool IsHttps(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    private static bool IsCdn(Uri uri) => uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    private static bool IsAssetHost(Uri uri) => uri.Host == "github.com" || IsCdn(uri);
    public void Dispose() => _client.Dispose();
}
