﻿/********************************************************************
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
using AudioEndPointControllerWrapper;

namespace SoundSwitch.Model
{

    public class ExceptionEvent : EventArgs
    {
        public ExceptionEvent(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; private set; }
    }

    public class DeviceListChanged : EventArgs
    {
        public DeviceListChanged(IEnumerable<string> seletedDevicesList, AudioDeviceType type)
        {
            SeletedDevicesList = seletedDevicesList;
            Type = type;
        }

        public IEnumerable<string> SeletedDevicesList { get; private set; }
        public AudioDeviceType Type { get; private set; }
    }
}