using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using KeyValidation.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KeyValidation.Infrastructure;
public sealed class WebhookTransport(WebhookTargetPolicy targets, IConfiguration configuration, TimeProvider clock)
{
    public async Task<string?> SendAsync(string url, byte[] secret, Guid eventId, string body, CancellationToken cancellationToken)
    {
        var timeout = Math.Clamp(configuration.GetValue("Webhooks:TimeoutSeconds", 10), 1, 30);
        var maximum = Math.Clamp(configuration.GetValue("Webhooks:MaxResponseBytes", 65536), 1024, 1048576);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        try
        {
            var uri = targets.Validate(url);
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(timeout),
                MaxResponseHeadersLength = 16,
                ConnectCallback = async (context, token) =>
                {
                    // Resolve once, validate every answer, then connect to the exact IP.
                    // HttpClient retains the original hostname for TLS/SNI validation.
                    var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
                    if (addresses.Length == 0)
                        throw new HttpRequestException("No destination address.");
                    foreach (var address in addresses)
                        targets.ValidateAddress(uri, address);
                    foreach (var address in addresses)
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                        {
                            NoDelay = true
                        };
                        try
                        {
                            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                            var connected = (socket.RemoteEndPoint as IPEndPoint)?.Address;
                            if (connected is null || !connected.Equals(address))
                                throw new RuleException("WEBHOOK_TARGET_BLOCKED", 400);
                            targets.ValidateAddress(uri, connected);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (SocketException)
                        {
                            socket.Dispose();
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    }

                    throw new HttpRequestException("Connection failed.");
                }
            };
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var signature = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(timestamp + "\n" + body))).ToLowerInvariant();
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add("X-KeyGate-Event-Id", eventId.ToString());
            request.Headers.Add("X-KeyGate-Timestamp", timestamp);
            request.Headers.Add("X-KeyGate-Signature", signature);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.Content.Headers.ContentLength > maximum)
                return "RESPONSE_TOO_LARGE";
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[4096];
            var length = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read == 0)
                    break;
                length += read;
                if (length > maximum)
                    return "RESPONSE_TOO_LARGE";
            }

            return response.IsSuccessStatusCode ? null : "HTTP_" + (int)response.StatusCode;
        }
        catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested)
        {
            return "TIMEOUT";
        }
        catch (RuleException)
        {
            return "WEBHOOK_TARGET_BLOCKED";
        }
        catch (HttpRequestException error)when (error.InnerException is RuleException)
        {
            return "WEBHOOK_TARGET_BLOCKED";
        }
        catch (Exception error)when (error is HttpRequestException or SocketException or IOException)
        {
            return "NETWORK_ERROR";
        }
    }
}
