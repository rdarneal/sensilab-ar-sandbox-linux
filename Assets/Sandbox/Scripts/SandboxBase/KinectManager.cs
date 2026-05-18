//
//  KinectManager.cs
//
//	Copyright 2021 SensiLab, Monash University <sensilab@monash.edu>
//
//  This file is part of sensilab-ar-sandbox.
//
//  sensilab-ar-sandbox is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  sensilab-ar-sandbox is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with sensilab-ar-sandbox.  If not, see <https://www.gnu.org/licenses/>.
//

using UnityEngine;
using System.Collections;
using System.IO;
#if !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_LINUX
using Windows.Kinect;
#endif

namespace ARSandbox
{
    public class KinectManager : MonoBehaviour
    {
        // Kinect v2 depth stream is a fixed 512x424 per hardware spec.
        // Used as the descriptor when no sensor is queried (Linux saved-data fallback).
        private const int KINECT_V2_DEPTH_WIDTH = 512;
        private const int KINECT_V2_DEPTH_HEIGHT = 424;

        public bool UseSavedData;
        public TextAsset SavedData;

        public delegate void OnDataStarted_Delegate();
        public static event OnDataStarted_Delegate OnDataStarted;

        private DepthFrameDescriptor kinectFrameDesc;
#if !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_LINUX
        private KinectSensor kinectSensor;
        private DepthFrameReader depthFrameReader;
#endif
        private ushort[] depthData;
        private bool dataReady = false;
        private bool newData = false;

        void Start()
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            // Linux: Microsoft Kinect SDK is unavailable; libfreenect2 integration
            // is a later port phase. Force saved-data playback so the simulations
            // can still run end-to-end against the recorded depth frame.
            UseSavedData = true;
            kinectFrameDesc = new DepthFrameDescriptor(KINECT_V2_DEPTH_WIDTH, KINECT_V2_DEPTH_HEIGHT);
            LoadDepthData();
            StartCoroutine(Emulate30Hz());
#else
            if (GetFrameDescriptor())
            {
                if (UseSavedData)
                {
                    LoadDepthData();
                    StartCoroutine(Emulate30Hz());
                }
                else
                {
                    SetUpKinectBuffer();
                }
            }
#endif
        }

        void Update()
        {
#if !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_LINUX
            if (!UseSavedData)
            {
                if (depthFrameReader != null)
                {
                    DepthFrame frame = depthFrameReader.AcquireLatestFrame();
                    if (frame != null)
                    {
                        if (!dataReady)
                        {
                            dataReady = true;
                            if (OnDataStarted != null) OnDataStarted();
                        }
                        frame.CopyFrameDataToArray(depthData);
                        newData = true;
                        frame.Dispose();
                        frame = null;
                    }
                }

                if (Input.GetKeyUp(KeyCode.S))
                {
                    //SaveDepthData();
                }
            }
#endif
        }

        void OnApplicationQuit()
        {
#if !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_LINUX
            if (!UseSavedData)
            {
                if (depthFrameReader != null)
                {
                    depthFrameReader.Dispose();
                    depthFrameReader = null;
                }

                if (kinectSensor != null)
                {
                    if (kinectSensor.IsOpen)
                    {
                        kinectSensor.Close();
                    }

                    kinectSensor = null;
                }
            }
#endif
        }
        private IEnumerator Emulate30Hz()
        {
            while (true)
            {
                newData = true;
                yield return new WaitForSeconds(1 / 30.0f);

                if (!dataReady)
                {
                    dataReady = true;
                    if (OnDataStarted != null) OnDataStarted();
                }
            }
        }
        public DepthFrameDescriptor GetKinectFrameDescriptor()
        {
            return kinectFrameDesc;
        }
        public Point GetKinectFrameSize()
        {
            return new Point(kinectFrameDesc.Width, kinectFrameDesc.Height);
        }
        public ushort[] GetCurrentData()
        {
            newData = false;
            return depthData;
        }

        public bool StreamStarted()
        {
            if (UseSavedData)
                return true;

            return dataReady;
        }

        public bool NewDataReady()
        {
            return newData;
        }

#if !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_LINUX
        private bool GetFrameDescriptor()
        {
            kinectSensor = KinectSensor.GetDefault();
            if (kinectSensor != null)
            {
                FrameDescription fd = kinectSensor.DepthFrameSource.FrameDescription;
                kinectFrameDesc = new DepthFrameDescriptor(fd.Width, fd.Height);
                return true;
            }
            else
            {
                print("Error: KinectSensor not found. Make sure Kinect has been installed correctly");
                return false;
            }
        }

        private void SetUpKinectBuffer()
        {
            if (kinectSensor != null)
            {
                if (!kinectSensor.IsOpen)
                {
                    kinectSensor.Open();
                }

                depthFrameReader = kinectSensor.DepthFrameSource.OpenReader();
                depthData = new ushort[kinectSensor.DepthFrameSource.FrameDescription.LengthInPixels];
            }
        }
#endif

        private void LoadDepthData()
        {
            using (Stream s = new MemoryStream(SavedData.bytes))
            {
                using (BinaryReader br = new BinaryReader(s))
                {
                    int length = br.ReadInt32();
                    depthData = new ushort[length];
                    for (int i = 0; i < length; i++)
                    {
                        depthData[i] = br.ReadUInt16();
                    }
                }
            }
        }
        private void SaveDepthData()
        {
            using (FileStream fs = new FileStream(Application.dataPath + "/Depth.txt", FileMode.OpenOrCreate, FileAccess.Write))
            {
                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    bw.Write(depthData.Length);
                    foreach (ushort value in depthData)
                    {
                        bw.Write(value);
                    }
                }
            }
        }
    }
}
