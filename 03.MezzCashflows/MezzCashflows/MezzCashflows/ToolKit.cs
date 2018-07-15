using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MezzCashflows
{
    class ToolKit
    {
        static public double lagrange(double x, double[] xd, double[] yd)
        {
            if (xd.Length != yd.Length)
            {
                throw new ArgumentException("Arrays must be of equal length."); //$NON-NLS-1$
            }
            double sum = 0;
            for (int i = 0; i < xd.Length; i++)
            {
                if (x<xd[i])
                {
                    sum = ((x - xd[i - 1]) * yd[i] + (xd[i] - x) * yd[i - 1]) / (xd[i] - xd[i - 1]);
                    break;
                }
            }
           
            return sum;
        }

        public static double XNPV(double dRate, double[] receipts, DateTime[] dates,  DateTime issueDate)
        {
            double sum =0;

            for (int i = 0; i < dates.Length; i++)
            {
                TimeSpan ts = dates[i].Subtract(issueDate);
                sum += receipts[i] / Math.Pow((1 + dRate) , ts.TotalDays / 360);
            }
            return sum;
        }
    }

    public static class XIRR
    {
        private const double tol = 0.001;
        private delegate double fx(double x);

        private static fx composeFunctions(fx f1, fx f2)
        {
            return (double x) => f1(x) + f2(x);
        }

        private static fx f_xirr(double p, double dt, double dt0)
        {
            return (double x) => p * Math.Pow((1.0 + x), ((dt0 - dt) / 365.0));
        }

        private static fx df_xirr(double p, double dt, double dt0)
        {
            return (double x) => (1.0 / 365.0) * (dt0 - dt) * p * Math.Pow((x + 1.0), (((dt0 - dt) / 365.0) - 1.0));
        }

        private static fx total_f_xirr(double[] payments, double[] days)
        {
            fx resf = (double x) => 0.0;

            for (int i = 0; i < payments.Length; i++)
            {
                resf = composeFunctions(resf, f_xirr(payments[i], days[i], days[0]));
            }

            return resf;
        }

        private static fx total_df_xirr(double[] payments, double[] days)
        {
            fx resf = (double x) => 0.0;

            for (int i = 0; i < payments.Length; i++)
            {
                resf = composeFunctions(resf, df_xirr(payments[i], days[i], days[0]));
            }

            return resf;
        }

        private static double Newtons_method(double guess, fx f, fx df)
        {
            double x0 = guess;
            double x1 = 0.0;
            double err = 1e+100;

            while (err > tol)
            {
                x1 = x0 - f(x0) / df(x0);
                err = Math.Abs(x1 - x0);
                x0 = x1;
            }

            return x0;
        }

        public static double GetIRR(double[] payments, DateTime[] Dates)
        {
            List<double> days = new List<double>();
            days.Add(1);
            for (int i = 1; i < Dates.Length; i++)
            {
                days.Add((Dates[i] - Dates[0]).Days+1);
            }

            double xirr = Newtons_method(0.1,
                                         total_f_xirr(payments, days.ToArray()),
                                         total_df_xirr(payments, days.ToArray()));

            return (xirr);
        }



    }
}
