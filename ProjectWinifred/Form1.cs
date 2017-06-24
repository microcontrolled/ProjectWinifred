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
        public static Dictionary<string,GMapMarker> nodeMarkers = new Dictionary<string,GMapMarker>();

        public static GMarkerGoogleType[] markerList = { GMarkerGoogleType.green_dot,GMarkerGoogleType.lightblue_dot,GMarkerGoogleType.orange_dot,GMarkerGoogleType.pink_dot,GMarkerGoogleType.purple_dot,GMarkerGoogleType.red_dot,GMarkerGoogleType.yellow_dot };

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
            GMaps.Instance.Mode = AccessMode.ServerAndCache;
            gmap.SetPositionByKeywords("Paris, France");

            markers = new GMapOverlay("markers");
            currNode = new GMarkerGoogle(new PointLatLng(48.8617774, 6.349272),GMarkerGoogleType.blue_dot);
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
                //NodeManagement.disableLocalNode();
                Application.Restart();
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
        //These are the updates that complete every tick (100ms)
        private void timer1_Tick(object sender, EventArgs e)
        {
            consoleTerm.Text = NodeManagement.deviceData;
            //If a GPS signal is found, convert and pin the location of the local node
            if (NodeManagement.signalFound)
            {
                try
                {
                    float[] latlog = ParseGPS.getDecCoordinates();
                    markers.Markers.Remove(currNode);
                    currNode = new GMarkerGoogle(new PointLatLng(latlog[0], latlog[1]), GMarkerGoogleType.blue_dot);
                    markers.Markers.Add(currNode);
                    gmap.Position = (new PointLatLng(latlog[0], latlog[1]));
                } catch (Exception)
                {
                    Console.WriteLine("False Packet");
                }
            }
            //Update the packet terminal with the incoming packets
            for (int i=0;i<100;i++)
            {
                if (NodeManagement.debugFlag[i])
                {
                    NodeManagement.debugFlag[i] = false;
                    debugTerm.Text = NodeManagement.debugSet[i] + "\n" + debugTerm.Text;
                }
            }
            //Refresh the list of connected nodes and update their status
            ushort markerCnt = 0;
            try
            {
                //if (!NodeManagement.waitForFormSort)
                //{
                foreach (KeyValuePair<string, string> item in NodeManagement.nodeIndex)
                {
                    //Pin the location of all nodes communicating with the local node
                    String[] parsedIn = item.Value.Split(',');
                    //if (!parsedIn[0].Equals(String.Format("{0:X}", NodeManagement.devID)))
                    //{
                    if (nodeMarkers.ContainsKey(parsedIn[0])) { markers.Markers.Remove(nodeMarkers[parsedIn[0]]); }
                    //nodeMarkers.Remove(parsedIn[0]);
                    nodeMarkers[parsedIn[0]] = new GMarkerGoogle(new PointLatLng(float.Parse(parsedIn[1]), float.Parse(parsedIn[2])), markerList[(markerCnt++) % markerList.Length]);    //Add the new marker, taking data from the string stored in the node directory and marking it with one of the listed markers, in incrementing order
                    markers.Markers.Add(nodeMarkers[parsedIn[0]]);               
                    nodeMarkers[parsedIn[0]].ToolTipText = item.Value.Split(',')[0];
                    //nodeMarkers[parsedIn[0]].ToolTipMode = MarkerTooltipMode.Always;
                    //Add new nodes to the GUI list
                    if (!nodeList.Items.Contains(item.Key))
                    {
                        nodeList.Items.Add(item.Key);
                    }
                    //}
                }
                for (int i = 0; i < nodeList.Items.Count; i++)                                       //Scan the listBox for expired nodes and remove them from the list
                {
                    if (!NodeManagement.nodeIndex.ContainsKey((string)nodeList.Items[i]))
                    {
                        markers.Markers.Remove(nodeMarkers[(string)nodeList.Items[i]]);
                        nodeList.Items.Remove((string)nodeList.Items[i]);
                    }
                }
                //}
            }
            catch (Exception) { };
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

        //These functions just pass parameters to static variables when the state of the options change
        private void button4_Click(object sender, EventArgs e)
        {
            NodeManagement.devID = Convert.ToUInt16(textBox5.Text,16);
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            NodeManagement.dynamicUpdates = radioButton1.Checked;
            NodeManagement.forceCoordinator = radioButton2.Checked;
            NodeManagement.forceRouter = radioButton3.Checked;
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            NodeManagement.dynamicUpdates = radioButton1.Checked;
            NodeManagement.forceCoordinator = radioButton2.Checked;
            NodeManagement.forceRouter = radioButton3.Checked;
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            NodeManagement.dynamicUpdates = radioButton1.Checked;
            NodeManagement.forceCoordinator = radioButton2.Checked;
            NodeManagement.forceRouter = radioButton3.Checked;
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            NodeManagement.postFixRouterConvert = checkBox3.Checked;
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            NodeManagement.waitForCoordReassign = Decimal.ToInt32(numericUpDown1.Value * 4);
        }

        private void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            NodeManagement.waitForRouterReassign = Decimal.ToInt32(numericUpDown2.Value * 4);
        }

        private void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            NodeManagement.packetsPerSecond = Decimal.ToInt32(numericUpDown3.Value);
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            NodeManagement.transmitCheck = checkBox4.Checked;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            debugTerm.Text = "";
        }

        private void numericUpDown4_ValueChanged(object sender, EventArgs e)
        {
            NodeManagement.transmitToLinkedData = (int)numericUpDown4.Value;
        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            NodeManagement.transmitLinkedData = checkBox5.Checked; 
        }

        private void numericUpDown5_ValueChanged(object sender, EventArgs e)
        {
            NodeManagement.transmitLinkedPause = (int)numericUpDown5.Value;
        }
    }
}
