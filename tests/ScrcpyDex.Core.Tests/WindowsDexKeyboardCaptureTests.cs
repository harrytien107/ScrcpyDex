using ScrcpyDex.Core.Configuration;
using ScrcpyDex.Core.Platform;
using ScrcpyDex.Services;
using Xunit;

namespace ScrcpyDex.Core.Tests
{
    public class WindowsDexKeyboardCaptureTests
    {
        [Theory]
        [InlineData(0x09)]
        [InlineData(0x5B)]
        [InlineData(0x73)]
        [InlineData(0x1B)]
        [InlineData(0x44)]
        [InlineData(0x52)]
        [InlineData(0x41)]
        public void Ownership_KeyDownInside_CapturesAnyKeyboardKey(uint virtualKeyCode)
        {
            var ownership = new KeyboardCaptureOwnership();
            bool capture = ownership.ShouldCapture(virtualKeyCode, keyDown: true, keyUp: false, pointerInside: true);

            Assert.True(capture);
            Assert.True(ownership.IsCaptured(virtualKeyCode));
        }

        [Fact]
        public void Ownership_CapturedKeyUpAfterPointerLeaves_RemainsDexOwned()
        {
            var ownership = new KeyboardCaptureOwnership();
            const uint leftAlt = 0xA4;

            Assert.True(ownership.ShouldCapture(leftAlt, keyDown: true, keyUp: false, pointerInside: true));
            Assert.True(ownership.ShouldCapture(leftAlt, keyDown: false, keyUp: true, pointerInside: false));
            Assert.False(ownership.IsCaptured(leftAlt));
        }

        [Fact]
        public void Ownership_HostKeyDownThenPointerEnters_KeyUpRemainsHostOwned()
        {
            var ownership = new KeyboardCaptureOwnership();
            const uint leftControl = 0xA2;

            Assert.False(ownership.ShouldCapture(leftControl, keyDown: true, keyUp: false, pointerInside: false));
            Assert.False(ownership.ShouldCapture(leftControl, keyDown: false, keyUp: true, pointerInside: true));
        }

        [Fact]
        public void Ownership_RepeatedKeyDownRemainsCapturedAfterPointerLeaves()
        {
            var ownership = new KeyboardCaptureOwnership();
            const uint keyA = 0x41;

            Assert.True(ownership.ShouldCapture(keyA, keyDown: true, keyUp: false, pointerInside: true));
            Assert.True(ownership.ShouldCapture(keyA, keyDown: true, keyUp: false, pointerInside: false));
            Assert.True(ownership.ShouldCapture(keyA, keyDown: false, keyUp: true, pointerInside: false));
        }

        [Fact]
        public void Ownership_HostRepeatInside_RemainsHostOwned()
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.False(ownership.ShouldCapture(0x41, true, false, false));
            Assert.False(ownership.ShouldCapture(0x41, true, false, true));
            Assert.False(ownership.ShouldCapture(0x41, false, true, true));
            Assert.True(ownership.ShouldCapture(0x41, true, false, true));
        }

