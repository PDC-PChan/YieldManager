using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YieldSurface
{
    public class MControl
    {
        public static string MasterDirectory = @"Z:\32. Structuring\25. Mezzanine Analysis\03.Tools\04.YieldSurface";
        public static string Directory_Model = MasterDirectory + @"\R_Model";
        public static string Directory_TrainData = MasterDirectory + @"\SurfaceTrainingData";
        public static string Directory_RScripts = MasterDirectory + @"\R_Scripts";
        public static string Directory_SurfaceOutput = MasterDirectory + @"\YieldSurfaceOutput";

        public static string Script_GenerateModel = Directory_RScripts + @"\GenerateModel.R";
        public static string Script_LoadPackages = Directory_RScripts + @"\LoadPackages.R";
        public static string Script_PopulateSurfaces = Directory_RScripts + @"\PrintSurfaces.R";
    }
}
