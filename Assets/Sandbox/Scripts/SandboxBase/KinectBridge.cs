using System;
using System.Runtime.InteropServices;

namespace ARSandbox
{
    public enum KbStatus
    {
        Ok = 0,
        ErrNotInitialised = -1,
        ErrAlreadyOpen = -2,
        ErrNoDevice = -3,
        ErrOpenFailed = -4,
        ErrNotOpen = -5,
        ErrNotStarted = -6,
        ErrPipelineFailed = -7,
        ErrBufferTooSmall = -8,
        ErrNoNewFrame = -9,
        ErrInternal = -99
    }

    public enum KbPipeline
    {
        Default = 0,
        Clkde = 1,
        Cl = 2,
        Gl = 3,
        Cpu = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KbDepthDescriptor
    {
        public int Width;
        public int Height;
        public float MinDepthMm;
        public float MaxDepthMm;
        public float Fx;
        public float Fy;
        public float Cx;
        public float Cy;
    }

    public static class KinectBridge
    {
        private const string DLL = "kinectbridge";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_init")]
        public static extern KbStatus KbInit();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_shutdown")]
        public static extern void KbShutdown();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_open")]
        public static extern KbStatus KbOpen(KbPipeline pipeline, int strict);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_close")]
        public static extern KbStatus KbClose();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_start")]
        public static extern KbStatus KbStart();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_stop")]
        public static extern KbStatus KbStop();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_get_depth_descriptor")]
        public static extern KbStatus KbGetDepthDescriptor(out KbDepthDescriptor descriptor);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_try_get_depth_frame")]
        private static extern KbStatus kb_try_get_depth_frame([Out] ushort[] buffer, UIntPtr bufferLenBytes, out ulong outSequence);

        public static KbStatus KbTryGetDepthFrame(ushort[] buffer, out ulong sequence)
        {
            UIntPtr lenBytes = (UIntPtr)((ulong)buffer.Length * sizeof(ushort));
            return kb_try_get_depth_frame(buffer, lenBytes, out sequence);
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_active_pipeline_name")]
        private static extern System.IntPtr kb_active_pipeline_name();

        public static string KbActivePipelineName()
        {
            System.IntPtr p = kb_active_pipeline_name();
            return p == System.IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "kb_freenect2_version")]
        private static extern System.IntPtr kb_freenect2_version();

        public static string KbFreenect2Version()
        {
            System.IntPtr p = kb_freenect2_version();
            return p == System.IntPtr.Zero ? null : Marshal.PtrToStringAnsi(p);
        }
    }
}
