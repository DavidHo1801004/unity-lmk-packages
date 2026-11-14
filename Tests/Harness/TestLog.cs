using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using UnityEngine;
using UnityEngine.TestTools;

namespace LMK.Editor.AbilitySystem.Tests
{
    /// <summary>
    /// Records every log message raised while it is alive, and declares the ones a test expects. <br/>
    /// An unexpected error still fails the test, as the test framework does by default.
    /// </summary>
    internal sealed class TestLog : IDisposable
    {
        private readonly List<(LogType type, string message)> entries = new();

        public IReadOnlyList<(LogType type, string message)> Entries => entries;

        /// <summary>
        /// Errors, exceptions and failed asserts logged so far.
        /// </summary>
        public int ErrorCount => Count(LogType.Error) + Count(LogType.Exception) + Count(LogType.Assert);

        public int WarningCount => Count(LogType.Warning);



        public TestLog()
        {
            Application.logMessageReceived += OnLogMessageReceived;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
        }



        /// <summary>
        /// Declares that a message of <paramref name="_Type"/> containing <paramref name="_Text"/> will be logged.
        /// </summary>
        /// <param name="_Text"> Plain text, matched case-insensitively anywhere in the message. </param>
        public void Expect(LogType _Type, string _Text)
        {
            // Inline flag: LogAssert keeps only the pattern string, so RegexOptions would be dropped.
            LogAssert.Expect(_Type, new Regex("(?i)" + Regex.Escape(_Text)));
        }

        public int Count(LogType _Type)
        {
            int count = 0;
            foreach (var entry in entries)
            {
                if (entry.type == _Type) count++;
            }
            return count;
        }

        private void OnLogMessageReceived(string _Message, string _StackTrace, LogType _Type)
        {
            entries.Add((_Type, _Message));
        }
    }
}
