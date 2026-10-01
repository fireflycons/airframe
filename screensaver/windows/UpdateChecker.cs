using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Web_Page_Screensaver
{
    /// <summary>
    /// Model representing GitHub release information
    /// </summary>
    public class GitHubReleaseInfo
    {
        public bool HasUpdate { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
        public string ReleaseNotes { get; set; }
    }

    /// <summary>
    /// Helper that queries the GitHub Releases API asynchronously to check for a newer version
    /// </summary>
    public static class UpdateChecker
    {
        private const string REPO_OWNER = "muro-dot";
        private const string REPO_NAME = "Webview2_WebPage_Screensaver";
        private const string CURRENT_VERSION = "1.0.6";

        /// <summary>
        /// Queries the latest GitHub release information asynchronously in the background.
        /// On failure it safely returns HasUpdate = false, with no UI delay or error pop-up.
        /// </summary>
        public static async Task<GitHubReleaseInfo> CheckForUpdateAsync()
        {
            return await Task.Run(() =>
            {
                var result = new GitHubReleaseInfo { HasUpdate = false };
                try
                {
                    // Force TLS 1.2+ (required by the GitHub API)
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                    string apiUrl = $"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/latest";
                    var request = (HttpWebRequest)WebRequest.Create(apiUrl);
                    request.UserAgent = "Webview2-Screensaver-UpdateChecker";
                    request.Timeout = 4000; // 4-second timeout
                    request.Method = "GET";

                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        var serializer = new JavaScriptSerializer();
                        var dict = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);

                        if (dict != null && dict.ContainsKey("tag_name"))
                        {
                            string tagName = dict["tag_name"].ToString().TrimStart('v', 'V');
                            string htmlUrl = dict.ContainsKey("html_url") ? dict["html_url"].ToString() : "";

                            result.LatestVersion = tagName;
                            result.ReleaseUrl = htmlUrl;

                            if (IsNewerVersion(tagName, CURRENT_VERSION))
                            {
                                result.HasUpdate = true;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore and exit safely when offline or rate-limited by the API
                    result.HasUpdate = false;
                }
                return result;
            });
        }

        private static bool IsNewerVersion(string latestStr, string currentStr)
        {
            try
            {
                if (Version.TryParse(latestStr, out Version latest) &&
                    Version.TryParse(currentStr, out Version current))
                {
                    return latest > current;
                }
            }
            catch { }
            return false;
        }
    }
}
