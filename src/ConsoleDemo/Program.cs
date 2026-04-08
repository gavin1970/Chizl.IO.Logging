using Chizl.IO.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleDemo
{
    internal class Program
    {
        static readonly string NewLine = Environment.NewLine;
        static readonly string NewLines = $"{Environment.NewLine}{Environment.NewLine}";
        static readonly LogLevel DefLogLevel = LogLevel.Application | LogLevel.Critical | LogLevel.Warning | LogLevel.Information | LogLevel.Trace;

        static TextLogger _logger = TextLogger.Empty;
        static LogLevel _logLevel = DefLogLevel;
        static bool _isRunning = false;

        static void Main(string[] args)
        {
            var msgCount = 1000000;
            var logLevelCount = 0;
            ConsoleKey ck = ConsoleKey.ExSel;

            Console.WriteLine($"Do you want to use default log levels ({_logLevel}) or log (All) levels?{NewLine}" +
                              $" D - Default{NewLine}" +
                              $" A - All{NewLine}" +
                              $" Esc - Exit Demo{NewLine}");

            while (ck != ConsoleKey.D && ck != ConsoleKey.A)
            {
                ck = Console.ReadKey(true).Key;
                if (ck == ConsoleKey.Escape)
                    return;
            }

            if (ck == ConsoleKey.A)
                _logLevel = LogLevel.All;
            else
                _logLevel = DefLogLevel;

            logLevelCount = GetLogLevelCount();

            _logger = new TextLogger("ConsoleDemo", ".\\logs", _logLevel, TimeSpan.FromDays(3));

            Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: This demo will write {msgCount:N0} messages for " +
                              $"each of the {logLevelCount} enabled log types.{NewLine}" +
                              $" {((logLevelCount) * (msgCount)):N0} messages total.{NewLine}");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: Log levels enabled: {_logLevel}{NewLine}");

            while (ck != ConsoleKey.Escape)
            {
                var startTime = DateTime.UtcNow;
                var tsk = ShowProgress(startTime, TimeSpan.FromMinutes(5));

                // ONLY set UseVarReplacement=true if message needs $[LL]$ replaced in the message with
                // the log level of the file being written to, This is useful for demonstrating writing
                // the same message to multiple log levels at the same time. Default is false for better
                // performance when writing a large number of messages.
                _logger.UseVarReplacement = true; 
                if (_logLevel == LogLevel.All)
                {
                    foreach (var i in Enumerable.Range(1, msgCount))
                    {
                        // The message will be written to all log files that match any of the log levels
                        // specified in the message, in this case All log levels.
                        // $[LL]$ will be replaced with the log level of the file being written to.
                        _logger.WriteLine(LogLevel.All, $"Message {i} at level {LogLevel.All}/$[LL]$");
                    }
                }
                else
                {
                    // Write a large number of messages to demonstrate the logger's performance and filtering capabilities.
                    foreach (var i in Enumerable.Range(1, msgCount))
                    {
                        // Write a message for each enabled log level. The logger will filter these
                        // messages and only write them to the files that match the log level.
                        _logger.WriteLine(LogLevel.All, $"Message {i} at level $[LL]$");
                    }
                }
                _logger.UseVarReplacement = false;

                Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: Flushing ({_logger.QueueCount} queued)...");
                var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                _logger.Flush(tokenSource.Token);
                _isRunning = false;

                // Wait a moment to ensure _isRunning=false stops the progress indicator has time to update before showing the results.
                while (!tsk.IsCompleted) Task.Delay(10).Wait();

                Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: Flush Finished ({_logger.QueueCount} queued)...{NewLine}");
                Console.WriteLine($"Example of one call to TextLogger.WriteLine() using multiple log levels: ({LogLevel.Warning | LogLevel.Information}){NewLine}");

                // Example of writing a message with multiple log levels at the same time. This will write the message to all files that match either level.
                _logger.WriteLine(LogLevel.Warning | LogLevel.Information, "Testing WARN and INFO levels at the same time.");

                var elapsed = DateTime.UtcNow - startTime;
                var files = new DirectoryInfo(_logger.LogPath).GetFiles();
                var path = (new DirectoryInfo(_logger.LogPath)).FullName;

                Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: All Messages Written{NewLine}" +
                                  $" - Elapsed Time: {elapsed}{NewLine}" +
                                  $" - Log files: {path}{NewLine}" +
                                  $" - Current Queue Size: {_logger.QueueCount}{NewLines}" +
                                  $"File names and their sizes");

                foreach(var file in files)
                    Console.WriteLine($" - {file.Name}{(new string(' ', (40-file.Name.Length)))} : {GetFileSize(file.Length)}");

                Console.WriteLine($"{NewLine}" +
                                  $"Press any key to use log level ({_logLevel}) again or press one of the keys shown below for a different result.{NewLine}" +
                                  $" D - Default{NewLine}" +
                                  $" A - All{NewLine}" +
                                  $" Esc - Exit Demo{NewLines}");

                // Log this after the above console text so it's not in the queue when showing Queue Count.
                _logger.WriteLine(LogLevel.All, $"--==[ Total elapsed time: {elapsed} ]==--");

                ck = Console.ReadKey(true).Key;
                if (ck != ConsoleKey.Escape)
                {
                    if (ck == ConsoleKey.D)
                        _logLevel = DefLogLevel;
                    else if (ck == ConsoleKey.A)
                        _logLevel = LogLevel.All;

                    _logger.EnabledLogLevels = _logLevel;
                    logLevelCount = GetLogLevelCount();

                    Console.WriteLine($"{(new string('=', 50))}{NewLines}");
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: This demo will write {msgCount:N0} messages for " +
                                      $"each of the {logLevelCount} enabled log types.{NewLine}" +
                                      $" {((logLevelCount) * (msgCount)):N0} messages total.{NewLine}");
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: Log levels enabled: {_logLevel}{NewLine}");
                }
            }

            // Stop all new messages being passed in.  Flush the queue to ensure
            // all messages are written to disk before we check the results.
            _logger.StopAndFlush();
            Console.WriteLine("Closed logger and flushed all messages to disk. Press any key to exit.");
            Console.ReadKey(true);
        }

        static string GetFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:N2} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):N2} MB";
            else
                return $"{bytes / (1024.0 * 1024 * 1024):N2} GB";
        }
        /// <summary>
        /// Displays a progress indicator until the operation completes or the specified maximum delay elapses.
        /// </summary>
        /// <param name="maxDelay">The maximum duration to display the progress indicator.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        static async Task ShowProgress(DateTime startTime, TimeSpan maxDelay)
        {
            Console.CursorVisible = false;
            var maxTime = DateTime.UtcNow + maxDelay;
            // Show a progress indicator while the messages are being written.
            // This will stop when the messages are done or when the max delay
            // is reached, whichever comes first.
            _isRunning = true;
            //int secCount = 1;
            int curLeft = Console.CursorLeft;

            await Task.Delay(1).ContinueWith(_ => {
                string ms = "";

                while (_isRunning && DateTime.UtcNow < maxTime)
                {
                    Console.CursorLeft = curLeft;
                    var t = DateTime.UtcNow - startTime;
                    ms = $"{t.Minutes:00}m {t.Seconds:00}s {t.Milliseconds:00}ms";
                    Console.Write(ms);
                    Task.Delay(100).Wait();
                }

                if (_isRunning)
                {
                    _isRunning = false;
                    Console.WriteLine(" - Timed out");
                }

                Console.CursorVisible = true;
            });
        }
        static int GetLogLevelCount()
        {
            var logLevelCount = 0;

            if (_logLevel == LogLevel.All)
                logLevelCount = Enum.GetValues<LogLevel>().Length - 1; // Subtract 1 to exclude the 'All' level.
            else
            {
                foreach (var ll in Enum.GetValues<LogLevel>())
                {
                    // Skip the 'All' level since it's not a real log level for messages.
                    if (ll.HasFlag(LogLevel.All)) continue;

                    if ((_logLevel & ll) == ll)
                        logLevelCount++;
                }
            }

            return logLevelCount;
        }
    }
}
