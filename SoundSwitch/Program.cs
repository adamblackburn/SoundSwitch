﻿/********************************************************************
* Copyright (C) 2015 Jeroen Pelgrims
* Copyright (C) 2015 Antoine Aflalo
* 
* This program is free software; you can redistribute it and/or
* modify it under the terms of the GNU General Public License
* as published by the Free Software Foundation; either version 2
* of the License, or (at your option) any later version.
* 
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
********************************************************************/

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Forms;
using AudioEndPointControllerWrapper;
using SoundSwitch.Framework;
using SoundSwitch.Framework.Configuration;
using SoundSwitch.Model;
using SoundSwitch.Util;

namespace SoundSwitch
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            AppLogger.Log.Info("Application Starts");
            using (new Mutex(true, Application.ProductName, out createdNew))
            {
                if (!createdNew)
                {
                    AppLogger.Log.Warn("Application already started");
                    return;
                }
                AppModel.Instance.ActiveAudioDeviceLister = new AudioDeviceLister(DeviceState.Active);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
#if !DEBUG
                Application.ThreadException += Application_ThreadException;
#endif
                WindowsAPIAdapter.Start();
                //Manage the Closing events send by Windows
                //Since this app don't use a Form as "main window" the app doesn't close 
                //when it should without this.
                WindowsAPIAdapter.RestartManagerTriggered += (sender, @event) =>
                {
                    using (AppLogger.Log.DebugCall())
                    {
                        AppLogger.Log.Debug("Restart Event recieved", @event);
                        switch (@event.Type)
                        {
                            case WindowsAPIAdapter.RestartManagerEventType.Query:
                                @event.Result = new IntPtr(1);

                                break;
                            case WindowsAPIAdapter.RestartManagerEventType.EndSession:
                            case WindowsAPIAdapter.RestartManagerEventType.ForceClose:
                                AppLogger.Log.Debug("Close Application");
                                Application.Exit();
                                break;
                        }
                    }
                };
                AppLogger.Log.Info("Set Exception Handler");
#if !DEBUG
                WindowsAPIAdapter.AddThreadExceptionHandler(Application_ThreadException);
#endif
                AppLogger.Log.Info("Set Tray Icon with Main");
#if !DEBUG
                try
                {
#endif
                using (var icon = new TrayIcon())
                {
                    if (AppConfigs.Configuration.FirstRun)
                    {
                        icon.ShowSettings();
                        AppConfigs.Configuration.FirstRun = false;
                        AppLogger.Log.Info("First run");
                    }
                    Application.Run();
                    WindowsAPIAdapter.Stop();
                }
#if !DEBUG
                }
               
                catch (Exception ex)
                {
                    HandleException(ex);

                }
#endif
            }
        }

        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleException(e.Exception);
        }

        private static void HandleException(Exception exception)
        {
            AppLogger.Log.Fatal("Exception Occured ", exception);
            var zipFile = Path.Combine(ApplicationPath.AppData,
                $"{Application.ProductName}-crashlog-{DateTime.UtcNow.Date.Day}_{DateTime.UtcNow.Date.Month}_{DateTime.UtcNow.Date.Year}.zip");
            var message =
                $"It seems {Application.ProductName} has crashed.\n" +
                $"Do you want to save a log of the error that ocurred?\n" +
                $"This could be useful to fix bugs. Please post this file in the issues section.\n" +
                $"File Location: " + zipFile;
            var result = MessageBox.Show(message, $"{Application.ProductName} crashed...", MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);

            if (result == DialogResult.Yes)
            {
                using (new HourGlass())
                using (new AppLogger.LogRestartor())
                {
                    if (File.Exists(zipFile))
                    {
                        File.Delete(zipFile);
                    }
                    ZipFile.CreateFromDirectory(ApplicationPath.Default, zipFile);
                }
                Process.Start("explorer.exe", "/select," + @zipFile);
            }
        }
    }
}