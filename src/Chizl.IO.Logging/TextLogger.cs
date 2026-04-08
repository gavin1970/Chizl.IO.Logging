using Chizl.IO.Logging.options;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chizl.IO.Logging
{
    /// <summary>
    /// Provides asynchronous, file-based logging with support for multiple log levels, log file rotation, and automatic
    /// retention management.
    /// </summary>
    /// <remarks>
    /// Creates daily log files for each log level and manages log retention by deleting old or empty
    /// files based on the configured retention period. Ensures thread-safe logging and automatic flushing on
    /// application shutdown.
    /// </remarks>
    public class TextLogger : LoggerOptions
    {
        #region Constructors
        private TextLogger() : base() { }
        /// <summary>
        /// Initializes a new instance of the TextLogger class with the specified application name and log file path.
        /// </summary>
        /// <param name="appName">The name of the application associated with the logger.</param>
        /// <param name="logPath">The directory path where log files are stored.</param>
        public TextLogger(string appName, string logPath) : this(appName, logPath, _defEnabledLogLevels, _defKeepLogDays) { }
        /// <summary>
        /// Initializes a new instance of the TextLogger class with the specified application name, log file path, and
        /// enabled log levels.
        /// </summary>
        /// <param name="appName">The name of the application associated with the logger.</param>
        /// <param name="logPath">The directory path where log files are stored.</param>
        /// <param name="enabledLogLevels">The log levels that are enabled for logging.<br/>
        /// Default: LogLevel.Application | LogLevel.Critical | LogLevel.Error | LogLevel.Warning | LogLevel.Information</param>
        public TextLogger(string appName, string logPath, LogLevel enabledLogLevels) : this(appName, logPath, enabledLogLevels, _defKeepLogDays) { }
        /// <summary>
        /// Initializes a new instance of the TextLogger class with the specified application name, log file path,
        /// enabled log levels, and log retention period.
        /// </summary>
        /// <param name="appName">The name of the application associated with the logger.</param>
        /// <param name="logPath">The directory path where log files are stored.</param>
        /// <param name="enabledLogLevels">The log levels that are enabled for logging.<br/>
        /// Default: LogLevel.Application | LogLevel.Critical | LogLevel.Error | LogLevel.Warning | LogLevel.Information</param>
        /// <param name="keepLogDays">The duration to retain log files before deletion.<br/>
        /// Default: 7 days. Minimum: 1 day. Maximum: 730 days (2 years). <br/>
        /// This helps to prevent excessive disk usage while ensuring logs are kept for a reasonable period for troubleshooting and analysis.</param>
        public TextLogger(string appName, string logPath, LogLevel enabledLogLevels, TimeSpan keepLogDays) : base(appName, logPath)
        {
            this.EnabledLogLevels = enabledLogLevels;
            this.KeepLogDays = keepLogDays; 
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets an empty instance of the TextLogger class.
        /// </summary>
        public static TextLogger Empty { get; } = new TextLogger();
        /// <summary>
        /// Determines whether there are any queued messages.
        /// </summary>
        /// <returns>true if there are queued messages; otherwise, false.</returns>
        public bool HasQueuedMsgs => !_queuedMsgs.IsEmpty;
        /// <summary>
        /// Indicates whether the logger is shutting down.
        /// </summary>
        /// <returns>true if the logger is shutting down; otherwise, false.</returns>
        public bool IsShuttingDown => _shuttingDown;
        /// <summary>
        /// Gets the number of messages currently queued.
        /// </summary>
        public int QueueCount => _queuedMsgs.Count;
        /// <summary>
        /// Enabled visibility of variables to replace. If false and vars exist, they will show up in the logs as is, not replaced.<br/>
        /// Default: false<br/>
        /// Available Vars:<br/>
        /// <code>
        ///     $[LL]$ = LogLevel written.
        /// 
        /// Example usage: 
        ///  WriteLine(LogLevel.Warning | LogLevel.Information, "This is a log message with log level: $[LL]$");
        /// </code>
        /// Warning log file results: 
        /// <code>
        /// "This is a log message with log level: Warning"
        /// </code>
        /// Information log file results:
        /// <code>
        /// "This is a log message with log level: Information".
        /// </code>
        /// NOTE: If true, this does add some additional processing overhead, so it is disabled <br/>
        /// by default to keep logging as efficient as possible.
        /// </summary>
        public bool UseVarReplacement { get; set; } = false;
        #endregion

        #region Public Methods
        /// <summary>
        /// WriteLine appends a Environment.NewLine to the end the message and passes it to Write() with all arguements unchanged, 
        /// to be enqueued for asynchronous logging.  We do this in WriteLine() instead of Write() to avoid 
        /// double newlines when Write() is called directly, since Write() is both are designed for simple string 
        /// messages.  All rules applied in <see cref="Write(LogLevel, string, bool)"/> are applied here.
        /// </summary>
        /// <param name="logLevel">The log level of the message to write.</param>
        /// <param name="msg">The message to write.</param>
        /// <param name="addDate">true to prepend the current date to the message; otherwise, false.</param>
        public void WriteLine(LogLevel logLevel, string msg, bool addDate = true)
        {
            // Append a newline to the message and call Write() to
            // enqueue it for writing.  We do this in WriteLine()
            // instead of Write() to avoid double newlines when
            // Write() is called directly.
            Write(logLevel, $"{msg}{NewLine}", addDate);
        }
        /// <summary>
        /// Enqueues a message for asynchronous logging, optionally prefixing it with the current time.<br/>
        /// Messages need to have param vars already formatted before being passed to this method, since it 
        /// is designed for simple string messages and does not perform any additional formatting or 
        /// processing on the message content.  We do this to keep the logging process as efficient as 
        /// possible, since any formatting or processing can be done by the caller before passing the 
        /// message to Write(), allowing Write() to focus solely on enqueuing the message for logging.  
        /// If the log level of the message is not enabled for logging, the message will be skipped and 
        /// not enqueued.  If the logger is in the process of shutting down, new messages will not be 
        /// accepted to avoid blocking the shutdown process.
        /// </summary>
        /// <param name="logLevel">The log level of the message to write.</param>
        /// <param name="msg">The message to log.</param>
        /// <param name="addDate">true to prefix the message with the current time; otherwise, false.</param>
        public void Write(LogLevel logLevel, string msg, bool addDate = true)
        {
            // If we are in the process of shutting down,
            // we should not accept new messages to avoid
            // potentially blocking the shutdown process.
            // We do this by checking the _shuttingDown
            // flag before enqueuing messages.
            if (_shuttingDown || IsEmpty || !Enable)
                return;

            // We can use bitwise operations to efficiently check which log levels
            // of the message are enabled for logging.
            // This works best for when calling Write with:
            // LogLevel.Warning | LogLevel.Information, as an example and Information
            // is not enabled.  Warning will be logged, but Information will not, and
            // we can determine this by using bitwise operations to efficiently check
            // which log levels of the message are enabled for logging without looping.
            // Example:
            //     EnabledLogLevels = LogLevel.Warning | LogLevel.Information;
            //     logLevel = LogLevel.Warning | LogLevel.Trace;
            //     var activeLevels = EnabledLogLevels & logLevel;
            // Result: 
            //     activeLevels = LogLevel.Warning
            var activeLevels = this.EnabledLogLevels & logLevel;

            // Check if the log level of the message is enabled
            // for logging.  If it is not, we can skip
            if (activeLevels == 0)
                return;

            // Ensure the logger is initialized before we attempt to log any messages.
            // We do this in Write() to ensure that the logger is initialized before
            // any messages are logged, and to allow for lazy initialization if the
            // logger is not used immediately at application startup.  Setup can be
            // changed after initialization, so we need to ensure that it is done before
            // each message is logged to handle any changes, but we only want to perform
            // the one-time initialization steps once, which is handled in InitializeLogger()
            // with the _initialize flag.
            if (!InitializeLogger())
                return;

            // Format the final message with the optional date prefix.  We do this here to ensure that the
            string finalMessage = $"{(addDate ? $"{MsgTime}: " : "")}{msg}";

            // Instead of evaluating the UseVarReplacement flag and performing
            // the replacement for each message, we can optimize this by checking
            // the flag once and then enqueuing the message with or without replacement
            // as needed.  This way, we are improving efficiency while still
            // providing the variable replacement feature when enabled.
            if (this.UseVarReplacement)
            {
                // Enqueue the message for each logLevel active and log level.
                // We do this to ensure that the message is logged to all and
                // it's much faster than looping through all log levels and
                // checking each one, since we can directly check the active
                // levels with bitwise operations and enqueue the message for
                // each active level without needing to loop through all
                // possible log levels.  LogLevel.All will be handled correctly
                // since it is a combination of all log levels, so if LogLevel.All
                // is enabled, the message will be enqueued for all log levels files.
                if ((activeLevels & LogLevel.Application) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Application));

                if ((activeLevels & LogLevel.Critical) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Critical));

                if ((activeLevels & LogLevel.Error) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Error));

                if ((activeLevels & LogLevel.Warning) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Warning));

                if ((activeLevels & LogLevel.Information) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Information));

                if ((activeLevels & LogLevel.Debug) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Debug));

                if ((activeLevels & LogLevel.Trace) != 0)
                    _queuedMsgs.Enqueue(CheckVarss(finalMessage, LogLevel.Trace));
            }
            else
            {
                // Enqueue the message for each logLevel active and log level.
                // We do this to ensure that the message is logged to all and
                // it's much faster than looping through all log levels and
                // checking each one, since we can directly check the active
                // levels with bitwise operations and enqueue the message for
                // each active level without needing to loop through all
                // possible log levels.  LogLevel.All will be handled correctly
                // since it is a combination of all log levels, so if LogLevel.All
                // is enabled, the message will be enqueued for all log levels files.
                if ((activeLevels & LogLevel.Application) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Application, finalMessage));

                if ((activeLevels & LogLevel.Critical) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Critical, finalMessage));

                if ((activeLevels & LogLevel.Error) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Error, finalMessage));

                if ((activeLevels & LogLevel.Warning) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Warning, finalMessage));

                if ((activeLevels & LogLevel.Information) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Information, finalMessage));

                if ((activeLevels & LogLevel.Debug) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Debug, finalMessage));

                if ((activeLevels & LogLevel.Trace) != 0)
                    _queuedMsgs.Enqueue((LogLevel.Trace, finalMessage));
            }

            // Enqueue the message to be written.  We do this in a thread-safe
            // way using ConcurrentQueue, which allows multiple threads to
            // write to the log without blocking each other.
            //_queuedMsgs.Enqueue((logLevel, finalMessage));
            _ = ProcessQueueAsync(); // Fire-and-forget with discard operator
        }
        /// <summary>
        /// Asynchronously processes and clears all pending items in the queue.
        /// </summary>
        public async void FlushAsync()
        {
            // exit, if none in queue, to avoid unnecessary processing and waiting.
            if (_queuedMsgs.IsEmpty) return;
            // Process the queue to write all pending messages to disk.
            // We do this in a separate task to avoid blocking the calling
            // thread, and we also include a short delay before processing
            // to allow any ongoing write operations to complete, which can
            // help ensure that we can flush the queue more efficiently
            // without getting blocked by ongoing writes.
            await Task.Delay(1).ContinueWith(_ => ProcessQueueAsync());
        }
        /// <summary>
        /// Flushes all pending messages in the queue to disk, honoring the provided cancellation token.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        public void Flush(CancellationToken cancellationToken)
        {
            // exit, if none in queue, to avoid unnecessary processing and waiting.
            if (_queuedMsgs.IsEmpty) return;

            // Just in case, wait a moment to allow any ongoing write
            // operations to complete before we start processing the queue.
            Task.Delay(100).Wait(CancellationToken.None);
            // Process the queue to write all pending messages to disk.
            // We do this in a separate task to avoid blocking the calling
            // thread, and we also include a short delay before processing
            // to allow any ongoing write operations to complete, which can
            // help ensure that we can flush the queue more efficiently
            // without getting blocked by ongoing writes.
            while (!_queuedMsgs.IsEmpty && !cancellationToken.IsCancellationRequested)
                ProcessQueueAsync().Wait(cancellationToken);
        }
        /// <summary>
        /// Blocks the calling thread until all queued messages are written. 
        /// Call this before the application exits.<br/>
        /// <br/>
        /// StopAndFlush is automatically called internally, when the parent 
        /// application exits, to ensure that all messages are flushed and 
        /// old logs are cleaned up before shutdown.  Can be called twice safely, 
        /// but subsequent calls after the first will have no effect since the 
        /// logger will already be in the process of shutting down and will not 
        /// accept new messages or perform additional flushing.  A new instance 
        /// of TextLogger will need to be created to log messages again after 
        /// shutdown is complete.<br/>
        /// <br/>
        /// We do this in the ProcessExit event handler to ensure that it is 
        /// called automatically when the application is shutting down, without 
        /// requiring manual intervention.  This method will wait for any ongoing 
        /// write operations to complete and for the queue to be fully flushed 
        /// before proceeding with log cleanup and finalization.  It also includes 
        /// safety measures to prevent infinite waiting in case of issues with 
        /// the writing process, and it ensures that no new messages are accepted 
        /// once shutdown has begun.  After flushing the queue, it performs log 
        /// cleanup to remove old and empty log files based on the configured 
        /// retention period.<br/>
        /// <br/>
        /// NOTE: This method will write 2 additional messages to the log to indicate 
        /// that shutdown has begun and that the flushing process is starting.  
        /// These messages will be the last entries in the log file and can be used 
        /// as markers to indicate when shutdown was initiated and when the flushing 
        /// process started, which can be helpful for troubleshooting and analysis 
        /// of log files during shutdown.
        /// </summary>
        public void StopAndFlush()
        {
            // Unregister the ProcessExit event handler to prevent it from
            // being called multiple times if StopAndFlush is called manually.
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            // Log the start of the shutdown process.  We do this to provide a
            // clear marker in the logs that the application is shutting down,
            // and to help with troubleshooting and analysis of log files during shutdown.
            // This should be the very last message in the queue and logs to
            // indicate that shutdown has begun.
            WriteLine(LogLevel.Application, $"---===[ {AppName} Shutting Down ] ===---");
            WriteLine(LogLevel.Application, $"---===[ Stopping and flushing logs from Queue ] ===---");

            // Create a cancellation token with a timeout to prevent infinite waiting during the flush process.
            // We do this to ensure that if there are any issues with the writing process that cause it to
            // take an excessively long time, we can cancel the flush operation and proceed with shutdown
            // without getting stuck waiting indefinitely.
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Flush the queue to ensure all messages are written before we proceed with shutdown.
            // We do this to ensure that all messages that were enqueued before shutdown are written
            // to disk, and we also include a timeout to prevent infinite waiting in case of issues
            // with the writing process.  If the flush operation takes longer than the specified
            // timeout, it will be cancelled to allow the shutdown process to proceed without getting
            // stuck waiting indefinitely.
            Flush(cancellationTokenSource.Token);

            // Set the _shuttingDown flag to prevent new messages from being enqueued
            // while we are flushing.  We do this to ensure that we can flush all
            // existing messages without worrying about new ones being added during
            // the process.
            if (!_shuttingDown.TrySetTrue())
                return;

            // Store the initial count of queued messages to track progress.
            var cnt = _queuedMsgs.Count;
            // Loop counter to track how many times we've checked for progress without seeing any changes.
            // We use this to determine when to trigger another write attempt if we are stuck waiting.
            var loops = 0;
            // Max number of retries before exiting the wait loop to prevent infinite waiting
            // in case of an issue with the writing process.  We do this as a safety measure
            // to ensure that we don't get stuck waiting indefinitely if there is a problem
            // with the writing process that prevents it from completing.
            var maxLoopResets = 10;
#if DEBUG
            // just showing if queue is backed up, watch it get cleaned, but only in debug.
            var initialCnt = cnt;
            if(cnt > 0)
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: #1 Queue Count: {cnt}");
#endif
            // Just in case, wait for any ongoing write operations to complete and for
            // the queue to be fully flushed.  We do this in a loop with a delay to avoid
            // busy-waiting, and we also check for progress to prevent infinite waiting
            // in case of an issue with the writing process.
            while (_isWriting || !_queuedMsgs.IsEmpty)
            {
                // Wait for a short period before checking again to avoid busy-waiting.
                Task.Delay(100).Wait();
                // Validate progress with queue count since the last check.
                // If it hasn't, we can increment the loop counter.
                if (cnt == _queuedMsgs.Count)
                    loops++;

                // If the loop counter reaches a certain threshold, we can trigger the writing
                // process again in case it was missed or stalled.  This is a safety measure to
                // ensure that we don't get stuck waiting indefinitely if there is an issue
                // with the writing process.
                if (loops >= 10)
                {
                    _ = ProcessQueueAsync(); // Fire-and-forget with discard operator
                    // Refresh last known count for next check.
                    cnt = _queuedMsgs.Count;

                    // Reset loop counter after triggering another write attempt.
                    loops = 0;

                    // Decrement the maxLoopResets counter and break the
                    // loop if it reaches zero to prevent infinite waiting.
                    if (--maxLoopResets <= 0)
                        break;
                }
            }

#if DEBUG
            if (initialCnt > 0)
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: #2 Queue Count: {_queuedMsgs.Count}");
#endif

            try
            {
                // Set the _exitWriteNow flag to true to signal any ongoing or future write
                // operations to exit immediately.  We do this to ensure that we can exit
                // the queue loop and proceed with log cleanup without waiting for any new
                // messages to be processed, since we are shutting down and want to
                // finalize as quickly as possible.  We have done all the waiting above that we
                // can to allow existing messages to be written, so at this point we want
                // to exit any remaining write operations and proceed with cleanup.
                _exitWriteNow.SetTrue();

                // Clear any remaining messages in the queue since we are shutting down and
                // want to finalize as quickly as possible.  We do this after setting
                // _exitWriteNow to ensure that any ongoing write operations will exit
                // immediately and not attempt to process these messages, since we are
                // shutting down and want to finalize as quickly as possible.
                while (_queuedMsgs.TryDequeue(out (LogLevel, string) _));

#if DEBUG
                if (initialCnt > 0)
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.ffff}: #3 Queue Count: {_queuedMsgs.Count}");
#endif

                // After queue is flushed, we can perform log cleanup to remove old and empty log files.
                // We do this here to ensure that it is done after all messages are written and before
                // the application exits, without requiring manual intervention.
                DirectoryInfo di = new DirectoryInfo(LogPath);
                foreach (FileInfo file in di.GetFiles().Where(s => 
                                           s.Name.StartsWith(AppName, StringComparison.CurrentCultureIgnoreCase) &&
                                           s.Extension.Equals($".{LogFileExt}", StringComparison.CurrentCultureIgnoreCase) &&
                                          (s.CreationTimeUtc < DateTime.UtcNow.AddDays(-KeepLogDays.TotalDays) || s.Length <= _tmFormat.Length)))
                {
                    // We silently catch exceptions here to avoid disrupting the logging process,
                    // especially during shutdown.  If a file cannot be deleted (e.g., due to
                    // permissions or being in use), we simply skip it and move on to the next one.
                    file.Delete();
                }
            }
            catch { /* Silently catch exceptions in log clearing */ }
            finally { ResetAllVars(); }
        }
        #endregion

        #region Support Methods
        /// <summary>
        /// Replaces the log level token in the specified message with the string representation of the provided log
        /// level.
        /// </summary>
        /// <param name="msg">The message containing the log level token to be replaced.</param>
        /// <param name="logLevel">The log level to insert into the message.</param>
        /// <returns>A tuple containing the log level and the formatted message.</returns>
        private static (LogLevel, string) CheckVarss(string msg, LogLevel logLevel) =>
                (logLevel, msg.Replace(TokenVars.LogLevel, logLevel.ToString()));
        /// <summary>
        /// Initializes the logger by ensuring the log directory exists, setting up the log file for the current date, and writing a session header.
        /// </summary>
        private bool InitializeLogger()
        {
            // Perform one-time initialization if it hasn't been
            // done yet.  We do this in Write() to ensure that the
            // logger is initialized before any messages are logged,
            // and to allow for lazy initialization if the logger
            // is not used immediately at application startup.
            if (!_initialize.TrySetTrue())
                return true;

            // Ensure the log directory exists.  We do this in
            // the initialization step to ensure that the log
            // directory is created before we attempt to write
            // any log files, and to avoid checking for the directory's
            // existence repeatedly.  If an Exception occurs here,
            // it will be caught in the fire-and-forget logging
            // and silently handled to avoid disrupting the application.
            try
            {
                if (!Directory.Exists(LogPath))
                    Directory.CreateDirectory(LogPath);
            }
            catch
            {
                // ProcessQueueAsync can be called via shutdown, on startup failures.
                // _queueMsgs can be added to before we set _shuttingDown, so
                // we need to ensure that any attempt to write or process
                // the queue will exit immediately since we can't log without
                // a log directory.
                _exitWriteNow.SetTrue();
                // If we can't create the log directory, we can't log, so we exit early.
                return false;
            }

            // Register cleanup method to run on process exit
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            // Perform log setup to initialize the log file for the current
            // date and write the session header.  We do this in the initialization
            // step to ensure that the log file
            LogSetup();

            // Log the start of the logging session with a header.
            // We do this in the initialization step to ensure that it is
            // logged at the beginning of the log file, even if the application
            // is restarted multiple times on the same day.
            _queuedMsgs.Enqueue((LogLevel.Application, $"{MsgTime}: ---===[ {AppName} Started ] ===---{NewLines}"));
            return true;
        }
        /// <summary>
        /// Resets all internal state variables to their default values.
        /// </summary>
        private void ResetAllVars()
        {
            _initialize.SetFalse();
            _isWriting.SetFalse();
            _shuttingDown.SetFalse();
            _exitWriteNow.SetFalse();
            _activeFileDate = DateTime.MinValue.Date;
        }
        /// <summary>
        /// Handles application process exit to finalize shutdown and perform log cleanup.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void OnProcessExit(object sender, EventArgs e)
        {
            // OnProcessExit has been called. Shutting down the logger.
            // We do this to ensure that we can finalize logging and perform cleanup
            WriteLine(LogLevel.Application, $"OnProcessExit() called...");

            // Call StopAndFlush to ensure all queued messages are written
            // and old logs are cleaned up before the application exits.
            // We do this in the ProcessExit event handler to ensure that
            // it is called automatically when the application is shutting
            // down, without requiring manual intervention.
            StopAndFlush();
        }
        #endregion
    }
}
