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
            //Console.WriteLine(ConnectDB.ReadDB(1, "select * from clos")[0][0]);
        }
    }
}
