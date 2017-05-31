using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;
using ProjectFastNet;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using GMap.NET.MapProviders;

namespace ProjectWinifred
{
    public partial class altGUI : Form
    {

        public static GMapMarker currNode;
        public static GMapOverlay markers;

        public altGUI()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //Initialize the selections with the currently available ports
            String[] currCOMS = SerialPort.GetPortNames();
            for (int i=0;i<currCOMS.Length;i++)
            {
                comboBox1.Items.Add(currCOMS[i]);
                comboBox2.Items.Add(currCOMS[i]);
            }
  
            //Initialize the GMap with a default coordinate
            gmap.MapProvider = BingMapProvider.Instance;
            GMaps.Instance.Mode = AccessMode.ServerOnly;
            gmap.SetPositionByKeywords("Paris, France");

            markers = new GMapOverlay("markers");
            currNode = new GMarkerGoogle(new PointLatLng(48.8617774, 6.349272),GMarkerGoogleType.blue_pushpin);
            markers.Markers.Add(currNode);
            gmap.Overlays.Add(markers);     

            timer1.Start();

        }

        private void gMapControl1_Load(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            //Use the selections from the ComboBox set to connect to the ALT5801 and the GPS module
            if (button1.Text.Equals("Connect"))
            {
                try
                {
                    NodeManagement.initializeLocalNode(comboBox2.Text, comboBox1.Text);

                    button1.Text = "Disconnect";
                    comboBox2.Enabled = false;
                    comboBox1.Enabled = false;
                }
                catch (Exception)
                {
                    printToConsole("ERROR: Failed to find COM Port(s)");
                }
            } else
            {
                NodeManagement.disableLocalNode();
                comboBox2.Enabled = true;
                comboBox1.Enabled = true;
                button1.Text = "Connect";
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            //Clear the current combobox selections
            comboBox1.Items.Clear();
            comboBox2.Items.Clear();
            //Reinitialize the selections with current ports
            String[] currCOMS = SerialPort.GetPortNames();
            for (int i = 0; i < currCOMS.Length; i++)
            {
                comboBox1.Items.Add(currCOMS[i]);
                comboBox2.Items.Add(currCOMS[i]);
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            NodeManagement.deviceData = ("0x" + String.Format("{0:X}", NodeManagement.devID) + "," + textBox1.Text + "," + textBox2.Text);
            String spoofedData = "$GPGGA,23:56:23.25," + textBox1.Text + "," + textBox3.Text + "," + textBox2.Text + "," + textBox4.Text + ",1,03,1.0,2.0,M,1.0,M,1.0,0200";    //Imply most of this string
            //ParseGPS.NMEAstring[0] = "$GPGGA";
            //ParseGPS.NMEAstring[2] = textBox1.Text;
            //ParseGPS.NMEAstring[4] = textBox2.Text;
            ParseGPS.parseNMEAstring(spoofedData);
            NodeManagement.signalFound = true;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            textBox1.Enabled = checkBox1.Checked;
            textBox2.Enabled = checkBox1.Checked;
            label3.Enabled = checkBox1.Checked;
            label4.Enabled = checkBox1.Checked;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            consoleTerm.Text = NodeManagement.deviceData;
            if (NodeManagement.signalFound)
            {
                try
                {
                    float[] latlog = ParseGPS.getDecCoordinates();
                    markers.Markers.Remove(currNode);
                    currNode = new GMarkerGoogle(new PointLatLng(latlog[0], latlog[1]), GMarkerGoogleType.blue_pushpin);
                    markers.Markers.Add(currNode);
                    gmap.Position = (new PointLatLng(latlog[0], latlog[1]));
                } catch (Exception)
                {
                    Console.WriteLine("False Packet");
                }
            }

            //Refresh the list of connected nodes and update their status
            foreach (KeyValuePair<string, string> item in NodeManagement.nodeIndex)         //Add new nodes to the GUI list
            {
                //nodeList.Items.Add(item.Value);
                //MessageBox.Show(item.Key + "   " + item.Value);
                if (!nodeList.Items.Contains(item.Key))
                {
                    nodeList.Items.Add(item.Key);
                }
            }
            for (int i=0;i<nodeList.Items.Count;i++)                                       //Scan the listBox for expired nodes and remove them from the list
            {
                if (!NodeManagement.nodeIndex.ContainsKey((string)nodeList.Items[i]))
                {
                    nodeList.Items.Remove((string)nodeList.Items[i]);
                }
            }
        }

        private void printToConsole(string text)
        {
            consoleTerm.Text = consoleTerm.Text + "\n" + text;
        }

        private void label3_Click(object sender, EventArgs e)
        {

        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            textBox5.Enabled = !checkBox2.Checked;
            button4.Enabled = !checkBox2.Checked;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            NodeManagement.devID = Convert.ToUInt16(textBox5.Text,16);
        }
    }
}
