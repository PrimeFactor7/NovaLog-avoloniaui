using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NovaLog.Avalonia.Services;

/// <summary>
/// Starts ThemeProxy.exe as a sidecar and assigns it to a Windows Job Object
/// so the proxy is terminated when the parent process exits (no zombie process).
/// No-op on non-Windows.
/// </summary>
public sealed class ThemeProxyManager : IDisposable
{
    private Process? _proxyProcess;
    private IntPtr _jobHandle;
    private const string ProxyFileName = "ThemeProxy.exe";
    private const int AppPort = 15707;

    /// <summary>
    /// Start the theme proxy exe in the given directory and bind it to a job so it exits with this process.
    /// No-op when not on Windows or when ThemeProxy.exe is not found.
    /// </summary>
    /// <param name="proxyDirectory">Directory containing ThemeProxy.exe (e.g. AppDomain.CurrentDomain.BaseDirectory or a ThemeProxy subfolder).</param>
    public void StartProxy(string proxyDirectory)
    {
        if (!OperatingSystem.IsWindows())
            return;

        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = info };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
            if (!SetInformationJobObject(_jobHandle, JobObjectInfoClass.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }

        string fullPath = Path.Combine(proxyDirectory, ProxyFileName);
        if (!File.Exists(fullPath))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = fullPath,
            Arguments = $"--urls=http://127.0.0.1:{AppPort}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            _proxyProcess = Process.Start(startInfo);
        }
        catch
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
            throw;
        }

        if (_proxyProcess != null && !AssignProcessToJobObject(_jobHandle, _proxyProcess.Handle))
        {
            _proxyProcess.Kill();
            _proxyProcess.Dispose();
            _proxyProcess = null;
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void StopProxy()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
        _proxyProcess?.Dispose();
        _proxyProcess = null;
    }

    public void Dispose() => StopProxy();

    #region P/Invoke

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoClass jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoCounters;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    #endregion
}