        [Theory]
        [InlineData(0xA0)]
        [InlineData(0xA1)]
        public void NativeShiftChord_PreservesPhysicalShiftAndNumberEvents(uint shift)
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.True(ownership.ShouldCapture(shift, true, false, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(shift, true, false, false, true));

            foreach (uint digit in new uint[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 })
            {
                Assert.True(ownership.ShouldCapture(digit, true, false, true));
                Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(digit, true, false, false, true));
                Assert.True(ownership.ShouldCapture(digit, false, true, true));
                Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(digit, false, true, true, true));
            }

            Assert.True(ownership.ShouldCapture(shift, false, true, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(shift, false, true, true, true));
            Assert.True(ownership.ShouldCapture(0x31, true, false, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(0x31, true, false, false, true));
        }

        [Theory]
        [InlineData(0x41)] // Letter
        [InlineData(0x25)] // Left arrow
        [InlineData(0x27)] // Right arrow
        [InlineData(0xBA)] // OEM punctuation
        [InlineData(0x09)] // Tab
        public void NativeShiftChord_PreservesTextAndSelectionKeys(uint key)
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.True(ownership.ShouldCapture(0xA0, true, false, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(0xA0, true, false, false, true));
            Assert.True(ownership.ShouldCapture(key, true, false, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(key, true, false, false, true));
        }

        [Theory]
        [InlineData(0xA4)] // Alt
        [InlineData(0x5B)] // Win
        [InlineData(0xA2)] // Ctrl
        [InlineData(0x1B)] // Escape
        [InlineData(0x73)] // F4
        public void NativeShiftChord_ReservedKeysRemainIntercepted(uint key)
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.True(ownership.ShouldCapture(0xA0, true, false, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(0xA0, true, false, false, true));
            Assert.True(ownership.ShouldCapture(key, true, false, true));
            Assert.Equal(CapturedKeyRoute.Forward, ownership.GetCapturedKeyRoute(key, true, false, false, true));
        }

        [Fact]
        public void NativeShiftChord_FocusLossSuppressesRepeatButReleasesPhysicalKey()
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.True(ownership.ShouldCapture(0xA0, true, false, true));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(0xA0, true, false, false, true));
            Assert.True(ownership.ShouldCapture(0xA0, true, false, false));
            Assert.Equal(CapturedKeyRoute.Suppress, ownership.GetCapturedKeyRoute(0xA0, true, false, true, false));
            Assert.True(ownership.ShouldCapture(0xA0, false, true, false));
            Assert.Equal(CapturedKeyRoute.Native, ownership.GetCapturedKeyRoute(0xA0, false, true, true, false));
        }

        [Fact]
        public void NativeShiftChord_DoesNotLeakWhenHostAltIsHeld()
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.False(ownership.ShouldCapture(0xA4, true, false, false));
            Assert.True(ownership.ShouldCapture(0xA0, true, false, true));
            Assert.Equal(CapturedKeyRoute.Forward, ownership.GetCapturedKeyRoute(0xA0, true, false, false, true));
        }

        [Theory]
        [InlineData(0xA4, 0x09)] // Alt+Tab
        [InlineData(0xA4, 0x73)] // Alt+F4
        [InlineData(0x5B, 0x44)] // Win+D
        [InlineData(0x5B, 0x52)] // Win+R
        [InlineData(0xA2, 0x1B)] // Ctrl+Esc
        [InlineData(0xA2, 0x43)] // Ctrl+C
        public void CapturedShortcut_UsesForwardedEventsEvenWhenForeground(uint modifier, uint key)
        {
            var ownership = new KeyboardCaptureOwnership();
            Assert.True(ownership.ShouldCapture(modifier, true, false, true));
            Assert.Equal(CapturedKeyRoute.Forward, ownership.GetCapturedKeyRoute(modifier, true, false, false, true));
            Assert.True(ownership.ShouldCapture(key, true, false, true));
            Assert.Equal(CapturedKeyRoute.Forward, ownership.GetCapturedKeyRoute(key, true, false, false, true));
        }

        [Theory]
        [InlineData(0xA0, 0x10)]
        [InlineData(0xA1, 0x10)]
        [InlineData(0xA2, 0x11)]
        [InlineData(0xA3, 0x11)]
        [InlineData(0xA4, 0x12)]
        [InlineData(0xA5, 0x12)]
        [InlineData(0x5B, 0x5B)]
        [InlineData(0x41, 0x41)]
        public void ModifierMessage_UsesWin32VirtualKey(uint original, uint expected)
        {
            Assert.Equal(expected, WindowsDexKeyboardCapture.NormalizeVirtualKey(original));
        }

        [Fact]
        public void CaptureSession_ExperimentalSdkSecondaryDisplay_IsEnabled()
        {
            var settings = new ScrcpyDeXSettings();
            settings.Device.ExperimentalDex = true;
            settings.Input.MouseDriver = "sdk";
            settings.Input.ForwardShortcuts = false;

            Assert.True(ScrcpyExecutionService.IsMouseDrivenDexKeyboardCaptureSession(settings, displayId: 2));
            var arguments = ScrcpyExecutionService.BuildArgumentsList(settings, displayId: 2);
            Assert.Contains("--raw-key-events", arguments);
            Assert.Contains("--keyboard=sdk", arguments);
            Assert.Contains("--display-id=2", arguments);
        }

        [Theory]
        [InlineData(false, "sdk", false, 2)]
        [InlineData(true, "uhid", false, 2)]
        [InlineData(true, "sdk", true, 2)]
        [InlineData(true, "sdk", false, 0)]
        public void CaptureSession_NonExperimentalOrNonSdkSession_IsDisabled(bool experimentalDex, string mouseDriver, bool forwardShortcuts, int displayId)
        {
            var settings = new ScrcpyDeXSettings();
            settings.Device.ExperimentalDex = experimentalDex;
            settings.Input.MouseDriver = mouseDriver;
            settings.Input.ForwardShortcuts = forwardShortcuts;

            Assert.False(ScrcpyExecutionService.IsMouseDrivenDexKeyboardCaptureSession(settings, displayId));
            Assert.DoesNotContain("--raw-key-events", ScrcpyExecutionService.BuildArgumentsList(settings, displayId: displayId));
        }
    }
}
