// Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using ScrcpyDex.Core.Platform;

namespace ScrcpyDex.Core.IO
{
    /// <summary>
    /// Dynamically resolves filesystem paths for scrcpy binaries, adb, and scrcpydex-server.jar.
    /// Windows resolution is application-local for portable deployments. Other platforms retain
    /// repository, PATH, and standard installation-location discovery.
    /// </summary>
    public static class ScrcpyPathResolver
    {
        private static readonly string[] ExecutableExtensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", "" }
            : new[] { "", ".exe" };

        /// <summary>
        /// Checks whether <paramref name="candidatePath"/> refers to the currently running application binary
        /// or known ScrcpyDeX host executables, preventing recursive self-invocation loops.
        /// </summary>
        public static bool IsSelfExecutable(string? candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            try
            {
                string fullCandidate = Path.GetFullPath(candidatePath);
                string? processPath = Environment.ProcessPath;
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                if (!string.IsNullOrWhiteSpace(processPath) &&
                    string.Equals(fullCandidate, Path.GetFullPath(processPath), comparison))
                {
                    return true;
                }

                string fileName = Path.GetFileName(fullCandidate);
                return fileName.Equals("ScrcpyDex.App.exe", StringComparison.OrdinalIgnoreCase) ||
                       fileName.Equals("ScrcpyDex.App", StringComparison.OrdinalIgnoreCase) ||
                       fileName.Equals("scrcpy-dex.exe", StringComparison.OrdinalIgnoreCase) ||
                       fileName.Equals("scrcpy-dex", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the file name matches the expected scrcpy binary name ('scrcpy' or 'scrcpy.exe').
        /// </summary>
        public static bool IsExpectedScrcpyExecutableName(string? candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            string fileName = Path.GetFileName(candidatePath);
            return fileName.Equals("scrcpy.exe", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("scrcpy", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the file name matches the expected adb binary name ('adb' or 'adb.exe').
        /// </summary>
        public static bool IsExpectedAdbExecutableName(string? candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            string fileName = Path.GetFileName(candidatePath);
            return fileName.Equals("adb.exe", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals("adb", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeConfiguredPath(string candidatePath)
        {
            if (OperatingSystem.IsWindows() && !Path.IsPathRooted(candidatePath))
            {
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, candidatePath));
            }

            return Path.GetFullPath(candidatePath);
        }

        private static bool IsInsidePortableWindowsRoot(string candidatePath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            string fullCandidate = NormalizeConfiguredPath(candidatePath);
            string relative = Path.GetRelativePath(baseDirectory, fullCandidate);

            return !Path.IsPathRooted(relative) &&
                   !string.Equals(relative, "..", StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }

        /// <summary>
        /// Validates that a user-configured scrcpy custom path is a directory or an expected scrcpy binary
        /// (and never the ScrcpyDeX host process itself).
        /// </summary>
        public static bool IsValidCustomScrcpyPath(string? customPath)
        {
            if (string.IsNullOrWhiteSpace(customPath))
            {
                return false;
            }

            try
            {
                string normalizedPath = NormalizeConfiguredPath(customPath);
                if (IsSelfExecutable(normalizedPath) || !IsInsidePortableWindowsRoot(normalizedPath))
                {
                    return false;
                }

                if (Directory.Exists(normalizedPath))
                {
                    string scrcpyBin = OperatingSystem.IsWindows() ? "scrcpy.exe" : "scrcpy";
                    return File.Exists(Path.Combine(normalizedPath, scrcpyBin));
                }

                return File.Exists(normalizedPath) && IsExpectedScrcpyExecutableName(normalizedPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that a user-configured adb custom path is a directory or an expected adb binary
        /// (and never the ScrcpyDeX host process itself).
        /// </summary>
        public static bool IsValidCustomAdbPath(string? customPath)
        {
            if (string.IsNullOrWhiteSpace(customPath))
            {
                return false;
            }

            try
            {
                string normalizedPath = NormalizeConfiguredPath(customPath);
                if (IsSelfExecutable(normalizedPath) || !IsInsidePortableWindowsRoot(normalizedPath))
                {
                    return false;
                }

                if (Directory.Exists(normalizedPath))
                {
                    string adbBin = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
                    return File.Exists(Path.Combine(normalizedPath, adbBin));
                }

                return File.Exists(normalizedPath) && IsExpectedAdbExecutableName(normalizedPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to resolve the absolute path to scrcpy.exe.
        /// Returns true if an existing binary is located, otherwise false.
        /// </summary>
        public static bool TryResolveScrcpyExecutable(out string resolvedPath, string? customPath = null)
        {
            string scrcpyBin = OperatingSystem.IsWindows() ? "scrcpy.exe" : "scrcpy";

            if (IsValidCustomScrcpyPath(customPath))
            {
                string normalizedPath = NormalizeConfiguredPath(customPath!);
                if (File.Exists(normalizedPath) && IsExpectedScrcpyExecutableName(normalizedPath))
                {
                    resolvedPath = normalizedPath;
                    return true;
                }
                if (Directory.Exists(normalizedPath))
                {
                    string candidateInDir = Path.Combine(normalizedPath, scrcpyBin);
                    if (File.Exists(candidateInDir))
                    {
                        resolvedPath = Path.GetFullPath(candidateInDir);
                        return true;
                    }
                }
            }

            string baseDir = AppContext.BaseDirectory;

            if (OperatingSystem.IsWindows())
            {
                string[] portableCandidates =
                {
                    Path.Combine(baseDir, "scrcpy", scrcpyBin),
                    Path.Combine(baseDir, scrcpyBin),
                    Path.Combine(baseDir, "tools", "scrcpy", scrcpyBin),
                    Path.Combine(baseDir, "tools", scrcpyBin)
                };

                foreach (string candidate in portableCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        resolvedPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }

                resolvedPath = Path.Combine(baseDir, "scrcpy", scrcpyBin);
                return false;
            }

            // 1. Check relative to base directory and upward directory hierarchy (up to 5 levels)
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                string[] candidates =
                {
                    Path.Combine(dir.FullName, "scrcpy", scrcpyBin),
                    Path.Combine(dir.FullName, scrcpyBin),
                    Path.Combine(dir.FullName, "tools", "scrcpy", scrcpyBin),
                    Path.Combine(dir.FullName, "tools", scrcpyBin),
                    Path.Combine(dir.FullName, "bin", "scrcpy", scrcpyBin),
                    Path.Combine(dir.FullName, "bin", scrcpyBin)
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        resolvedPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }

                dir = dir.Parent;
            }

            // 2. Search in PATH
            string? pathFromEnv = FindInPath("scrcpy");
            if (pathFromEnv != null && File.Exists(pathFromEnv))
            {
                resolvedPath = pathFromEnv;
                return true;
            }

            // 3. Linux / Unix well-known installation locations
            if (!OperatingSystem.IsWindows())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] unixCandidates =
                {
                    "/usr/local/bin/scrcpy",
                    "/usr/bin/scrcpy",
                    "/opt/scrcpy/scrcpy",
                    "/snap/bin/scrcpy",
                    Path.Combine(home, ".local", "bin", "scrcpy")
                };

                foreach (var unixPath in unixCandidates)
                {
                    if (File.Exists(unixPath))
                    {
                        resolvedPath = Path.GetFullPath(unixPath);
                        return true;
                    }
                }
            }

            resolvedPath = scrcpyBin;
            return false;
        }

        /// <summary>
        /// Resolves the scrcpy executable path, returning the portable application-local candidate on Windows when unresolved.
        /// </summary>
        public static string ResolveScrcpyExecutable(string? customPath = null)
        {
            return TryResolveScrcpyExecutable(out string path, customPath) ? path : path;
        }

        /// <summary>
        /// Attempts to resolve the absolute path to adb.exe.
        /// Returns true if an existing binary is located, otherwise false.
        /// </summary>
        public static bool TryResolveAdbExecutable(out string resolvedPath, string? customPath = null, string? scrcpyPath = null)
        {
            string adbBin = OperatingSystem.IsWindows() ? "adb.exe" : "adb";

            if (IsValidCustomAdbPath(customPath))
            {
                string normalizedPath = NormalizeConfiguredPath(customPath!);
                if (File.Exists(normalizedPath) && IsExpectedAdbExecutableName(normalizedPath))
                {
                    resolvedPath = normalizedPath;
                    return true;
                }
                if (Directory.Exists(normalizedPath))
                {
                    string candidateInDir = Path.Combine(normalizedPath, adbBin);
                    if (File.Exists(candidateInDir))
                    {
                        resolvedPath = Path.GetFullPath(candidateInDir);
                        return true;
                    }
                }
            }

            // 1. Sibling to resolved scrcpy executable (official releases bundle adb together)
            string? effectiveScrcpyPath = scrcpyPath;
            if (!string.IsNullOrWhiteSpace(effectiveScrcpyPath))
            {
                try
                {
                    effectiveScrcpyPath = NormalizeConfiguredPath(effectiveScrcpyPath);
                    if (!IsInsidePortableWindowsRoot(effectiveScrcpyPath))
                    {
                        effectiveScrcpyPath = null;
                    }
                }
                catch
                {
                    effectiveScrcpyPath = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(effectiveScrcpyPath) && Directory.Exists(effectiveScrcpyPath))
            {
                string siblingInDir = Path.Combine(effectiveScrcpyPath, adbBin);
                if (File.Exists(siblingInDir))
                {
                    resolvedPath = Path.GetFullPath(siblingInDir);
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(effectiveScrcpyPath) || !File.Exists(effectiveScrcpyPath))
            {
                if (TryResolveScrcpyExecutable(out string autoScrcpyPath, scrcpyPath))
                {
                    effectiveScrcpyPath = autoScrcpyPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(effectiveScrcpyPath) && File.Exists(effectiveScrcpyPath))
            {
                string? scrcpyDir = Path.GetDirectoryName(effectiveScrcpyPath);
                if (!string.IsNullOrEmpty(scrcpyDir))
                {
                    string siblingAdb = Path.Combine(scrcpyDir, adbBin);
                    if (File.Exists(siblingAdb))
                    {
                        resolvedPath = Path.GetFullPath(siblingAdb);
                        return true;
                    }
                }
            }

            string baseDir = AppContext.BaseDirectory;

            if (OperatingSystem.IsWindows())
            {
                string[] portableCandidates =
                {
                    Path.Combine(baseDir, "scrcpy", adbBin),
                    Path.Combine(baseDir, adbBin),
                    Path.Combine(baseDir, "tools", "scrcpy", adbBin),
                    Path.Combine(baseDir, "tools", adbBin),
                    Path.Combine(baseDir, "platform-tools", adbBin)
                };

                foreach (string candidate in portableCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        resolvedPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }

                resolvedPath = Path.Combine(baseDir, "scrcpy", adbBin);
                return false;
            }

            // 2. Search upward directory hierarchy
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                string[] candidates =
                {
                    Path.Combine(dir.FullName, "scrcpy", adbBin),
                    Path.Combine(dir.FullName, "tools", adbBin),
                    Path.Combine(dir.FullName, "platform-tools", adbBin),
                    Path.Combine(dir.FullName, adbBin)
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        resolvedPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }

                dir = dir.Parent;
            }

            // 3. Search in PATH
            string? pathFromEnv = FindInPath("adb");
            if (pathFromEnv != null && File.Exists(pathFromEnv))
            {
                resolvedPath = pathFromEnv;
                return true;
            }

            // 4. Check ANDROID_HOME and ANDROID_SDK_ROOT environment variables
            string?[] sdkEnvVars =
            {
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
                Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
            };
            foreach (var sdkEnv in sdkEnvVars)
            {
                if (!string.IsNullOrWhiteSpace(sdkEnv))
                {
                    string envSdkAdb = Path.Combine(sdkEnv.Trim(), "platform-tools", adbBin);
                    if (File.Exists(envSdkAdb))
                    {
                        resolvedPath = Path.GetFullPath(envSdkAdb);
                        return true;
                    }
                }
            }

            // 5. Linux / Unix well-known Android SDK locations
            if (!OperatingSystem.IsWindows())
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string[] unixCandidates =
                {
                    "/usr/bin/adb",
                    "/usr/local/bin/adb",
                    Path.Combine(home, "Android", "Sdk", "platform-tools", "adb"),
                    Path.Combine(home, ".android-sdk", "platform-tools", "adb"),
                    "/opt/android-sdk/platform-tools/adb"
                };

                foreach (var unixPath in unixCandidates)
                {
                    if (File.Exists(unixPath))
                    {
                        resolvedPath = Path.GetFullPath(unixPath);
                        return true;
                    }
                }
            }

            resolvedPath = adbBin;
            return false;
        }

        /// <summary>
        /// Resolves the adb executable path, returning the portable application-local candidate on Windows when unresolved.
        /// </summary>
        public static string ResolveAdbExecutable(string? customPath = null, string? scrcpyPath = null)
        {
            return TryResolveAdbExecutable(out string path, customPath, scrcpyPath) ? path : path;
        }

        /// <summary>
        /// Attempts to resolve the absolute path to scrcpydex-server.jar.
        /// Searches filesystem candidates and extracts from embedded assembly resources if not found.
        /// </summary>
        public static bool TryResolveServerJar(out string resolvedPath, string? customPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                try
                {
                    string normalizedPath = NormalizeConfiguredPath(customPath);
                    if (IsInsidePortableWindowsRoot(normalizedPath) && File.Exists(normalizedPath))
                    {
                        resolvedPath = normalizedPath;
                        return true;
                    }
                }
                catch
                {
                }
            }

            string baseDir = AppContext.BaseDirectory;

            if (OperatingSystem.IsWindows())
            {
                string[] portableCandidates =
                {
                    Path.Combine(baseDir, "scrcpydex-server.jar"),
                    Path.Combine(baseDir, "server", "scrcpydex-server.jar"),
                    Path.Combine(DefaultPlatformPaths.Instance.GetCacheDirectory(), "server", "scrcpydex-server.jar")
                };

                foreach (string candidate in portableCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        resolvedPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }

                if (TryExtractEmbeddedServerJar(out string portableExtractedPath))
                {
                    resolvedPath = portableExtractedPath;
                    return true;
                }

                resolvedPath = string.Empty;
                return false;
            }

            // 1. Search candidate directories relative to baseDir and upward (up to 5 levels)
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                string[] candidates =
                {
                    Path.Combine(dir.FullName, "server", "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "server", "build", "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "ScrcpyDex", "server", "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "ScrcpyDex", "publish", "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "publish", "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "tests", "server", "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "scrcpydex-server.jar"),
                    Path.Combine(dir.FullName, "client", "server", "scrcpydex-server.jar"),
                    "/usr/share/scrcpydex/server/scrcpydex-server.jar",
                    "/usr/local/share/scrcpydex/server/scrcpydex-server.jar",
                    "/opt/scrcpydex/server/scrcpydex-server.jar"
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate))
                    {
                        resolvedPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }

                dir = dir.Parent;
            }

            // 2. Check platform cache directory
            string cacheJar = Path.Combine(DefaultPlatformPaths.Instance.GetCacheDirectory(), "server", "scrcpydex-server.jar");
            if (File.Exists(cacheJar))
            {
                resolvedPath = Path.GetFullPath(cacheJar);
                return true;
            }

            // 3. Fallback: Extract embedded scrcpydex-server.jar resource
            if (TryExtractEmbeddedServerJar(out string extractedPath))
            {
                resolvedPath = extractedPath;
                return true;
            }

            resolvedPath = string.Empty;
            return false;
        }

        /// <summary>
        /// Resolves the absolute path to scrcpydex-server.jar, extracting from embedded resources if needed.
        /// </summary>
        public static string? ResolveServerJar(string? customPath = null)
        {
            return TryResolveServerJar(out string path, customPath) ? path : null;
        }

        private static bool TryExtractEmbeddedServerJar(out string extractedPath)
        {
            extractedPath = string.Empty;
            try
            {
                var assembly = typeof(ScrcpyPathResolver).Assembly;
                Stream? stream = assembly.GetManifestResourceStream("scrcpydex-server.jar")
                              ?? assembly.GetManifestResourceStream("ScrcpyDex.Core.scrcpydex-server.jar");

                if (stream == null)
                {
                    foreach (var name in assembly.GetManifestResourceNames())
                    {
                        if (name.EndsWith("scrcpydex-server.jar", StringComparison.OrdinalIgnoreCase))
                        {
                            stream = assembly.GetManifestResourceStream(name);
                            if (stream != null) break;
                        }
                    }
                }

                if (stream == null) return false;

                using (stream)
                {
                    string targetDir = Path.Combine(DefaultPlatformPaths.Instance.GetCacheDirectory(), "server");

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    string targetFile = Path.Combine(targetDir, "scrcpydex-server.jar");
                    using (var fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.CopyTo(fs);
                    }

                    extractedPath = targetFile;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Queries the installed scrcpy version by executing '{scrcpy} --version'.
        /// Returns parsed System.Version, or null if execution or parsing fails.
        /// </summary>
        public static Version? QueryScrcpyVersion(string? scrcpyPath = null)
        {
            string executable;
            if (!string.IsNullOrWhiteSpace(scrcpyPath))
            {
                try
                {
                    executable = NormalizeConfiguredPath(scrcpyPath);
                    if (!IsInsidePortableWindowsRoot(executable))
                    {
                        return null;
                    }
                }
                catch
                {
                    return null;
                }
            }
            else if (!TryResolveScrcpyExecutable(out executable))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(executable) ||
                !File.Exists(executable) ||
                IsSelfExecutable(executable) ||
                !IsExpectedScrcpyExecutableName(executable))
            {
                return null;
            }

            if (!ExecuteProbeCommand(executable, "--version", timeoutMs: 3000, out _, out string stdout, out _))
            {
                return null;
            }

            return ParseScrcpyVersion(stdout);
        }

        /// <summary>
        /// Validates that a selected executable is a genuine scrcpy binary without risking infinite spawns.
        /// </summary>
        public static bool TryValidateScrcpyExecutable(string? candidatePath, out Version? version)
        {
            version = null;
            if (!IsValidCustomScrcpyPath(candidatePath))
            {
                return false;
            }

            string normalizedPath = NormalizeConfiguredPath(candidatePath!);
            version = QueryScrcpyVersion(normalizedPath);
            return version != null;
        }

        /// <summary>
        /// Validates that a selected executable is a genuine Android Debug Bridge (adb) binary.
        /// </summary>
        public static bool TryValidateAdbExecutable(string? candidatePath)
        {
            if (!IsValidCustomAdbPath(candidatePath))
            {
                return false;
            }

            string normalizedPath = NormalizeConfiguredPath(candidatePath!);
            if (!ExecuteProbeCommand(normalizedPath, "version", timeoutMs: 3000, out int exitCode, out string stdout, out _))
            {
                return false;
            }

            return exitCode == 0 && stdout.Contains("Android Debug Bridge", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ExecuteProbeCommand(
            string executablePath,
            string arguments,
            int timeoutMs,
            out int exitCode,
            out string stdout,
            out string stderr)
        {
            exitCode = -1;
            stdout = string.Empty;
            stderr = string.Empty;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(executablePath),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                PortableProcessEnvironment.ApplyAdbEnvironment(
                    psi,
                    IsExpectedAdbExecutableName(executablePath) ? executablePath : null);

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return false;
                }

                try { proc.StandardInput.Close(); } catch { }

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return false;
                }

                try
                {
                    System.Threading.Tasks.Task.WhenAll(stdoutTask, stderrTask).Wait(500);
                }
                catch { }

                exitCode = proc.ExitCode;
                stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
                stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses the version string from 'scrcpy --version' output (e.g., 'scrcpy 2.4' or 'scrcpy 5.0 <https://...>').
        /// </summary>
        public static Version? ParseScrcpyVersion(string? versionOutput)
        {
            if (string.IsNullOrWhiteSpace(versionOutput)) return null;

            var match = Regex.Match(versionOutput, @"scrcpy\s+v?(\d+(\.\d+)+)", RegexOptions.IgnoreCase);
            if (match.Success && Version.TryParse(match.Groups[1].Value, out var ver))
            {
                return ver;
            }

            return null;
        }

        /// <summary>
        /// Checks whether the resolved scrcpy satisfies the minimum version required for ScrcpyDeX (v2.4+).
        /// </summary>
        public static bool IsScrcpyVersionSupported(Version? version)
        {
            return version != null && version >= new Version(2, 4);
        }

        /// <summary>
        /// Locates the application workspace root directory.
        /// </summary>
        public static string ResolveAppRootDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "server")) &&
                    (Directory.Exists(Path.Combine(dir.FullName, "client")) || File.Exists(Path.Combine(dir.FullName, "run-scrcpydex.ps1"))))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            return AppContext.BaseDirectory;
        }

        private static string? FindInPath(string binaryName)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv)) return null;

            char pathSep = Path.PathSeparator;
            string[] paths = pathEnv.Split(pathSep, StringSplitOptions.RemoveEmptyEntries);

            foreach (string p in paths)
            {
                try
                {
                    string trimmed = p.Trim();
                    if (string.IsNullOrEmpty(trimmed) || !Directory.Exists(trimmed)) continue;

                    foreach (string ext in ExecutableExtensions)
                    {
                        string candidate = Path.Combine(trimmed, binaryName + ext);
                        if (File.Exists(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
