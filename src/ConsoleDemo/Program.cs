using Chizl.ConsoleSupport;
using Chizl.IO.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleDemo
{
    internal class Program
    {
        const int _msgCount = 1000000;

        static readonly string NewLine = Environment.NewLine;
        static readonly string NewLines = $"{Environment.NewLine}{Environment.NewLine}";
        static readonly LogLevel DefLogLevel = LogLevel.Application | LogLevel.Critical | LogLevel.Warning | LogLevel.Information | LogLevel.Trace;
        static readonly LogLevel AllLogLevel = LogLevel.Application | LogLevel.Critical | LogLevel.Error | LogLevel.Warning | LogLevel.Information | LogLevel.Debug ;

        //************************************************
        //Set to true to write each log level in a separate call to WriteLine() instead of using LogLevel.All.
        //************************************************
        static bool _enableIndividualLevelWrites = false;
        static TextLogger _logger = TextLogger.Empty;
        static LogLevel _logLevel = DefLogLevel;
        static bool _isRunning = false;

        static void Main(string[] args)
        {
            // Enabling Virtual Terminal processing in the Windows console.
            VT.Enable();
            //var logLevelCount = 0;
            ConsoleKey ck = GetUserOptions();

            if (ck== ConsoleKey.Escape)
                return;

            _logger = new TextLogger("ConsoleDemo", ".\\logs", _logLevel, TimeSpan.FromDays(3));
            var files = new DirectoryInfo(_logger.LogPath).GetFiles();
            var path = (new DirectoryInfo(_logger.LogPath)).FullName;
            List<string> msges = new List<string>();

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
                    foreach (var i in Enumerable.Range(1, _msgCount))
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
                    foreach (var i in Enumerable.Range(1, _msgCount))
                    {
                        if (_enableIndividualLevelWrites)
                        {
                            // This is an alternative way to write messages for each log level, but for this demo, two loops is less
                            // efficient than the above approach since it requires multiple calls to WriteLine() for each message.
                            // The above approach writes one message with all log levels at once, and the logger handles the
                            // filtering and writing to the appropriate files.
                            foreach (var ll in Enum.GetValues<LogLevel>())
                            {
                                // Skip the 'All' level since it's not a real log level for messages.
                                if (ll.HasFlag(LogLevel.All)) continue;
                                if ((_logLevel & ll) == ll)
                                    _logger.WriteLine(ll, $"Message {i} at level {ll}");
                            }
                        }
                        else
                        {
                            // Write a message for each enabled log level. The logger will filter these
                            // messages and only write them to the files that match the log level.
                            _logger.WriteLine(LogLevel.All, $"Message {i} at level $[LL]$");
                        }
                    }
                }
                _logger.UseVarReplacement = false;

                Console.WriteLine($"{NewLine}{DateTime.Now:HH:mm:ss.ffff}: Flushing ({_logger.QueueCount} queued)...");
                var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                _logger.Flush(tokenSource.Token);
                _isRunning = false;

                // Wait a moment to ensure _isRunning=false stops the progress indicator has time to update before showing the results.
                while (!tsk.IsCompleted) Task.Delay(10).Wait();

                Console.WriteLine($"{NewLine}{DateTime.Now:HH:mm:ss.ffff}: Flush Finished ({_logger.QueueCount} queued)...{NewLine}");
                Console.WriteLine($"Example of one call to TextLogger.WriteLine() using multiple log levels: ({LogLevel.Warning | LogLevel.Information}){NewLine}");

                // Example of writing a message with multiple log levels at the same time. This will write the message to all files that match either level.
                _logger.WriteLine(LogLevel.Warning | LogLevel.Information, "Testing WARN and INFO levels at the same time.");

                var elapsed = DateTime.UtcNow - startTime;
                files = new DirectoryInfo(_logger.LogPath).GetFiles();

                Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: All Messages Written{NewLine}" +
                                  $" - Elapsed Time: {elapsed}{NewLine}" +
                                  $" - Log files: {path}{NewLine}" +
                                  $" - Current Queue Size: {_logger.QueueCount}{NewLines}" +
                                  $"File names and their sizes");

                var maxLength = 0;
                msges.Clear();
                foreach (var file in files)
                {
                    var msg = $" - {file.Name}{(new string(' ', (40 - file.Name.Length)))} : {GetFileSize(file.Length)}";
                    maxLength = Math.Max(maxLength, msg.Length);
                    Console.WriteLine($" - {file.Name}{(new string(' ', (40 - file.Name.Length)))} : {GetFileSize(file.Length)}");
                    msges.Add(msg);
                }

                // Log this after the above console text so it's not in the queue when showing Queue Count.
                _logger.WriteLine(LogLevel.All, $"--==[ Total elapsed time: {elapsed} ]==--");

                Console.WriteLine($"{(new string('=', maxLength))}{NewLines}");
                ck = GetUserOptions();
            }

            // Stop all new messages being passed in.  Flush the queue to ensure
            // all messages are written to disk before we check the results.
            _logger.StopAndFlush();

            if (files.Length > 0)
            {
                Console.WriteLine($"Do you want to delete the following files (Y/n): ");
                foreach (var msg in msges)
                    Console.WriteLine(msg);

                while (ck != ConsoleKey.Y && ck != ConsoleKey.N)
                    ck = Console.ReadKey(true).Key;

                if (ck == ConsoleKey.Y)
                {
                    var err = false;
                    foreach (var file in files)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error deleting file {file.FullName}: {ex.Message}");
                            err = true;
                        }
                    }
                    if (!err)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"{NewLine}Files deleted successfully. Press any key to exit.");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"{NewLine}Some files could not be deleted. Press any key to exit.");
                    }
                    Console.ResetColor();
                    Console.ReadKey(true);
                }
            }
            else
            {
                Console.WriteLine("Closed logger and flushed all messages to disk. Press any key to exit.");
                Console.ReadKey(true);
            }
            
        }
        /// <summary>
        /// Converts a file size in bytes to a human-readable string.
        /// </summary>
        /// <param name="bytes">The file size in bytes.</param>
        /// <returns>A human-readable string representing the file size.</returns>
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
            int curTop = Console.CursorTop;

            await Task.Delay(1).ContinueWith(_ => {
                string ms = "";

                while (_isRunning && DateTime.UtcNow < maxTime)
                {
                    Console.CursorTop = curTop;
                    Console.CursorLeft = curLeft;
                    var t = DateTime.UtcNow - startTime;
                    ms = $"{t.Minutes:00}m {t.Seconds:00}s {t.Milliseconds:00}ms - ({_logger.QueueCount}) queued.";

                    Console.Write(ms);
                    Task.Delay(950).Wait();
                }

                if (_isRunning)
                {
                    _isRunning = false;
                    Console.WriteLine(" - Timed out");
                }

                Console.CursorVisible = true;
            });
        }
        /// <summary>
        /// Counts the number of enabled log levels based on the current log level setting and displays the result.
        /// </summary>
        /// <remarks>Excludes the 'All' log level from the count.</remarks>
        static void GetLogLevelCount()
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

            Console.WriteLine($"{NewLine}{(new string('-', 100))}");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: This demo will write {_msgCount:N0} messages for " +
                              $"each of the {logLevelCount} enabled log types.{NewLine}" +
                              $" {((logLevelCount) * (_msgCount)):N0} messages total.{NewLine}");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: Log levels enabled: {_logLevel}{NewLines}");
        }
        /// <summary>
        /// Prompts the user to select log level options and returns the selected option.
        /// </summary>
        /// <returns>The selected console key representing the user's choice.</returns>
        static ConsoleKey GetUserOptions()
        {
            ConsoleKey ck = ConsoleKey.ExSel;
            // had to split Trace off of all, because All is a combination of all log
            // levels, and Trace is the highest bit, so it was causing issues when trying
            // to check if Trace was included in the log level using bitwise operations.
            Console.WriteLine($" Pick logging to test or open log folder to view existing...{NewLine}" +
                  $" {new string('-', 50)}{NewLine}" +
                  $" D - Default - Log Levels ({_logLevel}){NewLine}" +
                  $" A - All - Log Levels ({AllLogLevel}, Trace) {NewLine}" +
                  $" O - Open Log Folder in Explorer{NewLine}" +
                  $" Esc - Exit Demo{NewLine}");

            while (ck != ConsoleKey.D && ck != ConsoleKey.A)
            {
                ck = Console.ReadKey(true).Key;
                if (ck == ConsoleKey.Escape)
                    return ck;
                else if (ck == ConsoleKey.O)
                {
                    try
                    {
                        if(!Directory.Exists(_logger.LogPath))
                            Directory.CreateDirectory(_logger.LogPath);

                        System.Diagnostics.Process.Start("explorer.exe", _logger.LogPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error opening log folder: {ex.Message}");
                    }
                }
            }

            if (ck == ConsoleKey.A)
                _logLevel = LogLevel.All;
            else
            {
                _logLevel = DefLogLevel;
                Console.WriteLine($"{NewLine}Do you want to write each log level in a separate call to WriteLine()?{NewLine}" +
                                  $" Y - Yes (Slower){NewLine}" +
                                  $" N - No  (Faster, Uses LogLevel.All){NewLine}");
                while (ck != ConsoleKey.Y && ck != ConsoleKey.N)
                {
                    ck = Console.ReadKey(true).Key;
                    if (ck == ConsoleKey.Escape)
                        return ck;
                }
                _enableIndividualLevelWrites = ck == ConsoleKey.Y;
            }

            if (ck != ConsoleKey.Escape)
                GetLogLevelCount();

            return ck;
        }
    }
}
