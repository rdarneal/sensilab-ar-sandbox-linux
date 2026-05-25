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

namespace ARSandbox
{
    public class KinectManager : MonoBehaviour
    {
        // Kinect v2 depth stream is a fixed 512x424 per hardware spec.
        private const int KINECT_V2_DEPTH_WIDTH = 512;
        private const int KINECT_V2_DEPTH_HEIGHT = 424;
        private const int KINECT_V2_DEPTH_PIXELS = KINECT_V2_DEPTH_WIDTH * KINECT_V2_DEPTH_HEIGHT;

        public bool UseSavedData;
        public TextAsset SavedData;

        public delegate void OnDataStarted_Delegate();
        public static event OnDataStarted_Delegate OnDataStarted;

        private DepthFrameDescriptor kinectFrameDesc;
        private ushort[] depthData;
        private bool dataReady = false;
        private bool newData = false;

        // Bridge lifecycle state. kb_init / kb_open are idempotent on the native
        // side (re-open returns ALREADY_OPEN which we treat as success), so we
        // rely on that to survive Editor domain reloads rather than wiring
        // AssemblyReloadEvents.
        private bool bridgeInitialised = false;
        private bool bridgeOpened = false;
        private bool bridgeStarted = false;
        private ulong lastSequence = 0;

        void Start()
        {
            kinectFrameDesc = new DepthFrameDescriptor(KINECT_V2_DEPTH_WIDTH, KINECT_V2_DEPTH_HEIGHT);
            depthData = new ushort[KINECT_V2_DEPTH_PIXELS];

            if (UseSavedData)
            {
                LoadDepthData();
                StartCoroutine(Emulate30Hz());
                return;
            }

            if (!StartBridge())
            {
                Debug.LogWarning("[KinectManager] Falling back to saved depth data (Depth.txt).");
                UseSavedData = true;
                LoadDepthData();
                StartCoroutine(Emulate30Hz());
                return;
            }

            RefreshDescriptorFromBridge();
        }

        void Update()
        {
            if (UseSavedData || !bridgeStarted)
                return;

            ulong seq;
            KbStatus status = KinectBridge.KbTryGetDepthFrame(depthData, out seq);

            if (status == KbStatus.Ok)
            {
                if (seq != lastSequence)
                {
                    lastSequence = seq;
                    newData = true;
                    if (!dataReady)
                    {
                        dataReady = true;
                        if (OnDataStarted != null) OnDataStarted();
                        RefreshDescriptorFromBridge();
                    }
                }
            }
            else if (status != KbStatus.ErrNoNewFrame)
            {
                Debug.LogWarning("[KinectManager] kb_try_get_depth_frame returned " + status);
            }
        }

        void OnDisable()
        {
            ShutdownBridge();
        }

        void OnApplicationQuit()
        {
            ShutdownBridge();
        }

        private bool StartBridge()
        {
            if (!bridgeInitialised)
            {
                KbStatus s = KinectBridge.KbInit();
                if (s != KbStatus.Ok)
                {
                    Debug.LogError("[KinectManager] kb_init failed: " + s);
                    return false;
                }
                bridgeInitialised = true;
            }

            if (!bridgeOpened)
            {
                KbStatus s = KinectBridge.KbOpen(KbPipeline.Default, 0);
                if (s != KbStatus.Ok && s != KbStatus.ErrAlreadyOpen)
                {
                    Debug.LogError("[KinectManager] kb_open failed: " + s);
                    return false;
                }
                bridgeOpened = true;
            }

            if (!bridgeStarted)
            {
                KbStatus s = KinectBridge.KbStart();
                if (s != KbStatus.Ok)
                {
                    Debug.LogError("[KinectManager] kb_start failed: " + s);
                    return false;
                }
                bridgeStarted = true;
            }

            Debug.Log("[KinectManager] libkinectbridge ready (pipeline=" +
                      (KinectBridge.KbActivePipelineName() ?? "unknown") +
                      ", freenect2=" + (KinectBridge.KbFreenect2Version() ?? "unknown") + ")");
            return true;
        }

        private void RefreshDescriptorFromBridge()
        {
            KbDepthDescriptor d;
            KbStatus s = KinectBridge.KbGetDepthDescriptor(out d);
            if (s != KbStatus.Ok)
            {
                Debug.LogWarning("[KinectManager] kb_get_depth_descriptor returned " + s + "; keeping defaults.");
                return;
            }
            kinectFrameDesc = new DepthFrameDescriptor(d.Width, d.Height,
                                                      d.MinDepthMm, d.MaxDepthMm,
                                                      d.Fx, d.Fy, d.Cx, d.Cy);
        }

        private void ShutdownBridge()
        {
            if (bridgeStarted)
            {
                KinectBridge.KbStop();
                bridgeStarted = false;
            }
            if (bridgeOpened)
            {
                KinectBridge.KbClose();
                bridgeOpened = false;
            }
            if (bridgeInitialised)
            {
                KinectBridge.KbShutdown();
                bridgeInitialised = false;
            }
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
    }
}
