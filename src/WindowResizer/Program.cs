using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.CommandLine;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices.Custom;
using Windows.Win32;
using winmdroot = global::Windows.Win32;

var titleRxOption = new Option<Regex>(name: "--titleRx", description: "Regex for searching window", parseArgument: result => new Regex(result.Tokens.Single().Value)) { IsRequired = true };
var processNameRxOption = new Option<Regex>(name: "--processNameRx", description: "Regex for searching process");
var processIdOption = new Option<uint?>(name: "--pid", description: "process Id");
var wOption = new Option<ushort>(name: "--w", description: "Width") { IsRequired = true };
var hOption = new Option<ushort>(name: "--h", description: "Height") { IsRequired = true };
var debugOption = new Option<bool>(name: "--debug", description: "Show debug log");

var rootCommand = new RootCommand("Test");
rootCommand.AddOption(titleRxOption);
rootCommand.AddOption(wOption);
rootCommand.AddOption(hOption);
rootCommand.AddOption(debugOption);

static string GetWindowText(winmdroot.Foundation.HWND hWND)
{
    var buffer = ArrayPool<char>.Shared.Rent(2048);
    unsafe
    {
        fixed (char* bufferPtr = buffer)
        {
            if (0 == PInvoke.GetWindowText(hWND, bufferPtr, buffer.Length))
            {
                var err = new Win32Exception();
                if (err.NativeErrorCode != 0) throw err;
            }
        }
    }
    return new string(buffer);
}

static string GetProcessFileName(uint pid)
{
    var hProcess = PInvoke.OpenProcess(winmdroot.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | winmdroot.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ, false, pid);
    if (hProcess == winmdroot.Foundation.HANDLE.Null) throw new Win32Exception();
    var buffer = ArrayPool<char>.Shared.Rent(4096);
    unsafe
    {
        fixed (char* bufferPtr = buffer)
        {
            if (0 == PInvoke.GetModuleBaseName(new DangerousSafeHandle(hProcess), new DangerousSafeHandle(IntPtr.Zero), bufferPtr, (uint)buffer.Length)) throw new Win32Exception();
        }
        if (!PInvoke.CloseHandle(hProcess)) throw new Win32Exception();
    }
    return new string(buffer);
}


rootCommand.SetHandler(
    (titleRx, processNameRx, processId, w, h, debug) =>
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(debug ? LogLevel.Debug : LogLevel.Information));
            var logger = loggerFactory.CreateLogger("Program");

            try
            {
                var hWnds = new List<winmdroot.Foundation.HWND>();

                PInvoke.EnumWindows(
                    new winmdroot.UI.WindowsAndMessaging.WNDENUMPROC((hWnd, _) =>
                    {
                        string title = "UNKNWON";
                        uint pid = 0;
                        string processName = "UNKNOWN";
                        try
                        {
                            title = GetWindowText(hWnd);
                            if (!titleRx.IsMatch(title)) return true;

                            if (processId != null)
                            {
                                unsafe { if (0 == PInvoke.GetWindowThreadProcessId(hWnd, &pid)) throw new Win32Exception(); }
                                if (processId != pid) return true;
                            }


                            if (processNameRx != null)
                            {
                                processName = GetProcessFileName(pid);
                                if (!processNameRx.IsMatch(processName)) return true;
                            }

                            hWnds.Add(hWnd);
                            return true;

                        }
                        catch (Exception e)
                        {
                            logger.LogDebug(e, $"Title: {title}, PID: {pid}, ProcessName: ${processName}");
                            return true;
                        }
                    }),
                    0);

                if (hWnds.Count == 0) throw new Exception("Cannot found window that satisfies provided conditions.");
                if (hWnds.Count > 1) throw new Exception("Found multiple windows that satisfies provided conditions.");

                if (!PInvoke.SetWindowPos(hWnds.Single(), winmdroot.Foundation.HWND.Null, 0, 0, w, h, winmdroot.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOZORDER | winmdroot.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOMOVE))
                    throw new Win32Exception();
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
            finally
            {
                loggerFactory.Dispose();
                loggerFactory.Dispose();
            }
        },
    titleRxOption, processNameRxOption, processIdOption, wOption, hOption, debugOption);


await rootCommand.InvokeAsync(args);