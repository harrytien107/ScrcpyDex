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
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScrcpyDex.Core.Platform
{
    internal sealed class WindowsDexKeyboardCapture : IDisposable
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
        private const uint GaRoot = 2;

        private readonly int _targetProcessId;
        private readonly bool _mouseDrivenExclusive;
        private readonly HookProc _hookProc;
        private readonly ManualResetEventSlim _started = new(false);
        private readonly KeyboardCaptureOwnership _ownership = new();
        private Thread? _thread;
        private IntPtr _hookHandle;
        private IntPtr _targetWindow;
        private IntPtr _returnForegroundWindow;
        private uint _threadId;
        private Exception? _startupException;
        private bool _disposed;

        public WindowsDexKeyboardCapture(int targetProcessId, bool mouseDrivenExclusive)
        {
            _targetProcessId = targetProcessId;
            _mouseDrivenExclusive = mouseDrivenExclusive;
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
                Name = "ScrcpyDeX Mouse-Driven Keyboard Capture"
            };
            _thread.Start();

            if (!_started.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Timed out while installing the Windows DeX keyboard capture hook.");
            }

            if (_startupException != null)
            {
                throw new InvalidOperationException("Failed to install the Windows DeX keyboard capture hook.", _startupException);
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

                        if (!_mouseDrivenExclusive)
                        {
                            if (data.VirtualKeyCode == VkTab &&
                                (data.Flags & LlkhfAltDown) != 0 &&
                                TryGetForegroundScrcpyWindow(out IntPtr foregroundWindow))
                            {
                                uint targetMessage = keyDown ? WmKeyDown : WmKeyUp;
                                IntPtr keyLParam = BuildKeyLParam(data, keyUp, wasDown: false);
                                if (PostMessage(foregroundWindow, targetMessage, (IntPtr)VkTab, keyLParam))
                                {
                                    return (IntPtr)1;
                                }
                            }

                            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                        }

                        bool pointerInside = TryGetScrcpyWindowUnderCursor(out IntPtr hoveredWindow);
                        bool wasCaptured = _ownership.IsCaptured(data.VirtualKeyCode);

                        if (!pointerInside && keyDown && !wasCaptured && _returnForegroundWindow != IntPtr.Zero)
                        {
                            if (GetForegroundWindow() == _targetWindow && IsWindow(_returnForegroundWindow))
                            {
                                SetForegroundWindow(_returnForegroundWindow);
                            }
                            _returnForegroundWindow = IntPtr.Zero;
                        }

                        if (_ownership.ShouldCapture(data.VirtualKeyCode, keyDown, keyUp, pointerInside))
                        {
                            IntPtr targetWindow = pointerInside ? hoveredWindow : _targetWindow;
                            bool targetForeground = targetWindow != IntPtr.Zero && GetForegroundWindow() == targetWindow;
                            if (keyDown && !wasCaptured && pointerInside &&
                                _ownership.CanStartNativeShift(data.VirtualKeyCode) && !targetForeground)
                            {
                                // Native Shift state requires the real keyboard input queue.
                                IntPtr previousForeground = GetForegroundWindow();
                                SetForegroundWindow(targetWindow);
                                targetForeground = GetForegroundWindow() == targetWindow;
                                if (targetForeground && previousForeground != IntPtr.Zero)
                                {
                                    _returnForegroundWindow = previousForeground;
                                }
                            }

                            CapturedKeyRoute route = _ownership.GetCapturedKeyRoute(
                                data.VirtualKeyCode, keyDown, keyUp, wasCaptured, targetForeground);
                            if (route == CapturedKeyRoute.Native)
                            {
                                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                            }
                            if (route == CapturedKeyRoute.Suppress)
                            {
                                return (IntPtr)1;
                            }

                            if (targetWindow != IntPtr.Zero && IsWindowOwnedByTargetProcess(targetWindow))
                            {
                                _targetWindow = targetWindow;

                                // Normalize system-key messages to ordinary key messages so Alt-based
                                // chords reach scrcpy without triggering host-window accelerators.
                                uint targetMessage = keyDown ? WmKeyDown : WmKeyUp;
                                IntPtr keyLParam = BuildKeyLParam(data, keyUp, wasCaptured);
                                if (PostMessage(targetWindow, targetMessage, (IntPtr)NormalizeVirtualKey(data.VirtualKeyCode), keyLParam))
                                {
                                    return (IntPtr)1;
                                }
                            }

                            if (keyDown && !wasCaptured)
                            {
                                _ownership.CancelCapture(data.VirtualKeyCode);
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
            return IsWindowOwnedByTargetProcess(windowHandle);
        }

        private bool TryGetScrcpyWindowUnderCursor(out IntPtr windowHandle)
        {
            windowHandle = IntPtr.Zero;
            if (!GetCursorPos(out Point cursorPosition))
            {
                return false;
            }

            IntPtr hitWindow = WindowFromPoint(cursorPosition);
            if (hitWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr rootWindow = GetAncestor(hitWindow, GaRoot);
            if (rootWindow == IntPtr.Zero)
            {
                rootWindow = hitWindow;
            }

            if (!IsWindowVisible(rootWindow) || IsIconic(rootWindow) || !IsWindowOwnedByTargetProcess(rootWindow))
            {
                return false;
            }

            windowHandle = rootWindow;
            _targetWindow = rootWindow;
            return true;
        }

        private bool IsWindowOwnedByTargetProcess(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            GetWindowThreadProcessId(windowHandle, out uint processId);
            return processId == unchecked((uint)_targetProcessId);
        }

        // Win32 window messages use generic modifier VKs; scan codes and the
        // extended bit retain the left/right identity for SDL.
        internal static uint NormalizeVirtualKey(uint virtualKeyCode) => virtualKeyCode switch
        {
            0xA0 or 0xA1 => 0x10,
            0xA2 or 0xA3 => 0x11,
            0xA4 or 0xA5 => 0x12,
            _ => virtualKeyCode
        };

        private static IntPtr BuildKeyLParam(KeyboardHookData data, bool keyUp, bool wasDown)
        {
            long value = 1L | ((long)(data.ScanCode & 0xFF) << 16);
            if ((data.Flags & LlkhfExtended) != 0)
            {
                value |= 1L << 24;
            }

            if (wasDown || keyUp)
            {
                value |= 1L << 30;
            }

            if (keyUp)
            {
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

            _ownership.Clear();
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

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
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
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr windowHandle);

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

    internal enum CapturedKeyRoute
    {
        Forward,
        Native,
        Suppress
    }

    internal sealed class KeyboardCaptureOwnership
    {
        private readonly HashSet<uint> _capturedKeys = new();
        private readonly HashSet<uint> _hostKeys = new();
        private readonly HashSet<uint> _nativeKeys = new();

        public bool IsCaptured(uint virtualKeyCode) => _capturedKeys.Contains(virtualKeyCode);

        public bool ShouldCapture(uint virtualKeyCode, bool keyDown, bool keyUp, bool pointerInside)
        {
            if (keyDown)
            {
                if (_capturedKeys.Contains(virtualKeyCode))
                {
                    return true;
                }

                if (_hostKeys.Contains(virtualKeyCode) || !pointerInside)
                {
                    _hostKeys.Add(virtualKeyCode);
                    return false;
                }

                _capturedKeys.Add(virtualKeyCode);
                return true;
            }

            if (keyUp)
            {
                _hostKeys.Remove(virtualKeyCode);
                return _capturedKeys.Remove(virtualKeyCode);
            }

            return false;
        }

        public void CancelCapture(uint virtualKeyCode)
        {
            _capturedKeys.Remove(virtualKeyCode);
            _hostKeys.Add(virtualKeyCode);
        }

        public bool CanStartNativeShift(uint virtualKeyCode) =>
            IsShift(virtualKeyCode) && !HostShortcutModifierHeld();

        public CapturedKeyRoute GetCapturedKeyRoute(uint virtualKeyCode, bool keyDown, bool keyUp,
            bool wasCaptured, bool targetForeground)
        {
            if (_nativeKeys.Contains(virtualKeyCode))
            {
                if (keyUp)
                {
                    _nativeKeys.Remove(virtualKeyCode);
                    // Always release native keys, even if focus moved while held.
                    return CapturedKeyRoute.Native;
                }
                return targetForeground ? CapturedKeyRoute.Native : CapturedKeyRoute.Suppress;
            }

            // SDL clears a posted Shift press when GetKeyState says Shift is up.
            // Keep Shift and ordinary typing on the same real Windows input queue.
            // Posting ordinary keys after a native Shift release could otherwise
            // reorder them ahead of the release. Intercept other modifier chords.
            if (keyDown && !wasCaptured && targetForeground && !HostShortcutModifierHeld() &&
                (IsShift(virtualKeyCode) ||
                 !CapturedShortcutModifierHeld() && IsOrdinaryKey(virtualKeyCode)))
            {
                _nativeKeys.Add(virtualKeyCode);
                return CapturedKeyRoute.Native;
            }

            return CapturedKeyRoute.Forward;
        }

        private bool HostShortcutModifierHeld() => ShortcutModifierHeld(_hostKeys);

        private bool CapturedShortcutModifierHeld() => ShortcutModifierHeld(_capturedKeys);

        private static bool ShortcutModifierHeld(HashSet<uint> keys) =>
            keys.Contains(0x11) || keys.Contains(0x12) ||
            keys.Contains(0xA2) || keys.Contains(0xA3) ||
            keys.Contains(0xA4) || keys.Contains(0xA5) ||
            keys.Contains(0x5B) || keys.Contains(0x5C);

        private static bool IsShift(uint virtualKeyCode) => virtualKeyCode is 0x10 or 0xA0 or 0xA1;

        private static bool IsOrdinaryKey(uint virtualKeyCode) => virtualKeyCode is
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or // Numbers and letters
            >= 0x60 and <= 0x6F or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2 or
            >= 0x21 and <= 0x28 or 0x08 or 0x09 or 0x0D or 0x20 or 0x2D or 0x2E;

        public void Clear()
        {
            _capturedKeys.Clear();
            _hostKeys.Clear();
            _nativeKeys.Clear();
        }
    }
}
