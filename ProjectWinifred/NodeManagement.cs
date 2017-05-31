using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Ports;
using ProjectFastNet;

namespace ProjectWinifred
{
    class NodeManagement
    {

        //Imported variables from ProjectFastNet
        public const byte channel = 6;
        public const byte PANID = 0x34;

        public static bool runTranceiver = true;
        public static SerialPort wirelessIn;
        public static SerialPort GPSin;

        public static Thread receiveThread;
        public static Thread transmitThread;
        public static Thread GPSThread;
        public static Thread manageNode;

        public static byte[] rxPacket = new byte[259];
        public static String deviceData;
        public static String[] receivedData = new String[100];
        public static String[] devLookup = new String[100];
        public static bool[] devRefresh = new bool[100];
        public static int[] timeToRefresh = new int[100];
        public static String testStr = "27E9,3747.8690,N,08025.9719,W,23,26,14";
        public static String noSignalStr = "NO GPS DATA";
        //public static int devIndex = 0;
        public static ushort devID;
        public static bool isCoord = false;
        public static bool shouldTransmit = true;
        public static bool waitForSort = false;

        public static Dictionary<string,string> nodeIndex = new Dictionary<string,string>();
        public static Dictionary<string, long> nodeTimeout = new Dictionary<string, long>();

        public static bool signalFound = false;

        //Variables intended to change settings 
        public static int secondsToKill = 5;

        //Call before anything else. Initializes the local node and starts all the management threads
        public static void initializeLocalNode(String GPScom, String ALTcom)
        {
            GPSin = new SerialPort(GPScom, 4800, Parity.None, 8);       //Initialize the serial port for the GPS 
            GPSin.Open();                                                               //Open the port to communicate with the GPS
            wirelessIn = new SerialPort(ALTcom, 115200, Parity.None, 8, StopBits.One);
            wirelessIn.RtsEnable = true;                                            //RTS/DTR must be enabled for communication with the ALT5801
            wirelessIn.DtrEnable = false;
            wirelessIn.Open();

            //Start the recieve thread. This thread will wait for an input from the wireless module and process the input into local variables when received
            receiveThread = new Thread(wirelessReceive);
            transmitThread = new Thread(wirelessTransmit);
            //Start the GPS thread. This will read data from the GPS module and sort it into local variables until needed
            GPSThread = new Thread(GPSupdate);
            //Init the node management thread. This is forked from ProjectFastNet to run in a separate thread, allowing parallel excecution with the Form
            manageNode = new Thread(localNodeManagement);

            receiveThread.Start();
            GPSThread.Start();
            manageNode.Start();
        }
        //Call to kill all running threads and halt the node
        public static void disableLocalNode()
        {
            receiveThread.Abort();
            transmitThread.Abort();
            GPSThread.Abort();
            manageNode.Abort();
            wirelessIn.Close();
            GPSin.Close();
        }

        public static void localNodeManagement()
        {
            //Generate the device ID of this end
            Random devIDgen = new Random();
            devID = (ushort)devIDgen.Next(0x0000, 0xFFFF);
            deviceData = String.Format("{0:X}", devID) + "," + noSignalStr;

            initALT5801(false);

            transmitThread.Start();

            int cleanCount = 0;
            while (true)
            {
                bool cleanSweep = true;                            //Triggered true if any nodes are read active in the print loop
                //Console.Clear();
                //Console.WriteLine("Active Device: ");
                //Console.WriteLine(deviceData);
                //Console.WriteLine();
                long currentTicks = DateTime.UtcNow.Ticks / 10000000;
                if (nodeTimeout.Count == 0) { cleanSweep = false; }
                else
                {
                    waitForSort = true;
                    try
                    {
                        foreach (KeyValuePair<string, long> item in nodeTimeout)
                        {
                            //MessageBox.Show(item.Key + "   " + item.Value);
                            if ((currentTicks - item.Value) > secondsToKill)
                            {
                                nodeIndex.Remove(item.Key);
                                nodeTimeout.Remove(item.Key);
                            }
                        }
                    }
                    catch (Exception) { };
                    waitForSort = false;
                }
                

                /*
                    {
                        timeToRefresh[i] = 0;
                        devRefresh[i] = false;
                    }
                    else
                    {
                        timeToRefresh[i]++;
                    }
                    if (timeToRefresh[i] < 12)
                    {
                        //Console.WriteLine("Mesh Device {0}", i);
                        //Console.WriteLine(receivedData[i]);
                        //Console.WriteLine();
                        cleanSweep = true;
                    }
                }*/

                //If the device was auto-configed as a coordinator, turn back into a router once a node has been found
                if (isCoord && cleanSweep)
                {
                    //Console.Clear();
                    shouldTransmit = false;
                    initALT5801(false);
                    shouldTransmit = true;
                }
                if (!cleanSweep)
                {
                    cleanCount++;
                }
                else
                {
                    cleanCount = 0;
                }
                if (cleanCount > 40)
                {
                    //Console.Clear();
                    shouldTransmit = false;
                    initALT5801(!isCoord);
                    shouldTransmit = true;
                    cleanCount = 0;
                }
                Thread.Sleep(250);
            }
        }

