using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FastGithub.DomainResolve
{
    /// <summary>
    /// 服务启动类型（值同 WIN32 ServiceStartType）
    /// </summary>
    public enum ServiceStartType : uint
    {
        SERVICE_AUTO_START = 0x00000002,
        SERVICE_DEMAND_START = 0x00000003,
    }

    /// <summary>
    /// Windows 服务安装 / 启动 / 停止 / 删除工具。
    /// 使用自包含的原始 P/Invoke（IntPtr 句柄），不依赖 PInvoke.AdvApi32 封装，
    /// 以避免该封装中 CreateService 的 lpdwTagId 参数声明错误引发的 0xC0000005 访问违规。
    /// 失败时抛出带真实系统错误信息的 Win32Exception，便于定位（如“拒绝访问”）。
    /// </summary>
    public static class ServiceInstallUtil
    {
        private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        private const uint SERVICE_ALL_ACCESS = 0xF01FF;
        private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const uint SERVICE_ERROR_NORMAL = 0x00000001;

        private const uint SERVICE_CONTROL_STOP = 0x00000001;
        private const uint SERVICE_STOPPED = 0x00000001;
        private const uint SERVICE_STOP_PENDING = 0x00000003;

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateService(
            IntPtr hSCManager,
            string lpServiceName,
            string lpDisplayName,
            uint dwDesiredAccess,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string lpBinaryPathName,
            string? lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string? lpDependencies,
            string? lpServiceStartName,
            string? lpPassword);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, string[]? lpServiceArgVectors);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr hService);

        [SupportedOSPlatform("windows")]
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hService);

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        /// <summary>
        /// 安装并启动服务
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void InstallAndStartService(string serviceName, string binaryPath, ServiceStartType startType = ServiceStartType.SERVICE_AUTO_START)
        {
            var hSCManager = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSCManager == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"打开服务管理器失败（无法创建服务 {serviceName}）");
            }

            try
            {
                var hService = OpenService(hSCManager, serviceName, SERVICE_ALL_ACCESS);
                if (hService == IntPtr.Zero)
                {
                    // 路径含空格时必须加引号，否则 SCM 会把 "C:\Program Files\..." 误解析为可执行文件 + 参数
                    var fullPath = Path.GetFullPath(binaryPath);
                    var quotedPath = fullPath.Contains(' ') ? $"\"{fullPath}\"" : fullPath;

                    hService = CreateService(
                        hSCManager,
                        serviceName,
                        serviceName,
                        SERVICE_ALL_ACCESS,
                        SERVICE_WIN32_OWN_PROCESS,
                        (uint)startType,
                        SERVICE_ERROR_NORMAL,
                        quotedPath,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        null);

                    if (hService == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"创建服务 {serviceName} 失败");
                    }
                }

                try
                {
                    if (StartService(hService, 0, null) == false)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"启动服务 {serviceName} 失败");
                    }
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(hSCManager);
            }
        }

        /// <summary>
        /// 停止并删除服务（不存在时视为已删除，直接返回）
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static void StopAndDeleteService(string serviceName)
        {
            var hSCManager = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
            if (hSCManager == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"打开服务管理器失败（无法停止/删除服务 {serviceName}）");
            }

            try
            {
                var hService = OpenService(hSCManager, serviceName, SERVICE_ALL_ACCESS);
                if (hService == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    var status = new SERVICE_STATUS();
                    if (QueryServiceStatus(hService, ref status) &&
                        status.dwCurrentState != SERVICE_STOPPED &&
                        status.dwCurrentState != SERVICE_STOP_PENDING)
                    {
                        ControlService(hService, SERVICE_CONTROL_STOP, ref status);
                    }

                    if (DeleteService(hService) == false)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"删除服务 {serviceName} 失败");
                    }
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(hSCManager);
            }
        }
    }
}
