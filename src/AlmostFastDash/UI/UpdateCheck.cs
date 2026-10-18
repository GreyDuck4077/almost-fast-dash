using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AlmostFastDash.UI
{
    /// <summary>
    /// Asks GitHub for the project's latest release, only when the user has turned the check on.
    /// The one request carries nothing but the app's name as its user agent.
    /// </summary>
    internal static class UpdateCheck
    {
        public const string Releases = "https://github.com/Sota4077/almost-fast-dash/releases";
        private const string Latest = "https://api.github.com/repos/Sota4077/almost-fast-dash/releases/latest";

        /// <summary>The newer release's version and page, or null when this one is current or the check fails.</summary>
        public static Task<Tuple<Version, string>> NewerAsync(Version current) => Task.Run(() =>
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(Latest);
                request.UserAgent = "AlmostFastDash";
                request.Accept = "application/vnd.github+json";
                request.Timeout = 8000;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
                {
                    var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    string tag = (release.TryGetValue("tag_name", out object t) ? t as string : null) ?? "";
                    string page = (release.TryGetValue("html_url", out object u) ? u as string : null) ?? Releases;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version latest)) return null;
                    var mine = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
                    var theirs = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
                    return theirs > mine ? Tuple.Create(latest, page) : null;
                }
            }
            catch
            {
                return null;
            }
        });
    }
}
