using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// The checks run once, when the tests run, not when a test explorer lists them.
[assembly: TestDataSourceDiscovery(TestDataSourceDiscoveryOption.DuringExecution)]

namespace AlmostFastDash.Tests
{
    /// <summary>
    /// The app's end-to-end checks, one test each. They live in the app's Checks build because they drive the
    /// designer from inside (see src/AlmostFastDash/Checks). This starts that build in an empty folder, as a
    /// first run would, with --designer-selftest, and turns each line of its report into a test.
    /// </summary>
    [TestClass]
    public class EndToEndChecks
    {
        private static readonly TimeSpan Longest = TimeSpan.FromMinutes(10);
        private static readonly Lazy<string[]> Report = new Lazy<string[]>(RunChecks);

        /// <summary>The first line of the report: "ALL n CHECKS PASSED", "n OF m CHECKS FAILED" or "CRASHED".</summary>
        [TestMethod]
        public void EveryCheckPasses() => Assert.IsTrue(Report.Value[0].StartsWith("ALL "), string.Join(Environment.NewLine, Report.Value.Take(20)));

        [TestMethod]
        [DynamicData(nameof(Checks), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(CheckName))]
        public void Check(int number, string line) => Assert.IsTrue(line.StartsWith("PASS"), line);

        /// <summary>One row per check: its number and its report line ("PASS  name" or "FAIL  name  (detail)").</summary>
        public static IEnumerable<object[]> Checks()
        {
            string[] report = Report.Value;
            // A crash leaves a stack trace instead of check lines; that's one failure, not dozens.
            if (report[0] == "CRASHED") return new[] { new object[] { 1, "FAIL  the checks crashed: " + string.Join(" ", report.Skip(1).Take(3)) } };
            return report.Skip(1).Where(line => !line.StartsWith("INFO")).Select((line, index) => new object[] { index + 1, line });
        }

        public static string CheckName(MethodInfo method, object[] row) => "#" + row[0] + " " + ((string)row[1]).Substring(6).Split(new[] { "  (" }, StringSplitOptions.None)[0];

        private static string[] RunChecks()
        {
            string built = Path.Combine(Path.GetDirectoryName(typeof(EndToEndChecks).Assembly.Location), "AlmostFastDash.exe");
            string folder = Path.Combine(Path.GetTempPath(), "afd-checks-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "AlmostFastDash.exe");
            File.Copy(built, exe);
            string report = Path.Combine(folder, "report.txt");
            using (Process run = Process.Start(new ProcessStartInfo(exe, "--designer-selftest \"" + report + "\"") { WorkingDirectory = folder, UseShellExecute = false }))
            {
                if (!run.WaitForExit((int)Longest.TotalMilliseconds))
                {
                    run.Kill();
                    return new[] { "CRASHED", "The checks took longer than " + Longest.TotalMinutes + " minutes." };
                }
            }
            return File.Exists(report) ? File.ReadAllLines(report) : new[] { "CRASHED", "The checks wrote no report." };
        }
    }
}
