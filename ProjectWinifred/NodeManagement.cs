﻿using System;
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
        public static String noSignalStr = "NOSIG";
        //public static int devIndex = 0;
        public static ushort devID;
        public static bool isCoord = false;
        public static bool shouldTransmit = true;
        public static bool waitForSort = false;
        public static bool waitForFormSort = false;

        public static Dictionary<string,string> nodeIndex = new Dictionary<string,string>();
        public static Dictionary<string, long> nodeTimeout = new Dictionary<string, long>();
        public static Dictionary<ushort, int> receiveFlags = new Dictionary<ushort, int>();

        public static bool signalFound = false;

        //Variables intended to change settings (accessed by value_changed calls in Form1.cs) 
        public static int secondsToKill = 5;
        public static bool dynamicUpdates = true;
        public static bool forceCoordinator = false;
        public static bool forceRouter = false;
        public static bool postFixRouterConvert = true;
        public static int waitForCoordReassign = 40;
        public static int waitForRouterReassign = 40;
        public static int packetsPerSecond = 2;
        public static bool transmitCheck = true;
        public static bool transmitLinkedData = true;
        public static int transmitToLinkedData = 6;
        public static int transmitLinkedPause = 5;

        //Variables intended to pass data to Form1
        public static string[] debugSet = new string[100];
        public static bool[] debugFlag = new bool[100];
        public static int debugStringIndex = 0;

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

            //Set all threads to background threads to ensure they close when the form does
            manageNode.IsBackground = true;
            GPSThread.IsBackground = true;
            transmitThread.IsBackground = true;
            receiveThread.IsBackground = true;

            //Start all needed threads. The wireless receive will be started by the node management once the ALT5801 is initialized
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
                long currentTicks = DateTime.UtcNow.Ticks / 10000000;
                if (nodeIndex.Count == 0) { cleanSweep = false; }
                else
                {
                    waitForSort = true;
                    try
                    {
                        foreach (KeyValuePair<string, long> item in nodeTimeout)
                        {
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

                //If the device was auto-configed as a coordinator, turn back into a router once a node has been found
                if (isCoord && cleanSweep && postFixRouterConvert && dynamicUpdates)
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
                //Check if the ALT5801 should be reassigned (to coordinator)
                if ((cleanCount>waitForCoordReassign) && dynamicUpdates && isCoord)   //((((cleanCount > waitForCoordReassign) && (isCoord)) || ((cleanCount > waitForRouterReassign) && (!isCoord))) && (dynamicUpdates))
                {
                    shouldTransmit = false;
                    initALT5801(false);
                    shouldTransmit = true;
                    cleanCount = 0;
                }
                if ((cleanCount > waitForRouterReassign) && dynamicUpdates && (!isCoord))   //((((cleanCount > waitForCoordReassign) && (isCoord)) || ((cleanCount > waitForRouterReassign) && (!isCoord))) && (dynamicUpdates))
                {
                    shouldTransmit = false;
                    initALT5801(true);
                    shouldTransmit = true;
                    cleanCount = 0;
                }

                //Check for settings change
                if ((forceCoordinator&&(!isCoord))||(forceRouter&&isCoord))
                {
                    shouldTransmit = false;
                    initALT5801(forceCoordinator);
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
            int transmitCount = 0;
            while (runTranceiver)
            {
                //Thread.Sleep(getPause.Next(00, 1200));
                Thread.Sleep(1000 / packetsPerSecond);
                if (shouldTransmit)
                {
                    if (transmitLinkedData && (transmitToLinkedData < transmitCount))
                    {
                        try
                        {
                            waitForSort = waitForFormSort = true;
                            Thread.Sleep(transmitLinkedPause);
                            foreach (KeyValuePair<string, string> pair in nodeIndex)
                            {
                                AltCOM.genCom(wirelessIn, AltCOM.ZB_SEND_DATA(0xFFFD, 2, 0, 1, 1, Encoding.UTF8.GetBytes(pair.Value)));
                                waitForAck(mDef.ZB_SEND_DATA_RSP);
                                waitForAck(mDef.ZB_SEND_DATA_CONFIRM);
                            }
                            waitForSort = waitForFormSort = false;
                            transmitCount = 0;
                        }
                        catch (Exception) { }
                    }
                    AltCOM.genCom(wirelessIn, AltCOM.ZB_SEND_DATA(0xFFFD, 2, 0, 1, 1, Encoding.UTF8.GetBytes(deviceData)));
                    if (transmitCheck)
                    {
                        waitForAck(mDef.ZB_SEND_DATA_RSP);
                        waitForAck(mDef.ZB_SEND_DATA_CONFIRM);
                    }
                    transmitCount++;
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

                ushort compVal = BitConverter.ToUInt16(new byte[2] {rxPacket[3], rxPacket[2] }, 0);     //Log the packet for comparison

                //If it's a message, register the node with the index. Otherwise, flag that an RX packet has been received and move on
                if (compVal==0x4687)
                {
                    String newDat = Encoding.ASCII.GetString(AltGET.getRx(rxPacket));           //Store the packet locally
                    writeLine(newDat);
                    String[] cotSet = newDat.Split(',');
                    //Look up the devID in the index and see if it's already been entered
                    while (waitForSort);
                    if (nodeIndex.ContainsKey(cotSet[0]))
                    {
                        nodeIndex.Remove(cotSet[0]);
                        nodeTimeout.Remove(cotSet[0]);
                    }
                    nodeIndex.Add(cotSet[0], newDat);
                    nodeTimeout.Add(cotSet[0], DateTime.UtcNow.Ticks/10000000);
                }
                else
                {
                    if (receiveFlags.ContainsKey(compVal)) {                                    //Log the data in the table to indicate that the packet has been received
                        receiveFlags[compVal]++;
                    } else { receiveFlags.Add(compVal, 1); }
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
                            //FORMAT CHANGED 5/31/2017: INCOMPATABLE WITH ALL PREVIOUS VERSIONS OF THE SOFTWARE
                            List<String> TimeList = ParseGPS.getTime();
                            String[] assemStr = new String[] { String.Format("{0:X}", devID), ",", latlog[0].ToString(), ",", latlog[1].ToString(), ",", TimeList[0], ",", TimeList[1], ",", TimeList[2].Substring(0, 3) };
                            deviceData = String.Join("", assemStr);
                            signalFound = true;
                            //Console.WriteLine("Time: " + TimeList[0] + ":" + TimeList[1] + ":" + TimeList[2]);
                        }
                        else
                        {
                            deviceData = (String.Format("{0:X}", devID) + "," + noSignalStr);
                            signalFound = false;
                            //Console.WriteLine("No Signal Found");
                        }
                    }
                } while (ParseGPS.getCommand() != 0);
            }
        }

        //This will initialize the ALT5801 module as either a coordinator or router
        public static void initALT5801(bool isCoordinator)
        {
            //Run the full initialization procedure for the ALT5801
            AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x03, new byte[1] { 3 }));
            waitForAck(mDef.ZB_WRITE_CFG_RSP);
            writeLine("Config Reset");
            AltCOM.genCom(wirelessIn, AltCOM.SYS_RESET());
            waitForAck(mDef.SYS_RESET_IND);
            writeLine("Device Reset");
            AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x83, new byte[2] { 0x12, PANID }));
            waitForAck(mDef.ZB_WRITE_CFG_RSP);
            writeLine("Set PANID of the device");
            AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x84, new byte[4] { 0, 0, 0, channel }));
            waitForAck(mDef.ZB_WRITE_CFG_RSP);
            writeLine("Set the channel of the device to " + channel);
            AltCOM.genCom(wirelessIn, AltCOM.ZB_READ_CFG(0x84));
            waitForAck(mDef.ZB_READ_CFG_RSP);
            writeLine("//////CHANGES CONFIRMED//////");
            /*////////////COPY PASTABLE DEBUG BLOCK FOR PRINTING THE SERIAL TERMINAL/////////
            while (true)
            {
                string hexOutput = String.Format("{0:X}", wirelessIn.ReadByte());
                write("{0} ", hexOutput);
            }
            ///////////////////////////////////////////////////////////////////////////////*/
            if (isCoordinator)
            {
                writeLine("Configuring Coordinator");
                byte[] cmd_in = new byte[2] { 0x01, 0x00 };
                byte[] cmd_out = { 0x02, 0x00 };
                AltCOM.genCom(wirelessIn, AltCOM.ZB_APP_REGISTER(1, 1, 1, 0, 1, cmd_in, 1, cmd_out));
                AltCOM.genCom(wirelessIn, AltCOM.ZB_START_REQ());
                waitForAck(mDef.ZB_START_REQUEST_RSP);
                waitForAck(mDef.ZB_START_CONFIRM);
                writeLine("Configured Coordinator");
                isCoord = true;
            }
            else
            {
                writeLine("Configuring Router");

                int routerWait = 0;

                AltCOM.genCom(wirelessIn, AltCOM.ZB_WRITE_CFG(0x87, new byte[1] { 1 }));
                waitForAck(mDef.ZB_WRITE_CFG_RSP);
                writeLine("Configured Device Type");

                AltCOM.genCom(wirelessIn, AltCOM.SYS_RESET());
                waitForAck(mDef.SYS_RESET_IND);

                AltCOM.genCom(wirelessIn, AltCOM.ZB_READ_CFG(0x87));
                waitForAck(mDef.ZB_READ_CFG_RSP);

                byte[] cmd_in = new byte[2] { 0x02, 0x00 };
                byte[] cmd_out = { 0x01, 0x00 };
                AltCOM.genCom(wirelessIn, AltCOM.ZB_APP_REGISTER(1, 1, 0, 0, 1, cmd_in, 1, cmd_out));
                waitForAck(mDef.ZB_REGISTER_RSP);
                AltCOM.genCom(wirelessIn, AltCOM.ZB_START_REQ());
                waitForAck(mDef.ZB_START_REQUEST_RSP);
                //while (!AltGET.isMessage(rxPacket, mDef.ZB_START_CONFIRM)) ;
                int flagCnt = 0;
                while (flagCnt < 1)
                {
                    if (receiveFlags.ContainsKey(mDef.ZB_START_CONFIRM))
                    {
                        flagCnt = receiveFlags[mDef.ZB_START_CONFIRM];
                    }
                    if (routerWait > waitForRouterReassign)
                    {
                        break;              //Stop waiting for a response if you waited longer than 5 seconds
                    }
                    routerWait++;
                    Thread.Sleep(250);
                }
                if (routerWait <= waitForRouterReassign)
                {
                    receiveFlags[mDef.ZB_START_CONFIRM]--;
                    writeLine("Configured Router");
                    isCoord = false;
                }
                else
                {
                    initALT5801(dynamicUpdates);
                }
            }
        }

        //Wait for the RX packet to get flagged as received and return true when it does
        public static bool waitForAck(ushort ackFlag)
        {
            int flagCnt = 0;
            while (flagCnt<1)
            {
                if (receiveFlags.ContainsKey(ackFlag))
                {
                    flagCnt = receiveFlags[ackFlag];
                }
            }
            receiveFlags[ackFlag]--;
            return true;
        }

        //Call this function to write a message to the richTextBox terminal in Form1
        public static void writeLine(string input)
        {
            debugSet[debugStringIndex] = input;
            debugFlag[debugStringIndex++] = true;
            if (debugStringIndex == 100) { debugStringIndex = 0; }
        }
    }
}