        public static void wirelessTransmit()
        {
            Console.WriteLine("Transmit Thread Started");
            Random getPause = new Random();
            while (runTranceiver)
            {
                Thread.Sleep(getPause.Next(00, 1200));
                if (shouldTransmit)
                {
                    AltCOM.genCom(wirelessIn, AltCOM.ZB_SEND_DATA(0xFFFD, 2, 0, 1, 1, Encoding.UTF8.GetBytes(deviceData)));
                    while (!AltGET.isMessage(rxPacket, mDef.ZB_SEND_DATA_RSP)) ;
                    while (!AltGET.isMessage(rxPacket, mDef.ZB_SEND_DATA_CONFIRM)) ;
                }
            }
        }

        //This function runs in a separate thread and handles all receiving and processisng of the received data
        public static void wirelessReceive()
        {
            Console.WriteLine("Receive Thread Started");
            while (runTranceiver)
            {
                rxPacket = AltGET.getPacket(wirelessIn);

                /*Console.WriteLine("Packet Received");                             //Uncomment this to print all incoming transmissions to the console for debugging
                for (int i=0;i<rxPacket.Length;i++)
                {
                    Console.Write("{0} ", String.Format("{0:X}", rxPacket[i]));
                }
                Console.WriteLine(" ");*/

                if (AltGET.isMessage(rxPacket, 0x4687))
                {
                    String newDat = Encoding.ASCII.GetString(AltGET.getRx(rxPacket));           //Store the packet locally
                    String[] cotSet = newDat.Split(',');
                    //Look up the devID in the index and see if it's already been entered
                    while (waitForSort) ;
                    if (nodeIndex.ContainsKey(cotSet[0]))
                    {
                        nodeIndex.Remove(cotSet[0]);
                        nodeTimeout.Remove(cotSet[0]);
                    }
                    nodeIndex.Add(cotSet[0], newDat);
                    nodeTimeout.Add(cotSet[0], DateTime.UtcNow.Ticks/10000000);
                    /* int wasEntered = -1;
                    for (int i = 0; i < devIndex; i++)
                    {
                        if (devLookup[i] == cotSet[0])
                        {
                            wasEntered = i;
                        }
                    }
                    if (wasEntered != -1)                             //If it's already registered, override the current data
                    {
                        receivedData[wasEntered] = newDat;
                        devRefresh[wasEntered] = true;
                    }
                    else
                    {                                               //If not, register a new entry in the table and increase the index
                        devIndex++;
                        devLookup[devIndex] = cotSet[0];
                        receivedData[devIndex] = newDat;
                        devRefresh[devIndex] = true;
                    }*/
                }
            }
        }
        //This function runs in a separate thread and handles processing and parsing of the GPS data
        public static void GPSupdate()
        {
            Console.WriteLine("GPS Processing Thread Started");
            while (runTranceiver)
            {
                do
                {
                    String GPSinData = GPSin.ReadLine();
                    ParseGPS.parseNMEAstring(GPSinData);
                    if (ParseGPS.getCommand() == 0)
                    {
                        //Console.Clear();
                        if (ParseGPS.findSignal())
                        {
                            float[] latlog = ParseGPS.getDecCoordinates();
                            char[] Compass = ParseGPS.getCompass();
                            //Console.WriteLine("Latitude - " + latlog[0] + " " + Compass[0] + " Longitude - " + latlog[1] + " " + Compass[1]);
                            List<String> TimeList = ParseGPS.getTime();
                            String[] assemStr = new String[] { "0x", String.Format("{0:X}", devID), ",", latlog[0].ToString(), ",", Compass[0].ToString(), ",", latlog[1].ToString(), ",", Compass[1].ToString(), ",", TimeList[0], ",", TimeList[1], ",", TimeList[2].Substring(0, 3) };
                            deviceData = String.Join("", assemStr);
                            signalFound = true;
                            //Console.WriteLine("Time: " + TimeList[0] + ":" + TimeList[1] + ":" + TimeList[2]);
                        }
                        else
                        {
                            deviceData = ("0x" + String.Format("{0:X}", devID) + "," + noSignalStr);
                            signalFound = false;
                            //Console.WriteLine("No Signal Found");
                        }
                    }
                } while (ParseGPS.getCommand() != 0);
            }
        }

