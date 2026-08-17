using MQTTnet;
using TwinA.ControlServer.Models;

namespace TwinA.ControlServer.Services;

public sealed class MqttRuntimeService
{
    private readonly SettingsStore _settings;
    private readonly ILogger<MqttRuntimeService> _log;

    public MqttRuntimeService(SettingsStore settings, ILogger<MqttRuntimeService> log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task<(bool Ok, bool Verified, string Message, string? Data)> TestAsync(CancellationToken ct)
    {
        var config = RequireConfig();
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            var result = await client.ConnectAsync(BuildOptions(config), timeout.Token);
            if (!client.IsConnected)
                return (false, false, $"MQTT broker returned {result.ResultCode}, but the client is not connected.", null);
            await client.DisconnectAsync();
            return (true, true, $"MQTT login verified: {config.Host}:{config.Port}{(config.Tls ? " (TLS)" : "")}.", result.ResultCode.ToString());
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "MQTT test failed.");
            return (false, false, $"MQTT connection failed: {ex.Message}", null);
        }
    }

    public async Task<(bool Ok, bool Verified, string Message, string? Data)> SetDeviceAsync(string id, bool on, CancellationToken ct)
    {
        var config = RequireConfig();
        var device = config.Devices.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException("MQTT device not found.");
        if (string.IsNullOrWhiteSpace(device.CommandTopic))
            return (false, false, $"{device.Name} has no command topic.", null);

        var payload = on ? device.OnPayload : device.OffPayload;
        var expected = payload;
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        var observed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!string.IsNullOrWhiteSpace(device.StateTopic))
        {
            client.ApplicationMessageReceivedAsync += args =>
            {
                if (args.ApplicationMessage.Topic.Equals(device.StateTopic, StringComparison.Ordinal))
                    observed.TrySetResult(args.ApplicationMessage.ConvertPayloadToString());
                return Task.CompletedTask;
            };
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(BuildOptions(config), timeout.Token);
            if (!client.IsConnected) return (false, false, "MQTT connection did not become active.", null);

            if (!string.IsNullOrWhiteSpace(device.StateTopic))
            {
                var subscribe = factory.CreateSubscribeOptionsBuilder().WithTopicFilter(device.StateTopic).Build();
                await client.SubscribeAsync(subscribe, timeout.Token);
            }

            var message = new MqttApplicationMessageBuilder().WithTopic(device.CommandTopic).WithPayload(payload).Build();
            var publish = await client.PublishAsync(message, timeout.Token);

            if (string.IsNullOrWhiteSpace(device.StateTopic))
            {
                await client.DisconnectAsync();
                return (true, false, $"MQTT publish completed for {device.Name} ({publish.ReasonCode}), but no state topic is configured for verification.", payload);
            }

            using var verifyTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            verifyTimeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                while (!observed.Task.IsCompleted)
                    await Task.Delay(75, verifyTimeout.Token);
                var actual = await observed.Task;
                await client.DisconnectAsync();
                if (PayloadMatches(actual, expected))
                    return (true, true, $"{device.Name} state verified from {device.StateTopic}: {actual}", actual);
                return (true, false, $"Command was published, but {device.Name} reported '{actual}' instead of expected '{expected}'.", actual);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await client.DisconnectAsync();
                return (true, false, $"Command was published to {device.CommandTopic}, but no matching state arrived on {device.StateTopic} within 4 seconds.", payload);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "MQTT device command failed for {Device}", device.Name);
            return (false, false, $"MQTT command failed for {device.Name}: {ex.Message}", null);
        }
    }

    public async Task<MqttDeviceStateDto[]> ReadStatesAsync(CancellationToken ct)
    {
        var config = _settings.Get().Mqtt;
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.Host) || config.Devices.Count == 0)
            return config.Devices.Select(d => new MqttDeviceStateDto(d.Id, d.Name, null, false, d.StateTopic)).ToArray();

        var states = config.Devices.ToDictionary(d => d.Id, d => (string?)null, StringComparer.OrdinalIgnoreCase);
        var topicToDevice = config.Devices.Where(d => !string.IsNullOrWhiteSpace(d.StateTopic))
            .GroupBy(d => d.StateTopic, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (topicToDevice.Count == 0)
            return config.Devices.Select(d => new MqttDeviceStateDto(d.Id, d.Name, null, false, d.StateTopic)).ToArray();

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += args =>
        {
            if (topicToDevice.TryGetValue(args.ApplicationMessage.Topic, out var devices))
            {
                var payload = args.ApplicationMessage.ConvertPayloadToString();
                foreach (var device in devices) states[device.Id] = payload;
            }
            return Task.CompletedTask;
        };

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            await client.ConnectAsync(BuildOptions(config), timeout.Token);
            var subscribe = factory.CreateSubscribeOptionsBuilder();
            foreach (var topic in topicToDevice.Keys) subscribe.WithTopicFilter(topic);
            await client.SubscribeAsync(subscribe.Build(), timeout.Token);
            await Task.Delay(900, timeout.Token); // retained state messages normally arrive immediately.
            await client.DisconnectAsync();
        }
        catch (Exception ex) { _log.LogDebug(ex, "MQTT state probe failed."); }

        return config.Devices.Select(d =>
        {
            var value = states.GetValueOrDefault(d.Id);
            var online = value is not null;
            return new MqttDeviceStateDto(d.Id, d.Name, value, online, d.StateTopic);
        }).ToArray();
    }

    private MqttConfig RequireConfig()
    {
        var config = _settings.Get().Mqtt;
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.Host))
            throw new InvalidOperationException("MQTT is not configured/enabled.");
        return config;
    }

    private static MqttClientOptions BuildOptions(MqttConfig config)
    {
        var clientId = $"twina-{Environment.MachineName}-{Guid.NewGuid():N}";
        if (clientId.Length > 48) clientId = clientId[..48];
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(config.Host, config.Port);

        if (!string.IsNullOrWhiteSpace(config.Username))
            builder.WithCredentials(config.Username, Environment.GetEnvironmentVariable("TWINA_MQTT_PASSWORD") ?? "");
        if (config.Tls)
            builder.WithTlsOptions(new MqttClientTlsOptionsBuilder().Build());
        return builder.Build();
    }

    private static bool PayloadMatches(string actual, string expected)
        => actual.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
}
