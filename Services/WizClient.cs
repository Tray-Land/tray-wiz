using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace TrayWiz.Services;

/// <summary>
/// Talks to WiZ bulbs over their local UDP JSON protocol (port 38899).
/// The protocol is unauthenticated: any host on the LAN can read and control a bulb
/// as long as "Allow local communication" is enabled in the WiZ app.
/// </summary>
public static class WizClient
{
    public const int Port = 38899;

    /// <summary>
    /// When true (launch with --dry-run), commands that change a bulb are counted and dropped
    /// instead of sent. Reads still go out, so the UI can be exercised against real bulbs safely.
    /// </summary>
    public static bool DryRun { get; set; }

    public static int SuppressedCount => _suppressedCount;

    /// <summary>Raised on the caller's thread each time <see cref="DryRun"/> blocks a command.</summary>
    public static event Action? CommandSuppressed;

    private static int _suppressedCount;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMilliseconds(700);
    private const int RequestAttempts = 3;

    // Slider and color-picker drags produce a flood of changes. Each bulb gets at most one
    // setPilot per interval, carrying the merged latest values.
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(80);
    private static readonly Dictionary<string, JsonObject> PendingPilots = [];
    private static readonly Lock PendingLock = new();

    /// <summary>
    /// Broadcasts getPilot on every IPv4 interface and returns the IPs that answered,
    /// along with each bulb's getPilot result.
    /// </summary>
    public static async Task<Dictionary<string, JsonObject>> DiscoverAsync(TimeSpan listenFor, CancellationToken ct = default)
    {
        Dictionary<string, JsonObject> found = [];
        using UdpClient udp = new(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        byte[] probe = Encode("getPilot", null);

        List<IPAddress> targets = [IPAddress.Broadcast, .. GetDirectedBroadcastAddresses()];

        // UDP is lossy; send the probe a few times while listening.
        Task sender = Task.Run(async () =>
        {
            for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
            {
                foreach (IPAddress target in targets)
                {
                    try { await udp.SendAsync(probe, new IPEndPoint(target, Port), ct); }
                    catch (SocketException) { /* interface may not allow broadcast */ }
                }
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }, ct);

        using CancellationTokenSource listenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        listenCts.CancelAfter(listenFor);
        try
        {
            while (true)
            {
                UdpReceiveResult received = await udp.ReceiveAsync(listenCts.Token);
                if (TryGetResult(received.Buffer, out JsonObject? result))
                    found[received.RemoteEndPoint.Address.ToString()] = result;
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }

        try { await sender; } catch (OperationCanceledException) { }
        return found;
    }

    /// <summary>
    /// Sends one request to one bulb and waits for its reply, retrying on timeout.
    /// Returns the "result" object, or null if the bulb didn't answer or returned an error.
    /// </summary>
    public static async Task<JsonObject?> RequestAsync(string ip, string method, JsonObject? parameters = null, CancellationToken ct = default)
    {
        byte[] payload = Encode(method, parameters);
        IPEndPoint endpoint = new(IPAddress.Parse(ip), Port);

        for (int attempt = 0; attempt < RequestAttempts; attempt++)
        {
            using UdpClient udp = new(new IPEndPoint(IPAddress.Any, 0));
            try
            {
                await udp.SendAsync(payload, endpoint, ct);

                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(RequestTimeout);
                while (true)
                {
                    UdpReceiveResult received = await udp.ReceiveAsync(timeout.Token);
                    if (!received.RemoteEndPoint.Address.Equals(endpoint.Address))
                        continue;

                    return TryGetResult(received.Buffer, out JsonObject? result) ? result : null;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out; retry.
            }
            catch (SocketException)
            {
                // Host unreachable etc.; retry.
            }
        }

        return null;
    }

    /// <summary>
    /// Queues a setPilot for a bulb. Rapid calls are merged and sent at most once per
    /// <see cref="ThrottleInterval"/>, so the latest value always wins.
    /// </summary>
    public static void SetPilotThrottled(string ip, JsonObject parameters)
    {
        if (Suppress("setPilot", ip, parameters))
            return;

        bool startSender;
        lock (PendingLock)
        {
            startSender = !PendingPilots.TryGetValue(ip, out JsonObject? pending);
            if (startSender)
            {
                pending = [];
                PendingPilots[ip] = pending;
            }

            foreach ((string key, JsonNode? value) in parameters)
                pending![key] = value?.DeepClone();
        }

        if (!startSender)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(ThrottleInterval).ConfigureAwait(false);

            JsonObject toSend;
            lock (PendingLock)
            {
                toSend = PendingPilots[ip];
                PendingPilots.Remove(ip);
            }

            await SendRawAsync(ip, "setPilot", toSend).ConfigureAwait(false);
        });
    }

    /// <summary>Fire-and-forget send; the bulb's reply is ignored.</summary>
    public static Task SendAsync(string ip, string method, JsonObject? parameters) =>
        Suppress(method, ip, parameters) ? Task.CompletedTask : SendRawAsync(ip, method, parameters);

    private static bool Suppress(string method, string ip, JsonObject? parameters)
    {
        if (!DryRun || !method.StartsWith("set", StringComparison.Ordinal))
            return false;

        Interlocked.Increment(ref _suppressedCount);
        System.Diagnostics.Debug.WriteLine($"[WizClient] DRY RUN blocked {method} -> {ip}: {parameters?.ToJsonString()}");
        CommandSuppressed?.Invoke();
        return true;
    }

    private static async Task SendRawAsync(string ip, string method, JsonObject? parameters)
    {
        try
        {
            using UdpClient udp = new(new IPEndPoint(IPAddress.Any, 0));
            await udp.SendAsync(Encode(method, parameters), new IPEndPoint(IPAddress.Parse(ip), Port)).ConfigureAwait(false);
        }
        catch (SocketException) { }
    }

    private static byte[] Encode(string method, JsonObject? parameters)
    {
        JsonObject message = new()
        {
            ["method"] = method,
            ["params"] = parameters?.DeepClone() ?? new JsonObject(),
        };
        return Encoding.UTF8.GetBytes(message.ToJsonString());
    }

    private static bool TryGetResult(byte[] buffer, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonObject? result)
    {
        result = null;
        try
        {
            result = JsonNode.Parse(buffer)?["result"] as JsonObject;
        }
        catch (System.Text.Json.JsonException) { }
        return result is not null;
    }

    private static IEnumerable<IPAddress> GetDirectedBroadcastAddresses()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (UnicastIPAddressInformation unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                    continue;

                byte[] address = unicast.Address.GetAddressBytes();
                byte[] mask = unicast.IPv4Mask.GetAddressBytes();
                byte[] broadcast = new byte[4];
                for (int i = 0; i < 4; i++)
                    broadcast[i] = (byte)(address[i] | ~mask[i]);

                yield return new IPAddress(broadcast);
            }
        }
    }
}
