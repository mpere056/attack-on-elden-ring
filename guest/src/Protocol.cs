// C# view of the shared memory defined in host/include/bridge_protocol.h. Only the parts the
// guest uses so far. Keep offsets in sync with the header (the host static_asserts its sizes).
namespace Aoer
{
    internal static class Protocol
    {
        public const string ShmName = @"Local\AoER_bridge_v1";
        public const int ShmSize = 8 * 1024 * 1024;
        public const uint Magic = 0x52454F41; // "AOER"
        public const uint Version = 1;

        // ErmcHeader
        public const int HdrMagic = 0x00;
        public const int HdrVersion = 0x04;
        public const int HdrHostHeartbeat = 0x10;
        public const int HdrGuestHeartbeat = 0x18; // "mcHeartbeat"
        public const int HdrHostPid = 0x20;
        public const int HdrGuestPid = 0x24;       // "mcPid"
        public const int HdrGuestStartMs = 0x30;   // "mcStartMs"
        public const int HdrCoreStatus = 0x44;

        // ErmcGameState, written by Elden Ring under a seqlock
        public const int OffState = 0x100;
        public const int StSeq = 0x00;
        public const int StFlags = 0x04;
        public const int StFrame = 0x08;
        public const int StCamPos = 0x10;
        public const int StCamTarget = 0x1C;
        public const int StPlayerPos = 0x48;
        public const int StPlayerQuat = 0x54;
        public const int StStageId = 0x7C;
        public const int StateSize = 0x114;

        public const int HdrHostLife = 0x58;       // +1 each time the Tarnished is usable again somewhere new

        // ErmcControl, written by the guest under a seqlock (0x64 bytes)
        public const int OffControl = 0x800;
        public const int CtSeq = 0x00;
        public const int CtFlags = 0x04;
        public const int CtFrame = 0x08;
        public const int CtCamPos = 0x10;
        public const int CtCamTarget = 0x1C;
        public const int CtCamUp = 0x28;
        public const int CtFov = 0x34;
        public const int CtHunterPos = 0x38;
        public const int CtHunterYawDeg = 0x58;
        public const int ControlSize = 0x64;

        public const uint CtrlOverrideCamera = 1u << 0;
        public const uint CtrlMoveHunter = 1u << 1;    // the Tarnished stands in at CtHunterPos
        public const uint CtrlHideHunter = 1u << 2;
        public const uint CtrlFlying = 1u << 9;        // no moving-platform tracking

        public const uint StateCameraValid = 1u << 0;
        public const uint StatePlayerValid = 1u << 1;
        public const uint StatePlayerDead = 1u << 7;
        public const uint StateHostBusy = 1u << 8;
    }
}
