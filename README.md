# Chizl.IO.Logging

A simple, cross-platform, asynchronous, file-based logger for .NET with support for multiple log levels and automatic log retention.

***Demo will show:*** One million dynamic lines in each of 5 separate files, took less than 10s.

[![Example Logs](https://github.com/gavin1970/Chizl.IO.Logging/blob/master/imgs/logs_created.png)](https://github.com/gavin1970/Chizl.IO.Logging/blob/master/imgs/logs_created.png)
[![Process Explorer](https://github.com/gavin1970/Chizl.IO.Logging/blob/master/imgs/procexp64.png)](https://github.com/gavin1970/Chizl.IO.Logging/blob/master/imgs/procexp64.png)

---

| [ [log-levels](#log-levels) ] | [ [properties](#properties) ] | [ [methods](#methods) ]|  [ [basic-quick-start](#basic-quick-start) ]| [ [configuration](#configuration) ] | [ [license](#license) ] |
|--|--|--|--|--|--|

## Features

- **Cross-Platform** - Targets .NET Standard 2.0 and .NET 8.0 to show compatibility with a wide range of .NET applications.
- **Asynchronous** - Thread-safe logging with background queue processing
- **Multiple Log Levels** - Supports Application, Critical, Error, Warning, Information, Debug, and Trace.  
  - Also can Write to multiple log levels simultaneously using flags (e.g., `LogLevel.Warning | LogLevel.Information`)
  - If there are LogLevel flags not enabled, the message will still be logged to any enabled log levels.
- **Flexible Filtering** - Enable only the log levels you need using flags
- **Automatic Retention** - Configurable auto log file cleanup (1 day to 2 years)
- **Daily Rotation** - Creates daily log files for each log level.  Appends log level and date to file names (e.g., `MyApp_Application_2024-06-01.log`)
- **Configurable Log Format** - Optionally include timestamps and log level prefixes in log messages  

## Core Usage

### Log Levels

Is a [Flags](https://learn.microsoft.com/en-us/dotnet/api/system.flagsattribute) enum, allowing combination of multiple log levels using bitwise operations. 

Examples of use, shown in the configuration section [below](#configuration)

| Level | Value | Description |
|--|--|--|
| `Application` | 1 | Application lifecycle events |
| `Critical` | 2 | Critical failures |
| `Error` | 4 | Error conditions |
| `Warning` | 8 | Warning conditions |
| `Information` | 16 | Informational messages |
| `Debug` | 32 | Debug-level messages |
| `Trace` | 64 | Detailed trace messages |
| `All` | 127 | All log levels |

---

### Properties

| Property | Readonly | Description |
|--|--|--|
| `Enable` | No | Enable/disable logging without disposing |
| `EnabledLogLevels`| No | Get or set active log levels |
| `KeepLogDays` | No | Log retention period (1-730 days) |
| `LogFileExt` | No | Log file extension |
| `AppName` | Yes | Application name used in log file startup/shutdown |
| `LogPath` | Yes | Directory where logs are stored |
| `Empty` | Yes | Static property, returns an empty logger instance |
| `IsEmpty` | Yes | Returns true if the logger instance is empty |
| `HasQueuedMsgs` | Yes | Check if messages are pending |
| `QueueCount` | Yes | Number of queued messages |
| `IsShuttingDown` | Yes | Indicates logger shutdown state |

---

### Methods

```All methods are asynchronous to ensure non-blocking logging operations. The logger internally manages a background ConcurrentQueue to process log messages efficiently without the use of Tasks or async/await in the public API, providing a simple and intuitive interface for logging.```

| Method | Async | Description |
|--|--|--|
| `WriteLine` | Yes | Write a message with a newline, optionally including a timestamp |
| `Write` | Yes | Write a message without a newline, optionally including a timestamp |
| `StopAndFlush` | Yes | Stop the logger and flush any pending messages.<br/>**Note:** This method will auto execute, if not called by the user, on application shutdown to ensure all log messages queued are flushed to disk. |

```Demo provided shows basic usage of the WriteLine and Write methods, with examples of including timestamps with 1 million messages per active LogType.  With 5 currently active, 5 million messages are logged in the demo in less than 10 seconds, with all messages flushed to disk on application shutdown.```

---

## Installation

Add a reference to `Chizl.IO.Logging` in your project.

## Basic Quick Start

```csharp
using Chizl.IO.Logging;
// Create a logger with default settings (7-day retention) 
var logger = new TextLogger("MyApp", ".\logs");
// Write log messages, default log with DateTime and log level prefix.
logger.WriteLine(LogLevel.Information, "Application started"); 
logger.WriteLine(LogLevel.Warning, "Low memory detected"); 
logger.WriteLine(LogLevel.Error, "Failed to connect to database");
logger.WriteLine(LogLevel.Warning | LogLevel.Information, "Writes to both Warning and Information log files");

// Write log messages without prefix or timestamp
logger.WriteLine(LogLevel.Information, "Application started", false); 
logger.WriteLine(LogLevel.Warning, "Low memory detected", false); 
logger.WriteLine(LogLevel.Error, "Failed to connect to database", false);

// Write unended message, starting with timestamp.
logger.Write(LogLevel.Information, "Application started, ", true); 

// Appending to the above line without timestamp.
logger.Write(LogLevel.Information, "Append to line, ", false); 

// Appending to the above line without timestamp end ending the line.
logger.WriteLine(LogLevel.Information, "Appending and ending line", false);

```

## Configuration

### Custom Log Levels

```csharp
// Enable only Critical and Error logs 
LogLevel enabledLevels = LogLevel.Critical | LogLevel.Error;

// Enable specific log levels using flags 
LogLevel enabledLevels = LogLevel.Application | LogLevel.Critical | LogLevel.Error | LogLevel.Warning;

// Create logger with enabled log levels
var logger = new TextLogger("MyApp", ".\logs", enabledLevels);

// Alternatively, set enabled log levels after creation
var logger = new TextLogger("MyApp", ".\logs");
logger.EnabledLogLevels = enabledLevels;
```

### Custom Retention Period

```csharp
// Set log retention to 30 days
var logger = new TextLogger("MyApp", ".\logs", LogLevel.All, TimeSpan.FromDays(30));

// Alternatively, set retention period after creation
var logger = new TextLogger("MyApp", ".\logs");
logger.KeepLogDays = 30;
```

## License

See [LICENSE](LICENSE) file for details.
