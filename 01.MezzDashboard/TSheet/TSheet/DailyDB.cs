using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CSharpe_MySQL;

namespace MezzDailyDashboard
{
    public partial class Tearsheet : Form
    {
        private QueryManager qm;

        public Tearsheet()
        {
            InitializeComponent();
            //initializeFundList();
            initilizeGears();
            ShowDialog();
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        public void SetDataSource(DataTable dt)
        {
            this.dataGridView1.DataSource = dt;
        }

        private void initilizeGears()
        {
            qm = new QueryManager();
        }

        private void initializeFundList()
        {
            // Get list of funds
            List<List<string>> queryResult = ConnectDB.ReadDB(1, "SELECT DISTINCT `FUND` FROM PDC_HOLDINGS ORDER BY `FUND` ASC;");
            List<string> FundList = new List<string>();
            foreach (List<string> iRow in queryResult)
            {
                FundList.Add(iRow[0]);
            }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            Cursor.Current = Cursors.WaitCursor;
            DateTime AsOfDate = this.dateTimePicker1.Value;
            qm.DoSomeWork(this, AsOfDate);
            Cursor.Current = Cursors.Default;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
