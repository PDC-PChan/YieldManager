using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CDOnet;
using System.Collections.Concurrent;
using CSharpe_MySQL;

namespace MezzCashflows
{
    public class CashflowManager
    {
        private CDOnet.CDOnet ObjCDONet;
        private CDOnet.IDealLibrary CDONetDealLib;
        private bool Retry = true;
        private CDOnet.IDealAllUpdates CDONetAllUpdates;
        public List<string> DealList;
        public ConcurrentDictionary<string, Dictionary<DateTime, Dictionary<string, HashSet<double>>>> My_Deal_Date_Tranche_Price;

        public CashflowManager()//List<string> DealList, ConcurrentDictionary<string, Dictionary<DateTime, Dictionary<string, HashSet<double>>>> Deal_Date_Tranche_Price)
        {
            ObjCDONet = new CDOnet.CDOnet();
            while (Retry)
            {
                try
                {
                    CDONetDealLib = ObjCDONet.Application.OpenDealLibrary("https://lib.cdocalc.com",
                    CDONetDealLib_Settings.DealLibUserName, CDONetDealLib_Settings.DealLibUserPassword);
                    Retry = false;
                }
                catch (System.Runtime.InteropServices.COMException)
                { }
            }
        }

        public void LoadDeal(string TrancheID, DateTime AsOfDate)
        {
            string DealID = TrancheID.Split('.')[0];
            CDONetDealLib.LoadDealExt("DealID", DealID, AsOfDate, true);
        }

        public Tuple<double,double> GetSpecialCDRs(string TrancheID,DateTime AsOfDate, bool LoadDeal = true)
        {
            string DealID = TrancheID.Split('.')[0];
            string Label = TrancheID.Split('.')[1];
            Tuple<double, double> Result_CDRs;

            if (LoadDeal)
            {
                CDONetDealLib.LoadDealExt("DealID", DealID, AsOfDate, true);
            }

            DateTime CallDate = new DateTime(Math.Max(AsOfDate.AddDays(45).Ticks, ObjCDONet.CBOControl.ReinvestmentEndDate.Ticks));

            Result_CDRs = RunFirstLoss(TrancheID, AsOfDate, CallDate);

            ObjCDONet.Application.Close();

            ObjCDONet.Quit();

            return Result_CDRs;
        }

        public Tuple<double,double> GetPrices(string TrancheID, DateTime AsOfDate, double ReqDM, bool LoadDeal = true)
        {
            string DealID = TrancheID.Split('.')[0];
            string Label = TrancheID.Split('.')[1];
            Tuple<double, double> Result_Prices;

            if (LoadDeal)
            {
                CDONetDealLib.LoadDealExt("DealID", DealID, AsOfDate, true);
            }

            DateTime CallDate = new DateTime(Math.Max(AsOfDate.AddDays(45).Ticks, ObjCDONet.CBOControl.ReinvestmentEndDate.Ticks));

            Result_Prices = GetCleanDirty(TrancheID, AsOfDate, ReqDM, CallDate);

            ObjCDONet.Application.Close();

            ObjCDONet.Quit();

            return Result_Prices;
        }

