using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Specialized;
using System.Data;
using CSharpe_MySQL;
using StructuredFinancePortal;
using System.Collections.Concurrent;
using CallDateAPI;
using VB = Microsoft.VisualBasic;
using YieldSurface;

namespace MezzDailyDashboard
{
    class QueryManager
    {
        private OrderedDictionary QueryClusters;
        private DateTime AsOfDate;
        private DataTable MasterTable;
        private string FundName;
        private SFPortal sfp;
        private ConcurrentQueue<BondStructure> CallDateQueue;
        private ConcurrentQueue<DataRow> DMQueue;
        private bool LastBondToCalculateLife = false;
        private Dictionary<string, double> exLifeDictionary;
        private Dictionary<string, double> DM_Dictionary;

        public QueryManager()
        {
            QueryClusters = new OrderedDictionary();
            sfp = new SFPortal();
            sfp.Login("pChan", "PD0317");
            CallDateQueue = new ConcurrentQueue<BondStructure>();
            exLifeDictionary = new Dictionary<string, double>();
            DMQueue = new ConcurrentQueue<DataRow>();
            DM_Dictionary = new Dictionary<string, double>();
        }

        public void DoWork(Tearsheet ts, DateTime _AsOfDate)
        {
            // Variable declaration
            List<string> DealList = new List<string>();
            List<List<string>> queryResult;
            AsOfDate = _AsOfDate;

            // Initialization
            ReadInQueries();
            queryResult = ConnectDB.ReadDB(2, string.Format("SELECT `CDONETNAME`,`LABEL`,SUM(`NOTIONAL`) AS `FACE` FROM GHIF_HOLDINGS " +
                "WHERE `PURCHASEDATE` <= '{0}' GROUP BY `CDONETNAME` HAVING SUM(`NOTIONAL`) > 0",  AsOfDate.ToString("yyyy-MM-dd")));
            foreach (List<string> iRow in queryResult)
            {
                DealList.Add(string.Format("{0}.{1}", iRow[0],iRow[1]));
            }

            ProcessQueries_MultipleDeals(DealList.ToArray());

            CleanUp();
            ts.SetDataSource(MasterTable);
        }

        private void ReadInQueries()
        {
            using (StreamReader sr = new StreamReader(DDB_Control.Tearsheet_Queries))
            {
                List<string> QueryList = new List<string>(); ;
                while (!sr.EndOfStream)
                {
                    
                    string line = sr.ReadLine();
                    if (line.IndexOf("---") > -1)
                    {
                        string fieldName = line.Substring(0, line.IndexOf("---")).Trim();
                        QueryList.Add(line.Substring(line.IndexOf("---")+3).Trim());
                        QueryClusters.Add(fieldName, QueryList);
                        QueryList = new List<string>();
                    }
                    else
                    {
                        QueryList.Add(line);
                    }
                }
            }
        }

