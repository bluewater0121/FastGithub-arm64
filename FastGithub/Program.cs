using Microsoft.AspNetCore.Builder;
using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

#pragma warning disable CA1416 // ServiceBase 仅在 Windows 上受支持；本类的使用已被 OperatingSystem.IsWindows() 守卫
namespace FastGithub
{

    class Program
    {
        /// <summary>
        /// 程序入口
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            ConsoleUtil.DisableQuickEdit();
            var contentRoot = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(contentRoot) == false)
            {
                Environment.CurrentDirectory = contentRoot;
            }
            var options = new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = contentRoot
            };

            // 以 Windows 服务身份被 SCM 拉起时（非交互会话），用自带 ServiceBase 宿主托管，
            // 避免单文件发布与 Microsoft.Extensions.Hosting.Windows 的 UseWindowsService
            // 在查找入口点时抛 "Entry point was not found" 的冲突。
            if (OperatingSystem.IsWindows() && Environment.UserInteractive == false)
            {
                ServiceBase.Run(new FastGithubService(options));
                return;
            }

            CreateWebApplication(options).Run(singleton: true);
        }

        /// <summary>
        /// 创建host
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        private static WebApplication CreateWebApplication(WebApplicationOptions options)
        {
            var builder = WebApplication.CreateBuilder(options);
            builder.ConfigureHost();
            builder.ConfigureWebHost();
            builder.ConfigureConfiguration();
            builder.ConfigureServices();

            var app = builder.Build();
            app.ConfigureApp();
            return app;
        }

        /// <summary>
        /// 以 Windows 服务身份运行的宿主（替代 UseWindowsService，兼容单文件发布）。
        /// 由 SCM 启动 fastgithub.exe（无参数）时进入，负责向 SCM 回报运行状态，
        /// 并在后台线程真正拉起 Kestrel 代理服务。
        /// </summary>
        private sealed class FastGithubService : ServiceBase
        {
            private readonly WebApplicationOptions _options;
            private WebApplication? _app;
            private Thread? _hostThread;

            public FastGithubService(WebApplicationOptions options)
            {
                _options = options;
                ServiceName = "fastgithub";
                CanStop = true;
                CanShutdown = true;
                AutoLog = true;
            }

            protected override void OnStart(string[] args)
            {
                _hostThread = new Thread(() =>
                {
                    try
                    {
                        _app = CreateWebApplication(_options);
                        _app.Run();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            Directory.CreateDirectory("logs");
                            File.AppendAllText(Path.Combine("logs", "service-error.txt"),
                                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
                        }
                        catch { }
                    }
                })
                {
                    IsBackground = false,
                    Name = "FastGithubHost"
                };
                _hostThread.Start();
            }

            protected override void OnStop()
            {
                if (_app is not null)
                {
                    _app.StopAsync().GetAwaiter().GetResult();
                }
                _hostThread?.Join(TimeSpan.FromSeconds(10));
            }
        }
    }
}

#pragma warning restore CA1416
