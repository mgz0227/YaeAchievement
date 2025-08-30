using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.LibraryLoader;
using Windows.Win32.System.Threading;
using Spectre.Console;
using static Windows.Win32.System.Memory.VIRTUAL_ALLOCATION_TYPE;
using static Windows.Win32.System.Memory.PAGE_PROTECTION_FLAGS;
using static Windows.Win32.System.Memory.VIRTUAL_FREE_TYPE;

// ReSharper disable MemberCanBePrivate.Global

namespace YaeAchievement.Utilities;

internal sealed unsafe class GameProcess {

    public uint Id { get; }

    public HANDLE Handle { get; }

    public HANDLE MainThreadHandle { get; }

    public event Action? OnExit;

    public GameProcess(string path) {
        const PROCESS_CREATION_FLAGS flags = PROCESS_CREATION_FLAGS.CREATE_SUSPENDED;
        Span<char> cmdLines = stackalloc char[1]; // "\0"
        var si = new STARTUPINFOW {
            cb = (uint) sizeof(STARTUPINFOW)
        };
        var wd = Path.GetDirectoryName(path)!;
        if (!Native.CreateProcess(path, ref cmdLines, null, null, false, flags, null, wd, si, out var pi)) {
            var argumentData = new Dictionary<string, object> {
                { "path", path },
                { "workdir", wd },
                { "file_exists", File.Exists(path) },
            };
            throw new ApplicationException($"CreateProcess fail: {Marshal.GetLastPInvokeErrorMessage()}") {
                Data = {
                    { "sentry:context:Arguments", argumentData }
                }
            };
        }
        Id = pi.dwProcessId;
        Handle = pi.hProcess;
        MainThreadHandle = pi.hThread;
        Task.Run(() => {
            Native.WaitForSingleObject(Handle, 0xFFFFFFFF); // INFINITE
            OnExit?.Invoke();
        }).ContinueWith(task => { if (task.IsFaulted) Utils.OnUnhandledException(task.Exception!); });
    }

    public void LoadLibrary(string libPath) {
        try {
            var hKrnl32 = NativeLibrary.Load("kernel32");
            var mLoadLibraryW = NativeLibrary.GetExport(hKrnl32, "LoadLibraryW");
            var libPathLen = (uint) libPath.Length * sizeof(char);
            var lpLibPath = Native.VirtualAllocEx(Handle, null, libPathLen + 2, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
            if (lpLibPath == null) {
                throw new Win32Exception { Data = { { "api", "VirtualAllocEx" } } };
            }
            fixed (void* lpBuffer = libPath) {
                if (!Native.WriteProcessMemory(Handle, lpLibPath, lpBuffer, libPathLen)) {
                    throw new Win32Exception { Data = { { "api", "WriteProcessMemory" } } };
                }
            }
            var lpStartAddress = (delegate*unmanaged[Stdcall]<void*, uint>) mLoadLibraryW; // THREAD_START_ROUTINE
            var hThread = Native.CreateRemoteThread(Handle, null, 0, lpStartAddress, lpLibPath, 0);
            if (hThread.IsNull) {
                throw new Win32Exception { Data = { { "api", "CreateRemoteThread" } } };
            }
            if (Native.WaitForSingleObject(hThread, 2000) == 0) {
                Native.VirtualFreeEx(Handle, lpLibPath, 0, MEM_RELEASE);
            }
            // Get lib base address in target process
            byte* baseAddress = null;
            using (var hSnap = Native.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPMODULE, Id)) {
                if (hSnap.IsInvalid) {
                    throw new Win32Exception { Data = { { "api", "CreateToolhelp32Snapshot" } } };
                }
                var moduleEntry = new MODULEENTRY32 {
                    dwSize = (uint) sizeof(MODULEENTRY32)
                };
                if (Native.Module32First(hSnap, ref moduleEntry)) {
                    do {
                        if (new string((sbyte*) &moduleEntry.szExePath._0) == libPath) {
                            baseAddress = moduleEntry.modBaseAddr;
                            break;
                        }
                    } while (Native.Module32Next(hSnap, ref moduleEntry));
                }
            }
            if (baseAddress == null) {
                throw new InvalidOperationException("No matching module found in target process.");
            }
            //
            using var libHandle = Native.LoadLibraryEx(libPath, LOAD_LIBRARY_FLAGS.DONT_RESOLVE_DLL_REFERENCES);
            if (libHandle.IsInvalid) {
                throw new Win32Exception { Data = { { "api", "LoadLibraryEx" } } };
            }
            var libMainProc = Native.GetProcAddress(libHandle, "YaeMain");
            if (libMainProc.IsNull) {
                throw new Win32Exception { Data = { { "api", "GetProcAddress" } } };
            }
            var libMainProcRVA = libMainProc.Value - libHandle.DangerousGetHandle();
            var lpStartAddress2 = (delegate*unmanaged[Stdcall]<void*, uint>) (baseAddress + libMainProcRVA); // THREAD_START_ROUTINE
            //
            var hThread2 = Native.CreateRemoteThread(Handle, null, 0, lpStartAddress2, null, 0);
            if (hThread2.IsNull) {
                throw new Win32Exception { Data = { { "api", "CreateRemoteThread2" } } };
            }
            Native.CloseHandle(hThread2);
            Native.CloseHandle(hThread);
        } catch (Win32Exception e) {
            _ = Terminate(0);
            AnsiConsole.WriteLine(App.LoadLibraryFail, e.Data["api"]!, e.NativeErrorCode, e.Message);
            Environment.Exit(-1);
        }
    }

    public bool ResumeMainThread() => Native.ResumeThread(MainThreadHandle) != 0xFFFFFFFF;

    public bool Terminate(uint exitCode) => Native.TerminateProcess(Handle, exitCode);

}
