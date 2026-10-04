using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Web_Page_Screensaver
{
    /// <summary>
    /// Makes the .scr self-contained by extracting the native WebView2Loader.dll embedded in this
    /// assembly. Windows can only load a DLL from a file, so the loader matching the process
    /// architecture is written to disk once and WebView2 is pointed at it.
    /// </summary>
    public static class EmbeddedWebView2Loader
    {
        private const string LoaderFileName = "WebView2Loader.dll";

        /// <summary>
        /// Must be called before any other WebView2 API, because the loader can only be
        /// redirected before it has been loaded.
        /// </summary>
        public static void Configure()
        {
            string arch;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86: arch = "x86"; break;
                case Architecture.X64: arch = "x64"; break;
                case Architecture.Arm64: arch = "arm64"; break;
                default: return; // No embedded loader; leave WebView2's default search in place
            }

            // The folder is versioned, so an existing file never needs replacing. It could be
            // locked by another running instance that has already loaded it.
            string version = typeof(CoreWebView2Environment).Assembly.GetName().Version.ToString();
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Airframe-Screensaver", "WebView2Loader", version, arch);
            string path = Path.Combine(folder, LoaderFileName);

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);

                // Write to a unique temporary file and move it into place, so a concurrent
                // instance never sees a partially written DLL.
                string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("WebView2Loader." + arch + ".dll"))
                using (var file = File.Create(tempPath))
                {
                    resource.CopyTo(file);
                }

                try
                {
                    File.Move(tempPath, path);
                }
                catch (IOException)
                {
                    // Another instance won the race and the file is already in place
                    File.Delete(tempPath);
                }
            }

            CoreWebView2Environment.SetLoaderDllFolderPath(folder);
        }
    }
}
