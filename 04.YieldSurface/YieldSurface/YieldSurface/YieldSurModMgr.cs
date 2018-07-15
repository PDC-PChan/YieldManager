using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RDotNet;
using System.IO;

namespace YieldSurface
{
    public class YieldSurModMgr
    {
        private REngine engine;
        private string ModelName = "LinearModel.DM";
        private string[] RatingArray = new string[] { "BBB", "BB", "B" };
        private string runDate = "";
        private string ModelExtension = ".RData";

        public YieldSurModMgr(string _runDate = "")
        {
            if (_runDate == "")
            {
                runDate = DateTime.Today.ToString("yyyyMMdd");
            }
            else
            {
                runDate = _runDate;
            }
            
            // Initialize R object
            REngine.SetEnvironmentVariables();
            engine = REngine.GetInstance();

            string LoadPackages = string.Format("library(randomForest)");//, MControl.Script_LoadPackages.Replace("\\", "/"));
            engine.Evaluate(LoadPackages);
        }


        public double GetDM(string Rating,string RDataFrame)
        {
            double RequiredDM = 0;
            string LoadDataFrame = string.Format("newData = {0}",RDataFrame);
            string PredictNewData = string.Format("prediction = predict({0},newData)", ModelName);


            this.engine.Evaluate(RetrieveModel(Rating));
            this.engine.Evaluate(LoadDataFrame);
            this.engine.Evaluate(PredictNewData);
            RequiredDM = this.engine.GetSymbol("prediction").AsNumeric()[0];

            return RequiredDM;
        }


        public void TrainModels()
        {
            bool[] ModelExists = ModelTrained();

            for (int i = 0; i < ModelExists.Length; i++)
            {
                if (!ModelExists[i])
                {
                    TrainModel(RatingArray[i]);
                    //engine = REngine.GetInstance();
                }
            }
        }

        public void PopulateYieldSurfaces()
        {
            foreach (string iRating in RatingArray)
            {
                PrintYieldSurface(iRating);
            }
        }

        public void PrintYieldSurface(string Rating)
        {
            string SetRating = string.Format("Rating = '{0}'", Rating);
            string LoadData = string.Format("Data.Final = read.csv('{0}')", (MControl.Directory_TrainData + @"\" + runDate + "_" + Rating + ".csv").Replace("\\", "/"));
            string ModelAlias = string.Format("MyModel  = {0}", ModelName);
            string sourceScript = string.Format("source('{0}')",MControl.Script_PopulateSurfaces.Replace("\\","/"));
            string StartSink = string.Format("sink('{0}')",(MControl.Directory_SurfaceOutput + @"\" + runDate + "_" + Rating + ".csv").Replace("\\","/"));
            string PrintOutput = "lapply(names(All.Surfaces), function(CpnRate) {print(CpnRate)\nwrite.csv(All.Surfaces[[CpnRate]])})";
            string EndSink = "sink()";

            this.engine.Evaluate(RetrieveModel(Rating));
            this.engine.Evaluate(SetRating);
            this.engine.Evaluate(LoadData);
            this.engine.Evaluate(ModelAlias);
            this.engine.Evaluate(sourceScript);
            this.engine.Evaluate(StartSink);
            this.engine.Evaluate(PrintOutput);
            this.engine.Evaluate(EndSink);
        }

        private string RetrieveModel(string Rating)
        {
            string retrieveLine = string.Format("load('{0}')", GetModelPath(runDate, Rating).Replace("\\", "/"));
            return retrieveLine;
        }


        private bool[] ModelTrained()
        {
            
            bool[] resultArray = new bool[RatingArray.Length];

            for (int i = 0; i < RatingArray.Length; i++)
            {
                resultArray[i] = File.Exists(GetModelPath(runDate, RatingArray[i]));
            }

            return resultArray;
        }


        private void TrainModel(string iRating)
        {
            
            string CleanR = string.Format("rm(list=ls())");
            string CallGC = string.Format("gc()");
            string SetRating = string.Format("Rating = '{0}'", iRating);
            string SetAsOfDate = string.Format("LastDate = as.Date('{0}','%Y%m%d')", runDate);
            string GenerateModel = string.Format("source('{0}')",MControl.Script_GenerateModel.Replace("\\","/"));
            string ExportData = string.Format("write.csv(Data.Final,'{0}')",(MControl.Directory_TrainData + @"\" + runDate + "_" + iRating + ".csv").Replace("\\","/"));
            string ExportModel = string.Format("save({1}, file = '{0}')", GetModelPath(runDate, iRating).Replace("\\", "/"), ModelName);
            string CalculatePredictions = string.Format("prediction = predict({0},Data.Final)", ModelName);
            string CalculateMAE = string.Format("MAE = mean(abs(Data.Final$DM - prediction))");
            string CalculateMSE = string.Format("RMSE = sqrt(mean((Data.Final$DM - prediction) ^ 2))");
            string CalculateCorr = string.Format("corr = cor(prediction, Data.Final$DM)");
            string SaveModelInfo = string.Format("write.csv(c(N,MAE,RMSE,corr),'{0}')", GetModelPath(runDate, iRating).Replace("\\", "/").Replace(ModelExtension, ".txt"));
            
            this.engine.Evaluate(CleanR);
            this.engine.Evaluate(CallGC);
            this.engine.Evaluate(SetRating);
            this.engine.Evaluate(SetAsOfDate);
            this.engine.Evaluate(GenerateModel);
            this.engine.Evaluate(ExportData);
            this.engine.Evaluate(ExportModel);
            this.engine.Evaluate(CalculatePredictions);
            this.engine.Evaluate(CalculateMAE);
            this.engine.Evaluate(CalculateMSE);
            this.engine.Evaluate(CalculateCorr);
            this.engine.Evaluate(SaveModelInfo);
            //engine.Dispose();
        }

        private string GetModelPath(string RunDate,string Rating)
        {
            return MControl.Directory_Model + @"\" + RunDate + "_" + Rating + ModelExtension;
        }

    }
}