        public void DoWork()
        {
            ObjCDONet.Visible = true;

            foreach (string DealID in DealList)
            {
                try
                {
                    CDONetAllUpdates = CDONetDealLib.AvailableUpdates("DealID", DealID);
                    DateTime[] DateList = My_Deal_Date_Tranche_Price[DealID].Keys.ToArray();
                    DateTime currentCDONetDate = DateTime.Today.AddDays(100);
                    int ReportIterator = 0;
                    bool Reload = false;
                    DateList.OrderByDescending(d => d);

                    for (int i = 0; i < DateList.Length; i++)
                    {
                        while (DateList[i] < currentCDONetDate)
                        {
                            Reload = true;
                            currentCDONetDate = CDONetAllUpdates.Item[CDONetAllUpdates.Count() - ReportIterator++].AsOfDate;
                        }
                        if (Reload)
                        {
                            CDONetDealLib.LoadDealExt("DealID", DealID, currentCDONetDate, true);
                            Reload = false;
                        }
                        DateTime CallDate = new DateTime(Math.Max(DateList[i].AddDays(45).Ticks, ObjCDONet.CBOControl.ReinvestmentEndDate.Ticks));
                        string[] TrancheList = My_Deal_Date_Tranche_Price[DealID][DateList[i]].Keys.ToArray();
                        foreach (string itrche in TrancheList)
                        {
                            foreach (double iPrice in My_Deal_Date_Tranche_Price[DealID][DateList[i]][itrche])
                            {
                                Tuple<double, double> Returns = GetReturns(DealID + "." + itrche, iPrice, DateList[i], CallDate);
                                if (!double.IsNaN(Returns.Item1))
                                {
                                    string UpDateQuery = string.Format("UPDATE CONSOLIDATEDPRICE SET `YIELD(T2R)` =  '{0}' ,`DM(T2R)` = '{1}' WHERE " +
    "`TRANCHEID` = '{2}' AND DATE(`RECEIVEDTIME`) = '{3}' AND `SUGGESTEDPRICE` = '{4}';",
    Returns.Item1.ToString(),
    Returns.Item2.ToString(),
    DealID + "." + itrche,
    DateList[i].ToString("yyyy-MM-dd"),
    iPrice.ToString());
                                    ConnectDB.WriteDB(2, UpDateQuery);
                                }

                            }
                        }

                    }
                }
                catch (System.Runtime.InteropServices.COMException e)
                {
                    if (e.Message.IndexOf("out of range") == -1)
                    {
                        throw;
                    }
                }
            }


        }

        private Tuple<double, double> GetCleanDirty(string TrancheID,  DateTime AsOfDate, double ReqDM, DateTime CallDate )
        {
            // Set up
            string DealID = TrancheID.Split('.')[0];
            string trancheLabel = TrancheID.Split('.')[1];
            string[] RateName = new string[] { "Libor 1M", "Libor 2M", "Libor 3M", "Libor 6M", "Libor 1Y", "Swap 2Y", "Swap 3Y", "Swap 4Y", "Swap 5Y", "Swap 6Y" };
            double[] YearFracs = new double[] { 1 / 12.0, 2 / 12.0, 3 / 12.0, 6 / 12.0, 1, 2, 3, 4, 5, 6 };
            double[] Rates = new double[RateName.Length];
            double FixRate = 0;
            double CleanPrice, DirtyPrice = 0;
            DateTime StartAccrualDate = new DateTime(1,1,1);
            ITranche targetTranche = null;


            // Load Econ
            ObjCDONet.Application.Economy().Load(CDONetDealLib_Settings.StandardEcon);
            ObjCDONet.Application.Economy().CallDateOverrideOption = "Input";
            ObjCDONet.Application.Economy().CallDateOverrideValue = CallDate;
            ObjCDONet.Application.Economy().LoadRatesFromLibrary(AsOfDate);
            ObjCDONet.Application.Economy().Model.PrincipalLossSeverity = 60;
            ObjCDONet.Application.Deal().DefaultRate = 2;

            for (int i = 0; i < RateName.Length; i++)
            {
                Rates[i] = ObjCDONet.Economy.Rates().IndexRates[RateName[i]];
            }
            FixRate = ToolKit.lagrange((CallDate - AsOfDate).Days / 365.25, YearFracs, Rates) / 100;

            // Run Cashflows
            ObjCDONet.Application.Deal().Run();


            // Get Target Tranche
            foreach (ITranche itranche in ObjCDONet.Application.Deal().CMO().Tranches)
            {
                if (itranche.Label == trancheLabel)
                {
                    targetTranche = itranche;
                    break;
                }
            }

            // Get Start Accrual Date
            for (int i = 0; i < 50; i++)
            {
                if (ObjCDONet.Application.Deal().CMO().LiabilityFlow.PayDate[(short)i] < AsOfDate)
                {
                    StartAccrualDate = ObjCDONet.Application.Deal().CMO().LiabilityFlow.PayDate[(short)i];
                }
                else
                {
                    break;
                }
            }
            if (StartAccrualDate.Year == 1)
            {
                StartAccrualDate = ObjCDONet.Application.Deal().CMO().CutoffDatedDate;
            }

            // Calculate Numbers
            bool ToStop = false;
            List<double> CleanCashflows = new List<double>();
            List<double> DirtyCashflows = new List<double>();
            List<DateTime> AllDates = new List<DateTime>();
            double CurrentSize = targetTranche.UpdateFace;
            double IRR = 0;

            for (int m = 0; m < 100; m++)
            {
                if (!ToStop)
                {
                    DateTime iPayDate = targetTranche.PayDate[(short)m];
                    double iCashflow = targetTranche.Interest[(short)m] + targetTranche.Principal[(short)m];

                    if (iPayDate > AsOfDate && iCashflow != 0)
                    {
                        CleanCashflows.Add(iCashflow);
                        AllDates.Add(iPayDate);
                    }

                    ToStop = (targetTranche.Balance[(short)m] == 0);
                }
            }
            DirtyCashflows = new List<double>(CleanCashflows);
            DirtyCashflows[0] *=(1+ ((AsOfDate-StartAccrualDate).TotalDays-1) /((AllDates[0] - StartAccrualDate).TotalDays-1));

            CleanPrice = ToolKit.XNPV(ReqDM + FixRate, CleanCashflows.ToArray(), AllDates.ToArray(),  AsOfDate)/ targetTranche.UpdateFace;
            DirtyPrice = ToolKit.XNPV(ReqDM + FixRate, DirtyCashflows.ToArray(), AllDates.ToArray(), AsOfDate) / targetTranche.UpdateFace;

            return new Tuple<double, double>(CleanPrice,DirtyPrice);
        }

