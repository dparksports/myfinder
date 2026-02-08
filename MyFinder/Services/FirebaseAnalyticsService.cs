using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyFinder.Services;

/// <summary>
/// Firebase Analytics (GA4) service using Measurement Protocol.
/// Implements session management with 30-minute timeout and proper engagement tracking.
/// </summary>
public class FirebaseAnalyticsService
{
    private readonly HttpClient _httpClient = new HttpClient();
    private string? _measurementId;
    private string? _apiSecret;
    private string? _firebaseAppId;
    private string _clientId;
    private string? _currentSessionId;
    private DateTime _lastEventTime;
    private int _sessionNumber;
    private const int SessionTimeoutMinutes = 30;

    public FirebaseAnalyticsService()
    {
        _clientId = GetOrCreateClientId();
        _sessionNumber = SettingsHelper.Get("ga_session_number", 0);
        _lastEventTime = DateTime.MinValue;
        LoadConfiguration();
    }

    private void LoadConfiguration()
    {
        try
        {
            var configFile = Path.Combine(AppContext.BaseDirectory, "firebase_config.json");
            if (File.Exists(configFile))
            {
                var json = File.ReadAllText(configFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                _measurementId = root.TryGetProperty("measurementId", out var mid) ? mid.GetString() : null;
                _apiSecret = root.TryGetProperty("apiSecret", out var secret) ? secret.GetString() : null;
                _firebaseAppId = root.TryGetProperty("appId", out var appId) ? appId.GetString() : null;

                System.Diagnostics.Debug.WriteLine($"[Analytics] Loaded config: measurementId = {_measurementId}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Analytics] Config file not found: {configFile}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Failed to load configuration: {ex.Message}");
        }
    }

    private string GetOrCreateClientId()
    {
        var clientId = SettingsHelper.Get<string>("firebase_client_id", string.Empty);
        if (string.IsNullOrEmpty(clientId))
        {
            clientId = Guid.NewGuid().ToString();
            SettingsHelper.Set("firebase_client_id", clientId);
            System.Diagnostics.Debug.WriteLine($"[Analytics] New client ID created: {clientId}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Existing client ID loaded: {clientId}");
        }
        return clientId;
    }

    private string GetOrCreateSessionId()
    {
        // Check if session has timed out (30 minutes of inactivity)
        var timeSinceLastEvent = DateTime.Now - _lastEventTime;
        bool sessionExpired = timeSinceLastEvent.TotalMinutes > SessionTimeoutMinutes;

        if (string.IsNullOrEmpty(_currentSessionId) || sessionExpired)
        {
            // Create new session
            _currentSessionId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            _sessionNumber++;
            SettingsHelper.Set("ga_session_number", _sessionNumber);

            if (sessionExpired && _lastEventTime != DateTime.MinValue)
            {
                System.Diagnostics.Debug.WriteLine($"[Analytics] Session timeout detected (>{SessionTimeoutMinutes}min since last event)");
            }

            System.Diagnostics.Debug.WriteLine($"[Analytics] New session started: {_currentSessionId} (session #{_sessionNumber})");

            // Send session_start event
            _ = SendSessionStartEvent();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Existing session continued: {_currentSessionId}");
        }

        return _currentSessionId;
    }

    private async Task SendSessionStartEvent()
    {
        try
        {
            // Send session_start without calling LogEventAsync to avoid recursion
            await SendEventDirectly("session_start", new
            {
                session_id = _currentSessionId,
                engagement_time_msec = 100,
                ga_session_number = _sessionNumber
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Failed to send session_start: {ex.Message}");
        }
    }

    public async Task LogEventAsync(string eventName, object? parameters = null)
    {
        // Check if analytics is enabled (for future opt-out implementation)
        var enabled = SettingsHelper.Get("analytics_enabled", true);
        if (!enabled)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Event '{eventName}' skipped (analytics disabled)");
            return;
        }

        if (string.IsNullOrEmpty(_measurementId) || string.IsNullOrEmpty(_apiSecret))
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Event '{eventName}' skipped (invalid configuration)");
            return;
        }

        // Update session before sending event
        var sessionId = GetOrCreateSessionId();
        _lastEventTime = DateTime.Now;

        // Merge session parameters with custom parameters
        var eventParams = new
        {
            session_id = sessionId,
            engagement_time_msec = 100,
            ga_session_number = _sessionNumber
        };

        // If parameters provided, merge them
        object finalParams;
        if (parameters != null)
        {
            // Serialize both objects and merge
            var eventParamsJson = JsonSerializer.Serialize(eventParams);
            var customParamsJson = JsonSerializer.Serialize(parameters);
            
            var eventParamsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(eventParamsJson);
            var customParamsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(customParamsJson);

            if (eventParamsDict != null && customParamsDict != null)
            {
                foreach (var kvp in customParamsDict)
                {
                    eventParamsDict[kvp.Key] = kvp.Value;
                }
                finalParams = eventParamsDict;
            }
            else
            {
                finalParams = eventParams;
            }
        }
        else
        {
            finalParams = eventParams;
        }

        await SendEventDirectly(eventName, finalParams);
    }

    private async Task SendEventDirectly(string eventName, object parameters)
    {
        try
        {
            var url = $"https://www.google-analytics.com/mp/collect?measurement_id={_measurementId}&api_secret={_apiSecret}";

            var payload = new
            {
                client_id = _clientId,
                non_personalized_ads = true,
                events = new[]
                {
                    new
                    {
                        name = eventName,
                        @params = parameters
                    }
                }
            };

            // Add firebase_app_id if available (required for Firebase dashboard)
            object finalPayload;
            if (!string.IsNullOrEmpty(_firebaseAppId))
            {
                var payloadJson = JsonSerializer.Serialize(payload);
                var payloadDict = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);
                if (payloadDict != null)
                {
                    payloadDict["firebase_app_id"] = _firebaseAppId;
                    finalPayload = payloadDict;
                }
                else
                {
                    finalPayload = payload;
                }
            }
            else
            {
                finalPayload = payload;
            }

            var json = JsonSerializer.Serialize(finalPayload, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            System.Diagnostics.Debug.WriteLine($"[Analytics] Sending event: {eventName}");
            System.Diagnostics.Debug.WriteLine($"[Analytics] Payload: {json}");

            var response = await _httpClient.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[Analytics] Event '{eventName}' failed: {response.StatusCode} - {responseBody}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Analytics] Event '{eventName}' sent successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] Error sending event '{eventName}': {ex.Message}");
        }
    }
}
