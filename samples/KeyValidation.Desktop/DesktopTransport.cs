using System.Diagnostics;

namespace KeyValidation.Desktop;

public static class DesktopTransport
{
    public static HttpClientHandler CreateHandler() => new() { AllowAutoRedirect = false, UseCookies = false };

    public static HttpClient Create(DemoProfile profile, SafeDiagnostics diagnostics)
    {
        profile.Validate();
        return new HttpClient(new LoggingHandler(diagnostics, CreateHandler()))
        {
            BaseAddress = DemoProfile.ValidateApiUrl(profile.ApiUrl), Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 524288
        };
    }

    private sealed class LoggingHandler(SafeDiagnostics diagnostics, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var started = Stopwatch.GetTimestamp();
            diagnostics.RequestStarted();
            var operation = SafeDiagnostics.OperationForRoute(request.RequestUri?.AbsolutePath);
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                diagnostics.Record(operation, (int)response.StatusCode, Stopwatch.GetElapsedTime(started), response.IsSuccessStatusCode ? "HTTP_OK" : "HTTP_REJECTED");
                return response;
            }
            catch (Exception error)
            {
                diagnostics.Record(operation, null, Stopwatch.GetElapsedTime(started), error is OperationCanceledException ? "REQUEST_CANCELLED" : "NETWORK_UNAVAILABLE");
                throw;
            }
        }
    }
}