        private Tuple<double,double> GetReturns(string TrancheID, double Price, DateTime AsOfDate,DateTime CallDate)
        {
            // Set up
            string DealID = TrancheID.Split('.')[0];
            string trancheLabel = TrancheID.Split('.')[1];
            string[] RateName = new string[] { "Libor 1M", "Libor 2M", "Libor 3M", "Libor 6M", "Libor 1Y", "Swap 2Y", "Swap 3Y", "Swap 4Y", "Swap 5Y", "Swap 6Y" };
            double[] YearFracs = new double[] { 1 / 12.0, 2 / 12.0, 3 / 12.0, 6 / 12.0, 1, 2, 3, 4, 5,6 };
            double[] Rates = new double[RateName.Length];
            double FixRate = 0;

            ITranche targetTranche=null;


            // Load Econ
            ObjCDONet.Application.Economy().Load(CDONetDealLib_Settings.StandardEcon);
            ObjCDONet.Application.Economy().CallDateOverrideOption = "Input";
            ObjCDONet.Application.Economy().CallDateOverrideValue = CallDate;
            ObjCDONet.Application.Economy().LoadRatesFromLibrary(AsOfDate);
            ObjCDONet.Application.Economy().Model.PrincipalLossSeverity = 60;
            ObjCDONet.Application.Deal().DefaultRate = 2;

            for (int i = 0; i < RateName.Length; i++)
            {
                Rates[i] = ObjCDONet.Economy.Rates().IndexRates[RateName[i]];
            }
            FixRate = ToolKit.lagrange((CallDate - AsOfDate).Days / 365.25, YearFracs, Rates)/100;

            // Run Cashflows
            ObjCDONet.Application.Deal().Run();


            // Calculate Numbers
            foreach (ITranche itranche in ObjCDONet.Application.Deal().CMO().Tranches)
            {
                if (itranche.Label==trancheLabel)
                {
                    targetTranche = itranche;
                    break;
                }
            }

            bool ToStop = false;
            List<double> Cashflows = new List<double>();
            List<DateTime> AllDates = new List<DateTime>();
            double CurrentSize = targetTranche.UpdateFace;
            double IRR = 0;

            for (int m = 0; m < 100; m++)
            {
                if (!ToStop)
                {
                    DateTime iPayDate = targetTranche.PayDate[(short)m];
                    double iCashflow = targetTranche.Interest[(short)m] + targetTranche.Principal[(short)m];

                    if (iPayDate>AsOfDate && iCashflow != 0)
                    {
                        Cashflows.Add(iCashflow);
                        AllDates.Add(iPayDate);
                    }
                    
                    ToStop = (targetTranche.Balance[(short)m] == 0);
                }
            }
            Cashflows.Insert(0, -CurrentSize * Price / 100);
            AllDates.Insert(0, AsOfDate);

            IRR = XIRR.GetIRR(Cashflows.ToArray(), AllDates.ToArray());
            return new Tuple<double, double>(IRR, IRR - FixRate);
        }

