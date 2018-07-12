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


    }
}
