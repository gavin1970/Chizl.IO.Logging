using System;
using System.Runtime.InteropServices;

// Copyright (c) 2026 Gavin W. Landon (chizl.com)
// Licensed under the MIT License. See LICENSE file http://www.chizl.com/LICENSE.txt for full license information.
// SPDX-License-Identifier: MIT
namespace Chizl.ConsoleSupport
{
    /// <summary>
    /// Provides methods for enabling Virtual Terminal processing in the Windows console.
    /// </summary>
    public static class VT
    {
        const int STD_OUTPUT_HANDLE = -11;
        const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        /// <summary>
        /// Enables virtual terminal processing for the Windows console to support ANSI escape codes.
        /// </summary>
        /// <remarks>Checks if console output is not redirected and the application is running in a Windows
        /// console before enabling VT processing.</remarks>
        public static void Enable()
        {
            // if Console.IsOutputRedirected is true, then the console output is being
            // redirected to a file or pipe, and we should not attempt to enable VT processing
            if (!Console.IsOutputRedirected && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // How to check if Console app or Form app is running in a Windows console?
                // One way is to check if the standard output handle is valid and if it is
                // associated with a console. This can be done using the GetStdHandle and
                // GetConsoleMode functions from the Windows API.
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);

                if (GetConsoleMode(handle, out uint mode))
                {
                    // Console appears to be running in a Windows console, then attempt to enable VT processing.
                    // This is necessary for ANSI escape codes to work properly in the console.
                    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                }
            }
        }
    }
}