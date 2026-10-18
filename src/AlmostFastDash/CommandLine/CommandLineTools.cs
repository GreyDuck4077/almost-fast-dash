using System;
using System.IO;
using System.Linq;

namespace AlmostFastDash.CommandLine
{
    /// <summary>
    /// The developer and support tools on the command line: renders, audits, replays, live checks and hardware tests.
    /// docs/development.md lists each one.
    /// </summary>
    internal static class CommandLineTools
    {
        /// <summary>Runs the tool the command line asks for and returns its exit code, or null when it asks for none.</summary>
        internal static int? Run(string[] args)
        {
            string libraryDirectory = ParseOption(args, "--render-library");
            if (libraryDirectory != null) return LibraryAudit.Run(libraryDirectory);
            string templateDirectory = ParseOption(args, "--render-templates");
            if (templateDirectory != null) return RenderCommands.RenderTemplates(templateDirectory);
            string dashboardDirectory = ParseOption(args, "--render-dashboards");
            if (dashboardDirectory != null) return RenderCommands.RenderDashboards(dashboardDirectory);
            string flagDirectory = ParseOption(args, "--render-flags");
            if (flagDirectory != null) return RenderCommands.RenderFlags(flagDirectory);
            string auditReport = ParseOption(args, "--audit-dashboards");
            if (auditReport != null) return RenderCommands.AuditDashboards(auditReport);
#if CHECKS
            string selfTestReport = ParseOption(args, "--designer-selftest");
            if (selfTestReport != null) return Checks.CheckRunner.Run(selfTestReport);
            string radarReport = ParseOption(args, "--radar-scenarios");
            if (radarReport != null)
            {
                File.WriteAllText(radarReport, Checks.RadarScenarios.Report(Checks.RadarScenarios.Run()));
                return 0;
            }
#endif
            string soakPort = ParseOption(args, "--led-soak");
            if (soakPort != null) return LedSoak.Run(soakPort, int.TryParse(ParseOption(args, soakPort), out int soakLeds) ? soakLeds : 16,
                args.Contains("--fast"));
            string lightReport = ParseOption(args, "--light-frames");
            if (lightReport != null) return LightFrames.Run(lightReport);
            string recording = ParseOption(args, "--replay-ibt");
            if (recording != null) return RecordingReplay.Run(recording, ParseOption(args, recording) ?? "replay.txt");
            string controllerReport = ParseOption(args, "--controllers");
            if (controllerReport != null)
            {
                // Lists game controllers Windows sees, and the buttons held on each, for checking page buttons.
                var lines = WheelButtons.Read().Select(d => d.Key + ": buttons held 0x" + d.Value.ToString("X8"));
                File.WriteAllLines(controllerReport, lines.DefaultIfEmpty("No game controllers found."));
                return 0;
            }
            string profileReport = ParseOption(args, "--profile-dashboards");
            if (profileReport != null) return DashboardProfiler.Run(profileReport);
            // A test pattern on the wheel screen through the dashboard's own sending code, with an optional report.
            if (args.Contains("--screen-test")) return ScreenTest.Run(ParseOption(args, "--screen-test"));
            string liveReport = ParseOption(args, "--live-check");
            if (liveReport != null) return LiveCheck.Run(liveReport,
                int.TryParse(ParseOption(args, liveReport), out int liveSeconds) ? liveSeconds : 20);
            string uiScenario = ParseOption(args, "--ui-snapshot");
            if (uiScenario != null) return UiSnapshot.Run(uiScenario, ParseOption(args, uiScenario) ?? "ui.png");
            return null;
        }

        /// <summary>The value after an option, or null when the option isn't there.</summary>
        private static string ParseOption(string[] args, string option)
        {
            int optionIndex = Array.IndexOf(args, option);
            return optionIndex >= 0 && optionIndex + 1 < args.Length
                ? args[optionIndex + 1]
                : null;
        }
    }
}
