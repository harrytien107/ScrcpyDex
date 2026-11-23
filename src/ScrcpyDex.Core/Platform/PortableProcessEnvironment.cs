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

namespace ScrcpyDex.Core.Platform
{
    public static class PortableProcessEnvironment
    {
        public static string RootDirectory => Path.GetFullPath(AppContext.BaseDirectory);

        public static string AndroidUserHomeDirectory => Path.Combine(RootDirectory, ".android");

        public static void ApplyAdbEnvironment(ProcessStartInfo startInfo, string? adbPath = null)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            startInfo.Environment["ANDROID_USER_HOME"] = AndroidUserHomeDirectory;
            startInfo.Environment["ANDROID_SDK_HOME"] = RootDirectory;
            startInfo.Environment["HOME"] = RootDirectory;

            if (!string.IsNullOrWhiteSpace(adbPath) && File.Exists(adbPath))
            {
                startInfo.Environment["ADB"] = Path.GetFullPath(adbPath);
            }
        }
    }
}
