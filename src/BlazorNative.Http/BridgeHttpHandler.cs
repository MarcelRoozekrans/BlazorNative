using System.Net;
using System.Text;
using BlazorNative.Core;
using ZeroAlloc.Inject;

namespace BlazorNative.Http;

// ─────────────────────────────────────────────────────────────────────────────
// BridgeHttpHandler
//
// An HttpMessageHandler that routes all HTTP traffic through IMobileBridge.FetchAsync
// instead of using .NET's socket-based HttpClientHandler.
//
// Born as the fix for the wasm era's "no sockets" limitation; kept because
// host-mediated fetch remains the design (the native shell performs the actual
// HTTP request — platform networking stack, proxies, certs — and returns the
// response across the bridge). Existing HttpClient usage works without code
// changes. The bridge surface itself is redesigned as C-ABI in Phase 3.1.
//
// Usage (automatic via DI — see ServiceCollectionExtensions):
//   services.AddBlazorNativeHttp();
//   ...
//   // Inject HttpClient anywhere — it just works
//   public MyService(HttpClient http) { ... }
// ─────────────────────────────────────────────────────────────────────────────

[Transient]
public sealed class BridgeHttpHandler : HttpMessageHandler
{
    private readonly IMobileBridge _bridge;

    public BridgeHttpHandler(IMobileBridge bridge)
    {
        _bridge = bridge;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // ── Build bridge request ──────────────────────────────────────────────

        string? body = null;
        if (request.Content is not null)
            body = await request.Content.ReadAsStringAsync(cancellationToken);

        // Flatten headers — both request headers and content headers
        var headers = new Dictionary<string, string>();
        foreach (var (key, values) in request.Headers)
            headers[key] = string.Join(", ", values);
        if (request.Content is not null)
            foreach (var (key, values) in request.Content.Headers)
                headers[key] = string.Join(", ", values);

        var bridgeRequest = new BridgeHttpRequest(
            Url:     request.RequestUri!.ToString(),
            Method:  request.Method.Method,
            Body:    body,
            Headers: headers.Count > 0 ? headers : null);

        // ── Dispatch through native shell ─────────────────────────────────────

        BridgeHttpResponse bridgeResponse;
        try
        {
            bridgeResponse = await _bridge.FetchAsync(bridgeRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HttpRequestException(
                $"BlazorNative bridge fetch failed for {request.Method} {request.RequestUri}: {ex.Message}",
                ex);
        }

        // ── Map back to HttpResponseMessage ───────────────────────────────────

        var response = new HttpResponseMessage((HttpStatusCode)bridgeResponse.StatusCode)
        {
            RequestMessage = request,
            Content        = new StringContent(
                                bridgeResponse.Body,
                                Encoding.UTF8,
                                GetContentType(bridgeResponse.Headers))
        };

        // Restore response headers
        foreach (var (key, value) in bridgeResponse.Headers)
        {
            // Content headers go on Content, not the response
            if (ContentHeaderNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                response.Content.Headers.TryAddWithoutValidation(key, value);
            else
                response.Headers.TryAddWithoutValidation(key, value);
        }

        return response;
    }

    private static string GetContentType(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.TryGetValue("Content-Type", out var ct))
        {
            // Strip parameters like "; charset=utf-8" for StringContent ctor
            var idx = ct.IndexOf(';');
            return idx >= 0 ? ct[..idx].Trim() : ct.Trim();
        }
        return "application/octet-stream";
    }

    private static readonly string[] ContentHeaderNames =
    [
        "Content-Type", "Content-Length", "Content-Encoding",
        "Content-Language", "Content-Location", "Content-MD5",
        "Content-Range", "Content-Disposition", "Expires", "Last-Modified"
    ];
}