        private void ProcessQueries_MultipleDeals(string[] TrancheIDScope)
        {
            string TrancheID = "";

            MasterTable = new DataTable();
            ModelManager mm = new ModelManager();
            YieldSurModMgr ym = new YieldSurModMgr(AsOfDate.ToString("yyyyMMdd"));

            Task NonRTasks = Task.Run(() =>
            {
                if (TrancheIDScope.Length > 0)
                {
                    TrancheID = TrancheIDScope[0];
                    DataRow idr = ProcessQueries_SingleDeal(TrancheID.Split('.')[0], TrancheID.Split('.')[1]);
                    foreach (DataColumn idc in idr.Table.Columns)
                    {
                        MasterTable.Columns.Add(idc.ColumnName, idc.DataType);
                    }
                    MasterTable.Rows.Add(idr.ItemArray);

                    if (TrancheIDScope.Length > 1)
                    {
                        Parallel.For(1, TrancheIDScope.Length, (i) =>
                        {
                            TrancheID = TrancheIDScope[i];
                            DataRow jdr = ProcessQueries_SingleDeal(TrancheID.Split('.')[0], TrancheID.Split('.')[1]);
                            MasterTable.Rows.Add(jdr.ItemArray);
                        });
                    }
                }
                LastBondToCalculateLife = true;
            });

            Task RTasks = Task.Run(() => 
            {
                while (!LastBondToCalculateLife || CallDateQueue.Count > 0)
                {
                    BondStructure bs;
                    double expMaturiy;

                    if (CallDateQueue.TryDequeue(out bs))
                    {
                        mm.PriceBond(bs, false);
                        expMaturiy = Math.Max(0.1,((bs.EarliestCall.AddDays(mm.GetExpectedLife()) - bs.AsOfDate).Days / 365.25));
                        exLifeDictionary.Add(bs.TrancheID, expMaturiy);
                    }
                }

                while (DMQueue.Count>0)
                {
                    DataRow iRow;
                    double requiredDM = double.NaN;

                    if (DMQueue.TryDequeue(out iRow))
                    {
                        requiredDM  = ym.GetDM(iRow["MODIFIED_RATING"].ToString(), To_DM_Model_Dataframe(iRow));
                        DM_Dictionary.Add(iRow["Deal Name"].ToString(), requiredDM);
                    }
                    
                }
            });

            
            Task.WaitAll(NonRTasks, RTasks);

            //###### Post R operation #######
            string CallDate_ColumnName = "exLife";
            DataColumn CallDate_Column = new DataColumn(CallDate_ColumnName, typeof(double));
            MasterTable.Columns.Add(CallDate_Column);

            string Yield_ColumnName = "ReqYield";
            DataColumn Yield_Column = new DataColumn(Yield_ColumnName, typeof(double));
            MasterTable.Columns.Add(Yield_Column);

            foreach (DataRow idr in MasterTable.Rows)
            {
                idr[CallDate_ColumnName] = exLifeDictionary[idr["Deal Name"].ToString()];
                idr[Yield_ColumnName] = DM_Dictionary[idr["Deal Name"].ToString()];
            }
        }

