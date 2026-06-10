using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace Vue.Api.Services;

public sealed class FirebasePushService
{
    private readonly DeviceTokenStore _tokens;
    private readonly FirebaseOptions _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FirebasePushService> _logger;
    private bool _initialized;

    public FirebasePushService(
        DeviceTokenStore tokens,
        IOptions<FirebaseOptions> options,
        IWebHostEnvironment env,
        ILogger<FirebasePushService> logger)
    {
        _tokens = tokens;
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task NotifyAlertAsync(AlertMessageDto alert, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var deviceTokens = _tokens.Snapshot();
        if (deviceTokens.Count == 0)
        {
            return;
        }

        try
        {
            EnsureFirebaseApp();

            var title = alert.Severity switch
            {
                "CRITICAL" => "Alarm KRITIK",
                "WARNING" => "Paralajmërim",
                _ => "Alarm shëndetësor"
            };

            var body = $"{alert.PatientId} · Dhoma {alert.RoomNumber}: {alert.Message}";

            var message = new MulticastMessage
            {
                Tokens = deviceTokens.ToList(),
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = new Dictionary<string, string>
                {
                    ["alertId"] = alert.AlertId.ToString(),
                    ["patientId"] = alert.PatientId,
                    ["roomNumber"] = alert.RoomNumber,
                    ["severity"] = alert.Severity,
                    ["alertType"] = alert.AlertType,
                    ["message"] = alert.Message,
                    ["route"] = "/alerts"
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification
                    {
                        ChannelId = "health-alerts",
                        Sound = "default"
                    }
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, cancellationToken);

            if (response.FailureCount > 0)
            {
                for (var i = 0; i < response.Responses.Count; i++)
                {
                    if (response.Responses[i].IsSuccess) continue;
                    _tokens.Unregister(deviceTokens[i]);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase push failed for alert {AlertId}.", alert.AlertId);
        }
    }

    private void EnsureFirebaseApp()
    {
        if (_initialized || FirebaseApp.DefaultInstance is not null)
        {
            _initialized = true;
            return;
        }

        var path = Path.IsPathRooted(_options.ServiceAccountPath)
            ? _options.ServiceAccountPath
            : Path.Combine(_env.ContentRootPath, _options.ServiceAccountPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Firebase service account not found at {path}");
        }

        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(path),
            ProjectId = string.IsNullOrWhiteSpace(_options.ProjectId) ? null : _options.ProjectId
        });

        _initialized = true;
        _logger.LogInformation("Firebase Admin SDK initialized.");
    }
}