        private Tuple<double,double> RunFirstLoss(string TrancheID, DateTime AsOfDate, DateTime CallDate)
        {
            // Set up
            string DealID = TrancheID.Split('.')[0];
            string trancheLabel = TrancheID.Split('.')[1];
            double PIK_CDR = 0;
            double BKE_CDR = 0;

            ITranche targetTranche = null;


            // Load Econ
            ObjCDONet.Application.Economy().Load(CDONetDealLib_Settings.StandardEcon);
            ObjCDONet.Application.Economy().CallDateOverrideOption = "Input";
            ObjCDONet.Application.Economy().CallDateOverrideValue = CallDate;
            ObjCDONet.Application.Economy().LoadRatesFromLibrary(AsOfDate);
            ObjCDONet.Application.Economy().Model.PrincipalLossSeverity = 60;

            // Calculate Numbers
            foreach (ITranche itranche in ObjCDONet.Application.Deal().CMO().Tranches)
            {
                if (itranche.Label == trancheLabel)
                {
                    targetTranche = itranche;
                    break;
                }
            }


            PIK_CDR = ObjCDONet.Application.Analysis().FirstLossCalculator(targetTranche, "Interest", "CDR");
            if (PIK_CDR == -1)
            {
                PIK_CDR = BisectionPIK_CDR(TrancheID,AsOfDate,CallDate);
            }

            BKE_CDR = ObjCDONet.Application.Analysis().YieldSolver(targetTranche, "Yield", 0);

            return new Tuple<double, double>(PIK_CDR, BKE_CDR);
        }

        private double BisectionPIK_CDR(string TrancheID, DateTime AsOfDate, DateTime CallDate)
        {
            // Set up
            string DealID = TrancheID.Split('.')[0];
            string trancheLabel = TrancheID.Split('.')[1];

            ITranche targetTranche = null;


            // Load Econ
            ObjCDONet.Application.Economy().Load(CDONetDealLib_Settings.StandardEcon);
            ObjCDONet.Application.Economy().CallDateOverrideOption = "Input";
            ObjCDONet.Application.Economy().CallDateOverrideValue = CallDate;
            ObjCDONet.Application.Economy().LoadRatesFromLibrary(AsOfDate);
            ObjCDONet.Application.Economy().Model.PrincipalLossSeverity = 60;


            // Calculate Numbers
            foreach (ITranche itranche in ObjCDONet.Application.Deal().CMO().Tranches)
            {
                if (itranche.Label == trancheLabel)
                {
                    targetTranche = itranche;
                    break;
                }
            }

            // Run Cashflows
            

            double TrancheSize = targetTranche.UpdateFace;
            double CDR_U = 100;
            double CDR_L = 0;
            double CDR_M = 0;
            int precision = 3;

            do
            {
                CDR_M = (CDR_U + CDR_L) / 2;
                ObjCDONet.Application.Deal().DefaultRate = CDR_M;
                ObjCDONet.Application.Deal().Run();

                double currentMax = 0;
                for (int m = 0; m < 100; m++)
                {
                    currentMax = Math.Max(currentMax, targetTranche.Balance[(short)m]);
                }
                if (currentMax <= TrancheSize)
                {
                    CDR_L = CDR_M;
                }
                else
                {
                    CDR_U = CDR_M;
                }

            } while (Math.Round(CDR_M,precision)==CDR_M);

            return CDR_M;
        }
    }
}
