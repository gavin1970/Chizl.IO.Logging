using Chizl.ThreadSupport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Chizl.IO.Logging.options
{
    /// <summary>
    /// Abstract class to provide configuration options for logger behavior, including 
    /// log file location, retention, enabled log levels, and file extension.
    /// </summary>
    public abstract class LoggerOptions
    {
        internal const string _tmFormat = "HH:mm:ss.ffff";
        const string _appNamePattern = @"[^a-zA-Z0-9\.]";
        const string _logPathPattern = @"[^a-zA-Z0-9/\-\\\s]";
        const int _maxIoFailures = 5;

        internal int _ioFailureCount = 0;
        // setup default values for log levels and retention period, and define min/max limits for retention to prevent excessive disk usage
        internal static readonly LogLevel _defEnabledLogLevels = LogLevel.Application | LogLevel.Critical |
                                                                LogLevel.Error | LogLevel.Warning | LogLevel.Information;
        internal static readonly TimeSpan _defKeepLogDays = TimeSpan.FromDays(7);    // 7 days.
        internal readonly TimeSpan _minKeepLogDays = TimeSpan.FromDays(1);           // 1 day minimum retention to ensure logs are kept for a reasonable period for troubleshooting and analysis
        internal readonly TimeSpan _maxKeepLogDays = TimeSpan.FromDays(365 * 2);     // 2 years max retention to prevent excessive disk usage
        internal readonly string NewLine = Environment.NewLine;
        internal readonly string NewLines = $"{Environment.NewLine}{Environment.NewLine}";

        // internal fields to manage logger state and configuration, such as log retention,
        // initialization status, writing status, shutdown status, active log file date,
        // and queued messages for asynchronous processing
        internal TimeSpan _keepLogDays = _defKeepLogDays;
        internal LogLevel _enabledLogLevels = _defEnabledLogLevels;
        internal string _logFileExt = "log";
        internal ABool _initialize = ABool.False;
        internal ABool _isWriting = ABool.False;
        internal ABool _shuttingDown = ABool.False;
        internal ABool _exitWriteNow = ABool.False;
        // Might think it's a trick, but this thread safe ADateTime is an "Atomic DateTime" class.
        // Initialize to a date far in the past to ensure that log setup runs on the
        // first log attempt and creates the initial log file.
        internal ADateTime _activeFileDate = Now.Date.AddDays(-1);
        internal List<FileInfo> _fileInfo = new List<FileInfo>();
        // Concurrent queue to hold log messages before they are written to the log file,
        // allowing for asynchronous processing and improved performance by decoupling
        // message generation from file I/O operations.
        internal ConcurrentQueue<(LogLevel Level, string Msg)> _queuedMsgs = new ConcurrentQueue<(LogLevel, string)>();
        /// <summary>
        /// Local class shortcut to get the current local date and time.
        /// </summary>
        internal static DateTime Now => DateTime.Now;

        /// <summary>
        /// Internal property to get the current time formatted as a string according to 
        /// the specified time format. This is used for timestamping log messages and can 
        /// be accessed throughout the logger implementation to ensure consistent time 
        /// formatting across all log entries.
        /// </summary>
        internal string MsgTime => Now.ToString(_tmFormat);

        /// <summary>
        /// Constructor that only initializes a for static Empty method is called. This will
        /// set IsEmpty to true, Enable to false, and shutting down to true to indicate 
        /// that the logger is not active and should not accept log messages until properly 
        /// configured and enabled. This constructor can be used to create a placeholder or 
        /// default options instance that can be later configured with specific settings as 
        /// needed.
        /// </summary>
        internal LoggerOptions() { IsEmpty = true; Enable = false; _shuttingDown.SetTrue(); }
        /// <summary>
        /// Initializes a new instance of the LoggerOptions class with the specified application name and log file path.
        /// </summary>
        /// <param name="appName">The name of the application associated with the logger.</param>
        /// <param name="logPath">The directory path where log files are stored.</param>
        internal LoggerOptions(string appName, string logPath)
        {
            // if not null, trim whitespace
            appName = appName?.Trim();
            // if not null, trim whitespace
            logPath = logPath?.Trim();
            // only allow a period at the start of the path.
            var startWithPeriod = logPath?.StartsWith(".") ?? false;

            // since I'm filtering out invalid characters, I want to allow for a colon in the log path
            // for drive letters (e.g., "C:\logs"), but I will remove any additional colons that may
            // be present to prevent issues with file paths and ensure the log path is valid and safe for
            // use in file I/O operations.
            var colonNdx = logPath?.IndexOf(':') ?? -1;

            // Sanitize the application name by removing any characters that are not
            // letters, numbers, or periods to ensure it is safe for use in file
            // names and log entries.  If the provided appName is null, we will
            // keep the default AppName value, which is "TextLogger".  This allows
            // for flexibility in configuring the logger while ensuring that the
            // application name is always valid and does not contain any potentially
            // problematic characters.
            this.AppName = Regex.Replace(appName ?? this.AppName, _appNamePattern, "");
            // Sanitize the log path by removing any characters that are not letters,
            // numbers, dashes, backslashes, or whitespace to ensure it is safe for
            // use as a directory path. If the provided logPath is null, we will
            // keep the default LogPath value, which is ".\\logs". This allows for
            // flexibility in configuring the logger while ensuring that the log
            // path is always valid and does not contain any potentially problematic
            // characters that could lead to issues with file I/O operations or
            // security vulnerabilities.
            this.LogPath = Regex.Replace(logPath ?? this.LogPath, _logPathPattern, "");

            // if a colon was found, we need to re-insert it after sanitization to
            // allow for valid drive letter paths (e.g., "C:\logs") while still removing
            // any additional colons that may have been present in the original logPath
            // to ensure the final log path is valid and safe for use in file I/O operations.
            if (colonNdx == 1)
                this.LogPath = this.LogPath.Insert(colonNdx, ":"); // Re-insert the colon after sanitization if it was
                                                                   // present at the expected position for a drive letter
            else if (startWithPeriod)
                this.LogPath = "." + this.LogPath; // Re-add the period at the start of the path if it was present in the original logPath
        }

        #region Public Methods
        /// <summary>
        /// Gets a value indicating whether the collection is empty.
        /// </summary>
        public bool IsEmpty { get; } = false;
        /// <summary>
        /// Gets or sets a value indicating whether Logging is enabled.<br/>
        /// <br/>
        /// No queuing will occur if false.  This can be used to temporarily 
        /// disable logging without having to dispose of the logger or change 
        /// log levels, and it allows for quick re-enabling of logging when 
        /// needed without losing the existing configuration.<br/>
        /// If messages exists in queued before logging is disabled, they will 
        /// still be written when ProcessQueueAsync runs, but no new messages 
        /// will be accepted until logging is re-enabled.<br/>
        /// This allows for a quick and easy way to pause, purge, and resume 
        /// logging as needed without having to manage the logger's 
        /// lifecycle or configuration.<br/>
        /// This is a great safety measure when needed.  Validation can be done 
        /// through HasQueueMessages and QueueCount properties.
        /// </summary>
        public bool Enable { get; set; } = true;
        /// <summary>
        /// Application Name to include in log entries and session headers.  
        /// This helps to identify the source of log messages, especially when 
        /// multiple applications are logging to the same directory.  It is 
        /// included in the session header at the start of each log file to 
        /// provide context for the logged messages.
        /// </summary>
        public string AppName { get; } = "TextLogger";
        /// <summary>
        /// Gets or sets the path where log files are stored.
        /// </summary>
        public string LogPath { get; } = ".\\logs";
        /// <summary>
        /// Gets or sets the file extension for log files.  During operation, this can 
        /// still be used safely to change the log file extension for new log files 
        /// that are created, but it will not affect existing log files or the current 
        /// log file until the next time a new log file is created (e.g., on the next 
        /// day when the date changes and a new log file is needed).  This allows for 
        /// flexibility in changing the log file format without disrupting ongoing 
        /// logging operations or causing inconsistencies in the log files.
        /// </summary>
        public string LogFileExt
        {
            get { return _logFileExt; }
            set
            {
                // Messages will still be queued while we wait to acquire the
                // write block, but they will not be written until we have
                // updated the log levels and released the block.  This allows
                // for a safe update of log levels without losing any messages
                // or causing inconsistencies in the log files, even if there
                // are ongoing logging operations at the time of the update.
                if (!BlockWriteForUpdate(2000))
                    return;

                _logFileExt = value;
                // Force log setup to run on next log attempt to ensure
                // FileInfo is up to date with enabled log levels.
                _activeFileDate.AdjustTime(Now.Date.AddDays(-1));

                // Since Date has been moved back, LogSetup will be called and setup new File Ext.
                // _ = ProcessQueueAsync();
                _isWriting.SetFalse();
            }
        }
        /// <summary>
        /// Gets or sets the duration for which log entries are retained 
        /// and will safely make changes even during current operations.
        /// </summary>
        public TimeSpan KeepLogDays
        {
            get { return _keepLogDays; }
            set
            {
                // Messages will still be queued while we wait to acquire the
                // write block, but they will not be written until we have
                // updated the log levels and released the block.  This allows
                // for a safe update of log levels without losing any messages
                // or causing inconsistencies in the log files, even if there
                // are ongoing logging operations at the time of the update.
                if (!BlockWriteForUpdate(2000))
                    return;

                _keepLogDays = CheckLogDays(value);
                // Force log setup to run on next log attempt to ensure
                // FileInfo is up to date with enabled log levels. Using Date
                // over TimeSpan to ensure that log setup runs on the next
                // day change to handle log file cleanup based on the new
                // retention period, without having to wait for a write
                // operation to trigger the setup.
                _activeFileDate.AdjustTime(Now.Date.AddDays(-1));

                // Since Date has been moved back, LogSetup will be called and setup new KeepDays.
                // _ = ProcessQueueAsync();
                _isWriting.SetFalse();
            }
        }
        /// <summary>
        /// Gets or sets the log levels that are enabled for logging.<br/>
        /// Default: LogLevel.Application | LogLevel.Critical | LogLevel.Error | LogLevel.Warning | LogLevel.Information
        /// </summary>
        public LogLevel EnabledLogLevels
        {
            get { return _enabledLogLevels; }
            set
            {
                // Messages will still be queued while we wait to acquire the
                // write block, but they will not be written until we have
                // updated the log levels and released the block.  This allows
                // for a safe update of log levels without losing any messages
                // or causing inconsistencies in the log files, even if there
                // are ongoing logging operations at the time of the update.
                if (!BlockWriteForUpdate(2000))
                    return;

                _enabledLogLevels = value;

                // Force log setup to run on next log attempt to ensure
                // FileInfo is up to date with enabled log levels.
                _activeFileDate.AdjustTime(Now.Date.AddDays(-1));

                // Since Date has been moved back, LogSetup will be called and setup new Log Levels.
                // _ = ProcessQueueAsync();
                _isWriting.SetFalse();
            }
        }
        /// <summary>
        /// Wait up to 2 seconds for any ongoing write operation to complete
        /// before updating log levels.  This helps to ensure that we don't
        /// interrupt an active write operation, which could lead to inconsistent
        /// log files or other issues.  If the write operation takes longer than
        /// the specified time, we will proceed with the update anyway to avoid
        /// getting stuck indefinitely, but this should be a rare occurrence and
        /// may indicate a larger issue with the logger that needs to be addressed.
        /// </summary>
        /// <param name="msWait">Max Milliseconds to wait for _isWriting to be set to True</param>
        /// <returns>If _isWriting is set and the update can proceed</returns>
        private bool BlockWriteForUpdate(int msWait)
        {
            var maxTime = DateTime.UtcNow.AddMilliseconds(msWait);

            // Wait until any ongoing write operation is complete before
            // allowing updates to the logger configuration to ensure thread safety
            while (DateTime.UtcNow < maxTime)
            {
                if (_isWriting.TrySetTrue())
                    return true; // We were able to set _isWriting to true,
                                 // which means there is no active write operation
                                 // and we can proceed with the update.  We will
                                 // set it back to false after the update is
                                 // complete to allow future writes to proceed.

                Task.Delay(100).Wait();
            }

            return false; // Timout reached, proceed with update anyway to avoid getting stuck indefinitely
        }
        #endregion

        #region Internal Methods
        /// <summary>
        /// Initializes or updates the log file for the current date, creating a new file if necessary and writing a
        /// session header.
        /// </summary>
        /// <remarks>Creates a new log file each day based on the current date. If the log file already
        /// exists, adds a blank line to separate sessions. Ensures the log file and related metadata are up to date
        /// before logging.</remarks>
        internal void LogSetup()
        {
            // Check if the log file date is the same as today's
            // date.  If it is, we can skip the rest of the setup since
            if (_activeFileDate.Date.Equals(Now.Date))
                return;
            else
            {
                // Update the log file date and name to reflect the new date.
                // We do this before creating the file to ensure that
                _activeFileDate.AdjustTime(Now.Date);

            }

            // Clear the existing FileInfo list and create new FileInfo objects
            // for the new log file.  We do this to ensure that the FileInfo is
            // always up to date and reflects the current log file, even if it
            // doesn't exist yet.
            _fileInfo.Clear();

            try
            {
                // Loop through all log levels to create a log file for each level.
                // This allows us to separate logs by level and makes it easier to
                // find specific types of log messages.  We do this in the LogSetup()
                // method to ensure that it is done automatically when the date
                // changes, without requiring manual intervention. 
                foreach (LogLevel logLevel in Enum.GetValues(typeof(LogLevel)))
                {
                    if (logLevel == LogLevel.All)
                        continue; // Skip the "All" log level since it is not an actual
                                  // log level to write to, but rather a combination of
                                  // all levels for configuration purposes.

                    // Get the integer value of the log level to use as an index for the FileInfo list.
                    int logLevelVal = (int)Math.Log((int)logLevel, 2);

                    // Use a date-based log file name to create a new log file each day.
                    // This helps to keep log files manageable and makes it easier to find logs for specific dates.
                    var logFile = $"{AppName}_{_activeFileDate.Year:00}{_activeFileDate.Month:00}{_activeFileDate.Day:00}_{logLevel}.{LogFileExt.Replace(".", "")}";

                    // Combine the log path and file name to get the full path to the log file.
                    var fullFile = Path.Combine(LogPath, logFile);

                    // Update the FileInfo for the new log file.  We do this before creating
                    // the file to ensure that the FileInfo is always up to date and reflects
                    // the current log file, even if it doesn't exist yet.
                    _fileInfo.Add(new FileInfo(fullFile));

                    // Check if the current log level is enabled for logging.  If it
                    // is not, we can skip and check next level.  We do this to avoid
                    // opening writers for log levels that are not enabled, which helps
                    // to reduce overhead and improve performance, especially if there
                    // are many log levels and only a few are enabled.
                    // NOTE: & is faster than HasFlag() and works with [Flags] enums,
                    // but it requires a bitwise check to ensure the exact flag is set.
                    if ((EnabledLogLevels & logLevel) != logLevel)
                        continue;

                    // Refresh the FileInfo to ensure it has the latest information about
                    // the file, especially if it was created or modified by another process.
                    _fileInfo[logLevelVal].Refresh();

                    // if the log file doesn't exist, create it.  If it does exist, add a
                    // blank line to separate logs from different runs on the same day.
                    if (!_fileInfo[logLevelVal].Exists)
                    {
                        // Create the file and immediately close it to release
                        // the handle.  We do this to ensure that the file is
                        // created and ready for writing before we start logging messages.
                        _fileInfo[logLevelVal].Create().Close();
                        // Refresh to update the file info after creation
                        _fileInfo[logLevelVal].Refresh();
                    }
                    else if (logLevel == LogLevel.Application && _fileInfo[logLevelVal].Exists)
                    {
                        // If the file already exists, we can add a blank line to
                        // separate logs from different runs on the same day.
                        _queuedMsgs.Enqueue((logLevel, $"{NewLines}"));
                    }
                }
            }
            catch
            {
                // If an exception occurs during log setup, we need to ensure that the logger is put
                // into a safe state to prevent further issues.  We set the shutting down and exit
                // write now flags to true to signal any ongoing write operations to stop immediately
                // and prevent further logging attempts, which helps to avoid potential data corruption
                // or other issues that could arise from trying to log when the logger is not properly
                // initialized or configured.
                _exitWriteNow.SetTrue(); // Set exit write now to true to signal any ongoing write operations to stop immediately
                throw; // Re-throw the exception to allow it to be handled by the caller or to crash the application if not handled
            }
        }
        /// <summary>
        /// Processes all queued log messages asynchronously, writing them to the log file in batches.
        /// </summary>
        /// <remarks>Ensures only one write operation occurs at a time. Automatically continues processing
        /// if new messages are enqueued during execution. Exceptions are silently handled to avoid disrupting
        /// logging.</remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        internal async Task ProcessQueueAsync()
        {
            // Returns if _isWriting has been changed from False to True.  If it was
            // already true, another write is in progress, so we can exit and let
            // that one handle the queue.
            if (!_isWriting.TrySetTrue() || _exitWriteNow)
                return;

            try
            {
                await Task.Run(() =>
                {
                    StreamWriter[] writer = new StreamWriter[Enum.GetValues(typeof(LogLevel)).Length];

                    try
                    {
                        // If there are messages in the queue, we need to set up the
                        // log file and open the writers before we can start writing.
                        // Only once, before the writing loop, to minimize file I/O
                        // and reduce contention.  We do this inside the Task.Run to
                        // ensure that it is done in the background and does not
                        // block the calling thread, especially if there are a large
                        // number of messages to write or if the log file needs to be
                        // created or updated.  We also check _exitWriteNow to allow
                        // for an early exit if shutdown has begun while we were waiting
                        // to start writing.
                        if (!_queuedMsgs.IsEmpty && !_exitWriteNow)
                        {
                            // Ensure log file is set up, datetime filename change,
                            // and _fileInfo refreshed, before we start writing.
                            LogSetup();

                            // Open a StreamWriter for each log level to write messages to the appropriate log file.
                            // We do this in a loop to handle all log levels and to ensure that we have a writer ready
                            // for each level that we need to write to, without having to check the log level of each
                            // message during the writing loop, which helps to reduce overhead and improve performance.
                            foreach (LogLevel logLevel in Enum.GetValues(typeof(LogLevel)))
                            {
                                if (logLevel == LogLevel.All)
                                    continue; // Skip the "All" log level since it is not an actual
                                              // log level to write to, but rather a combination of
                                              // all levels for configuration purposes.

                                // Check if the current log level is enabled for logging.  If it
                                // is not, we can skip and check next level.  We do this to avoid
                                // opening writers for log levels that are not enabled, which helps
                                // to reduce overhead and improve performance, especially if there
                                // are many log levels and only a few are enabled.
                                // NOTE: & is faster than HasFlag() and works with [Flags] enums,
                                // but it requires a bitwise check to ensure the exact flag is set.
                                if ((EnabledLogLevels & logLevel) != logLevel)
                                    continue;

                                // loading all levels, just in case log levels are added to the
                                // queue while we are writing, so we don't have to worry about loading
                                int logLevelVal = (int)Math.Log((int)logLevel, 2);
                                // We can also skip opening a writer for any log level that is
                                // not enabled for logging, since we won't be writing any messages for those levels.
                                writer[logLevelVal] = _fileInfo[logLevelVal].AppendText();
                            }

                            // The "Drain Loop": Keep going as long as there is work.
                            // This prevents the race condition where a message is 
                            // enqueued just as we are finishing the previous batch.
                            while (!_queuedMsgs.IsEmpty && !_exitWriteNow)
                            {
                                // Try to dequeue a message.  If successful, write it to the appropriate log file based on its log level.
                                while (_queuedMsgs.TryDequeue(out (LogLevel LogLvl, string Msg) logEntry) && !_exitWriteNow)
                                {
                                    int logLevelVal = (int)Math.Log((int)logEntry.LogLvl, 2);
                                    // Use writer.Write() instead of writer.WriteLine() since we already append a
                                    // newline to the message in public WriteLine() above to avoid double newlines when Write()
                                    // is called directly.  Enabled Loglevel has already been verified and the queue will not
                                    // contain messages with disabled levels, so we can write directly without checking the
                                    // log level here to improve performance.
                                    writer[logLevelVal].Write(logEntry.Msg);
                                }
                            }
                        }
                        // No need to Interlocked since we are already in a single-threaded context
                        // within the Task.Run, and this is only accessed here after successfully
                        // setting _isWriting to true, which ensures that only one thread can be
                        // executing this block at a time.  We reset the IO failure count after a
                        // successful write operation to allow for retries if future write operations
                        // encounter issues, while still providing a mechanism to track and handle
                        // repeated failures without prematurely giving up on logging.
                        _ioFailureCount = 0; // Reset IO failure count after a successful write operation
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message;
                        if (++_ioFailureCount >= _maxIoFailures)
                        {
                            // Set exit write now to true to signal any ongoing write operations to
                            // stop immediately if the maximum number of IO failures has been reached
                            // to prevent further issues and potential data corruption.  We can also
                            // log this event to a separate error log or take other appropriate action
                            // as needed.
                            _exitWriteNow.SetVal(true);
                        }
                    }
                    finally
                    {
                        // Ensure all writers are properly closed to
                        // release file handles and flush buffers.
                        foreach (var w in writer)
                            w?.Close();
                    }
                });
            }
            catch
            {
                // Silently catch exceptions in fire-and-forget logging
            }
            finally
            {
                // we are now done writing, so set _isWriting back
                // to false to allow the next write to proceed.
                _isWriting.SetFalse();
                // Check if any messages were queued while we were writing.
                // If so, start another write to process them.
                if (!_queuedMsgs.IsEmpty && !_exitWriteNow)
                    _ = ProcessQueueAsync();
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Ensures the specified log retention period is within the allowed minimum and maximum range.
        /// </summary>
        /// <param name="value">The requested log retention period.</param>
        /// <returns>A TimeSpan representing the validated log retention period.</returns>
        private TimeSpan CheckLogDays(TimeSpan value)
        {
            // If the value is zero or less than the minimum, return the minimum.
            // If it is greater than the maximum, return the maximum.
            // Otherwise, return the value as is.
            if (value == TimeSpan.Zero || value < _minKeepLogDays)
                return _minKeepLogDays;
            else if (value > _maxKeepLogDays)
                return _maxKeepLogDays;
            else
                return value;
        }
        #endregion
    }
}
