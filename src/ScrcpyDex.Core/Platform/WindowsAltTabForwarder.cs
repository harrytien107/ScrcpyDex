// Copyright 2026 Aureliano Peixoto and ScrcpyDeX Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScrcpyDex.Core.Platform
{
    internal sealed class WindowsAltTabForwarder : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const uint VkTab = 0x09;
        private const uint WmKeyDown = 0x0100;
        private const uint WmKeyUp = 0x0101;
        private const uint WmSysKeyDown = 0x0104;
        private const uint WmSysKeyUp = 0x0105;
        private const uint WmQuit = 0x0012;
        private const uint PmNoRemove = 0x0000;
        private const uint LlkhfExtended = 0x01;
        private const uint LlkhfAltDown = 0x20;

        private readonly int _targetProcessId;
        private readonly HookProc _hookProc;
        private readonly ManualResetEventSlim _started = new(false);
        private Thread? _thread;
        private IntPtr _hookHandle;
        private uint _threadId;
        private Exception? _startupException;
        private bool _disposed;

        public WindowsAltTabForwarder(int targetProcessId)
        {
            _targetProcessId = targetProcessId;
            _hookProc = HookCallback;
        }

        public void Start()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "ScrcpyDeX Alt+Tab Forwarder"
            };
            _thread.Start();

            if (!_started.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Timed out while installing the Windows Alt+Tab keyboard hook.");
            }

            if (_startupException != null)
            {
                throw new InvalidOperationException("Failed to install the Windows Alt+Tab keyboard hook.", _startupException);
            }
        }

        private void HookThreadMain()
        {
            try
            {
                _threadId = GetCurrentThreadId();

                // Force creation of the thread message queue so Dispose() can post WM_QUIT reliably.
                PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);

                IntPtr moduleHandle = GetModuleHandle(null);
                _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
                if (_hookHandle == IntPtr.Zero)
                {
                    _startupException = new Win32Exception(Marshal.GetLastWin32Error());
                    return;
                }
            }
            catch (Exception ex)
            {
                _startupException = ex;
                return;
            }
            finally
            {
                _started.Set();
            }

            try
            {
                while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            finally
            {
                if (_hookHandle != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    uint message = unchecked((uint)wParam.ToInt64());
                    bool keyDown = message == WmKeyDown || message == WmSysKeyDown;
                    bool keyUp = message == WmKeyUp || message == WmSysKeyUp;

                    if (keyDown || keyUp)
                    {
                        KeyboardHookData data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
                        if (data.VirtualKeyCode == VkTab &&
                            (data.Flags & LlkhfAltDown) != 0 &&
                            TryGetForegroundScrcpyWindow(out IntPtr windowHandle))
                        {
                            // Windows handles Alt+Tab before scrcpy can receive it. Suppress the host
                            // shortcut and re-post Tab as a normal key message. scrcpy already received
                            // the Alt key state, so its SDK keyboard path forwards the real Alt+Tab
                            // combination to the selected Android display.
                            uint targetMessage = keyDown ? WmKeyDown : WmKeyUp;
                            IntPtr keyLParam = BuildKeyLParam(data, keyUp);
                            if (PostMessage(windowHandle, targetMessage, (IntPtr)VkTab, keyLParam))
                            {
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Never let an input-hook failure break the host keyboard chain.
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private bool TryGetForegroundScrcpyWindow(out IntPtr windowHandle)
        {
            windowHandle = GetForegroundWindow();
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(windowHandle, out uint processId);
            return processId == unchecked((uint)_targetProcessId);
        }

        private static IntPtr BuildKeyLParam(KeyboardHookData data, bool keyUp)
        {
            long value = 1L | ((long)(data.ScanCode & 0xFF) << 16);
            if ((data.Flags & LlkhfExtended) != 0)
            {
                value |= 1L << 24;
            }

            if (keyUp)
            {
                value |= 1L << 30;
                value |= 1L << 31;
            }

            return new IntPtr(value);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(TimeSpan.FromSeconds(1));
            }

            _started.Dispose();
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardHookData
        {
            public uint VirtualKeyCode;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr WindowHandle;
            public uint MessageId;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int PointX;
            public int PointY;
            public uint Private;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr moduleHandle, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out Message message, IntPtr windowHandle, uint minMessage, uint maxMessage);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out Message message, IntPtr windowHandle, uint minMessage, uint maxMessage, uint removeMessage);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
