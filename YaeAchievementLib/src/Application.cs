using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Yae.Utilities;

namespace Yae;

internal static unsafe class Application {

    [UnmanagedCallersOnly(EntryPoint = "YaeMain")]
    private static uint Awake(nint hModule) {
        Native.RegisterUnhandledExceptionHandler();
        Log.UseConsoleOutput();
        Log.Trace("~");
        Goshujin.Init();
        Goshujin.LoadCmdTable();
        Goshujin.LoadMethodTable();
        Goshujin.ResumeMainThread();
        //
        Native.WaitMainWindow();
        Log.ResetConsole();
        //
        RecordChecksum();
        MinHook.Attach(GameMethod.DoCmd, &OnDoCmd, out _doCmd);
        MinHook.Attach(GameMethod.ToUInt16, &OnToUInt16, out _toUInt16);
        MinHook.Attach(GameMethod.UpdateNormalProp, &OnUpdateNormalProp, out _updateNormalProp);
        return 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "YaeWndHook")]
    private static nint WndHook(int nCode, nint wParam, nint lParam) {
        return User32.CallNextHookEx(0, nCode, wParam, lParam);
    }

    #region RecvPacket

    private static delegate*unmanaged<byte*, int, ushort> _toUInt16;

    [UnmanagedCallersOnly]
    private static ushort OnToUInt16(byte* val, int startIndex) {
        var ret = _toUInt16(val, startIndex);
        if (ret != 0xAB89 || *(ushort*) (val += 0x20) != 0x6745) {
            return ret;
        }
        var cmdId = BinaryPrimitives.ReverseEndianness(*(ushort*) (val + 2));
        if (cmdId == CmdId.PlayerStoreNotify) {
            Goshujin.PushStoreData(GetData(val));
        } else if (cmdId == CmdId.AchievementAllDataNotify) {
            Goshujin.PushAchievementData(GetData(val));
        }
        return ret;
        static Span<byte> GetData(byte* val) {
            var headLen = BinaryPrimitives.ReverseEndianness(*(ushort*) (val + 4));
            var dataLen = BinaryPrimitives.ReverseEndianness(*(uint*) (val + 6));
            return new Span<byte>(val + 10 + headLen, (int) dataLen);
        }
    }

    #endregion

    #region Prop

    /*
     * PROP_PLAYER_HCOIN = 10015,
     * PROP_PLAYER_WAIT_SUB_HCOIN = 10022,
     * PROP_PLAYER_SCOIN = 10016,
     * PROP_PLAYER_WAIT_SUB_SCOIN = 10023,
     * PROP_PLAYER_MCOIN = 10025,
     * PROP_PLAYER_WAIT_SUB_MCOIN = 10026,
     * PROP_PLAYER_HOME_COIN = 10042,
     * PROP_PLAYER_WAIT_SUB_HOME_COIN = 10043,
     * PROP_PLAYER_ROLE_COMBAT_COIN = 10053,
     * PROP_PLAYER_MUSIC_GAME_BOOK_COIN = 10058,
     */
    public static HashSet<int> RequiredPlayerProperties { get; } = [
        10015, 10022, 10016, 10023, 10025, 10026, 10042, 10043, 10053, 10058
    ];

    private static delegate*unmanaged<nint, int, double, double, int, void> _updateNormalProp;

    [UnmanagedCallersOnly]
    private static void OnUpdateNormalProp(nint @this, int type, double value, double lastValue, int state) {
        _updateNormalProp(@this, type, value, lastValue, state);
        if (RequiredPlayerProperties.Remove(type)) {
            Goshujin.PushPlayerProp(type, value);
        }
    }

    #endregion

    #region Checksum

    [StructLayout(LayoutKind.Sequential)]
    private struct RecordChecksumCmdData {

        public int Type;

        public void* Buffer;

        public int Length;

    }

    private static readonly RecordChecksumCmdData[] RecordedChecksum = new RecordChecksumCmdData[3];

    private static void RecordChecksum() {
        for (var i = 0; i < 3; i++) {
            var buffer = NativeMemory.AllocZeroed(256);
            var data = new RecordChecksumCmdData {
                Type = i,
                Buffer = buffer,
                Length = 256
            };
            _ = GameMethod.DoCmd(23, Unsafe.AsPointer(ref data), sizeof(RecordChecksumCmdData));
            RecordedChecksum[i] = data;
            //REPL//Log.Trace($"nType={i}, value={new string((sbyte*) buffer, 0, data.Length)}");
        }
    }

    private static delegate*unmanaged<int, void*, int, int> _doCmd;

    [UnmanagedCallersOnly]
    public static int OnDoCmd(int cmdType, void* data, int size) {
        var result = _doCmd(cmdType, data, size);
        if (cmdType == 23) {
            var cmdData = (RecordChecksumCmdData*) data;
            if (cmdData->Type < 3) {
                var recordedData = RecordedChecksum[cmdData->Type];
                cmdData->Length = recordedData.Length;
                Buffer.MemoryCopy(recordedData.Buffer, cmdData->Buffer, recordedData.Length, recordedData.Length);
                //REPL//Log.Trace($"Override type {cmdData->Type} result");
            }
        }
        return result;
    }

    #endregion

}
