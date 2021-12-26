﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TSManager.Core.Plugin
{
    public abstract class TSMPluginBase
    {
        public string Name { get; }
        public abstract void Initialize();
        public virtual bool Dispose()
        {

        }
    }
}
