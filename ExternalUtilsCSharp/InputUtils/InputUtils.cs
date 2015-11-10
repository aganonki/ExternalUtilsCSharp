﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExternalUtilsCSharp.InputUtils
{
    public class InputUtilities{
        public KeyUtils Keys;
        public MouseHook Mouse;
        private Thread mouseThread;
   
        public InputUtilities()
        {
            Init();
        }

        private void Init()
        {
            Keys = new KeyUtils();
            Mouse = new MouseHook();
            mouseThread = new Thread(()=>Mouse.InstallHook());
            mouseThread.IsBackground = true;
            mouseThread.Start();
        }
        /// <summary>
        /// If true mouse changed since last update
        /// </summary>
        public bool MouseChanged = false;

        /// <summary>
        /// Updates keys and mouse
        /// </summary>
        public void Update()
        {
            Keys.Update();
            MouseChanged = Mouse.Update();
        }
    }
}
