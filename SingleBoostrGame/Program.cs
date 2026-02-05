using Microsoft.Win32;
using Steam4NET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamTimeBooster
{
    class Program
    {
        private static List<Process> _childProcesses = new List<Process>();
        private static Dictionary<uint, string> _appIdToGameName = new Dictionary<uint, string>();
        private static readonly object _lockObj = new object();

        private static bool ParseAppIds(string input, out uint[] appIds)
        {
            appIds = Array.Empty<uint>();
            var appIdList = new List<uint>();

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string[] inputParts = input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in inputParts)
            {
                if (uint.TryParse(part.Trim(), out uint appId) && appId > 0)
                {
                    appIdList.Add(appId);
                }
                else
                {
                    Console.WriteLine($"[忽略] 无效AppId：{part}");
                }
            }

            if (appIdList.Count == 0) return false;
            appIds = appIdList.Distinct().ToArray();
            return true;
        }

        private static string GetUnicodeString(string str)
        {
            byte[] bytes = Encoding.Default.GetBytes(str);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string GetGameNameByAppId(uint appId)
        {
            ISteamClient012 steamClient = null;
            ISteamApps001 steamApps = null;
            int pipe = 0;
            int user = 0;

            try
            {
                Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(), EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(), EnvironmentVariableTarget.Process);

                if (!Steamworks.Load(true))
                {
                    return "未知游戏";
                }

                steamClient = Steamworks.CreateInterface<ISteamClient012>();
                if (steamClient == null)
                {
                    return "未知游戏";
                }

                pipe = steamClient.CreateSteamPipe();
                if (pipe == 0)
                {
                    return "未知游戏";
                }

                user = steamClient.ConnectToGlobalUser(pipe);
                if (user == 0)
                {
                    return "未知游戏";
                }

                steamApps = steamClient.GetISteamApps<ISteamApps001>(user, pipe);
                if (steamApps == null)
                {
                    return "未知游戏";
                }

                var sb = new StringBuilder(60);
                steamApps.GetAppData(appId, "name", sb);
                string gameName = sb.ToString().Trim();
                return string.IsNullOrWhiteSpace(gameName) ? $"未知游戏(AppId_{appId})" : GetUnicodeString(gameName);
            }
            catch
            {
                return $"未知游戏(AppId_{appId})";
            }
            finally
            {
                if (steamApps != null)
                {
                    try
                    {
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(steamApps);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static int SelectMode()
        {
            Console.Clear();
            Console.WriteLine("======================================");
            Console.WriteLine("Steam刷时长工具");
            Console.WriteLine("Made by Vanwater");
            Console.WriteLine("邮箱：vanwater@126.com");
            Console.WriteLine("======================================");
            Console.WriteLine("  请选择操作模式：");
            Console.WriteLine("  1. 手动输入AppId ");
            Console.WriteLine("  2. 读取gameindex.txt ");
            Console.WriteLine("  3. 进入命令模式 ");
            Console.WriteLine("  4. 退出程序 ");
            Console.WriteLine("======================================");
            Console.Write("  输入模式编号（1/2/3/4）：");

            while (true)
            {
                string input = Console.ReadLine()?.Trim();
                if (int.TryParse(input, out int mode) && mode >= 1 && mode <= 4)
                {
                    return mode;
                }
                Console.Write("  输入无效，请重新输入1、2、3或4：");
            }
        }
        private static bool ParseManualInput(out uint[] appIds)
        {
            Console.WriteLine("======================================");
            Console.WriteLine("  请输入AppId：");
            string input = Console.ReadLine()?.Trim() ?? string.Empty;
            return ParseAppIds(input, out appIds);
        }

        private static bool ParseGameIndexFile(out uint[] appIds)
        {
            appIds = Array.Empty<uint>();
            var appIdList = new List<uint>();
            string filePath = "gameindex.txt";

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[错误] 未找到{filePath}，请先创建并填入AppId");
                return false;
            }

            try
            {
                string fileContent = File.ReadAllText(filePath, Encoding.UTF8).Trim();
                return ParseAppIds(fileContent, out appIds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[错误] 读取{filePath}失败：{ex.Message}");
                appIds = Array.Empty<uint>();
                return false;
            }
        }

        private static bool GetAppIdsByMode(int mode, out uint[] appIds)
        {
            appIds = Array.Empty<uint>();
            switch (mode)
            {
                case 1: return ParseManualInput(out appIds);
                case 2: return ParseGameIndexFile(out appIds);
                default: return true;
            }
        }

        private static void StartChildProcess(uint appId)
        {
            if (!_appIdToGameName.ContainsKey(appId))
            {
                _appIdToGameName.Add(appId, GetGameNameByAppId(appId));
            }
            string gameName = _appIdToGameName[appId];

            Process process = new Process();
            process.StartInfo.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
            process.StartInfo.Arguments = appId.ToString();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = false;
            process.EnableRaisingEvents = true;

            process.Exited += (sender, e) =>
            {
                Process currentProcess = sender as Process;
                if (currentProcess == null) return;

                lock (_lockObj)
                {
                    if (_childProcesses.Contains(currentProcess))
                    {
                        _childProcesses.Remove(currentProcess);
                    }
                }

                uint currentAppId = 0;
                if (uint.TryParse(currentProcess.StartInfo.Arguments, out currentAppId))
                {
                    string currentGameName = _appIdToGameName.ContainsKey(currentAppId) ? _appIdToGameName[currentAppId] : $"未知游戏(AppId_{currentAppId})";
                    Console.WriteLine($"[子进程退出] {currentGameName}（AppId：{currentAppId}）");
                }
                else
                {
                    Console.WriteLine($"[子进程退出] 未知进程（进程ID：{currentProcess.Id}）");
                }
            };

            try
            {
                process.Start();
                lock (_lockObj)
                {
                    if (!_childProcesses.Contains(process))
                    {
                        _childProcesses.Add(process);
                    }
                }
                Console.WriteLine($"[子进程启动] {gameName}（AppId：{appId} | 进程ID：{process.Id}）");
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[子进程失败] {gameName} 启动失败：{ex.Message}");
            }
        }

        private static void StopSingleChildProcess(uint appId)
        {
            string gameName = _appIdToGameName.ContainsKey(appId) ? _appIdToGameName[appId] : $"未知游戏(AppId_{appId})";
            Process targetProcess = null;

            lock (_lockObj)
            {
                foreach (var process in _childProcesses.ToList())
                {
                    if (uint.TryParse(process.StartInfo.Arguments, out uint runningAppId) && runningAppId == appId)
                    {
                        targetProcess = process;
                        break;
                    }
                }

                if (targetProcess == null)
                {
                    Console.WriteLine($"[错误] 未找到运行中的 {gameName}（AppId：{appId}）进程");
                    return;
                }
            }

            try
            {
                if (!targetProcess.HasExited)
                {
                    targetProcess.EnableRaisingEvents = false;
                    targetProcess.Kill();
                    bool exited = targetProcess.WaitForExit(3000);
                    lock (_lockObj)
                    {
                        if (_childProcesses.Contains(targetProcess))
                        {
                            _childProcesses.Remove(targetProcess);
                        }
                    }

                    if (exited)
                    {
                        Console.WriteLine($"[已关闭] {gameName}（AppId：{appId} | 进程ID：{targetProcess.Id}）");
                    }
                    else
                    {
                        Console.WriteLine($"[强制关闭] {gameName}（AppId：{appId} | 进程ID：{targetProcess.Id}）（等待超时，强制终止）");
                    }
                }
                else
                {
                    lock (_lockObj)
                    {
                        if (_childProcesses.Contains(targetProcess))
                        {
                            _childProcesses.Remove(targetProcess);
                        }
                    }
                    Console.WriteLine($"[已退出] {gameName}（AppId：{appId}）进程已提前退出");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[关闭失败] {gameName}（AppId：{appId} | 进程ID：{targetProcess.Id}）：{ex.Message}");
            }
        }

        private static void ProcessPrint()
        {
            for (int i = 1; i < 11; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    Console.Write("**");

                }
                System.Threading.Thread.Sleep(100);
                Console.Write($"{i}0%");
                System.Threading.Thread.Sleep(100);
                Console.Clear();
                Console.WriteLine();
            }
        }

        private static void CloseAllChildProcesses()
        {
            List<Process> processesToClose = new List<Process>();

            lock (_lockObj)
            {
                processesToClose.AddRange(_childProcesses);
                _childProcesses.Clear();
            }

            if (processesToClose.Count == 0)
            {
                Console.WriteLine("[执行] 无运行中的挂机进程");
                return;
            }

            Console.WriteLine($"[执行] 正在关闭 {processesToClose.Count} 个挂机进程...");
            foreach (var process in processesToClose)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.EnableRaisingEvents = false;
                        process.Kill();
                        bool exited = process.WaitForExit(3000);
                        string gameName = "未知游戏";
                        uint appId = 0;
                        if (uint.TryParse(process.StartInfo.Arguments, out appId))
                        {
                            gameName = _appIdToGameName.ContainsKey(appId) ? _appIdToGameName[appId] : $"AppId_{appId}";
                        }

                        if (exited)
                        {
                            Console.WriteLine($"[已关闭] {gameName}（AppId：{appId} | 进程ID：{process.Id}）");
                        }
                        else
                        {
                            Console.WriteLine($"[强制关闭] {gameName}（AppId：{appId} | 进程ID：{process.Id}）");
                        }
                    }
                }
                catch (Exception ex)
                {
                    string gameName = "未知游戏";
                    uint appId = 0;
                    if (uint.TryParse(process.StartInfo.Arguments, out appId))
                    {
                        gameName = _appIdToGameName.ContainsKey(appId) ? _appIdToGameName[appId] : $"AppId_{appId}";
                    }
                    Console.WriteLine($"[关闭失败] {gameName}（AppId：{appId} | 进程ID：{process.Id}）：{ex.Message}");
                }
            }
            Console.WriteLine("[执行] 所有进程关闭操作已完成");
        }

        private static List<uint> GetLocalSteamLibraryAppIds()
        {
            List<uint> localAppIds = new List<uint>();
            string steamInstallPath = string.Empty;

            try
            {
                using (RegistryKey key64 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    steamInstallPath = key64?.GetValue("InstallPath")?.ToString() ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(steamInstallPath))
                {
                    using (RegistryKey key32 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                    {
                        steamInstallPath = key32?.GetValue("InstallPath")?.ToString() ?? string.Empty;
                    }
                }
                if (string.IsNullOrWhiteSpace(steamInstallPath) || !Directory.Exists(steamInstallPath))
                {
                    Console.WriteLine("未找到Steam安装路径，请确保Steam已安装并登录");
                    return localAppIds;
                }

                string libraryVdfPath = Path.Combine(steamInstallPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryVdfPath))
                {
                    Console.WriteLine("未找到 libraryfolders.vdf 文件，Steam可能未创建本地库配置");
                    return localAppIds;
                }

                string vdfContent = File.ReadAllText(libraryVdfPath, new UTF8Encoding(false))
                    .Replace("\\\\", "\\");
                string[] vdfLines = vdfContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                HashSet<uint> appIdSet = new HashSet<uint>();
                bool isInAppsNode = false;

                foreach (string line in vdfLines)
                {
                    string cleanLine = line.Trim();
                    if (string.IsNullOrWhiteSpace(cleanLine)) continue;

                    if (cleanLine.Trim('"').Equals("apps", StringComparison.OrdinalIgnoreCase))
                    {
                        isInAppsNode = true;
                        continue;
                    }

                    if (isInAppsNode && cleanLine.Equals("}"))
                    {
                        isInAppsNode = false;
                        continue;
                    }

                    if (isInAppsNode)
                    {
                        string[] lineParts = cleanLine.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lineParts.Length >= 1)
                        {
                            string appIdStr = lineParts[0].Trim().Trim('"');
                            if (uint.TryParse(appIdStr, out uint appId) && appId > 0)
                            {
                                appIdSet.Add(appId);
                            }
                        }
                    }
                }

                localAppIds = appIdSet.ToList();
                Console.WriteLine($"直接从 libraryfolders.vdf 的 apps 节点提取到 {localAppIds.Count} 个有效Steam游戏AppId");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析 libraryfolders.vdf 的 apps 节点失败：{ex.Message}");
            }

            return localAppIds;
        }

        private static void RunBatchInteractiveMode()
        {
            Console.Clear();
            ProcessPrint();
            Console.Clear();
            Console.WriteLine("======================================");
            Console.WriteLine("Steam刷时长工具");
            Console.WriteLine("Made by Vanwater");
            Console.WriteLine("邮箱：vanwater@126.com");
            Console.WriteLine("======================================");
            Console.WriteLine("  支持命令（大小写不敏感）：");
            Console.WriteLine("  1. Stop [AppId]   关闭指定AppId挂机进程");
            Console.WriteLine("  2. List           查看当前运行的挂机进程");
            Console.WriteLine("  3. StopAll        一键关闭所有挂机进程");
            Console.WriteLine("  4. Help           重新显示命令列表");
            Console.WriteLine("  5. Exit           回到初始模式选择界面");
            Console.WriteLine("  6. File           编辑gameindex.txt内容");
            Console.WriteLine("  7. Version        获取程序版本信息");
            Console.WriteLine("  9. Clear          清屏");
            Console.WriteLine("======================================");
            lock (_lockObj)
            {
                Console.WriteLine($"  运行中子进程数：{_childProcesses.Count}");
                if (_childProcesses.Count == 0)
                {
                    Console.WriteLine("  无运行中的挂机进程");
                }
                else
                {
                    foreach (var process in _childProcesses)
                    {
                        if (uint.TryParse(process.StartInfo.Arguments, out uint _appId))
                        {
                            string gameName = _appIdToGameName.ContainsKey(_appId) ? _appIdToGameName[_appId] : $"AppId_{_appId}";
                            Console.WriteLine($"  - {gameName}（AppId：{_appId} | 进程ID：{process.Id}）");
                        }
                    }
                }
            }
            Console.WriteLine("======================================");
            while (true)
            {
                Console.Write("> ");
                string input = Console.ReadLine()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input)) continue;

                string[] cmdParts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string mainCmd = cmdParts[0].ToLower();

                switch (mainCmd)
                {
                    case "stop":
                        if (cmdParts.Length < 2)
                        {
                            Console.WriteLine("[错误] 缺少AppId，格式：Stop 730 或 Stop 730,883710 或 Stop 730 883710");
                            break;
                        }
                        string stopInput = string.Join(" ", cmdParts.Skip(1));
                        if (ParseAppIds(stopInput, out uint[] stopAppIds))
                        {
                            foreach (var appId in stopAppIds)
                            {
                                StopSingleChildProcess(appId);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[错误] 无有效AppId，无法执行停止操作");
                        }
                        break;

                    case "list":
                        Console.WriteLine("======================================");
                        lock (_lockObj)
                        {
                            Console.WriteLine($"  运行中子进程数：{_childProcesses.Count}");
                            if (_childProcesses.Count == 0)
                            {
                                Console.WriteLine("  无运行中的挂机进程");
                            }
                            else
                            {
                                foreach (var process in _childProcesses)
                                {
                                    if (uint.TryParse(process.StartInfo.Arguments, out uint _appId))
                                    {
                                        string gameName = _appIdToGameName.ContainsKey(_appId) ? _appIdToGameName[_appId] : $"AppId_{_appId}";
                                        Console.WriteLine($"  - {gameName}（AppId：{_appId} | 进程ID：{process.Id}）");
                                    }
                                }
                            }
                        }
                        Console.WriteLine("======================================");
                        break;

                    case "stopall":
                        Console.WriteLine("[执行] 正在关闭所有挂机进程...");
                        CloseAllChildProcesses();
                        Console.WriteLine("[执行] 所有进程已关闭/无运行进程");
                        break;

                    case "help":
                        Console.WriteLine("======================================");
                        Console.WriteLine("  支持命令：");
                        Console.WriteLine("  Stop [AppId] / List / StopAll / Help / Exit / Version / File / Clear");
                        Console.WriteLine("======================================");
                        break;

                    case "exit":
                        Console.WriteLine("[执行] 正在清理资源，回到模式选择界面");
                        CloseAllChildProcesses();
                        _appIdToGameName.Clear();
                        Console.WriteLine("[执行] 已准备就绪，即将回到模式选择界面");
                        System.Threading.Thread.Sleep(1000);
                        return;
                    case "version":
                        Console.WriteLine("======================================");
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        string assemblyName = assembly.GetName().Name;
                        Console.WriteLine("程序集名称：" + assemblyName);
                        Version assemblyVersion = assembly.GetName().Version;
                        Console.WriteLine("程序集版本号：" + assemblyVersion);
                        Type[] types = assembly.GetTypes();
                        foreach (Type type in types)
                        {
                            Console.WriteLine("类型：" + type.FullName);
                        }
                        Console.WriteLine("作者：Vanwater");
                        Console.WriteLine("该软件调用了Steam4NET程序集，GitHub:https://github.com/SteamRE/Steam4NET");
                        Console.WriteLine("该程序不用于盈利哦");
                        break;
                    case "file":
                        string filePath = "gameindex.txt";
                        Process.Start(new ProcessStartInfo(filePath)
                        {
                            UseShellExecute = true
                        });
                        break;
                    case "clear":
                        ForceHardClear();
                        Console.WriteLine("======================================");
                        Console.WriteLine("Steam刷时长工具");
                        Console.WriteLine("Made by Vanwater");
                        Console.WriteLine("邮箱：vanwater@126.com");
                        Console.WriteLine("======================================");
                        Console.WriteLine("  支持命令（大小写不敏感）：");
                        Console.WriteLine("  1. Stop [AppId]   关闭指定AppId挂机进程");
                        Console.WriteLine("  2. List           查看当前运行的挂机进程");
                        Console.WriteLine("  3. StopAll        一键关闭所有挂机进程");
                        Console.WriteLine("  4. Help           重新显示命令列表");
                        Console.WriteLine("  5. Exit           回到初始模式选择界面");
                        Console.WriteLine("  6. File           编辑gameindex.txt内容");
                        Console.WriteLine("  7. Version        获取程序版本信息");
                        Console.WriteLine("  9. Clear          清屏");
                        Console.WriteLine("======================================");
                        break;
                    default:
                        Console.WriteLine($"[错误] 无效命令：{mainCmd}，输入Help查看支持的命令");
                        break;
                }
            }
        }

        static void ForceHardClear()
        {
            try
            {
                Console.BufferWidth = Console.WindowWidth;
                Console.BufferHeight = Console.WindowHeight;
                Console.CursorVisible = false;
                int screenWidth = Console.WindowWidth;
                int screenHeight = Console.WindowHeight;
                for (int row = 0; row < screenHeight; row++)
                {
                    Console.SetCursorPosition(0, row);
                    Console.Write(new string(' ', screenWidth));
                }

                Console.SetCursorPosition(0, 0);
                Console.CursorVisible = true;
            }
            catch (Exception ex)
            {
                Console.CursorVisible = true;
                Console.SetCursorPosition(0, 0);
                Console.Write(new string(' ', Console.WindowWidth * Console.WindowHeight));
                Console.SetCursorPosition(0, 0);
            }
        }

        private static void RunCommandMode()
        {
            Console.Clear();
            Console.WriteLine("======================================");
            Console.WriteLine("Steam刷时长工具");
            Console.WriteLine("Made by Vanwater");
            Console.WriteLine("邮箱：vanwater@126.com");
            Console.WriteLine("======================================");
            Console.WriteLine("  支持命令（大小写不敏感）：");
            Console.WriteLine("  1. Start [AppId]  启动指定AppId挂机（支持多ID，逗号/空格分隔）");
            Console.WriteLine("  2. Stop [AppId]   关闭指定AppId挂机（支持多ID，逗号/空格分隔）");
            Console.WriteLine("  3. List           查看当前运行的挂机进程");
            Console.WriteLine("  4. StopAll        一键关闭所有挂机进程");
            Console.WriteLine("  5. Help           重新显示命令列表");
            Console.WriteLine("  6. File           编辑gameindex.txt内容");
            Console.WriteLine("  7. Version        获取程序信息");
            Console.WriteLine("  8. All            一键启动本地Steam库所有游戏挂机");
            Console.WriteLine("  9. Exit           回到初始模式选择界面");
            Console.WriteLine("  10. Clear         清屏");
            Console.WriteLine("======================================");

            while (true)
            {
                Console.Write("> ");
                string input = Console.ReadLine()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(input)) continue;

                string[] cmdParts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string mainCmd = cmdParts[0].ToLower();

                switch (mainCmd)
                {
                    case "start":
                        if (cmdParts.Length < 2)
                        {
                            Console.WriteLine("[错误] 缺少AppId");
                            break;
                        }
                        string startInput = string.Join(" ", cmdParts.Skip(1));
                        if (ParseAppIds(startInput, out uint[] startAppIds))
                        {
                            Console.WriteLine($"[执行] 开始启动 {startAppIds.Length} 个游戏挂机进程...");
                            foreach (var appId in startAppIds)
                            {
                                StartChildProcess(appId);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[错误] 无有效AppId，无法执行启动操作");
                        }
                        break;

                    case "stop":
                        if (cmdParts.Length < 2)
                        {
                            Console.WriteLine("[错误] 缺少AppId，格式：Stop 730 或 Stop 730,883710 或 Stop 730 883710");
                            break;
                        }
                        string stopInput = string.Join(" ", cmdParts.Skip(1));
                        if (ParseAppIds(stopInput, out uint[] stopAppIds))
                        {
                            Console.WriteLine($"[执行] 开始关闭 {stopAppIds.Length} 个游戏挂机进程...");
                            foreach (var appId in stopAppIds)
                            {
                                StopSingleChildProcess(appId);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[错误] 无有效AppId，无法执行停止操作");
                        }
                        break;

                    case "list":
                        Console.WriteLine("======================================");
                        lock (_lockObj)
                        {
                            Console.WriteLine($"  运行中子进程数：{_childProcesses.Count}");
                            if (_childProcesses.Count == 0)
                            {
                                Console.WriteLine("  无运行中的挂机进程");
                            }
                            else
                            {
                                foreach (var process in _childProcesses)
                                {
                                    if (uint.TryParse(process.StartInfo.Arguments, out uint _appId))
                                    {
                                        string gameName = _appIdToGameName.ContainsKey(_appId) ? _appIdToGameName[_appId] : $"AppId_{_appId}";
                                        Console.WriteLine($"  - {gameName}（AppId：{_appId} | 进程ID：{process.Id}）");
                                    }
                                }
                            }
                        }
                        Console.WriteLine("======================================");
                        break;

                    case "stopall":
                        Console.WriteLine("[执行] 正在关闭所有挂机进程");
                        CloseAllChildProcesses();
                        Console.WriteLine("[执行] 所有进程已关闭/无运行进程");
                        break;

                    case "help":
                        Console.WriteLine("======================================");
                        Console.WriteLine("  支持命令：");
                        Console.WriteLine("  Start [AppId] / Stop [AppId] / List / StopAll / All / Help / Exit / Version / File / Clear");
                        Console.WriteLine("======================================");
                        break;

                    case "version":
                        Console.WriteLine("======================================");
                        Assembly assembly = Assembly.GetExecutingAssembly();
                        string assemblyName = assembly.GetName().Name;
                        Console.WriteLine("程序集名称：" + assemblyName);
                        Version assemblyVersion = assembly.GetName().Version;
                        Console.WriteLine("程序集版本号：" + assemblyVersion);
                        Type[] types = assembly.GetTypes();
                        foreach (Type type in types)
                        {
                            Console.WriteLine("类型：" + type.FullName);
                        }
                        Console.WriteLine("作者：Vanwater");
                        Console.WriteLine("该软件调用了Steam4NET程序集，GitHub:https://github.com/SteamRE/Steam4NET");
                        Console.WriteLine("该程序不用于盈利哦");
                        break;

                    case "file":
                        Console.WriteLine("======================================");
                        Console.WriteLine("正在打开文件");
                        string filePath = "gameindex.txt";
                        Process.Start(new ProcessStartInfo(filePath)
                        {
                            UseShellExecute = true
                        });
                        Console.WriteLine("打开文件成功");
                        break;

                    case "all":
                        Console.WriteLine("======================================");
                        Console.WriteLine("[执行] 开始获取本地Steam库所有游戏...");
                        List<uint> localAppIds = GetLocalSteamLibraryAppIds();

                        if (localAppIds.Count == 0)
                        {
                            Console.WriteLine("[提示] 无有效本地游戏AppId，无法启动挂机");
                            break;
                        }

                        Console.WriteLine($"[执行] 开始批量启动 {localAppIds.Count} 个游戏挂机进程...");
                        foreach (uint appId_temp in localAppIds)
                        {
                            StartChildProcess(appId_temp);
                        }
                        break;

                    case "exit":
                        Console.WriteLine("[执行] 正在清理资源，回到模式选择界面...");
                        CloseAllChildProcesses();
                        _appIdToGameName.Clear();
                        Console.WriteLine("[执行] 已准备就绪，即将回到模式选择界面");
                        System.Threading.Thread.Sleep(1000);
                        return;

                    case "clear":
                        ForceHardClear();
                        Console.WriteLine("======================================");
                        Console.WriteLine("Steam刷时长工具");
                        Console.WriteLine("Made by Vanwater");
                        Console.WriteLine("邮箱：vanwater@126.com");
                        Console.WriteLine("======================================");
                        Console.WriteLine("  支持命令（大小写不敏感）：");
                        Console.WriteLine("  1. Start [AppId]  启动指定AppId挂机（支持多ID，逗号/空格分隔）");
                        Console.WriteLine("  2. Stop [AppId]   关闭指定AppId挂机（支持多ID，逗号/空格分隔）");
                        Console.WriteLine("  3. List           查看当前运行的挂机进程");
                        Console.WriteLine("  4. StopAll        一键关闭所有挂机进程");
                        Console.WriteLine("  5. Help           重新显示命令列表");
                        Console.WriteLine("  6. File           编辑gameindex.txt内容");
                        Console.WriteLine("  7. Version        获取程序信息");
                        Console.WriteLine("  8. All            一键启动本地Steam库所有游戏挂机");
                        Console.WriteLine("  9. Exit           回到初始模式选择界面");
                        Console.WriteLine("  10. Clear         清屏");
                        Console.WriteLine("======================================");
                        break;

                    default:
                        Console.WriteLine($"[错误] 无效命令：{mainCmd}，输入Help查看支持的命令");
                        break;
                }
            }
        }

        private static void FileOpen(int mode, uint[] appIds)
        {
            if (!GetAppIdsByMode(mode, out appIds) || appIds.Length == 0)
            {
                Console.WriteLine("无法获取有效AppId，3秒后回到模式选择界面");
                System.Threading.Thread.Sleep(3000);
                return;
            }

            Console.WriteLine("正在获取游戏名称，请稍候...");
            foreach (uint appId in appIds)
            {
                if (!_appIdToGameName.ContainsKey(appId))
                {
                    _appIdToGameName.Add(appId, GetGameNameByAppId(appId));
                }
            }
            Console.WriteLine($"成功获取 {appIds.Length} 个有效AppId，开始启动子进程");
            System.Threading.Thread.Sleep(1000);

            foreach (uint appId in appIds)
            {
                StartChildProcess(appId);
            }

            RunBatchInteractiveMode();
        }

        static void Main(string[] args)
        {
            Console.Title = "Steam刷时长工具 - Made by Vanwater";

            if (args.Length == 1 && uint.TryParse(args[0], out uint singleAppId) && singleAppId > 0)
            {
                RunSingleAppIdBoostr(singleAppId);
                return;
            }

            while (true)
            {
                int mode = SelectMode();
                uint[] appIds = Array.Empty<uint>();

                switch (mode)
                {
                    case 1:
                    case 2:
                        FileOpen(mode, appIds);
                        break;
                    case 3:
                        RunCommandMode();
                        break;
                    case 4:
                        Console.Clear();
                        Console.WriteLine("Made by Vanwater");
                        Console.WriteLine("邮箱：vanwater@126.com");
                        Console.WriteLine("非盈利使用,感谢使用！");
                        System.Threading.Thread.Sleep(3000);
                        Environment.Exit(0);
                        break;
                }
            }
        }

        private static void RunSingleAppIdBoostr(uint appId)
        {
            ISteamClient012 steamClient = null;
            ISteamApps001 steamApps = null;
            int pipe = 0;
            int user = 0;

            bool SteamConfirmAppId()
            {
                try
                {
                    Environment.SetEnvironmentVariable("SteamAppId", appId.ToString(), EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable("SteamGameId", appId.ToString(), EnvironmentVariableTarget.Process);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            bool ConnectToSteam()
            {
                Console.WriteLine($"[单进程挂机] 正在连接Steam（AppId：{appId}）...");
                if (!SteamConfirmAppId()) return false;
                if (!Steam4NET.Steamworks.Load(true)) return false;

                steamClient = Steam4NET.Steamworks.CreateInterface<Steam4NET.ISteamClient012>();
                if (steamClient == null) return false;

                pipe = steamClient.CreateSteamPipe();
                if (pipe == 0) return false;

                user = steamClient.ConnectToGlobalUser(pipe);
                if (user == 0) return false;

                steamApps = steamClient.GetISteamApps<Steam4NET.ISteamApps001>(user, pipe);
                if (steamApps == null) return false;

                var sb = new StringBuilder(60);
                steamApps.GetAppData(appId, "name", sb);
                string gameName = sb.ToString().Trim();
                gameName = string.IsNullOrWhiteSpace(gameName) ? $"AppId_{appId}" : GetUnicodeString(gameName);
                Console.WriteLine($"[单进程挂机] 已绑定 {gameName}（AppId：{appId}），开始挂机");
                return true;
            }

            if (!ConnectToSteam())
            {
                Console.WriteLine($"[单进程挂机] AppId {appId} 连接Steam失败。");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("[单进程挂机] 此窗口为子进程，关闭窗口即可停止挂机");
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
            }
        }
    }
}