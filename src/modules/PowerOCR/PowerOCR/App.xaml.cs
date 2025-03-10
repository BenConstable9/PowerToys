﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Windows;
using ManagedCommon;
using PowerOCR.Helpers;
using PowerOCR.Keyboard;
using PowerOCR.Settings;

namespace PowerOCR;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application, IDisposable
{
    private KeyboardMonitor? keyboardMonitor;
    private EventMonitor? eventMonitor;
    private Mutex? _instanceMutex;
    private int _powerToysRunnerPid;

    private CancellationTokenSource NativeThreadCTS { get; set; }

    public App()
    {
        NativeThreadCTS = new CancellationTokenSource();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        keyboardMonitor?.Dispose();
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // allow only one instance of PowerOCR
        _instanceMutex = new Mutex(true, @"Local\PowerToys_PowerOCR_InstanceMutex", out bool createdNew);
        if (!createdNew)
        {
            Logger.LogWarning("Another running TextExtractor instance was detected. Exiting TextExtractor");
            _instanceMutex = null;
            Shutdown();
            return;
        }

        if (e.Args?.Length > 0)
        {
            try
            {
                _ = int.TryParse(e.Args[0], out _powerToysRunnerPid);
                Logger.LogInfo($"TextExtractor started from the PowerToys Runner. Runner pid={_powerToysRunnerPid}");

                RunnerHelper.WaitForPowerToysRunner(_powerToysRunnerPid, () =>
                {
                    Logger.LogInfo("PowerToys Runner exited. Exiting TextExtractor");
                    NativeThreadCTS.Cancel();
                    Application.Current.Dispatcher.Invoke(() => Shutdown());
                });
                var userSettings = new UserSettings(new Helpers.ThrottledActionInvoker());
                eventMonitor = new EventMonitor(Application.Current.Dispatcher, NativeThreadCTS.Token);
            }
            catch (Exception ex)
            {
                Logger.LogError($"TextExtractor got an exception on start: {ex}");
            }
        }
        else
        {
            Logger.LogInfo($"TextExtractor started detached from PowerToys Runner.");
            _powerToysRunnerPid = -1;
            var userSettings = new UserSettings(new Helpers.ThrottledActionInvoker());
            keyboardMonitor = new KeyboardMonitor(userSettings);
            keyboardMonitor?.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex != null)
        {
            _instanceMutex.ReleaseMutex();
        }

        base.OnExit(e);
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        Dispose();
    }
}
