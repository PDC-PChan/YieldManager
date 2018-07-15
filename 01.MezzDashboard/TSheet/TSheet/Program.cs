using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpe_MySQL;

namespace MezzDailyDashboard
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Tearsheet ts = new Tearsheet();

            //string outputAddress = args[0];
            //int UpdateType = Convert.ToInt16(args[1]);

            //QueryManager qm = new QueryManager(outputAddress);

            //switch (UpdateType)
            //{
            //    case 0:
            //        string[] DealList = args[2].Split(',');
            //        qm.AddBonds(DateTime.Today, DealList);
            //        break;
            //    case 1:
            //        qm.CurrentPortfolioBehindScene(DateTime.Today);
            //        break;
            //    default:
            //        break;
            //}
        }


    }
}
