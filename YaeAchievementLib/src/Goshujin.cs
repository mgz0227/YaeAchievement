using System.IO.Pipes;
using Yae.Utilities;

namespace Yae;

internal static class CmdId {

    public static uint AchievementAllDataNotify { get; set; }

    public static uint PlayerStoreNotify { get; set; }

}

internal static unsafe class GameMethod {

    public static delegate*unmanaged<int, void*, int, int> DoCmd { get; set; }

    public static delegate*unmanaged<byte*, int, ushort> ToUInt16 { get; set; }

    public static delegate*unmanaged<nint, int, double, double, int, void> UpdateNormalProp { get; set; }

}

internal static class Goshujin {

    private static NamedPipeClientStream _pipeStream = null!;
    private static BinaryReader _pipeReader = null!;
    private static BinaryWriter _pipeWriter = null!;

    public static void Init(string pipeName = "YaeAchievementPipe") {
        _pipeStream = new NamedPipeClientStream(pipeName);
        _pipeReader = new BinaryReader(_pipeStream);
        _pipeWriter = new BinaryWriter(_pipeStream);
        _pipeStream.Connect();
        Log.Trace("Pipe server connected.");
    }

    public static void PushAchievementData(Span<byte> data) {
        _pipeWriter.Write((byte) 1);
        _pipeWriter.Write(data.Length);
        _pipeWriter.Write(data);
        _achievementDataPushed = true;
        ExitIfFinished();
    }

    public static void PushStoreData(Span<byte> data) {
        _pipeWriter.Write((byte) 2);
        _pipeWriter.Write(data.Length);
        _pipeWriter.Write(data);
        _storeDataPushed = true;
        ExitIfFinished();
    }

    public static void PushPlayerProp(int type, double value) {
        _pipeWriter.Write((byte) 3);
        _pipeWriter.Write(type);
        _pipeWriter.Write(value);
        ExitIfFinished();
    }

    public static void LoadCmdTable() {
        _pipeWriter.Write((byte) 0xFC);
        CmdId.AchievementAllDataNotify = _pipeReader.ReadUInt32();
        CmdId.PlayerStoreNotify = _pipeReader.ReadUInt32();
    }

    public static unsafe void LoadMethodTable() {
        _pipeWriter.Write((byte) 0xFD);
        GameMethod.DoCmd = (delegate*unmanaged<int, void*, int, int>) Native.RVAToVA(_pipeReader.ReadUInt32());
        GameMethod.ToUInt16 = (delegate*unmanaged<byte*, int, ushort>) Native.RVAToVA(_pipeReader.ReadUInt32());
        GameMethod.UpdateNormalProp = (delegate*unmanaged<nint, int, double, double, int, void>) Native.RVAToVA(_pipeReader.ReadUInt32());
    }

    public static void ResumeMainThread() {
        _pipeWriter.Write((byte) 0xFE);
    }

    private static bool _storeDataPushed;

    private static bool _achievementDataPushed;

    private static void ExitIfFinished() {
        if (_storeDataPushed && _achievementDataPushed && Application.RequiredPlayerProperties.Count == 0) {
            _pipeWriter.Write((byte) 0xFF);
            Environment.Exit(0);
        }
    }
}
