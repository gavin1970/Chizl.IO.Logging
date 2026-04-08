using System;

namespace Chizl.IO.Logging
{
    /// <summary>
    /// Specifies log levels for categorizing and filtering log messages.
    /// </summary>
    /// <remarks>Supports bitwise combination of its member values.</remarks>
    [Flags]
    public enum LogLevel
    {
        /// <summary>
        /// Indicates the application log level.<br/>
        /// 0000001 (decimal 1)
        /// </summary>
        Application = 1 << 0,
        /// <summary>
        /// Indicates the critical log level.<br/>
        /// 0000010 (decimal 2)
        /// </summary>
        Critical = 1 << 1,
        /// <summary>
        /// Indicates an error condition.<br/>
        /// 0000100 (decimal 4)
        /// </summary>
        Error = 1 << 2,
        /// <summary>
        /// Indicates an warning condition.<br/>
        /// 0001000 (decimal 8)
        /// </summary>
        Warning = 1 << 3,
        /// <summary>
        /// Indicates an Information condition.<br/>
        /// 0010000 (decimal 16)
        /// </summary>
        Information = 1 << 4,
        /// <summary>
        /// Indicates an debug condition.<br/>
        /// 0100000 (decimal 32)
        /// </summary>
        Debug = 1 << 5,
        /// <summary>
        /// Indicates an trace condition.<br/>
        /// 1000000 (decimal 64)
        /// </summary>
        Trace = 1 << 6,
        /// <summary>
        /// Indicates an all conditions.<br/>
        /// 1111111 (decimal 127)
        /// </summary>
        All = Application | Critical | Error | 
              Warning | Information | Debug | Trace
    }
}
