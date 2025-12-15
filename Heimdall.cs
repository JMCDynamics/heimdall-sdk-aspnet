using Microsoft.AspNetCore.Http;

namespace Heimdall;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal record RequestLog
(
    string ServiceName,
    long Timestamp,
    string Method,
    string Url,
    int StatusCode,
    double Duration,
    string Ip,
    string UserAgent,
    Dictionary<string, string> Query,
    Dictionary<string, string> Params,
    Dictionary<string, string> Headers,
    object? Body
);

public class Connector
{
    private string _serviceName;
    private string _baseUrl;
    private string _apiKey;
    private int _flushIntervalMs;
    private int _flushSize;
    private bool _developerMode;

    private int _maxBufferSize;

    private static List<RequestLog> _buffer = [];
    private static bool _isFlushing = false;
    
    public Connector(string serviceName, string baseUrl, string apiKey, int flushIntervalMs = 5000, int flushSize = 50, bool developerMode = false, int maxBufferSize = 1000)
    {
        _serviceName = serviceName;
        _baseUrl = baseUrl + "/api";
        _apiKey = apiKey;
        _flushIntervalMs = flushIntervalMs;
        _flushSize = flushSize;
        _developerMode = developerMode;
        _maxBufferSize = maxBufferSize;
        
        if (_developerMode)
        {
            Console.WriteLine("Heimdall Developer Mode is ON.");
            _baseUrl = baseUrl;
        }

        _ = Task.Run(async () => {
            while (true)
            {
                await Task.Delay(_flushIntervalMs);
                await _flushBuffer();
            }
        });
    }
    
    public Func<HttpContext, Func<Task>, Task> Watcher()
    {
        return async (context, next)=>
        {
            var stopwatch = Stopwatch.StartNew();
            
            await next();

            _audit(context, stopwatch.Elapsed.TotalMilliseconds).Wait();
        };  
    }

    private async Task _audit(HttpContext context, double duration)
    {
        var request = context.Request;
        var response = context.Response;

        var body = await ReadRequestBody(request);

        var log = new RequestLog(
            ServiceName: _serviceName,
            Timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method: request.Method,
            Url: $"{request.Scheme}://{request.Host}{request.Path}",
            StatusCode: response.StatusCode,
            Duration: duration,
            Ip: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent: request.Headers["User-Agent"].ToString(),
            Query: request.Query.ToDictionary(q => q.Key, q => q.Value.ToString()),
            Params: context.Request.RouteValues.ToDictionary(
                r => r.Key,
                r => r.Value?.ToString() ?? string.Empty
            ),
            Headers: request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            Body: body
        );
        
        AddToBuffer(log);
    }
    
    private async Task<object?> ReadRequestBody(HttpRequest request)
    {
        if (request.ContentLength == null || request.ContentLength == 0)
            return null;

        request.EnableBuffering();

        using var reader = new StreamReader(
            request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true
        );

        var bodyString = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(bodyString))
            return null;

        try
        {
            return JsonSerializer.Deserialize<object>(bodyString);
        }
        catch
        {
            return bodyString;
        }
    }
    
    private void AddToBuffer(RequestLog log)
    {
        _buffer.Add(log);

        if (_buffer.Count >= _flushSize)
        {
            _ = _flushBuffer();
        }
    }
    
    private async Task _flushBuffer()
    {
        if (_isFlushing || _buffer.Count == 0)
            return;

        _isFlushing = true;

        var logsToSend = _buffer.ToArray();
        _buffer.Clear();
        
        if (logsToSend.Length == 0)
        {
            _isFlushing = false;
            return;
        }

        if (_developerMode)
        {
            Console.WriteLine("Heimdall sending " + logsToSend.Length + " logs.");
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);

            var content = new StringContent(JsonSerializer.Serialize(logsToSend), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync($"{_baseUrl}/requests", content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            lock (_buffer)
            {
                var total = _buffer.Count + logsToSend.Length;
                if (total > _maxBufferSize)
                {
                    var overflow = total - _maxBufferSize;
                    _buffer.RemoveRange(0, overflow);
                }

                _buffer.AddRange(logsToSend);
            }
            
            Console.WriteLine($"failed to flush logs: {ex.Message}");
        }
        finally
        {
            _isFlushing = false;
        }
    }
}
