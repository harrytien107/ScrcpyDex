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