        public static void initALT5801(bool isCoordinator)
        {
            //Run the full initialization procedure for the ALT5801
            AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x03, new byte[1] { 3 }));
            while (!AltGET.isMessage(rxPacket, mDef.ZB_WRITE_CFG_RSP)) ;
            Console.WriteLine("Config Reset");
            AltCOM.genCom(wirelessIn, AltCOM.SYS_RESET());
            while (!AltGET.isMessage(rxPacket, mDef.SYS_RESET_IND)) ;
            Console.WriteLine("Device Reset");
            AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x83, new byte[2] { 0x12, PANID }));
            while (!AltGET.isMessage(rxPacket, mDef.ZB_WRITE_CFG_RSP)) ;
            Console.WriteLine("Set PANID of the device");
            AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x84, new byte[4] { 0, 0, 0, channel }));
            while (!AltGET.isMessage(rxPacket, mDef.ZB_WRITE_CFG_RSP)) ;
            Console.WriteLine("Set the channel of the device to " + channel);
            AltCOM.genCom(wirelessIn, AltCOM.ZB_READ_CFG(0x84));
            while (!AltGET.isMessage(rxPacket, mDef.ZB_READ_CFG_RSP)) ;
            Console.WriteLine("//////CHANGES CONFIRMED//////");
            /*////////////COPY PASTABLE DEBUG BLOCK FOR PRINTING THE SERIAL TERMINAL/////////
            while (true)
            {
                string hexOutput = String.Format("{0:X}", wirelessIn.ReadByte());
                Console.Write("{0} ", hexOutput);
            }
            ///////////////////////////////////////////////////////////////////////////////*/
            if (isCoordinator)
            {
                Console.WriteLine("Configuring Coordinator");
                byte[] cmd_in = new byte[2] { 0x01, 0x00 };
                byte[] cmd_out = { 0x02, 0x00 };
                AltCOM.genCom(wirelessIn, AltCOM.ZB_APP_REGISTER(1, 1, 1, 0, 1, cmd_in, 1, cmd_out));
                AltCOM.genCom(wirelessIn, AltCOM.ZB_START_REQ());
                while (!AltGET.isMessage(rxPacket, mDef.ZB_START_REQUEST_RSP)) ;
                while (!AltGET.isMessage(rxPacket, mDef.ZB_START_CONFIRM)) ;
                Console.WriteLine("Configured Coordinator");
                isCoord = true;
            }
            else
            {
                Console.WriteLine("Configuring Router");

                int routerWait = 0;

                AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x87, new byte[1] { 1 }));
                while (!AltGET.isMessage(rxPacket, mDef.ZB_WRITE_CFG_RSP)) ;
                Console.WriteLine("Configured Device Type");

                AltCOM.genCom(wirelessIn, AltCOM.SYS_RESET());
                while (!AltGET.isMessage(rxPacket, mDef.SYS_RESET_IND)) ;

                AltCOM.genCom(wirelessIn, AltCOM.ZB_READ_CFG(0x87));
                while (!AltGET.isMessage(rxPacket, mDef.ZB_READ_CFG_RSP)) ;

                byte[] cmd_in = new byte[2] { 0x02, 0x00 };
                byte[] cmd_out = { 0x01, 0x00 };
                AltCOM.genCom(wirelessIn, AltCOM.ZB_APP_REGISTER(1, 1, 0, 0, 1, cmd_in, 1, cmd_out));
                while (!AltGET.isMessage(rxPacket, mDef.ZB_REGISTER_RSP)) ;
                AltCOM.genCom(wirelessIn, AltCOM.ZB_START_REQ());
                while (!AltGET.isMessage(rxPacket, mDef.ZB_START_REQUEST_RSP)) ;
                while (!AltGET.isMessage(rxPacket, mDef.ZB_START_CONFIRM))
                {
                    if (routerWait > 20)
                    {
                        break;              //Stop waiting for a response if you waited longer than 5 seconds
                    }
                    routerWait++;
                    Thread.Sleep(250);
                };
                if (routerWait < 21)
                {
                    Console.WriteLine("Configured Router");
                    isCoord = false;
                }
                else
                {
                    initALT5801(true);
                }
            }
        }
    }
}
