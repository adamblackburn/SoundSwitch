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
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SoundSwitch.Framework
{
    public class WindowsAPIAdapter : Form
    {
        public enum RestartManagerEventType
        {
            Query,
            EndSession,
            ForceClose
        }

        /**
            #define WM_QUERYENDSESSION              0x0011
            #define WM_ENDSESSION                   0x0016
            #define ENDSESSION_CLOSEAPP         0x00000001
            #define WM_CLOSE                        0x0010
            #define WM_DEVICECHANGE                 0x0219
        */
        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;
        private const int ENDSESSION_CLOSEAPP = 0x00000001;
        private const int WM_CLOSE = 0x0010;
        private const int WM_DEVICECHANGE = 0x0219;
        private const int WM_HOTKEY = 0x0312;
        private static WindowsAPIAdapter _instance;
        private readonly Dictionary<HotKeys, int> _registeredHotkeys = new Dictionary<HotKeys, int>();
        private int _hotKeyId;

        private WindowsAPIAdapter()
        {
        }

        private static readonly AutoResetEvent Instancied  = new AutoResetEvent(false);
        public static event EventHandler<RestartManagerEvent> RestartManagerTriggered;
        public static event EventHandler<DeviceChangeEvent> DeviceChanged;
        public static event EventHandler<KeyPressedEventArgs> HotKeyPressed;

        /// <summary>
        ///     Star the Adapter thread
        /// </summary>
        public static void Start()
        {
            if (_instance != null)
                throw new InvalidOperationException("Adapter already started");

            var t = new Thread(RunForm) {Name = typeof (WindowsAPIAdapter).Name};
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>
        ///     Stop the adapter thread
        /// </summary>
        public static void Stop()
        {
            if (_instance == null)
                throw new InvalidOperationException("Adapter not started");

            RestartManagerTriggered = null;
            DeviceChanged = null;
            HotKeyPressed = null;

            if (!_instance.IsDisposed)
            {
                try
                {
                    _instance.Invoke(new MethodInvoker(_instance.EndForm));
                }
                catch (Exception ex)
                {
                    //Can happen when the instance got dispose in its own thread
                    //when in the same time the Application thread call the Stop() method.
                    Trace.WriteLine("Thread Race Condition: " + ex);
                }
            }
        }

        /// <summary>
        ///     Add an Exception handler for the Adapter thread
        /// </summary>
        /// <param name="handler"></param>
        public static void AddThreadExceptionHandler(ThreadExceptionEventHandler handler)
        {
            Instancied.WaitOne();
            try
            {
                _instance.Invoke(new Action(() => { Application.ThreadException += handler; }));
            }
            finally
            {
                Instancied.Set();
            }
        }

        private static void RunForm()
        {
            _instance = new WindowsAPIAdapter();
            _instance.CreateHandle();
            Instancied.Set();
            Application.Run(_instance);
        }

        private void EndForm()
        {
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var hotKeyId in _instance._registeredHotkeys.Values)
                {
                    NativeMethods.UnregisterHotKey(_instance.Handle, hotKeyId);
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        ///     Registers a HotKey in the system.
        /// </summary>
        /// <param name="hotKeys">Represent the hotkey to register</param>
        public static bool RegisterHotKey(HotKeys hotKeys)
        {
            Instancied.WaitOne();
            try
            {
                return (bool) _instance.Invoke(new Func<bool>(() =>
                {
                    if (_instance._registeredHotkeys.ContainsKey(hotKeys))
                        return false;

                    var id = _instance._hotKeyId++;
                    _instance._registeredHotkeys.Add(hotKeys, id);
                    // register the hot key.
                    return NativeMethods.RegisterHotKey(_instance.Handle, id, (uint) hotKeys.Modifier,
                        (uint) hotKeys.Keys);
                }));
            }
            finally
            {
                Instancied.Set();
            }
        }

        /// <summary>
        ///     Unregister a registered HotKey
        /// </summary>
        /// <param name="hotKeys"></param>
        /// <returns></returns>
        public static bool UnRegisterHotKey(HotKeys hotKeys)
        {
            Instancied.WaitOne();
            try
            {
                return (bool) _instance.Invoke(new Func<bool>(() =>
                {
                    int id;
                    if (!_instance._registeredHotkeys.TryGetValue(hotKeys, out id))
                    {
                        return false;
                    }
                    _instance._registeredHotkeys.Remove(hotKeys);
                    return NativeMethods.UnregisterHotKey(_instance.Handle, id);
                }));
            }
            finally
            {
                Instancied.Set();
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            //Check for shutdown message from windows
            switch (m.Msg)
            {
                case WM_QUERYENDSESSION:
                    if (ConvertLParam(m.LParam) != ENDSESSION_CLOSEAPP)
                        break;
                    var closingEvent = new RestartManagerEvent(RestartManagerEventType.Query);
                    RestartManagerTriggered?.Invoke(this, closingEvent);
                    m.Result = closingEvent.Result;
                    break;
                case WM_ENDSESSION:
                    if (ConvertLParam(m.LParam) != ENDSESSION_CLOSEAPP)
                        break;
                    RestartManagerTriggered?.Invoke(this, new RestartManagerEvent(RestartManagerEventType.EndSession));
                    break;

                case WM_CLOSE:
                    RestartManagerTriggered?.Invoke(this,
                        new RestartManagerEvent(RestartManagerEventType.ForceClose));
                    break;
                case WM_DEVICECHANGE:
                    DeviceChanged?.Invoke(this, new DeviceChangeEvent());
                    break;
                case WM_HOTKEY:
                    ProcessHotKeyEvent(m);
                    break;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        ///     To avoid overflow on 64 bit platform use this method
        /// </summary>
        /// <param name="lParam"></param>
        /// <returns></returns>
        private long ConvertLParam(IntPtr lParam)
        {
            try
            {
                return lParam.ToInt32();
            }
            catch (OverflowException)
            {
                return lParam.ToInt64();
            }
        }

        private void ProcessHotKeyEvent(Message m)
        {
            var key = (Keys) ((ConvertLParam(m.LParam) >> 16) & 0xFFFF);
            var modifier = (HotKeys.ModifierKeys) (ConvertLParam(m.LParam) & 0xFFFF);

            HotKeyPressed?.Invoke(this, new KeyPressedEventArgs(new HotKeys(key, modifier)));
        }

        #region WindowsNativeMethods

        public static class NativeMethods
        {
            // Registers a hot key with Windows.
            [DllImport("user32.dll")]
            internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            // Unregisters the hot key with Windows.
            [DllImport("user32.dll")]
            internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }

        #endregion

        #region Events

        public class RestartManagerEvent : EventArgs
        {
            public RestartManagerEvent(RestartManagerEventType type)
            {
                Result = new IntPtr(0);
                Type = type;
            }

            public IntPtr Result { get; set; }
            public RestartManagerEventType Type { get; }
        }

        public class DeviceChangeEvent : EventArgs
        {
        }

        /// <summary>
        ///     Event Args for the event that is fired after the hot key has been pressed.
        /// </summary>
        public class KeyPressedEventArgs : EventArgs
        {
            public KeyPressedEventArgs(HotKeys hotKeys)
            {
                HotKeys = hotKeys;
            }

            public HotKeys HotKeys { get; set; }
        }

        #endregion
    }
}