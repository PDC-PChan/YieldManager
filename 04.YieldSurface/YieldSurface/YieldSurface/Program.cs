﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YieldSurface
{
    class Program
    {
        static void Main(string[] args)
        {
            YieldSurModMgr ym = new YieldSurModMgr();
            //ym.TrainModels();
            ym.PopulateYieldSurfaces();
        }
    }
}