        private DataRow ProcessQueries_SingleDeal(string DealName, string TrancheLabel)
        {
            string baseQuery = string.Format("SET @DEAL_NAME = '{0}'; " +
                "SET @TRANCHE_ID = '{2}'; " +
                "SET @REPORT_DATE = (SELECT MAX(`REPORTDATE`) FROM MONTHLY_DEAL_DATA WHERE `CDONETNAME` = @DEAL_NAME AND `REPORTDATE` <= '{1}' AND `REPORTDATE` > DATE_ADD('{1}',INTERVAL -60 DAY));" +
                "SET @AS_OF_DATE = '{1}';",
                DealName,
                AsOfDate.ToString("yyyy-MM-dd"),
                TrancheLabel);

            string RDateName = "As Of Date";
            string headerColname = "Deal Name";

            DataTable MasterResultTable = new DataTable();
            DataRow ThisRow;

            // Add Report Date and Deal/Tranche Name
            MasterResultTable.Columns.Add(RDateName, typeof(DateTime));
            MasterResultTable.Columns.Add(headerColname, typeof(string));
            ThisRow = MasterResultTable.NewRow();
            ThisRow[RDateName] = AsOfDate.Date;
            ThisRow[headerColname] = DealName + "." + TrancheLabel;
            MasterResultTable.Rows.Add(ThisRow);



            foreach (string key in QueryClusters.Keys)
            {
                List<string> QueryList = new List<string>((List<string>)QueryClusters[key]);
                DataTable resultDT = new DataTable();
                
                // Get Result from SQL
                QueryList.Insert(0, baseQuery);
                resultDT.Load(ConnectDB.GetCommand(1, string.Join("", QueryList.ToArray())).ExecuteReader());

                // Merge to Master
                MasterResultTable.Columns.Add(key,resultDT.Columns[0].DataType);
                try
                {
                    MasterResultTable.Rows[0][key] = resultDT.Rows[0][0];
                }
                catch (IndexOutOfRangeException)
                {
                    MasterResultTable.Rows[0][key] = DBNull.Value;
                }
                
                //QueryList.Clear();
            }

            // Get Tranche Type
            int trancheType = GetTrancheType(DealName);
            MasterResultTable.Columns.Add("DealType", typeof(int));
            while (trancheType == 2)
            {
                trancheType = Math.Min((short)2,Convert.ToInt16(VB.Interaction.InputBox("Please input tranche type:", "Invalid Tranche Type for " + DealName, "0")));
            }
            MasterResultTable.Rows[0]["DealType"] = trancheType;

            // Earliest Call Date
            DateTime EarliestCallDate;
            EarliestCallDate = (trancheType == 1) ? sfp.GetRefiResetDate(DealName) : DateTime.ParseExact(MasterResultTable.Rows[0]["NCEDATE"].ToString(), "dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture).AddDays(90);
            MasterResultTable.Columns.Add("EarliestCallDate", typeof(DateTime));
            MasterResultTable.Rows[0]["EarliestCallDate"] = EarliestCallDate;

            //Calculate ECallDate
            BondStructure bs = new BondStructure(DealName + "." + TrancheLabel)
            {
                rating = (Rating)Enum.Parse(typeof(Rating), MasterResultTable.Rows[0]["MODIFIED_RATING"].ToString()),
                TrancheType = trancheType,
                AsOfDate = AsOfDate,
                EarliestCall = EarliestCallDate,

                Deal_AAA = Convert.ToDouble(MasterResultTable.Rows[0]["DEAL_AAA"].ToString()),
                JPM_AAA_1 = Convert.ToDouble(MasterResultTable.Rows[0]["JPM_AAA1"].ToString()),
                JPM_AAA_2 = Convert.ToDouble(MasterResultTable.Rows[0]["JPM_AAA2"].ToString()),

                Tranche_Spd = Convert.ToDouble(MasterResultTable.Rows[0]["Coupon"].ToString()),
                JPM_Sprd1 = Convert.ToDouble(MasterResultTable.Rows[0]["JPM_TRANCHE1"].ToString()),
                JPM_Sprd2 = Convert.ToDouble(MasterResultTable.Rows[0]["JPM_TRANCHE2"].ToString()),

                WAS = Convert.ToDouble(MasterResultTable.Rows[0]["WAS"].ToString()),
                WAL = Convert.ToDouble(MasterResultTable.Rows[0]["WAL"].ToString()),
                NAV = Convert.ToDouble(MasterResultTable.Rows[0]["NAV"].ToString())
            };
            CallDateQueue.Enqueue(bs);

            DMQueue.Enqueue(MasterResultTable.Rows[0]);

            return MasterResultTable.Rows[0];
        }


        private int GetTrancheType(string DealName)
        {
            DateTime RefiDate;
            DateTime Pre_RED, Post_RED;
            string Pre_Query, Post_Query;
            int PrePostBand = 50;

            
            try
            {
                RefiDate = sfp.GetRefiResetDate(DealName);

                if (RefiDate.Year == 1)
                {
                    return 0;
                }
                else
                {
                    Pre_Query = string.Format("SELECT DATE_FORMAT(`REDATE`,'%Y-%m-%d') FROM MONTHLY_DEAL_DATA WHERE `CDONETNAME` = '{0}' AND `REPORTDATE` <= '{1}' ORDER BY `REPORTDATE` DESC LIMIT 0,1",
                                        DealName, RefiDate.AddDays(-PrePostBand).ToString("yyyy-MM-dd"));
                    Post_Query = string.Format("SELECT DATE_FORMAT(`REDATE`,'%Y-%m-%d') FROM MONTHLY_DEAL_DATA WHERE `CDONETNAME` = '{0}' AND `REPORTDATE` >= '{1}' ORDER BY `REPORTDATE` DESC LIMIT 0,1",
                                        DealName, RefiDate.AddDays(PrePostBand).ToString("yyyy-MM-dd"));

                    Pre_RED = DateTime.ParseExact(ConnectDB.ReadDB(1, Pre_Query)[0][0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    Post_RED = DateTime.ParseExact(ConnectDB.ReadDB(1, Post_Query)[0][0], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    return Convert.ToInt16(Pre_RED == Post_RED);
                }

            }
            catch (Exception)
            {
                return 2;
            }

        }

        private string To_DM_Model_Dataframe(DataRow irow)
        {
            string returnString = string.Format("data.frame(" +
                "T2R = {0}," +
                "CpnRate = {1}," +
                "JPM_DM = {2}," +
                "WAS = {3}," +
                "WAL = {4}," +
                "NAV = {5}," +
                "MVOC = {6})",
                Convert.ToDouble(irow["T2R"]),
                Convert.ToDouble(irow["Coupon"]),
                Convert.ToDouble(irow["JPM_TRANCHE1"]),
                Convert.ToDouble(irow["WAS"]),
                Convert.ToDouble(irow["WAL"]),
                Convert.ToDouble(irow["NAV"]),
                Convert.ToDouble(irow["MVOC"])
                );
            return returnString;
        }

        private void CleanUp()
        {
            QueryClusters.Clear();
        }
    }
}
