# @heimdall-sdk/express

> **Requires Sentinel** — this middleware only works when a Sentinel instance is running. It sends request logs and metrics to Sentinel using a buffered, asynchronous mechanism.

Middleware for Express to send request logs and basic metrics to Sentinel with minimal overhead.

## Features

- Automatically logs incoming HTTP requests
- Sends logs and metrics to Sentinel asynchronously
- Buffered sending: flush when buffer reaches `flushSize` or after `flushIntervalMs`
- Identify services via `serviceName`
- API Key authentication support
- Non-blocking — does not delay request handling
- Compatible with ASP.NET Core (.NET 8+)

## Installation

```bash
dotnet add package Heimdall.AspNetCore
```

## Usage

```csharp
using Heimdall;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var heimdall = new Heimdall(
    serviceName: "my-company-api",
    baseUrl: "http://localhost:8080", // Sentinel URL
    apiKey: "heim_XXXX",              // Generated in Sentinel
    flushIntervalMs: 10_000,           // Optional (default: 5000)
    flushSize: 50,                     // Optional (default: 50)
);

app.UseHeimdall(heimdall);

app.MapGet("/", () => Results.Json(new { message = "hello world" }));

app.Run();
```
