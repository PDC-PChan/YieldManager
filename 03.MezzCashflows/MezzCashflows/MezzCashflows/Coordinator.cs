using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CDOnet;
using CSharpe_MySQL;
using System.Collections.Concurrent;

namespace MezzCashflows
{
    public class Coordinator
    {
        public static void DatabaseGeneralUpdate()
        {
            int numSplit = 6;
            ConcurrentDictionary<string, Dictionary<DateTime, Dictionary<string, HashSet<double>>>> Deal_Date_Tranche_Price = new ConcurrentDictionary<string, Dictionary<DateTime, Dictionary<string, HashSet<double>>>>();
            string Query = string.Format("SELECT `TRANCHEID`,DATE(`RECEIVEDTIME`),`SUGGESTEDPRICE` FROM CONSOLIDATEDPRICE " +
                "WHERE  `YIELD(T2R)` IS NULL AND `SUGGESTEDPRICE` IS NOT NULL AND `REGION` = 'US' " +
                "AND `RECEIVEDTIME` > '{0}' AND `TRANCHEID` IS NOT NULL AND `TRANCHEID` <> '' " +
                "AND `MODIFIEDRATING` <> '' AND `MODIFIEDRATING` <> 'EQ' AND `MODIFIEDRATING` IS NOT NULL " +
                "AND `SUGGESTEDPRICE` > 10 ORDER BY RECEIVEDTIME DESC", DateTime.Today.AddDays(-100));

            List<List<string>> QueryResult = ConnectDB.ReadDB(2, Query);
            List<string>[] DealLists = new List<string>[numSplit];

            // Read list of Deals
            foreach (List<string> iRow in QueryResult)
            {
                string DealID = iRow[0].Split('.')[0];
                string Tranche = iRow[0].Split('.')[1];
                DateTime AsOfDate = DateTime.ParseExact(iRow[1], "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                double Price = Convert.ToDouble(iRow[2]);


                if (!Deal_Date_Tranche_Price.ContainsKey(DealID))
                {
                    Deal_Date_Tranche_Price.TryAdd(DealID, new Dictionary<DateTime, Dictionary<string, HashSet<double>>>());
                }
                if (!Deal_Date_Tranche_Price[DealID].ContainsKey(AsOfDate))
                {
                    Deal_Date_Tranche_Price[DealID].Add(AsOfDate, new Dictionary<string, HashSet<double>>());
                }
                if (!Deal_Date_Tranche_Price[DealID][AsOfDate].ContainsKey(Tranche))
                {
                    Deal_Date_Tranche_Price[DealID][AsOfDate][Tranche] = new HashSet<double>();
                }
                Deal_Date_Tranche_Price[DealID][AsOfDate][Tranche].Add(Price);
            }

            // Split Deals
            string[] AllDealList = Deal_Date_Tranche_Price.Keys.ToArray();
            for (int i = 0; i < numSplit; i++)
            {
                DealLists[i] = new List<string>();
            }
            for (int i = 0; i < AllDealList.Length; i++)
            {
                DealLists[i % numSplit].Add(AllDealList[i]);
            }

            List<Task> Tasks = new List<Task>();
            for (int i = 0; i < numSplit; i++)
            {
                int ii = i;
                Tasks.Add(Task.Factory.StartNew(() => {
                    CashflowManager cm = new CashflowManager { DealList = DealLists[ii], My_Deal_Date_Tranche_Price = Deal_Date_Tranche_Price };
                    cm.DoWork();
                }));
            }
            Task.WaitAll(Tasks.ToArray());
        }
    }
}
